namespace IntentSystem.Cli.Commands;

internal static class RunCommandRenderer
{
    public static void WriteSummary(TextWriter writer, RunCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine("Run orchestration processed.");
        writer.WriteLine($"Stop reason: {result.StopReason}");
        if (!string.IsNullOrWhiteSpace(result.ExecutionUnit))
        {
            writer.WriteLine($"Execution unit: {result.ExecutionUnit}");
        }

        if (!string.IsNullOrWhiteSpace(result.Detail))
        {
            writer.WriteLine($"Detail: {result.Detail}");
        }

        writer.WriteLine($"Actions executed: {result.Actions.Count}");
        foreach (var action in result.Actions)
        {
            writer.WriteLine($"- {action.Name} {action.ExecutionUnit}");
        }
    }
}
