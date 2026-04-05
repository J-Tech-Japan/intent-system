using IntentSystem.ConceptIntake.Interview;
using IntentSystem.ConceptIntake.Models;
using IntentSystem.ConceptIntake.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class InterviewStartCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Interview start command requires a domain.");
            return 1;
        }

        var domain = args[0];

        try
        {
            var items = DiscoverInterviewArtifacts(context.RepoRoot, domain);
            var nextQuestion = InterviewQueue.GetNextPendingForDomain(items, domain);
            InterviewStartRenderer.Write(writer, domain, nextQuestion);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static IReadOnlyList<InterviewQueueItem> DiscoverInterviewArtifacts(string repoRoot, string domain)
    {
        var interviewsRoot = Path.Combine(
            repoRoot,
            ".intent-cli",
            "interviews",
            domain.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(interviewsRoot))
        {
            return [];
        }

        var items = new List<InterviewQueueItem>();
        foreach (var artifactPath in Directory.EnumerateFiles(interviewsRoot, "*.json", SearchOption.AllDirectories))
        {
            var item = InterviewQueueSerializer.Deserialize(File.ReadAllText(artifactPath));
            if (!string.Equals(item.DomainSlug, domain, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Interview artifact domain '{item.DomainSlug}' must match requested domain '{domain}'.");
            }

            items.Add(item);
        }

        return items;
    }
}
