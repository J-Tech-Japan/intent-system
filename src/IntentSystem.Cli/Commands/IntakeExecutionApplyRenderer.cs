namespace IntentSystem.Cli.Commands;

internal static class IntakeExecutionApplyRenderer
{
    public static void WriteSummary(TextWriter writer, IntakeExecutionApplyResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Intake execution apply completed for domain '{result.Domain}'.");
        writer.WriteLine($"Applied unit count: {result.AppliedUnitCount}");
        writer.WriteLine("Changed execution file paths:");
        WriteList(writer, result.ChangedFilePaths);
        writer.WriteLine("Preserved dependency refs:");
        WriteList(writer, result.PreservedDependencyRefs);
    }

    private static void WriteList(TextWriter writer, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            writer.WriteLine("- none");
            return;
        }

        foreach (var value in values)
        {
            writer.WriteLine($"- {value}");
        }
    }
}
