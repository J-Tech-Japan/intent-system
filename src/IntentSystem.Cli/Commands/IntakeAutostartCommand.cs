using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class IntakeAutostartCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Intake autostart command requires an execution unit.");
            return 1;
        }

        var executionUnit = args[0];
        var dispatchWriter = new StringWriter();
        var dispatchExitCode = QueueDispatchCommand.Execute(context, [executionUnit], dispatchWriter);
        if (dispatchExitCode != 0)
        {
            writer.Write(dispatchWriter.ToString());
            return dispatchExitCode;
        }

        var startWriter = new StringWriter();
        var startExitCode = RunStartCommand.Execute(context, [executionUnit], startWriter);
        if (startExitCode != 0)
        {
            writer.Write(dispatchWriter.ToString());
            writer.Write(startWriter.ToString());
            return startExitCode;
        }

        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(context.GetQueueStatePath()));
        var queueItem = queueState.Items.FirstOrDefault(item =>
            string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));

        if (queueItem?.LinkedIssue is null)
        {
            writer.WriteLine($"Execution unit '{executionUnit}' must have a linked issue after intake autostart.");
            return 1;
        }

        var worktreePath = RunStartCommand.ResolveWorktreePath(context, executionUnit);
        var branchName = RunStartCommand.ResolveBranchName(executionUnit, queueItem.LinkedIssue);
        IntakeAutostartRenderer.WriteSummary(
            writer,
            executionUnit,
            queueItem.LinkedIssue.Url,
            worktreePath,
            branchName);
        return 0;
    }
}
