using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentBridgeCommand
{
    private static readonly DateTimeOffset CreatedAtBase = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            GenerateFromCurrentBridgeRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentBridgeResult ExecuteCore(CliContext context, string[] args)
    {
        var domain = ParseDomain(args);
        var reconstructedConcept = LoadReconstructedConcept(context.RepoRoot, domain);
        var reconstructedInterview = LoadReconstructedInterview(context.RepoRoot, domain);
        var recommendedUpdates = BuildRecommendedUpdates(reconstructedInterview);
        var conceptPacket = BuildConceptPacket(reconstructedConcept, reconstructedInterview);

        var conceptArtifactPath = IntakeConceptArtifactWriter.Write(
            IntakeConceptArtifactYaml.Serialize(conceptPacket),
            domain,
            context.RepoRoot);

        var interviewArtifactPaths = WriteInterviewArtifacts(
            context.RepoRoot,
            domain,
            reconstructedConcept,
            reconstructedInterview,
            recommendedUpdates);

        IReadOnlyList<string> skippedBridgeSteps = interviewArtifactPaths.Count == 0
            ? ["No reconstructed follow-up questions were present; interview artifact generation was skipped."]
            : Array.Empty<string>();

        return new GenerateFromCurrentBridgeResult
        {
            Domain = domain,
            ConceptArtifactPath = ToRelativePath(context.RepoRoot, conceptArtifactPath),
            InterviewArtifactPaths = interviewArtifactPaths,
            RecommendedUpdates = recommendedUpdates,
            ReturnToIntentPaths = reconstructedInterview.ReturnToIntentPaths,
            Gaps = reconstructedInterview.Gaps,
            SkippedBridgeSteps = skippedBridgeSteps
        };
    }

    private static string ParseDomain(string[] args)
    {
        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current bridge requires a domain.");
        }

        return args[0].Trim();
    }

    private static ReconstructedConceptArtifact LoadReconstructedConcept(string repoRoot, string domain)
    {
        var artifactPath = Path.Combine(
            repoRoot,
            ReconstructedConceptArtifactPathResolver.Resolve(domain).Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(artifactPath))
        {
            throw new InvalidOperationException($"Reconstructed concept artifact was not found at {artifactPath}");
        }

        var artifact = ReconstructedConceptArtifactYaml.Deserialize(File.ReadAllText(artifactPath));
        if (!string.Equals(artifact.DomainSlug, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Reconstructed concept artifact domain '{artifact.DomainSlug}' does not match requested domain '{domain}'.");
        }

        return artifact;
    }

    private static ReconstructedInterviewArtifact LoadReconstructedInterview(string repoRoot, string domain)
    {
        var artifactPath = Path.Combine(
            repoRoot,
            ReconstructedInterviewArtifactPathResolver.Resolve(domain).Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(artifactPath))
        {
            throw new InvalidOperationException($"Reconstructed interview artifact was not found at {artifactPath}");
        }

        var artifact = ReconstructedInterviewArtifactMarkdown.Deserialize(File.ReadAllText(artifactPath));
        if (!string.Equals(artifact.Domain, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Reconstructed interview artifact domain '{artifact.Domain}' does not match requested domain '{domain}'.");
        }

        return artifact;
    }

    private static IReadOnlyList<string> BuildRecommendedUpdates(ReconstructedInterviewArtifact reconstructedInterview)
    {
        return reconstructedInterview.RootNearIntentCandidates
            .Concat(reconstructedInterview.ExecutionNearUpdateCandidates)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static ConceptIntakePacket BuildConceptPacket(
        ReconstructedConceptArtifact reconstructedConcept,
        ReconstructedInterviewArtifact reconstructedInterview)
    {
        return new ConceptIntakePacket
        {
            DomainSlug = reconstructedConcept.DomainSlug,
            ConceptSource = "generate-from-current-bridge",
            ConceptText = BuildConceptText(reconstructedConcept, reconstructedInterview),
            UpstreamPaths = reconstructedInterview.ReturnToIntentPaths,
            InitialGoal = reconstructedConcept.InitialGoal,
            Constraints = reconstructedConcept.CandidateRules
                .Concat(reconstructedConcept.CandidateSpecs)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            KnownUnknowns = reconstructedInterview.RecommendedFollowUpQuestions
                .Concat(reconstructedInterview.Gaps)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static string BuildConceptText(
        ReconstructedConceptArtifact reconstructedConcept,
        ReconstructedInterviewArtifact reconstructedInterview)
    {
        var lines = new List<string>
        {
            $"Reconstructed concept bridge for domain '{reconstructedConcept.DomainSlug}'."
        };

        AppendSection(lines, "candidate_intent_nodes", reconstructedConcept.CandidateIntentNodes);
        AppendSection(lines, "candidate_user_context", reconstructedConcept.CandidateUserContext);
        AppendSection(lines, "candidate_means", reconstructedConcept.CandidateMeans);
        AppendSection(lines, "candidate_rules", reconstructedConcept.CandidateRules);
        AppendSection(lines, "candidate_specs", reconstructedConcept.CandidateSpecs);
        AppendSection(lines, "candidate_execution_units", reconstructedConcept.CandidateExecutionUnits);
        AppendSection(lines, "confidence_by_altitude", reconstructedConcept.ConfidenceByAltitude);
        AppendSection(lines, "source_concept_refs", reconstructedConcept.SourceConceptRefs);
        AppendSection(lines, "recommended_follow_up_questions", reconstructedInterview.RecommendedFollowUpQuestions);

        return string.Join(Environment.NewLine, lines);
    }

    private static IReadOnlyList<string> WriteInterviewArtifacts(
        string repoRoot,
        string domain,
        ReconstructedConceptArtifact reconstructedConcept,
        ReconstructedInterviewArtifact reconstructedInterview,
        IReadOnlyList<string> recommendedUpdates)
    {
        var interviewsRoot = Path.Combine(
            repoRoot,
            ".intent-cli",
            "interviews",
            domain.Replace('/', Path.DirectorySeparatorChar));

        if (Directory.Exists(interviewsRoot))
        {
            Directory.Delete(interviewsRoot, recursive: true);
        }

        if (reconstructedInterview.BridgeQuestions.Count == 0)
        {
            return [];
        }

        Directory.CreateDirectory(interviewsRoot);
        var artifactPaths = new List<string>();

        for (var index = 0; index < reconstructedInterview.BridgeQuestions.Count; index++)
        {
            var bridgeQuestion = reconstructedInterview.BridgeQuestions[index];
            var item = new InterviewQueueItem
            {
                DomainSlug = domain,
                SourceConceptRef = reconstructedConcept.SourceConceptRefs.FirstOrDefault()
                    ?? $"generate-from-current:{domain}",
                QuestionId = bridgeQuestion.QuestionId,
                QuestionText = bridgeQuestion.QuestionText,
                Reason = bridgeQuestion.Reason,
                Affects = bridgeQuestion.Affects,
                BlockingOrNonblocking = bridgeQuestion.BlockingOrNonblocking,
                Status = InterviewQueueItemStatus.Open,
                ReturnToIntentPaths = reconstructedInterview.ReturnToIntentPaths,
                CreatedAt = CreatedAtBase.AddMinutes(index),
                Answer = null,
                RecommendedUpdates = recommendedUpdates
            };

            var yamlPath = Path.Combine(interviewsRoot, $"{bridgeQuestion.QuestionId}.yaml");
            var markdownPath = Path.Combine(interviewsRoot, $"{bridgeQuestion.QuestionId}.md");
            File.WriteAllText(yamlPath, InterviewArtifactYaml.Serialize(item));
            File.WriteAllText(
                markdownPath,
                RenderInterviewMarkdown(item, recommendedUpdates, reconstructedInterview.Gaps));

            artifactPaths.Add(ToRelativePath(repoRoot, yamlPath));
            artifactPaths.Add(ToRelativePath(repoRoot, markdownPath));
        }

        return artifactPaths;
    }

    private static string RenderInterviewMarkdown(
        InterviewQueueItem item,
        IReadOnlyList<string> recommendedUpdates,
        IReadOnlyList<string> gaps)
    {
        var lines = new List<string>
        {
            "# Interview Question",
            string.Empty,
            "## Domain",
            string.Empty,
            $"`{item.DomainSlug}`",
            string.Empty,
            $"question_id: {item.QuestionId}",
            $"question_text: {item.QuestionText}",
            $"reason: {item.Reason}",
            $"blocking_or_nonblocking: {item.BlockingOrNonblocking}",
            string.Empty,
            "return_to_intent_paths:"
        };

        AppendBullets(lines, item.ReturnToIntentPaths);
        lines.Add(string.Empty);
        lines.Add("recommended_updates:");
        AppendBullets(lines, recommendedUpdates);
        lines.Add(string.Empty);
        lines.Add("gaps:");
        AppendBullets(lines, gaps);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
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

    private static void AppendBullets(List<string> lines, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            lines.Add("- none");
            return;
        }

        lines.AddRange(values.Select(value => $"- {value}"));
    }

    private static string ToRelativePath(string repoRoot, string absolutePath)
    {
        return Path.GetRelativePath(repoRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
    }
}
