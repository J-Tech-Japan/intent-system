namespace IntentSystem.Cli.Commands;

internal static class IntentInitRenderer
{
    public static void WriteMarkdown(TextWriter writer, IntentInitResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"# Intent init — domain '{result.Domain}'");
        writer.WriteLine();
        writer.WriteLine($"- Host repo root: `{result.HostRepoRoot}`");
        writer.WriteLine($"- Target repo: {(string.IsNullOrWhiteSpace(result.TargetRepo) ? "_unspecified_" : "`" + result.TargetRepo + "`")}");
        writer.WriteLine($"- Mode: {(result.WriteApplied ? "write" : "dry-run")}");
        writer.WriteLine();

        writer.WriteLine("## Planned paths");
        WriteList(writer, result.PlannedPaths);

        writer.WriteLine();
        writer.WriteLine("## Written paths");
        WriteList(writer, result.WrittenPaths);

        writer.WriteLine();
        writer.WriteLine("## Existing paths (preserved)");
        WriteList(writer, result.ExistingPaths);

        writer.WriteLine();
        writer.WriteLine("## Next steps");
        foreach (var step in result.NextSteps)
        {
            writer.WriteLine($"- {step}");
        }
    }

    private static void WriteList(TextWriter writer, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            writer.WriteLine("- _none_");
            return;
        }

        foreach (var value in values)
        {
            writer.WriteLine($"- `{value}`");
        }
    }
}
