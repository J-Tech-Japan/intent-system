namespace IntentSystem.Cli.Commands;

internal static class IntakeExecutionRenderer
{
    public static string RenderMarkdown(IntakeExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lines = new List<string>
        {
            "# Intake Execution Draft",
            string.Empty,
            "## Domain",
            string.Empty,
            $"`{request.Domain}`",
            string.Empty,
            "## Proposed Execution Units",
            string.Empty
        };

        foreach (var candidate in request.ProposedExecutionUnits)
        {
            lines.Add($"### `{candidate.ExecutionUnitId}`");
            lines.Add(string.Empty);
            lines.Add($"source_file_path: {candidate.SourceFilePath}");
            lines.Add($"target_part: {candidate.TargetPart}");
            AppendList(lines, "dependencies", candidate.Dependencies);
            AppendList(lines, "readiness_notes", candidate.ReadinessNotes);
            AppendList(lines, "verification_hints", candidate.VerificationHints);
            lines.Add(string.Empty);
        }

        if (request.ProposedExecutionUnits.Count > 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static void WriteSummary(TextWriter writer, IntakeExecutionRequest request, string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        writer.WriteLine($"Intake execution draft generated for domain '{request.Domain}'.");
        writer.WriteLine($"Artifact path: {artifactPath}");
        writer.WriteLine($"Proposed execution units: {request.ProposedExecutionUnits.Count}");
        writer.WriteLine(
            $"Dependencies: {request.ProposedExecutionUnits.Sum(candidate => candidate.Dependencies.Count)}");
        writer.WriteLine(
            $"Verification hints: {request.ProposedExecutionUnits.Sum(candidate => candidate.VerificationHints.Count)}");
    }

    private static void AppendList(List<string> lines, string label, IReadOnlyList<string> values)
    {
        lines.Add($"{label}:");
        if (values.Count == 0)
        {
            lines.Add("- none");
            return;
        }

        lines.AddRange(values.Select(value => $"- {value}"));
    }
}
