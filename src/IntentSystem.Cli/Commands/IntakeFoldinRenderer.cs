namespace IntentSystem.Cli.Commands;

internal static class IntakeFoldinRenderer
{
    public static string RenderMarkdown(IntakeFoldinRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lines = new List<string>
        {
            "# Intake Fold-In Draft",
            string.Empty,
            "## Domain",
            string.Empty,
            $"`{request.Domain}`",
            string.Empty,
            "## Interview Coverage",
            string.Empty,
            "## Parent Source-Of-Truth Update Candidates",
            string.Empty,
            "The following items are compile-derived update candidates for the parent source of truth.",
            string.Empty
        };

        AppendList(lines, "answered_question_ids", request.AnsweredQuestionIds);
        lines.Add(string.Empty);
        AppendList(lines, "recommended_updates", request.RecommendedUpdates);
        lines.Add(string.Empty);
        AppendList(lines, "return_to_intent_paths", request.ReturnToIntentPaths);
        lines.Add(string.Empty);
        AppendList(lines, "source_concept_refs", request.SourceConceptRefs);

        return string.Join(Environment.NewLine, lines);
    }

    public static void WriteSummary(TextWriter writer, IntakeFoldinRequest request, string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        writer.WriteLine($"Intake fold-in draft generated for domain '{request.Domain}'.");
        writer.WriteLine($"Artifact path: {artifactPath}");
        writer.WriteLine($"Answered questions: {request.AnsweredQuestionIds.Count}");
        writer.WriteLine($"Recommended updates: {request.RecommendedUpdates.Count}");
        writer.WriteLine($"Return paths: {request.ReturnToIntentPaths.Count}");
        writer.WriteLine($"Source concept refs: {request.SourceConceptRefs.Count}");
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
