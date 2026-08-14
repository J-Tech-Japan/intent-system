using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class IssuePublishCommand
{
    private const string TransitionActor = "intent-cli";
    private const string IssueCreatedPublishStatus = "issue-created";
    private const string PublishedStatus = "published";
    private const string PublishLabelName = "intent-target";

    public static Func<IQueueDispatchPublisher> PublisherFactory { get; set; } = () => new GhQueueDispatchPublisher();

    public static Func<IGitRemoteCommandRunner> GitCommandRunnerFactory { get; set; } = () => new GitRemoteCommandRunner();

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Issue publish command requires an execution unit.");
            return 1;
        }

        try
        {
            var result = ExecuteCore(context, args[0].Trim());
            writer.WriteLine($"Issue published for {result.Artifact.ExecutionUnit}: {result.LinkedIssue.Url}");
            writer.WriteLine($"Publish artifact: {result.ArtifactPath}");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static IssuePublishCommandResult ExecuteCore(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var artifactPath = IssuePublishArtifactPathResolver.Resolve(executionUnit);
        var absoluteArtifactPath = ResolveRepoPath(context.RepoRoot, artifactPath);
        if (!File.Exists(absoluteArtifactPath))
        {
            throw new InvalidOperationException($"Issue-created publish artifact was not found at {absoluteArtifactPath}");
        }

        var artifact = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(absoluteArtifactPath));
        if (!string.Equals(artifact.ExecutionUnit, executionUnit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Issue publish artifact execution unit '{artifact.ExecutionUnit}' does not match requested execution unit '{executionUnit}'.");
        }

        if (!string.Equals(artifact.PublishStatus, IssueCreatedPublishStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Issue publish artifact for '{executionUnit}' must be in '{IssueCreatedPublishStatus}' status.");
        }

        if (artifact.CreatedIssueNumber is null || string.IsNullOrWhiteSpace(artifact.CreatedIssueUrl))
        {
            throw new InvalidOperationException(
                $"Issue publish artifact for '{executionUnit}' must contain created issue metadata.");
        }

        if (artifact.TeamMode is not null && !TeamMode.IsKnown(artifact.TeamMode))
        {
            throw new InvalidOperationException(
                $"Issue publish artifact for '{executionUnit}' contains unknown team_mode '{artifact.TeamMode}'; refusing the publish boundary.");
        }

        if (string.Equals(artifact.TeamMode, TeamMode.AuthoringOnly, StringComparison.Ordinal)
            && (!string.Equals(artifact.ActorRole, "design", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(artifact.OperatorAcceptanceEvidence)))
        {
            throw new InvalidOperationException(
                "authoring-only issue publish requires actor_role 'design' and operator acceptance evidence; the intent-target boundary was not applied.");
        }

        var packetPath = ResolveRepoPath(context.RepoRoot, artifact.PacketPath);
        if (!File.Exists(packetPath))
        {
            throw new InvalidOperationException($"Projection packet artifact was not found at {packetPath}");
        }

        var packet = ProjectionPacketRuntimeReader.Read(File.ReadAllText(packetPath));
        var targetRepo = GitHubRepositoryTargetResolver.Resolve(context.RepoRoot, packet.TargetRepo, GitCommandRunnerFactory());

        if (string.Equals(artifact.TeamMode, TeamMode.AuthoringOnly, StringComparison.Ordinal))
        {
            var handoff = PublishedExternalHandoffStore.Read(context.RepoRoot, executionUnit);
            if (handoff.Record is null
                || !PublishedExternalHandoffStore.MatchesIssue(
                    handoff.Record,
                    executionUnit,
                    targetRepo,
                    artifact.CreatedIssueNumber.Value,
                    artifact.CreatedIssueUrl!))
            {
                throw new InvalidOperationException(
                    "authoring-only issue publish requires a matching published-external-handoff record; "
                    + "the intent-target boundary was not applied.");
            }
        }

        PublisherFactory().AddLabel(targetRepo, artifact.CreatedIssueNumber.Value, PublishLabelName);

        var updatedArtifact = artifact with
        {
            PublishStatus = PublishedStatus,
            PublishedLabelName = PublishLabelName
        };

        File.WriteAllText(absoluteArtifactPath, IssuePublishArtifactYaml.Serialize(updatedArtifact));
        AppendRunEvent(
            context,
            new RunEvent
            {
                Ts = TimestampFactory(),
                ExecutionUnit = executionUnit,
                Event = "issue-published",
                By = TransitionActor,
                LinkedIssue = artifact.CreatedIssueUrl,
                PacketRef = artifact.PacketPath,
                ResultRef = artifactPath,
                TeamMode = artifact.TeamMode,
                ActorRole = artifact.ActorRole,
                OperatorAcceptanceEvidence = artifact.OperatorAcceptanceEvidence,
                ExternalHandoffRef = artifact.ExternalHandoffRef,
            });

        return new IssuePublishCommandResult
        {
            Artifact = updatedArtifact,
            ArtifactPath = artifactPath,
            LinkedIssue = new LinkedIssue
            {
                Repo = targetRepo,
                Number = artifact.CreatedIssueNumber.Value,
                Url = artifact.CreatedIssueUrl
            }
        };
    }

    private static void AppendRunEvent(CliContext context, RunEvent runEvent)
    {
        var runLogPath = context.GetRunLogPath();
        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");

        Directory.CreateDirectory(runLogDirectory);
        File.AppendAllText(runLogPath, RunLogSerializer.SerializeLine(runEvent) + Environment.NewLine);
    }

    private static string ResolveRepoPath(string repoRoot, string relativePath)
    {
        return Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}

internal sealed record IssuePublishCommandResult
{
    public required IssuePublishArtifact Artifact { get; init; }

    public required string ArtifactPath { get; init; }

    public required LinkedIssue LinkedIssue { get; init; }
}
