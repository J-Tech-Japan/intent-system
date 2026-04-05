using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.Cli.Commands;

internal static class InterviewStartRenderer
{
    public static void Write(TextWriter writer, string domain, InterviewQueueItem? item)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        if (item is null)
        {
            writer.WriteLine($"No open interview questions found for domain '{domain}'.");
            return;
        }

        writer.WriteLine("Next interview question:");
        writer.WriteLine($"Domain: {item.DomainSlug}");
        writer.WriteLine($"Question: {item.QuestionText}");
        writer.WriteLine($"Reason: {item.Reason}");
        writer.WriteLine($"Affects: {string.Join(", ", item.Affects)}");
        writer.WriteLine($"Blocking mode: {item.BlockingOrNonblocking}");
        writer.WriteLine($"Return paths: {string.Join(", ", item.ReturnToIntentPaths)}");
        writer.WriteLine($"Question id: {item.QuestionId}");
    }
}
