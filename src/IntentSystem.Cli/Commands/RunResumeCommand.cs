using IntentSystem.Projection.Serialization;
using IntentSystem.Review;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class RunResumeCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Run resume command requires an execution unit.");
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

        if (queueItem.State is not (QueueItemState.Active or QueueItemState.Fixing))
        {
            writer.WriteLine(
                $"Execution unit '{executionUnit}' must be active or fixing before run resume.");
            return 1;
        }

        if (queueItem.LinkedIssue is null)
        {
            writer.WriteLine($"Execution unit '{executionUnit}' must have a linked issue before run resume.");
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
            var packet = ProjectionPacketSerializer.Deserialize(File.ReadAllText(packetPath));
            var childRepoRef = packet.ImplementationIssuePacket.TargetRepo;
            if (string.IsNullOrWhiteSpace(childRepoRef))
            {
                throw new InvalidOperationException("Projection packet must contain a target repo.");
            }

            var childRepoPath = ResolveChildRepoPath(context.RepoRoot, childRepoRef);
            if (!Directory.Exists(childRepoPath))
            {
                throw new InvalidOperationException($"Child repo path was not found at {childRepoPath}");
            }

            var worktreePath = RunStartCommand.ResolveWorktreePath(context, executionUnit);
            if (!Directory.Exists(worktreePath))
            {
                throw new InvalidOperationException($"Worktree path was not found at {worktreePath}");
            }

            var branchName = RunStartCommand.ResolveBranchName(executionUnit, queueItem.LinkedIssue);
            var latestLinkedPr = ResolveLatestLinkedPr(context, executionUnit);

            WriteContext(
                writer,
                queueItem,
                worktreePath,
                childRepoPath,
                branchName,
                latestLinkedPr);

            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void WriteContext(
        TextWriter writer,
        QueueItem queueItem,
        string worktreePath,
        string childRepoPath,
        string branchName,
        string? latestLinkedPr)
    {
        writer.WriteLine($"Execution unit: {queueItem.ExecutionUnit}");
        writer.WriteLine($"State: {FormatState(queueItem.State)}");
        writer.WriteLine($"Worktree path: {worktreePath}");
        writer.WriteLine($"Child repo path: {childRepoPath}");
        writer.WriteLine($"Branch: {branchName}");
        writer.WriteLine($"Linked issue: {queueItem.LinkedIssue!.Url}");

        if (!string.IsNullOrWhiteSpace(latestLinkedPr))
        {
            writer.WriteLine($"Latest linked PR: {latestLinkedPr}");
        }
    }

    private static string? ResolveLatestLinkedPr(CliContext context, string executionUnit)
    {
        var runLogPath = context.GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            return null;
        }

        var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
        return LatestLinkedPrResolver.TryResolve(runEvents, executionUnit);
    }

    private static string ResolveChildRepoPath(string repoRoot, string childRepoRef)
    {
        return Path.IsPathRooted(childRepoRef)
            ? Path.GetFullPath(childRepoRef)
            : Path.GetFullPath(Path.Combine(repoRoot, childRepoRef));
    }

    private static string FormatState(QueueItemState state)
    {
        return state switch
        {
            QueueItemState.ClarifyBlocked => "clarify-blocked",
            _ => state.ToString().ToLowerInvariant()
        };
    }
}
