namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentReconstructionCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            GenerateFromCurrentReconstructionRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentReconstructionResult ExecuteCore(CliContext context, string[] args)
    {
        var domain = ParseDomain(args);
        var currentSources = LoadCurrentSources(context.RepoRoot, domain);

        var intentNodes = BuildIntentNodes(currentSources);
        var userContext = BuildUserContext(currentSources);
        var means = BuildMeans(currentSources);
        var rules = BuildRules(currentSources);
        var specs = BuildSpecs(currentSources);
        var executionUnits = BuildExecutionUnits(currentSources);
        var confidenceByAltitude = BuildConfidenceByAltitude(currentSources);
        var sourceConceptRefs = BuildSourceConceptRefs(currentSources);
        var bridgeQuestions = BuildBridgeQuestions(currentSources);
        var interviewQuestions = bridgeQuestions.Select(question => question.QuestionText).ToArray();
        var returnToIntentPaths = BuildReturnToIntentPaths(context.RepoRoot, currentSources);

        var conceptArtifact = new ReconstructedConceptArtifact
        {
            DomainSlug = domain,
            InitialGoal = BuildInitialGoal(currentSources),
            CandidateIntentNodes = intentNodes,
            CandidateUserContext = userContext,
            CandidateMeans = means,
            CandidateRules = rules,
            CandidateSpecs = specs,
            CandidateExecutionUnits = executionUnits,
            ConfidenceByAltitude = confidenceByAltitude,
            SourceConceptRefs = sourceConceptRefs
        };

        var conceptArtifactPath = ReconstructedConceptArtifactWriter.Write(context.RepoRoot, domain, conceptArtifact);
        var interviewMarkdown = GenerateFromCurrentReconstructionRenderer.RenderInterviewMarkdown(
            domain,
            currentSources.SelectedAltitudes,
            intentNodes
                .Concat(userContext)
                .Concat(means)
                .Concat(rules)
                .Concat(specs)
                .ToArray(),
            executionUnits,
            confidenceByAltitude,
            sourceConceptRefs,
            interviewQuestions,
            bridgeQuestions,
            returnToIntentPaths,
            currentSources.Gaps);
        var interviewArtifactPath = ReconstructedInterviewArtifactWriter.Write(context.RepoRoot, domain, interviewMarkdown);

        return new GenerateFromCurrentReconstructionResult
        {
            Domain = domain,
            ConceptArtifactPath = ToRelativePath(context.RepoRoot, conceptArtifactPath),
            InterviewArtifactPath = ToRelativePath(context.RepoRoot, interviewArtifactPath),
            SelectedAltitudes = currentSources.SelectedAltitudes,
            CandidateIntentNodes = intentNodes.Concat(userContext).Concat(means).Concat(rules).Concat(specs).ToArray(),
            CandidateExecutionUnits = executionUnits,
            ConfidenceByAltitude = confidenceByAltitude,
            SourceConceptRefs = sourceConceptRefs,
            RecommendedFollowUpQuestions = interviewQuestions,
            ReturnToIntentPaths = returnToIntentPaths,
            Gaps = currentSources.Gaps
        };
    }

    private static string ParseDomain(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current reconstruction requires a domain.");
        }

        if (args.Length != 1)
        {
            throw new InvalidOperationException(
                "Generate-from-current reconstruction stage only accepts '<domain>' without source selection options.");
        }

        return args[0].Trim();
    }

    private static CurrentSourcesArtifact LoadCurrentSources(string repoRoot, string domain)
    {
        var artifactPath = Path.Combine(
            repoRoot,
            CurrentSourcesArtifactPathResolver.Resolve(domain).Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(artifactPath))
        {
            throw new InvalidOperationException($"Current sources artifact was not found at {artifactPath}");
        }

        var artifact = CurrentSourcesArtifactYaml.Deserialize(File.ReadAllText(artifactPath));
        if (!string.Equals(artifact.DomainSlug, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Current sources artifact domain '{artifact.DomainSlug}' does not match requested domain '{domain}'.");
        }

        return artifact;
    }

    private static IReadOnlyList<string> BuildIntentNodes(CurrentSourcesArtifact artifact)
    {
        if (!artifact.SelectedAltitudes.Contains("purpose", StringComparer.Ordinal))
        {
            return [];
        }

        return
        [
            $"Clarify the primary purpose for domain '{artifact.DomainSlug}' from selected issue and PR signals."
        ];
    }

    private static IReadOnlyList<string> BuildUserContext(CurrentSourcesArtifact artifact)
    {
        if (!artifact.SelectedAltitudes.Contains("user-context", StringComparer.Ordinal))
        {
            return [];
        }

        return
        [
            $"Validate the user-facing context for '{artifact.DomainSlug}' from selected issue and PR discussion."
        ];
    }

    private static IReadOnlyList<string> BuildMeans(CurrentSourcesArtifact artifact)
    {
        if (!artifact.SelectedAltitudes.Contains("means", StringComparer.Ordinal))
        {
            return [];
        }

        return artifact.SelectedPaths
            .Where(path => !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .Select(path => $"Inspect current implementation seam at {path}.")
            .DefaultIfEmpty($"Inspect current implementation seams under '{artifact.SourceRoot}'.")
            .ToArray();
    }

    private static IReadOnlyList<string> BuildRules(CurrentSourcesArtifact artifact)
    {
        if (!artifact.SelectedAltitudes.Contains("rules", StringComparer.Ordinal))
        {
            return [];
        }

        return artifact.SelectedPaths
            .Where(path => string.Equals(path, "AGENTS.md", StringComparison.Ordinal)
                || string.Equals(path, "CLAUDE.md", StringComparison.Ordinal))
            .Select(path => $"Preserve repo rule guidance captured in {path}.")
            .DefaultIfEmpty("Clarify which repo-level rules remain mandatory for future changes.")
            .ToArray();
    }

    private static IReadOnlyList<string> BuildSpecs(CurrentSourcesArtifact artifact)
    {
        if (!artifact.SelectedAltitudes.Contains("specs", StringComparer.Ordinal))
        {
            return [];
        }

        return artifact.SelectedPaths
            .Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .Select(path => $"Reconcile external contract and documentation signal from {path}.")
            .DefaultIfEmpty("Reconstruct the current external surface and contract expectations.")
            .ToArray();
    }

    private static IReadOnlyList<string> BuildExecutionUnits(CurrentSourcesArtifact artifact)
    {
        if (!artifact.SelectedAltitudes.Contains("execution", StringComparer.Ordinal))
        {
            return [];
        }

        return artifact.SelectedPaths
            .Where(path => !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .Select(path => $"Execution candidate from {path}.")
            .DefaultIfEmpty($"Execution candidate from source root '{artifact.SourceRoot}'.")
            .ToArray();
    }

    private static IReadOnlyList<string> BuildConfidenceByAltitude(CurrentSourcesArtifact artifact)
    {
        return artifact.SelectedAltitudes
            .Select(altitude => $"{altitude}: {ResolveConfidence(artifact, altitude)}")
            .ToArray();
    }

    private static string ResolveConfidence(CurrentSourcesArtifact artifact, string altitude)
    {
        var relevantCount = altitude switch
        {
            "purpose" or "user-context" => artifact.SourceRefs.Count(reference =>
                reference.StartsWith("issue:", StringComparison.Ordinal)
                || reference.StartsWith("issue-comment:", StringComparison.Ordinal)
                || reference.StartsWith("pr:", StringComparison.Ordinal)
                || reference.StartsWith("pr-comment:", StringComparison.Ordinal)
                || reference.StartsWith("pr-review:", StringComparison.Ordinal)),
            "means" or "execution" => artifact.SourceRefs.Count(reference =>
                reference.StartsWith("code:", StringComparison.Ordinal)
                || reference.StartsWith("test:", StringComparison.Ordinal)),
            "rules" or "specs" => artifact.SourceRefs.Count(reference =>
                reference.StartsWith("doc:", StringComparison.Ordinal)
                || reference.StartsWith("readme:", StringComparison.Ordinal)),
            _ => 0
        };

        return relevantCount switch
        {
            >= 4 => "high",
            >= 1 => "medium",
            _ => "low"
        };
    }

    private static IReadOnlyList<string> BuildSourceConceptRefs(CurrentSourcesArtifact artifact)
    {
        return artifact.SourceRefs
            .Where(reference =>
                !reference.StartsWith("code:", StringComparison.Ordinal)
                && !reference.StartsWith("test:", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(reference => reference, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<ReconstructedBridgeQuestion> BuildBridgeQuestions(CurrentSourcesArtifact artifact)
    {
        var questions = new List<ReconstructedBridgeQuestion>();
        var nextQuestionNumber = 1;

        if (artifact.SelectedAltitudes.Contains("purpose", StringComparer.Ordinal)
            || artifact.SelectedAltitudes.Contains("user-context", StringComparer.Ordinal))
        {
            questions.Add(CreateBridgeQuestion(
                artifact.DomainSlug,
                nextQuestionNumber++,
                $"What user-facing outcome should '{artifact.DomainSlug}' prioritize based on the selected current signals?",
                "Clarify root-near intent before standard intake resumes.",
                "blocking"));
        }

        if (artifact.SelectedAltitudes.Contains("rules", StringComparer.Ordinal)
            || artifact.SelectedAltitudes.Contains("specs", StringComparer.Ordinal))
        {
            questions.Add(CreateBridgeQuestion(
                artifact.DomainSlug,
                nextQuestionNumber++,
                "Which current rules or specs are mandatory versus historical context only?",
                "Clarify root-near rule and spec expectations before standard intake resumes.",
                "blocking"));
        }

        if (artifact.SelectedAltitudes.Contains("execution", StringComparer.Ordinal))
        {
            questions.Add(CreateBridgeQuestion(
                artifact.DomainSlug,
                nextQuestionNumber++,
                "Which execution-ready change slice should be cut first from the selected current paths?",
                "Clarify execution-near detail before standard intake resumes.",
                "nonblocking"));
        }

        if (questions.Count == 0)
        {
            questions.Add(CreateBridgeQuestion(
                artifact.DomainSlug,
                nextQuestionNumber,
                $"Which missing intent detail should be clarified first for domain '{artifact.DomainSlug}'?",
                "Clarify root-near intent before standard intake resumes.",
                "blocking"));
        }

        return questions;
    }

    private static ReconstructedBridgeQuestion CreateBridgeQuestion(
        string domain,
        int questionNumber,
        string questionText,
        string reason,
        string blockingOrNonblocking)
    {
        return new ReconstructedBridgeQuestion
        {
            QuestionId = $"iq-{questionNumber}",
            QuestionText = questionText,
            Reason = reason,
            Affects = [domain],
            BlockingOrNonblocking = blockingOrNonblocking
        };
    }

    private static IReadOnlyList<string> BuildReturnToIntentPaths(string repoRoot, CurrentSourcesArtifact artifact)
    {
        var candidates = new List<string>();
        AddIfExists(repoRoot, "README.md", candidates);

        if (artifact.SelectedAltitudes.Contains("rules", StringComparer.Ordinal))
        {
            AddIfExists(repoRoot, "AGENTS.md", candidates);
            AddIfExists(repoRoot, "CLAUDE.md", candidates);
        }

        return candidates
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddIfExists(string repoRoot, string relativePath, List<string> values)
    {
        var absolutePath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(absolutePath))
        {
            values.Add(relativePath);
        }
    }

    private static string BuildInitialGoal(CurrentSourcesArtifact artifact)
    {
        var firstIssueOrPr = artifact.SourceRefs.FirstOrDefault(reference =>
            reference.StartsWith("issue:", StringComparison.Ordinal)
            || reference.StartsWith("pr:", StringComparison.Ordinal));

        if (firstIssueOrPr is not null)
        {
            var titleStart = firstIssueOrPr.IndexOf("] ", StringComparison.Ordinal);
            if (titleStart >= 0 && titleStart + 2 < firstIssueOrPr.Length)
            {
                return firstIssueOrPr[(titleStart + 2)..];
            }

            return firstIssueOrPr;
        }

        return $"Reconstruct current intent for domain '{artifact.DomainSlug}' from source root '{artifact.SourceRoot}'.";
    }

    private static string ToRelativePath(string repoRoot, string absolutePath)
    {
        return Path.GetRelativePath(repoRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
    }
}
