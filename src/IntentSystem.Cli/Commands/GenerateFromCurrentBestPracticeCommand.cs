namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentBestPracticeCommand
{
    private static readonly string[] ReviewedDimensions =
    [
        "architecture",
        "security",
        "auth",
        "data-modeling",
        "error-handling",
        "performance",
        "testability"
    ];

    private static readonly string[] ParentRuleSpecCandidates =
    [
        "intents/intent-cli/specs/04-concept-intake-and-interview.md",
        "intents/intent-cli/specs/05-intent-cli-surface.md",
        "intents/intent-cli/specs/10-generate-from-current-and-historical-signals.md",
        "intents/intent-cli/specs/11-reconstruction-review-and-confirmation.md",
        "intents/intent-cli/intent-tree/means/02-tooling-strategy.md",
        "intents/rules/reconstruction-feedback-loop.md",
        "intents/rules/issue-lifecycle-and-landing.md"
    ];

    private static readonly string[] ModelRegistryDirectories =
    [
        ".intent/model-registry",
        ".intent/models",
        ".intent-cli/model-registry"
    ];

    private static readonly string[] KnowledgeBaseDirectories =
    [
        ".intent/best-practices",
        ".intent/best-practice",
        ".intent-cli/best-practices",
        "docs/best-practices"
    ];

    public static Func<CliContext, string[], GenerateFromCurrentResult> SourceBundleExecutor { get; set; } =
        (context, args) => GenerateFromCurrentCommand.ExecuteSourceBundleCore(context, args);

    public static Func<CliContext, string, GenerateFromCurrentReconstructionResult> ReconstructionExecutor { get; set; } =
        (context, domain) => GenerateFromCurrentReconstructionCommand.ExecuteCore(context, [domain]);

    public static Func<string, IReadOnlyList<string>> ModelRefProvider { get; set; } =
        repoRoot => ReadProjectRefs(repoRoot, ModelRegistryDirectories);

    public static Func<string, IReadOnlyList<string>> KnowledgeRefProvider { get; set; } =
        repoRoot => ReadProjectRefs(repoRoot, KnowledgeBaseDirectories);

    public static Func<string?, IReadOnlyList<string>> ParentRuleSpecRefProvider { get; set; } =
        parentRepoRoot => ReadParentRuleSpecRefs(parentRepoRoot, ParentRuleSpecCandidates);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            GenerateFromCurrentBestPracticeRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentBestPracticeResult ExecuteCore(CliContext context, string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current best-practice requires a domain.");
        }

        var domain = args[0].Trim();
        var sourceBundleResult = SourceBundleExecutor(context, args);
        var reconstructionResult = ReconstructionExecutor(context, domain);
        var modelRefs = ModelRefProvider(context.RepoRoot);
        var knowledgeRefs = KnowledgeRefProvider(context.RepoRoot);
        var parentRefs = ParentRuleSpecRefProvider(context.ResolveParentIntentRepoRootPath());
        var reviewedDimensions = BuildReviewedDimensions(
            sourceBundleResult,
            reconstructionResult,
            modelRefs,
            knowledgeRefs,
            parentRefs);
        var recommendedIntentAdditions = BuildRecommendedIntentAdditions(
            domain,
            reconstructionResult,
            modelRefs,
            knowledgeRefs,
            parentRefs);
        var recommendedClarifications = BuildRecommendedClarifications(
            domain,
            reconstructionResult,
            modelRefs,
            knowledgeRefs,
            parentRefs);
        var developerConfirmationItems = BuildDeveloperConfirmationItems(
            domain,
            recommendedIntentAdditions,
            recommendedClarifications,
            modelRefs,
            knowledgeRefs,
            parentRefs);
        var confidenceDeltas = BuildConfidenceDeltas(
            reconstructionResult.ConfidenceByAltitude,
            modelRefs,
            knowledgeRefs,
            parentRefs);
        var readinessStatus = DetermineReadinessStatus(modelRefs, knowledgeRefs, parentRefs, recommendedClarifications);
        var skippedStages = BuildSkippedStages(modelRefs, knowledgeRefs, parentRefs);

        var artifactPath = WriteArtifact(
            context.RepoRoot,
            domain,
            RenderArtifact(
                domain,
                sourceBundleResult.ArtifactPath,
                [
                    reconstructionResult.ConceptArtifactPath,
                    reconstructionResult.InterviewArtifactPath
                ],
                reviewedDimensions,
                modelRefs,
                knowledgeRefs,
                parentRefs,
                recommendedIntentAdditions,
                recommendedClarifications,
                developerConfirmationItems,
                reconstructionResult.ReturnToIntentPaths,
                confidenceDeltas,
                readinessStatus,
                skippedStages));

        return new GenerateFromCurrentBestPracticeResult
        {
            Domain = domain,
            SourceBundleArtifactPath = sourceBundleResult.ArtifactPath,
            ReconstructedArtifactPaths =
            [
                reconstructionResult.ConceptArtifactPath,
                reconstructionResult.InterviewArtifactPath
            ],
            ReviewArtifactPath = ToRelativePath(context.RepoRoot, artifactPath),
            ReviewedDimensions = reviewedDimensions,
            ModelRefs = modelRefs,
            KnowledgeRefs = knowledgeRefs,
            RecommendedIntentAdditions = recommendedIntentAdditions,
            RecommendedClarifications = recommendedClarifications,
            DeveloperConfirmationItems = developerConfirmationItems,
            ReturnToIntentPaths = reconstructionResult.ReturnToIntentPaths,
            ConfidenceDeltas = confidenceDeltas,
            ReadinessStatus = readinessStatus,
            SkippedStages = skippedStages
        };
    }

    private static IReadOnlyList<string> ReadProjectRefs(string repoRoot, IReadOnlyList<string> relativeDirectories)
    {
        var refs = new List<string>();

        foreach (var relativeDirectory in relativeDirectories)
        {
            var fullDirectory = Path.Combine(repoRoot, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(fullDirectory))
            {
                continue;
            }

            refs.AddRange(
                Directory.EnumerateFiles(fullDirectory, "*", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .Select(path => ToRelativePath(repoRoot, path)));
        }

        return refs.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> ReadParentRuleSpecRefs(string? parentRepoRoot, IReadOnlyList<string> relativePaths)
    {
        if (string.IsNullOrWhiteSpace(parentRepoRoot) || !Directory.Exists(parentRepoRoot))
        {
            return [];
        }

        return relativePaths
            .Where(relativePath => File.Exists(Path.Combine(parentRepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildReviewedDimensions(
        GenerateFromCurrentResult sourceBundleResult,
        GenerateFromCurrentReconstructionResult reconstructionResult,
        IReadOnlyList<string> modelRefs,
        IReadOnlyList<string> knowledgeRefs,
        IReadOnlyList<string> parentRefs)
    {
        var hasModelRefs = modelRefs.Count > 0;
        var hasKnowledgeRefs = knowledgeRefs.Count > 0;
        var hasParentRefs = parentRefs.Count > 0;
        var hasAuthKnowledge = knowledgeRefs.Any(reference => reference.Contains("auth", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("security", StringComparison.OrdinalIgnoreCase));
        var hasTestSignals = sourceBundleResult.SourceRefs.Any(reference => reference.StartsWith("test:", StringComparison.Ordinal));

        return ReviewedDimensions.Select(dimension => dimension switch
        {
            "architecture" => $"{dimension}: {(hasParentRefs && hasModelRefs ? "needs-confirmation" : hasParentRefs ? "missing-model-registry" : "missing-parent-rules-specs")}",
            "security" => $"{dimension}: {(hasParentRefs && hasKnowledgeRefs ? "needs-confirmation" : hasParentRefs ? "missing-knowledge-base" : "missing-parent-rules-specs")}",
            "auth" => $"{dimension}: {(hasParentRefs && hasAuthKnowledge ? "clarify" : hasParentRefs ? "missing-auth-guidance" : "missing-parent-rules-specs")}",
            "data-modeling" => $"{dimension}: {(hasParentRefs && hasModelRefs ? "clarify" : hasParentRefs ? "missing-model-registry" : "missing-parent-rules-specs")}",
            "error-handling" => $"{dimension}: {(hasParentRefs && hasKnowledgeRefs ? "needs-confirmation" : hasParentRefs ? "missing-knowledge-base" : "missing-parent-rules-specs")}",
            "performance" => $"{dimension}: {(reconstructionResult.CandidateExecutionUnits.Count > 0 ? "defer" : "needs-confirmation")}",
            "testability" => $"{dimension}: {(hasTestSignals ? "needs-confirmation" : "missing-test-signal")}",
            _ => $"{dimension}: needs-confirmation"
        }).ToArray();
    }

    private static IReadOnlyList<string> BuildRecommendedIntentAdditions(
        string domain,
        GenerateFromCurrentReconstructionResult reconstructionResult,
        IReadOnlyList<string> modelRefs,
        IReadOnlyList<string> knowledgeRefs,
        IReadOnlyList<string> parentRefs)
    {
        var suggestions = new List<string>();

        if (parentRefs.Count > 0 && reconstructionResult.CandidateIntentNodes.Count > 0)
        {
            suggestions.Add($"Promote reconstructed intent candidates for '{domain}' into explicit parent intent additions after confirmation.");
        }

        if (parentRefs.Count > 0 && reconstructionResult.CandidateExecutionUnits.Count > 0)
        {
            suggestions.Add($"Add execution-near intent for '{domain}' before issue-cut based on the reconstructed execution candidates.");
        }

        if (modelRefs.Count == 0)
        {
            suggestions.Add($"Add project-local model registry entries for '{domain}' covering aggregate, read model, API, infrastructure, or auth model seams.");
        }

        if (knowledgeRefs.Count == 0)
        {
            suggestions.Add($"Add project-local best-practice knowledge entries for '{domain}' covering security, error-handling, and testability expectations.");
        }

        return suggestions.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> BuildRecommendedClarifications(
        string domain,
        GenerateFromCurrentReconstructionResult reconstructionResult,
        IReadOnlyList<string> modelRefs,
        IReadOnlyList<string> knowledgeRefs,
        IReadOnlyList<string> parentRefs)
    {
        var clarifications = reconstructionResult.Gaps
            .Select(gap => $"Clarify: {gap}")
            .ToList();

        if (parentRefs.Count == 0)
        {
            clarifications.Add($"Clarify the runtime parent intent repo root and required parent rules/spec refs for '{domain}' before best-practice review is treated as complete.");
        }

        if (modelRefs.Count == 0)
        {
            clarifications.Add($"Clarify the aggregate boundary and data ownership model for '{domain}' before canonical mutation.");
        }

        if (knowledgeRefs.Count == 0)
        {
            clarifications.Add($"Clarify the security/auth and retry/error-handling expectations for '{domain}' before issue-cut.");
        }

        if (!knowledgeRefs.Any(reference => reference.Contains("auth", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("security", StringComparison.OrdinalIgnoreCase)))
        {
            clarifications.Add($"Clarify the authn/authz model and trust boundary for '{domain}'.");
        }

        return clarifications.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> BuildDeveloperConfirmationItems(
        string domain,
        IReadOnlyList<string> recommendedIntentAdditions,
        IReadOnlyList<string> recommendedClarifications,
        IReadOnlyList<string> modelRefs,
        IReadOnlyList<string> knowledgeRefs,
        IReadOnlyList<string> parentRefs)
    {
        var items = new List<string>
        {
            $"confirm: validate the best-practice review suggestions for '{domain}' against parent rules/specs before any canonical mutation.",
            "reject: explicitly reject any suggested intent addition that conflicts with project rules or specs."
        };

        if (recommendedClarifications.Count > 0)
        {
            items.Add($"clarify: resolve {recommendedClarifications.Count} clarification candidate(s) before issue-cut-ready treatment.");
        }

        if (modelRefs.Count == 0 || knowledgeRefs.Count == 0 || parentRefs.Count == 0)
        {
            items.Add("defer: keep the reconstructed result out of issue-cut and source-of-truth mutation until parent rules/specs, project-local model registry, and knowledge base coverage are resolved.");
        }

        if (recommendedIntentAdditions.Count > 0)
        {
            items.Add($"confirm: choose which of the {recommendedIntentAdditions.Count} suggested intent additions should return to the parent intent tree.");
        }

        return items.ToArray();
    }

    private static IReadOnlyList<string> BuildConfidenceDeltas(
        IReadOnlyList<string> confidenceByAltitude,
        IReadOnlyList<string> modelRefs,
        IReadOnlyList<string> knowledgeRefs,
        IReadOnlyList<string> parentRefs)
    {
        var hasCompleteReviewInputs = modelRefs.Count > 0 && knowledgeRefs.Count > 0 && parentRefs.Count > 0;
        return confidenceByAltitude
            .Select(entry =>
            {
                var parts = entry.Split(':', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                {
                    return $"{entry} -> {entry}";
                }

                var nextConfidence = hasCompleteReviewInputs
                    ? RaiseConfidence(parts[1])
                    : LowerConfidence(parts[1]);
                return $"{parts[0]}: {parts[1]} -> {nextConfidence}";
            })
            .ToArray();
    }

    private static string RaiseConfidence(string confidence)
    {
        return confidence switch
        {
            "low" => "medium",
            "medium" => "high",
            _ => "high"
        };
    }

    private static string LowerConfidence(string confidence)
    {
        return confidence switch
        {
            "high" => "medium",
            "medium" => "low",
            _ => "low"
        };
    }

    private static string DetermineReadinessStatus(
        IReadOnlyList<string> modelRefs,
        IReadOnlyList<string> knowledgeRefs,
        IReadOnlyList<string> parentRefs,
        IReadOnlyList<string> recommendedClarifications)
    {
        return modelRefs.Count > 0
            && knowledgeRefs.Count > 0
            && parentRefs.Count > 0
            && recommendedClarifications.Count == 0
            ? "ready"
            : "not-ready";
    }

    private static IReadOnlyList<string> BuildSkippedStages(
        IReadOnlyList<string> modelRefs,
        IReadOnlyList<string> knowledgeRefs,
        IReadOnlyList<string> parentRefs)
    {
        var skippedStages = new List<string>();
        if (parentRefs.Count == 0)
        {
            skippedStages.Add("parent-rule-spec-review");
        }

        if (modelRefs.Count == 0)
        {
            skippedStages.Add("model-registry-review");
        }

        if (knowledgeRefs.Count == 0)
        {
            skippedStages.Add("best-practice-knowledge-review");
        }

        return skippedStages;
    }

    private static string RenderArtifact(
        string domain,
        string sourceBundleArtifactPath,
        IReadOnlyList<string> reconstructedArtifactPaths,
        IReadOnlyList<string> reviewedDimensions,
        IReadOnlyList<string> modelRefs,
        IReadOnlyList<string> knowledgeRefs,
        IReadOnlyList<string> parentRuleSpecRefs,
        IReadOnlyList<string> recommendedIntentAdditions,
        IReadOnlyList<string> recommendedClarifications,
        IReadOnlyList<string> developerConfirmationItems,
        IReadOnlyList<string> returnToIntentPaths,
        IReadOnlyList<string> confidenceDeltas,
        string readinessStatus,
        IReadOnlyList<string> skippedStages)
    {
        var lines = new List<string>
        {
            "# Best Practice Review",
            string.Empty,
            "## Domain",
            string.Empty,
            $"`{domain}`",
            string.Empty,
            $"source_bundle_artifact_path: {sourceBundleArtifactPath}",
            "reconstructed_artifact_paths:"
        };

        AppendBullets(lines, reconstructedArtifactPaths);
        lines.Add(string.Empty);
        lines.Add("reviewed_dimensions:");
        AppendBullets(lines, reviewedDimensions);
        lines.Add(string.Empty);
        lines.Add("model_refs:");
        AppendBullets(lines, modelRefs);
        lines.Add(string.Empty);
        lines.Add("knowledge_refs:");
        AppendBullets(lines, knowledgeRefs);
        lines.Add(string.Empty);
        lines.Add("parent_rule_spec_refs:");
        AppendBullets(lines, parentRuleSpecRefs);
        lines.Add(string.Empty);
        lines.Add("recommended_intent_additions:");
        AppendBullets(lines, recommendedIntentAdditions);
        lines.Add(string.Empty);
        lines.Add("recommended_clarifications:");
        AppendBullets(lines, recommendedClarifications);
        lines.Add(string.Empty);
        lines.Add("developer_confirmation_items:");
        AppendBullets(lines, developerConfirmationItems);
        lines.Add(string.Empty);
        lines.Add("return_to_intent_paths:");
        AppendBullets(lines, returnToIntentPaths);
        lines.Add(string.Empty);
        lines.Add("confidence_deltas:");
        AppendBullets(lines, confidenceDeltas);
        lines.Add(string.Empty);
        lines.Add($"readiness_status: {readinessStatus}");
        lines.Add("skipped_stages:");
        AppendBullets(lines, skippedStages);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static void AppendBullets(List<string> lines, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            lines.Add("- none");
            return;
        }

        foreach (var value in values)
        {
            lines.Add($"- {value}");
        }
    }

    private static string WriteArtifact(string repoRoot, string domain, string markdown)
    {
        var artifactPath = Path.Combine(
            repoRoot,
            ".intent-cli",
            "intake",
            $"{domain}.best-practice-review.md");
        var directoryPath = Path.GetDirectoryName(artifactPath)
            ?? throw new InvalidOperationException("Best-practice review artifact path did not contain a directory.");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(artifactPath, markdown);
        return artifactPath;
    }

    private static string ToRelativePath(string repoRoot, string absolutePath)
    {
        return Path.GetRelativePath(repoRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
    }
}
