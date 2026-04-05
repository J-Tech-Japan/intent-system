using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.Cli.Commands;

internal static class InterviewAnswerRenderer
{
    public static void Write(TextWriter writer, string domain, InterviewAnswerResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Interview answered for domain '{domain}'.");
        writer.WriteLine($"Question id: {result.AnsweredItem.QuestionId}");
        writer.WriteLine($"Status: {result.AnsweredItem.Status}");

        if (result.RecommendedUpdates.Count == 0)
        {
            writer.WriteLine("Recommended updates: none");
        }
        else
        {
            writer.WriteLine("Recommended updates:");
            foreach (var update in result.RecommendedUpdates)
            {
                writer.WriteLine($"- {update}");
            }
        }

        writer.WriteLine($"Return paths: {string.Join(", ", result.ReturnToIntentPaths)}");
    }
}
