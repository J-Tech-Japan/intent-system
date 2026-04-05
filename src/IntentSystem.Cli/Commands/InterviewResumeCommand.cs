using IntentSystem.ConceptIntake.Interview;
using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.Cli.Commands;

internal static class InterviewResumeCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Interview resume command requires a domain.");
            return 1;
        }

        var domain = args[0];

        try
        {
            var artifacts = InterviewArtifactYaml.Discover(context.RepoRoot, domain);
            var result = CreateResult(domain, artifacts);
            InterviewResumeRenderer.Write(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static InterviewResumeResult CreateResult(
        string domain,
        IReadOnlyList<InterviewArtifactYaml.ArtifactFile> artifacts)
    {
        var items = artifacts.Select(artifact => artifact.Item).ToArray();
        var nextQuestion = InterviewQueue.GetNextPendingForDomain(items, domain);
        var answeredItems = items
            .Where(item => item.Status == InterviewQueueItemStatus.Answered)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.QuestionId, StringComparer.Ordinal)
            .ToArray();

        return new InterviewResumeResult
        {
            Domain = domain,
            HasArtifacts = artifacts.Count > 0,
            NextQuestion = nextQuestion,
            AnsweredQuestionIds = answeredItems
                .Select(item => item.QuestionId)
                .ToArray(),
            RecommendedUpdates = answeredItems
                .SelectMany(item => item.RecommendedUpdates ?? [])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(update => update, StringComparer.Ordinal)
                .ToArray(),
            ReturnToIntentPaths = answeredItems
                .SelectMany(item => item.ReturnToIntentPaths)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray()
        };
    }
}
