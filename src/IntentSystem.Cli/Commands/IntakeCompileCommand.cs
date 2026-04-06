using IntentSystem.ConceptIntake.Interview;
using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.Cli.Commands;

internal static class IntakeCompileCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Intake compile command requires a domain.");
            return 1;
        }

        var domain = args[0];

        try
        {
            var result = ExecuteCore(context.RepoRoot, domain);
            if (!result.IsReady)
            {
                if (result.NextQuestion is null)
                {
                    IntakeCompileRenderer.WriteNoArtifactsNotReady(writer, domain);
                }
                else
                {
                    IntakeCompileRenderer.WriteNotReady(writer, domain, result.NextQuestion);
                }

                return 0;
            }

            IntakeCompileRenderer.WriteSummary(writer, result.Request!, result.ArtifactPath!);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static IntakeCompileCoreResult ExecuteCore(string repoRoot, string domain)
    {
        var artifacts = InterviewArtifactYaml.Discover(repoRoot, domain);
        if (artifacts.Count == 0)
        {
            return new IntakeCompileCoreResult
            {
                Domain = domain,
                IsReady = false,
                Request = null,
                ArtifactPath = null,
                NextQuestion = null
            };
        }

        var items = artifacts.Select(artifact => artifact.Item).ToArray();
        var nextQuestion = InterviewQueue.GetNextPendingForDomain(items, domain);
        if (nextQuestion is not null)
        {
            return new IntakeCompileCoreResult
            {
                Domain = domain,
                IsReady = false,
                Request = null,
                ArtifactPath = null,
                NextQuestion = nextQuestion
            };
        }

        var request = CreateRequest(domain, items);
        var markdown = IntakeCompileRenderer.RenderMarkdown(request);
        var artifactPath = IntakeCompileArtifactWriter.Write(markdown, domain, repoRoot);
        return new IntakeCompileCoreResult
        {
            Domain = domain,
            IsReady = true,
            Request = request,
            ArtifactPath = artifactPath,
            NextQuestion = null
        };
    }

    private static IntakeCompileRequest CreateRequest(string domain, IReadOnlyList<InterviewQueueItem> items)
    {
        var answeredItems = items
            .Where(item => item.Status == InterviewQueueItemStatus.Answered)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.QuestionId, StringComparer.Ordinal)
            .ToArray();

        return new IntakeCompileRequest
        {
            Domain = domain,
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
                .ToArray(),
            SourceConceptRefs = answeredItems
                .Select(item => item.SourceConceptRef)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray()
        };
    }
}
