using IntentSystem.Projection.Serialization;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class QueueDispatchCommand
{
    private const string TransitionActor = "intent-cli";

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
            writer.WriteLine("Queue dispatch command requires an execution unit.");
            return 1;
        }

        var queueState = QueueCommandSupport.LoadQueueState(context, writer);
        if (queueState is null)
        {
            return 1;
        }

        var executionUnit = args[0];
        var queueItem = queueState.Items.FirstOrDefault(item =>
            string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));

        if (queueItem is null)
        {
            writer.WriteLine($"Execution unit '{executionUnit}' was not found in queue state.");
            return 1;
        }

        var packetPath = ResolveArtifactPath(context.RepoRoot, queueItem.PacketPaths.Yaml);
        if (!File.Exists(packetPath))
        {
            writer.WriteLine($"Projection packet artifact was not found at {packetPath}");
            return 1;
        }

        var githubBodyPath = ResolveGitHubBodyPath(context.RepoRoot, queueItem.PacketPaths.Yaml);
        if (!File.Exists(githubBodyPath))
        {
            writer.WriteLine($"GitHub issue body artifact was not found at {githubBodyPath}");
            return 1;
        }

        try
        {
            var packet = ProjectionPacketSerializer.Deserialize(File.ReadAllText(packetPath));
            var packetTargetRepo = packet.ImplementationIssuePacket.TargetRepo;
            if (string.IsNullOrWhiteSpace(packetTargetRepo))
            {
                throw new InvalidOperationException("Projection packet must contain a target repo.");
            }

            var issueTitle = packet.ImplementationIssuePacket.IssueTitle;
            if (string.IsNullOrWhiteSpace(issueTitle))
            {
                throw new InvalidOperationException("Projection packet must contain a non-empty issue title.");
            }

            var body = File.ReadAllText(githubBodyPath);
            var githubTargetRepo = GitHubRepositoryTargetResolver.Resolve(
                context.RepoRoot,
                packetTargetRepo,
                GitCommandRunnerFactory());
            var linkedIssue = PublisherFactory().CreateIssue(githubTargetRepo, issueTitle, body);
            var result = QueueManager.LinkIssue(
                queueState,
                executionUnit,
                linkedIssue,
                TransitionActor,
                TimestampFactory());

            PersistDispatch(context, result);

            writer.WriteLine($"Queue item {executionUnit} dispatched to {linkedIssue.Url}.");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static string ResolveArtifactPath(string repoRoot, string artifactRef)
    {
        return Path.GetFullPath(Path.Combine(repoRoot, artifactRef.Replace('/', Path.DirectorySeparatorChar)));
    }

    internal static string ResolveGitHubBodyPath(string repoRoot, string packetYamlRef)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(packetYamlRef);

        var directoryRef = Path.GetDirectoryName(packetYamlRef.Replace('/', Path.DirectorySeparatorChar))
            ?? throw new InvalidOperationException("Packet YAML ref did not contain a directory.");

        return Path.GetFullPath(Path.Combine(repoRoot, directoryRef, "github-body.md"));
    }

    private static void PersistDispatch(CliContext context, QueueTransitionResult result)
    {
        var queueStatePath = context.GetQueueStatePath();
        File.WriteAllText(queueStatePath, QueueStateSerializer.Serialize(result.UpdatedState));

        var runLogPath = context.GetRunLogPath();
        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");
        Directory.CreateDirectory(runLogDirectory);
        File.AppendAllText(
            runLogPath,
            RunLogSerializer.SerializeLine(result.Event) + Environment.NewLine);
    }
}
