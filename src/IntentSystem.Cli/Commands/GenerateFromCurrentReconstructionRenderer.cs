namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentReconstructionRenderer
{
    public static string RenderInterviewMarkdown(
        string domain,
        IReadOnlyList<string> selectedAltitudes,
        IReadOnlyList<string> rootNearIntentCandidates,
        IReadOnlyList<string> executionNearUpdateCandidates,
        IReadOnlyList<string> confidenceByAltitude,
        IReadOnlyList<string> sourceConceptRefs,
        IReadOnlyList<string> recommendedFollowUpQuestions,
        IReadOnlyList<string> returnToIntentPaths,
        IReadOnlyList<string> gaps)
    {
        var lines = new List<string>
        {
            "# Reconstructed Interview",
            string.Empty,
            "## Domain",
            string.Empty,
            $"`{domain}`",
            string.Empty
        };

        AppendSection(lines, "selected_altitudes", selectedAltitudes);
        AppendSection(lines, "root_near_intent_candidates", rootNearIntentCandidates);
        AppendSection(lines, "execution_near_update_candidates", executionNearUpdateCandidates);
        AppendSection(lines, "confidence_by_altitude", confidenceByAltitude);
        AppendSection(lines, "source_concept_refs", sourceConceptRefs);
        AppendSection(lines, "recommended_follow_up_questions", recommendedFollowUpQuestions);
        AppendSection(lines, "return_to_intent_paths", returnToIntentPaths);
        AppendSection(lines, "gaps", gaps);

        return string.Join(Environment.NewLine, lines);
    }

    public static void WriteSummary(TextWriter writer, GenerateFromCurrentReconstructionResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Generate-from-current reconstruction processed for domain '{result.Domain}'.");
        writer.WriteLine($"Reconstructed concept artifact: {result.ConceptArtifactPath}");
        writer.WriteLine($"Reconstructed interview artifact: {result.InterviewArtifactPath}");
        writer.WriteLine("Selected altitudes:");
        WriteList(writer, result.SelectedAltitudes);
        writer.WriteLine("Candidate intent nodes:");
        WriteList(writer, result.CandidateIntentNodes);
        writer.WriteLine("Candidate execution units:");
        WriteList(writer, result.CandidateExecutionUnits);
        writer.WriteLine("Confidence by altitude:");
        WriteList(writer, result.ConfidenceByAltitude);
        writer.WriteLine("Source concept refs:");
        WriteList(writer, result.SourceConceptRefs);
        writer.WriteLine("Recommended follow-up interview questions:");
        WriteList(writer, result.RecommendedFollowUpQuestions);
        writer.WriteLine("Return-to-intent paths:");
        WriteList(writer, result.ReturnToIntentPaths);
        writer.WriteLine("Gaps:");
        WriteList(writer, result.Gaps);
    }

    private static void AppendSection(List<string> lines, string title, IReadOnlyList<string> values)
    {
        lines.Add($"{title}:");
        if (values.Count == 0)
        {
            lines.Add("- none");
        }
        else
        {
            lines.AddRange(values.Select(value => $"- {value}"));
        }

        lines.Add(string.Empty);
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
