using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class IssueCreateCommand
{
    private const string TransitionActor = "intent-cli";
    private const string DraftedPublishStatus = "drafted";
    private const string IssueCreatedPublishStatus = "issue-created";

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
            writer.WriteLine("Issue create command requires an execution unit.");
            return 1;
        }

        try
        {
            var result = ExecuteCore(context, args[0].Trim());
            writer.WriteLine($"Issue created for {result.Artifact.ExecutionUnit}: {result.LinkedIssue.Url}");
            writer.WriteLine($"Publish artifact: {result.ArtifactPath}");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static IssueCreateCommandResult ExecuteCore(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var artifactPath = IssuePublishArtifactPathResolver.Resolve(executionUnit);
        var absoluteArtifactPath = ResolveRepoPath(context.RepoRoot, artifactPath);
        if (!File.Exists(absoluteArtifactPath))
        {
            throw new InvalidOperationException($"Drafted publish artifact was not found at {absoluteArtifactPath}");
        }

        var artifact = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(absoluteArtifactPath));
        if (!string.Equals(artifact.ExecutionUnit, executionUnit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Issue publish artifact execution unit '{artifact.ExecutionUnit}' does not match requested execution unit '{executionUnit}'.");
        }

        if (!string.Equals(artifact.PublishStatus, DraftedPublishStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Issue publish artifact for '{executionUnit}' must be in '{DraftedPublishStatus}' status.");
        }

        var packetPath = ResolveRepoPath(context.RepoRoot, artifact.PacketPath);
        if (!File.Exists(packetPath))
        {
            throw new InvalidOperationException($"Projection packet artifact was not found at {packetPath}");
        }

        var packet = ProjectionPacketRuntimeReader.Read(File.ReadAllText(packetPath));
        if (!string.Equals(packet.ExecutionUnit, executionUnit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Projection packet execution unit '{packet.ExecutionUnit}' does not match requested execution unit '{executionUnit}'.");
        }

        var issueBodyPath = ResolveRepoPath(context.RepoRoot, artifact.IssueBodyPath);
        if (!File.Exists(issueBodyPath))
        {
            throw new InvalidOperationException($"GitHub issue body artifact was not found at {issueBodyPath}");
        }

        var body = File.ReadAllText(issueBodyPath);
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException("GitHub issue body artifact must not be empty.");
        }

        var targetRepo = GitHubRepositoryTargetResolver.Resolve(context.RepoRoot, packet.TargetRepo, GitCommandRunnerFactory());
        var linkedIssue = PublisherFactory().CreateIssue(targetRepo, packet.IssueTitle, body);

        var updatedArtifact = artifact with
        {
            PublishStatus = IssueCreatedPublishStatus,
            CreatedIssueNumber = linkedIssue.Number,
            CreatedIssueUrl = linkedIssue.Url
        };

        File.WriteAllText(absoluteArtifactPath, IssuePublishArtifactYaml.Serialize(updatedArtifact));
        AppendRunEvent(
            context,
            new RunEvent
            {
                Ts = TimestampFactory(),
                ExecutionUnit = executionUnit,
                Event = "issue-created",
                By = TransitionActor,
                LinkedIssue = linkedIssue.Url,
                PacketRef = artifact.PacketPath,
                ResultRef = artifactPath
            });

        return new IssueCreateCommandResult
        {
            Artifact = updatedArtifact,
            ArtifactPath = artifactPath,
            LinkedIssue = linkedIssue
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

internal sealed record IssueCreateCommandResult
{
    public required IssuePublishArtifact Artifact { get; init; }

    public required string ArtifactPath { get; init; }

    public required LinkedIssue LinkedIssue { get; init; }
}
