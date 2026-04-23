using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class IssueDraftCommand
{
    private const string TransitionActor = "intent-cli";
    private const string DraftedPublishStatus = "drafted";

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Issue draft command requires an execution unit.");
            return 1;
        }

        try
        {
            var result = ExecuteCore(context, args[0].Trim());
            writer.WriteLine($"Issue draft prepared for {result.Artifact.ExecutionUnit}.");
            writer.WriteLine($"Publish artifact: {result.ArtifactPath}");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static IssueDraftCommandResult ExecuteCore(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var packetPath = QueueEnqueueCommand.ResolvePacketPath(context, executionUnit);
        if (!File.Exists(packetPath))
        {
            throw new InvalidOperationException($"Projection packet artifact was not found at {packetPath}");
        }

        var packetYaml = File.ReadAllText(packetPath);
        _ = QueueEnqueueCommand.ReadPacket(packetYaml, executionUnit);

        var issueBodyPath = ResolveIssueBodyPath(packetPath);
        if (!File.Exists(issueBodyPath))
        {
            throw new InvalidOperationException($"GitHub issue body artifact was not found at {issueBodyPath}");
        }

        var issueBody = File.ReadAllText(issueBodyPath);
        if (string.IsNullOrWhiteSpace(issueBody))
        {
            throw new InvalidOperationException("GitHub issue body artifact must not be empty.");
        }

        var relativePacketPath = ToRelativeRepoPath(context.RepoRoot, packetPath);
        var relativeIssueBodyPath = ToRelativeRepoPath(context.RepoRoot, issueBodyPath);
        var artifact = new IssuePublishArtifact
        {
            ExecutionUnit = executionUnit,
            PublishStatus = DraftedPublishStatus,
            PacketPath = relativePacketPath,
            IssueBodyPath = relativeIssueBodyPath,
            CreatedIssueNumber = null,
            CreatedIssueUrl = null,
            PublishedLabelName = null
        };

        var artifactPath = WriteArtifact(context.RepoRoot, artifact);
        AppendRunEvent(
            context,
            new RunEvent
            {
                Ts = TimestampFactory(),
                ExecutionUnit = executionUnit,
                Event = "issue-drafted",
                By = TransitionActor,
                PacketRef = relativePacketPath,
                ResultRef = artifactPath
            });

        return new IssueDraftCommandResult
        {
            Artifact = artifact,
            ArtifactPath = artifactPath
        };
    }

    internal static string ResolveIssueBodyPath(string packetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packetPath);

        var directoryPath = Path.GetDirectoryName(packetPath)
            ?? throw new InvalidOperationException("Packet path did not contain a directory.");

        return Path.Combine(directoryPath, "github-body.md");
    }

    private static string WriteArtifact(string repoRoot, IssuePublishArtifact artifact)
    {
        var relativePath = IssuePublishArtifactPathResolver.Resolve(artifact.ExecutionUnit);
        var absolutePath = Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException("Issue publish artifact path did not contain a directory.");

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, IssuePublishArtifactYaml.Serialize(artifact));

        return relativePath;
    }

    private static void AppendRunEvent(CliContext context, RunEvent runEvent)
    {
        var runLogPath = context.GetRunLogPath();
        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");

        Directory.CreateDirectory(runLogDirectory);
        File.AppendAllText(runLogPath, RunLogSerializer.SerializeLine(runEvent) + Environment.NewLine);
    }

    private static string ToRelativeRepoPath(string repoRoot, string absolutePath)
    {
        return Path.GetRelativePath(repoRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
    }
}

internal sealed record IssueDraftCommandResult
{
    public required IssuePublishArtifact Artifact { get; init; }

    public required string ArtifactPath { get; init; }
}
