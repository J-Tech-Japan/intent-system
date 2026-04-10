namespace IntentSystem.Cli.Commands;

internal static class RunCommandRenderer
{
    public static void WriteSummary(TextWriter writer, RunCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine("Run orchestration processed.");
        writer.WriteLine($"Stop reason: {result.StopReason}");
        writer.WriteLine(
            $"Touched execution units: {FormatListOrNone(result.TouchedExecutionUnits)}");
        writer.WriteLine(
            $"Reused child command refs: {FormatListOrNone(result.ReusedChildCommandRefs)}");
        if (!string.IsNullOrWhiteSpace(result.ExecutionUnit))
        {
            writer.WriteLine($"Execution unit: {result.ExecutionUnit}");
        }

        if (!string.IsNullOrWhiteSpace(result.Detail))
        {
            writer.WriteLine($"Detail: {result.Detail}");
        }

        if (!string.IsNullOrWhiteSpace(result.ArtifactPath))
        {
            writer.WriteLine($"Root run result artifact: {result.ArtifactPath}");
        }

        writer.WriteLine($"Actions executed: {result.Actions.Count}");
        foreach (var action in result.Actions)
        {
            writer.WriteLine($"- {action.Name} {action.ExecutionUnit}");
        }
    }

    private static string FormatListOrNone(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return values.Count == 0 ? "none" : string.Join(", ", values);
    }
}
