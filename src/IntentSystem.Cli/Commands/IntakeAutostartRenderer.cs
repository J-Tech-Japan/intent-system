namespace IntentSystem.Cli.Commands;

internal static class IntakeAutostartRenderer
{
    public static void WriteSummary(
        TextWriter writer,
        string executionUnit,
        string linkedIssueUrl,
        string worktreePath,
        string branchName)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(linkedIssueUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);

        writer.WriteLine($"Intake autostart completed for {executionUnit}.");
        writer.WriteLine($"Linked issue: {linkedIssueUrl}");
        writer.WriteLine($"Worktree path: {worktreePath}");
        writer.WriteLine($"Branch: {branchName}");
    }
}
