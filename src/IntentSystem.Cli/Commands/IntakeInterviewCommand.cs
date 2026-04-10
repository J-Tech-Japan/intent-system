using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.Cli.Commands;

internal static class IntakeInterviewCommand
{
    internal static readonly DateTimeOffset CreatedAtBase = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context.RepoRoot, args);
            IntakeInterviewRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static IntakeInterviewResult ExecuteCore(string repoRoot, string[] args)
    {
        var domain = ParseDomain(args);
        var conceptArtifactRelativePath = IntakeConceptArtifactPathResolver.Resolve(domain);
        var conceptArtifactPath = Path.Combine(
            repoRoot,
            conceptArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(conceptArtifactPath))
        {
            throw new InvalidOperationException($"Intake concept artifact was not found at {conceptArtifactPath}");
        }

        var conceptPacket = IntakeConceptArtifactYaml.Deserialize(File.ReadAllText(conceptArtifactPath));
        if (!string.Equals(conceptPacket.DomainSlug, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Intake concept artifact domain '{conceptPacket.DomainSlug}' must match requested domain '{domain}'.");
        }

        var interviewsRoot = ResolveInterviewsRoot(repoRoot, domain);
        var existingArtifactPaths = DiscoverExistingArtifacts(repoRoot, interviewsRoot);
        if (existingArtifactPaths.Count > 0)
        {
            return new IntakeInterviewResult
            {
                Domain = domain,
                ConceptArtifactPath = conceptArtifactRelativePath,
                WasSkipped = true,
                GeneratedArtifactPaths = [],
                ExistingArtifactPaths = existingArtifactPaths,
                CreatedQuestionIds = []
            };
        }

        var generatedArtifactPaths = new List<string>();
        var createdQuestionIds = new List<string>();
        var questions = BuildBootstrapQuestions(conceptPacket, conceptArtifactRelativePath);

        foreach (var question in questions.Select((value, index) => (value, index)))
        {
            var item = new InterviewQueueItem
            {
                DomainSlug = domain,
                SourceConceptRef = conceptArtifactRelativePath,
                QuestionId = question.value.QuestionId,
                QuestionText = question.value.QuestionText,
                Reason = question.value.Reason,
                Affects = question.value.Affects,
                BlockingOrNonblocking = question.value.BlockingOrNonblocking,
                Status = InterviewQueueItemStatus.Open,
                ReturnToIntentPaths = conceptPacket.UpstreamPaths,
                CreatedAt = CreatedAtBase.AddMinutes(question.index),
                Answer = null
            };

            generatedArtifactPaths.AddRange(
                InterviewArtifactFileWriter.Write(repoRoot, item, [], conceptPacket.KnownUnknowns));
            createdQuestionIds.Add(item.QuestionId);
        }

        return new IntakeInterviewResult
        {
            Domain = domain,
            ConceptArtifactPath = conceptArtifactRelativePath,
            WasSkipped = false,
            GeneratedArtifactPaths = generatedArtifactPaths,
            ExistingArtifactPaths = [],
            CreatedQuestionIds = createdQuestionIds
        };
    }

    private static string ParseDomain(string[] args)
    {
        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Intake interview command requires a domain.");
        }

        return args[0].Trim();
    }

    private static string ResolveInterviewsRoot(string repoRoot, string domain)
    {
        return Path.Combine(
            repoRoot,
            ".intent-cli",
            "interviews",
            domain.Replace('/', Path.DirectorySeparatorChar));
    }

    private static IReadOnlyList<string> DiscoverExistingArtifacts(string repoRoot, string interviewsRoot)
    {
        if (!Directory.Exists(interviewsRoot))
        {
            return [];
        }

        return Directory.EnumerateFiles(interviewsRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .ToArray();
    }

    private static IReadOnlyList<BootstrapQuestion> BuildBootstrapQuestions(
        ConceptIntakePacket conceptPacket,
        string conceptArtifactRelativePath)
    {
        var domain = conceptPacket.DomainSlug;
        var initialGoalPreview = conceptPacket.InitialGoal;
        var constraintPreview = FormatPreview(conceptPacket.Constraints);
        var unknownsPreview = FormatPreview(conceptPacket.KnownUnknowns);

        return
        [
            new BootstrapQuestion(
                "iq-goal",
                $"Initial goal is '{initialGoalPreview}'. What concrete outcome should this intake treat as the first shippable success for '{domain}'?",
                $"Clarify the initial goal captured in '{conceptArtifactRelativePath}' before standard interview flow resumes.",
                [domain],
                "blocking"),
            new BootstrapQuestion(
                "iq-constraints",
                $"Current constraints are {constraintPreview}. Which hard constraints or invariants must stay true for '{domain}'?",
                $"Clarify repo-local constraints from '{conceptArtifactRelativePath}' so later intake updates stay bounded.",
                [domain, "constraints"],
                "nonblocking"),
            new BootstrapQuestion(
                "iq-unknowns",
                $"Known unknowns are {unknownsPreview}. Which unresolved unknown should this intake resolve first for '{domain}'?",
                $"Clarify the highest-priority unknown captured in '{conceptArtifactRelativePath}' before compile/fold-in work continues.",
                [domain, "unknowns"],
                "blocking")
        ];
    }

    private static string FormatPreview(IReadOnlyList<string> values)
    {
        return values.Count switch
        {
            0 => "'none'",
            1 => $"'{values[0]}'",
            _ => $"'{string.Join("; ", values)}'"
        };
    }

    private sealed record BootstrapQuestion(
        string QuestionId,
        string QuestionText,
        string Reason,
        IReadOnlyList<string> Affects,
        string BlockingOrNonblocking);
}
