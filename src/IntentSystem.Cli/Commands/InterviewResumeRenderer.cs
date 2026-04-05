namespace IntentSystem.Cli.Commands;

internal static class InterviewResumeRenderer
{
    public static void Write(TextWriter writer, InterviewResumeResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.Domain);

        if (result.NextQuestion is not null)
        {
            InterviewStartRenderer.Write(writer, result.Domain, result.NextQuestion);
            return;
        }

        if (!result.HasArtifacts)
        {
            writer.WriteLine($"No interview artifacts found for domain '{result.Domain}'.");
            return;
        }

        if (result.AnsweredQuestionIds.Count == 0)
        {
            writer.WriteLine($"No open interview questions or fold-in-ready answers found for domain '{result.Domain}'.");
            return;
        }

        writer.WriteLine("Interview fold-in-ready summary:");
        writer.WriteLine($"Domain: {result.Domain}");
        WriteList(writer, "answered_question_ids", result.AnsweredQuestionIds);
        WriteList(writer, "recommended_updates", result.RecommendedUpdates);
        WriteList(writer, "return_to_intent_paths", result.ReturnToIntentPaths);
    }

    private static void WriteList(TextWriter writer, string label, IReadOnlyList<string> values)
    {
        writer.WriteLine($"{label}:");
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
