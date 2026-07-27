using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class QueueEnqueueCommand
{
    private const string TransitionActor = "intent-cli";
    private const string DefaultPriority = "high";

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Queue enqueue command requires an execution unit.");
            return 1;
        }

        var queueState = QueueCommandSupport.LoadQueueState(context, writer);
        if (queueState is null)
        {
            return 1;
        }

        var executionUnit = args[0].Trim();
        var packetPath = ResolvePacketPath(context, executionUnit);
        if (!File.Exists(packetPath))
        {
            writer.WriteLine($"Projection packet artifact was not found at {packetPath}");
            return 1;
        }

        try
        {
            var packet = ReadPacket(File.ReadAllText(packetPath), executionUnit);
            var packetPaths = ResolvePacketPaths(context.RepoRoot, executionUnit);
            var queueItem = CreateQueueItem(context, packet, packetPaths);
            var result = QueueManager.Enqueue(
                queueState,
                queueItem,
                TransitionActor,
                TimestampFactory());

            if (result.WasEnqueued)
            {
                PersistEnqueue(context, queueState, result);
            }

            QueueEnqueueRenderer.WriteSummary(writer, new QueueEnqueueCommandResult
            {
                ExecutionUnit = executionUnit,
                EnqueuedExecutionUnits = result.WasEnqueued ? [executionUnit] : [],
                PacketPaths =
                [
                    result.QueueItem.PacketPaths.Implementation,
                    result.QueueItem.PacketPaths.ReviewContext,
                    result.QueueItem.PacketPaths.Yaml
                ],
                SkippedUnits = result.WasEnqueued ? [] : [executionUnit]
            });

            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static string ResolvePacketPath(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        return Path.Combine(
            context.ResolveArtifactRootPath(),
            "issues",
            executionUnit,
            "packet.yaml");
    }

    internal static ResolvedQueuePacket ReadPacket(string yaml, string expectedExecutionUnit)
    {
        var packet = ProjectionPacketRuntimeReader.Read(yaml);

        if (!string.Equals(
                packet.ExecutionUnit,
                expectedExecutionUnit,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Projection packet execution unit '{packet.ExecutionUnit}' does not match requested execution unit '{expectedExecutionUnit}'.");
        }

        if (string.IsNullOrWhiteSpace(packet.IssueTitle))
        {
            throw new InvalidOperationException("Projection packet must contain a non-empty issue title.");
        }

        if (string.IsNullOrWhiteSpace(packet.ClarificationReturnPath))
        {
            throw new InvalidOperationException("Projection packet must contain a clarification return path.");
        }

        return new ResolvedQueuePacket
        {
            ExecutionUnit = packet.ExecutionUnit,
            IssueTitle = packet.IssueTitle,
            Dependencies = packet.Dependencies,
            ClarificationReturnPath = packet.ClarificationReturnPath
        };
    }

    internal static PacketPaths ResolvePacketPaths(string repoRoot, string executionUnit)
    {
        var issueDirectory = Path.Combine(
            repoRoot,
            CliRuntimeContracts.IntentCliDirectoryName,
            "issues",
            executionUnit);

        return new PacketPaths
        {
            Implementation = Path.GetRelativePath(repoRoot, Path.Combine(issueDirectory, "implementation.md"))
                .Replace(Path.DirectorySeparatorChar, '/'),
            ReviewContext = Path.GetRelativePath(repoRoot, Path.Combine(issueDirectory, "review-context.md"))
                .Replace(Path.DirectorySeparatorChar, '/'),
            Yaml = Path.GetRelativePath(repoRoot, Path.Combine(issueDirectory, "packet.yaml"))
                .Replace(Path.DirectorySeparatorChar, '/')
        };
    }

    internal static QueueItem CreateQueueItem(
        CliContext context,
        ResolvedQueuePacket packet,
        PacketPaths packetPaths)
    {
        return new QueueItem
        {
            ExecutionUnit = packet.ExecutionUnit,
            Title = packet.IssueTitle,
            State = QueueItemState.Queued,
            Dependencies = packet.Dependencies,
            BlockedBy = [],
            ClarificationReturnPath = packet.ClarificationReturnPath,
            PacketPaths = packetPaths,
            LinkedIssue = null,
            WorkerRole = context.Config.Roles.Implement,
            ReviewRole = context.Config.Roles.Review,
            Priority = DefaultPriority
        };
    }

    internal static void PersistEnqueue(CliContext context, QueueState baseState, QueueEnqueueResult result)
    {
        var queueStatePath = context.GetQueueStatePath();
        // G548: guarded write (no-item-loss + stale-base re-application).
        QueueStatePersistence.Persist(queueStatePath, baseState, result.UpdatedState);

        if (result.Event is null)
        {
            return;
        }

        var runLogPath = context.GetRunLogPath();
        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");
        Directory.CreateDirectory(runLogDirectory);
        File.AppendAllText(
            runLogPath,
            RunLogSerializer.SerializeLine(result.Event) + Environment.NewLine);
    }

    internal sealed record ResolvedQueuePacket
    {
        public required string ExecutionUnit { get; init; }

        public required string IssueTitle { get; init; }

        public required IReadOnlyList<string> Dependencies { get; init; }

        public required string ClarificationReturnPath { get; init; }
    }
}
