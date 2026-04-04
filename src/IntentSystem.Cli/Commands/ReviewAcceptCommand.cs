using IntentSystem.Projection.Serialization;
using IntentSystem.Review;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class ReviewAcceptCommand
{
    private const string TransitionActor = "intent-cli";

    public static Func<IReviewAcceptClient> AcceptClientFactory { get; set; } = () => new GhReviewAcceptClient();

    public static Func<IGitCommandRunner> GitCommandRunnerFactory { get; set; } = () => new GitCommandRunner();

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Review accept command requires an execution unit.");
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

        var runLogPath = context.GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            writer.WriteLine($"Run log was not found at {runLogPath}");
            return 1;
        }

        var packetPath = Path.Combine(
            context.RepoRoot,
            queueItem.PacketPaths.Yaml.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(packetPath))
        {
            writer.WriteLine($"Projection packet artifact was not found at {packetPath}");
            return 1;
        }

        try
        {
            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            var linkedPr = LatestLinkedPrResolver.Resolve(runEvents, executionUnit);
            var linkedIssue = LatestLinkedIssueResolver.Resolve(runEvents, executionUnit);
            var packet = ProjectionPacketSerializer.Deserialize(File.ReadAllText(packetPath));
            var childRepoRef = packet.ImplementationIssuePacket.TargetRepo;
            if (string.IsNullOrWhiteSpace(childRepoRef))
            {
                throw new InvalidOperationException("Projection packet must contain a target repo.");
            }

            var acceptClient = AcceptClientFactory();
            var mergedCommitSha = acceptClient.MergePullRequest(linkedPr);
            acceptClient.CloseIssue(linkedIssue);

            var gitRunner = GitCommandRunnerFactory();
            ChildRepoMainSynchronizer.Sync(context.RepoRoot, childRepoRef, mergedCommitSha, gitRunner);
            ParentSubmodulePointerUpdater.Stage(context.RepoRoot, childRepoRef, gitRunner);

            var timestamp = TimestampFactory();
            var transition = QueueManager.AcceptReview(queueState, executionUnit, TransitionActor, timestamp);
            PersistCloseout(
                context,
                transition.UpdatedState,
                CreateCloseoutEvents(executionUnit, linkedPr, linkedIssue, timestamp));

            writer.WriteLine($"Review accepted for {executionUnit}.");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static IReadOnlyList<RunEvent> CreateCloseoutEvents(
        string executionUnit,
        string linkedPr,
        string linkedIssue,
        DateTimeOffset timestamp)
    {
        return
        [
            new RunEvent
            {
                Ts = timestamp,
                ExecutionUnit = executionUnit,
                Event = "pr-merged",
                By = TransitionActor,
                LinkedPr = linkedPr
            },
            new RunEvent
            {
                Ts = timestamp,
                ExecutionUnit = executionUnit,
                Event = "issue-closed",
                By = TransitionActor,
                LinkedIssue = linkedIssue
            },
            new RunEvent
            {
                Ts = timestamp,
                ExecutionUnit = executionUnit,
                Event = "completed",
                By = TransitionActor
            }
        ];
    }

    private static void PersistCloseout(CliContext context, QueueState updatedState, IReadOnlyList<RunEvent> events)
    {
        var queueStatePath = context.GetQueueStatePath();
        File.WriteAllText(queueStatePath, QueueStateSerializer.Serialize(updatedState));

        var runLogPath = context.GetRunLogPath();
        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");
        Directory.CreateDirectory(runLogDirectory);

        foreach (var runEvent in events)
        {
            File.AppendAllText(
                runLogPath,
                RunLogSerializer.SerializeLine(runEvent) + Environment.NewLine);
        }
    }
}
