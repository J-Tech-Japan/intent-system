using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.Cli.Commands;

internal static class IntakeCompileRenderer
{
    public static string RenderMarkdown(IntakeCompileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lines = new List<string>
        {
            "# Intake Compile",
            string.Empty,
            "## Domain",
            string.Empty,
            $"`{request.Domain}`",
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

    public static void WriteNotReady(TextWriter writer, string domain, InterviewQueueItem nextQuestion)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(nextQuestion);

        writer.WriteLine($"Intake compile is not ready for domain '{domain}'.");
        InterviewStartRenderer.Write(writer, domain, nextQuestion);
    }

    public static void WriteSummary(TextWriter writer, IntakeCompileRequest request, string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        writer.WriteLine($"Intake compile artifact generated for domain '{request.Domain}'.");
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
