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

        try
        {
            var result = ExecuteCore(context, args[0].Trim());
            if (result.ReusedExistingIssue)
            {
                writer.WriteLine($"Queue item {result.ExecutionUnit} reused existing linked issue {result.LinkedIssueUrl}.");
            }
            else
            {
                writer.WriteLine($"Queue item {result.ExecutionUnit} dispatched to {result.LinkedIssueUrl}.");
            }

            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static QueueDispatchCommandResult ExecuteCore(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var queueStatePath = context.GetQueueStatePath();
        var queueState = QueueCommandSupport.LoadQueueState(context, TextWriter.Null);
        if (queueState is null)
        {
            throw new InvalidOperationException($"No queue state found at {queueStatePath}");
        }

        var queueItem = queueState.Items.FirstOrDefault(item =>
            string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));

        if (queueItem is null)
        {
            throw new InvalidOperationException($"Execution unit '{executionUnit}' was not found in queue state.");
        }

        if (queueItem.LinkedIssue is not null)
        {
            PersistRunEvent(
                context,
                new IntentSystem.Supervisor.Models.RunEvent
                {
                    Ts = TimestampFactory(),
                    ExecutionUnit = executionUnit,
                    Event = "issue-reused",
                    By = TransitionActor,
                    LinkedIssue = queueItem.LinkedIssue.Url
                });

            return new QueueDispatchCommandResult
            {
                ExecutionUnit = executionUnit,
                LinkedIssueUrl = queueItem.LinkedIssue.Url,
                ReusedExistingIssue = true
            };
        }

        var packetPath = ResolveArtifactPath(context.RepoRoot, queueItem.PacketPaths.Yaml);
        if (!File.Exists(packetPath))
        {
            throw new InvalidOperationException($"Projection packet artifact was not found at {packetPath}");
        }

        var githubBodyPath = ResolveGitHubBodyPath(context.RepoRoot, queueItem.PacketPaths.Yaml);
        if (!File.Exists(githubBodyPath))
        {
            throw new InvalidOperationException($"GitHub issue body artifact was not found at {githubBodyPath}");
        }

        var packet = ProjectionPacketRuntimeReader.Read(File.ReadAllText(packetPath));
        var packetTargetRepo = packet.TargetRepo;
        if (string.IsNullOrWhiteSpace(packetTargetRepo))
        {
            throw new InvalidOperationException("Projection packet must contain a target repo.");
        }

        ChildWorkTargetGuard.EnsureTargetAllowed(
            executionUnit,
            context.RepoRoot,
            packet.TargetRepo,
            Path.GetFullPath(Path.Combine(context.ResolveWorktreeRootPath(), executionUnit)),
            packet.TargetPath,
            packet.TargetPart);

        var issueTitle = packet.IssueTitle;
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

        return new QueueDispatchCommandResult
        {
            ExecutionUnit = executionUnit,
            LinkedIssueUrl = linkedIssue.Url,
            ReusedExistingIssue = false
        };
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

        PersistRunEvent(context, result.Event);
    }

    private static void PersistRunEvent(CliContext context, IntentSystem.Supervisor.Models.RunEvent runEvent)
    {
        var runLogPath = context.GetRunLogPath();
        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");
        Directory.CreateDirectory(runLogDirectory);
        File.AppendAllText(
            runLogPath,
            RunLogSerializer.SerializeLine(runEvent) + Environment.NewLine);
    }
}
