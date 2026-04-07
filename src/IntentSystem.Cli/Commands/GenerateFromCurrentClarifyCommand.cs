namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentClarifyCommand
{
    public static Func<CliContext, string[], GenerateFromCurrentBestPracticeResult> BestPracticeExecutor { get; set; } =
        (context, args) => GenerateFromCurrentBestPracticeCommand.ExecuteCore(context, args);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            GenerateFromCurrentClarifyRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentClarifyResult ExecuteCore(CliContext context, string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current clarify requires a domain.");
        }

        var domain = args[0].Trim();
        var bestPracticeResult = BestPracticeExecutor(context, args);
        var confirmationArtifactRef = $".intent-cli/intake/{domain}.developer-confirmation.yaml";
        var confirmationArtifactPath = ResolvePath(context.RepoRoot, confirmationArtifactRef);
        var reviewArtifactPath = ResolvePath(context.RepoRoot, bestPracticeResult.ReviewArtifactPath);
        if (!File.Exists(confirmationArtifactPath))
        {
            throw new InvalidOperationException($"Developer confirmation artifact was not found at {confirmationArtifactPath}");
        }

        if (!File.Exists(reviewArtifactPath))
        {
            throw new InvalidOperationException($"Best-practice review artifact was not found at {reviewArtifactPath}");
        }

        var confirmationArtifact = DeveloperConfirmationArtifactYaml.Deserialize(File.ReadAllText(confirmationArtifactPath));
        var reviewArtifact = BestPracticeReviewArtifactMarkdown.Deserialize(File.ReadAllText(reviewArtifactPath));
        ValidateDeveloperConfirmationArtifact(domain, bestPracticeResult, confirmationArtifact, confirmationArtifactRef);
        ValidateBestPracticeReviewArtifact(domain, reviewArtifact);

        var clarifyItems = confirmationArtifact.ClarifyItems;
        var affectedParentRefs = reviewArtifact.ParentRuleSpecRefs;
        var reasons = BuildReasons(reviewArtifact, clarifyItems);
        var blockingness = BuildBlockingness(clarifyItems, confirmationArtifact);

        var clarificationReturnArtifact = new ClarificationReturnArtifact
        {
            DomainSlug = domain,
            SourceBundleArtifactPath = bestPracticeResult.SourceBundleArtifactPath,
            ReconstructedArtifactPaths = bestPracticeResult.ReconstructedArtifactPaths,
            ReviewArtifactPath = bestPracticeResult.ReviewArtifactPath,
            DeveloperConfirmationArtifactPath = confirmationArtifactRef,
            ClarifyItems = clarifyItems,
            AffectedParentRefs = affectedParentRefs,
            Reasons = reasons,
            Blockingness = blockingness,
            ReturnToIntentPaths = reviewArtifact.ReturnToIntentPaths,
            DownstreamReadiness = confirmationArtifact.DownstreamReadiness
        };

        var artifactPath = WriteArtifact(context.RepoRoot, domain, ClarificationReturnArtifactYaml.Serialize(clarificationReturnArtifact));

        return new GenerateFromCurrentClarifyResult
        {
            Domain = domain,
            SourceBundleArtifactPath = bestPracticeResult.SourceBundleArtifactPath,
            ReconstructedArtifactPaths = bestPracticeResult.ReconstructedArtifactPaths,
            ReviewArtifactPath = bestPracticeResult.ReviewArtifactPath,
            DeveloperConfirmationArtifactPath = confirmationArtifactRef,
            ClarificationReturnArtifactPath = ToRelativePath(context.RepoRoot, artifactPath),
            ClarifyItems = clarifyItems,
            AffectedParentRefs = affectedParentRefs,
            Reasons = reasons,
            Blockingness = blockingness,
            ReturnToIntentPaths = reviewArtifact.ReturnToIntentPaths,
            DownstreamReadiness = confirmationArtifact.DownstreamReadiness
        };
    }

    private static void ValidateDeveloperConfirmationArtifact(
        string domain,
        GenerateFromCurrentBestPracticeResult bestPracticeResult,
        DeveloperConfirmationArtifact artifact,
        string artifactRef)
    {
        if (!string.Equals(artifact.DomainSlug, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Developer confirmation artifact domain '{artifact.DomainSlug}' does not match requested domain '{domain}'.");
        }

        if (!string.Equals(artifact.SourceBundleArtifactPath, bestPracticeResult.SourceBundleArtifactPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Developer confirmation artifact source bundle path '{artifact.SourceBundleArtifactPath}' must match current source bundle path '{bestPracticeResult.SourceBundleArtifactPath}'.");
        }

        if (!artifact.ReconstructedArtifactPaths.SequenceEqual(bestPracticeResult.ReconstructedArtifactPaths, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Developer confirmation artifact reconstructed artifact paths must match the current reconstruction output.");
        }

        if (!string.Equals(artifact.ReviewArtifactPath, bestPracticeResult.ReviewArtifactPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Developer confirmation artifact review path '{artifact.ReviewArtifactPath}' must match current review path '{bestPracticeResult.ReviewArtifactPath}'.");
        }

        if (artifact.ClarifyItems.Count == 0)
        {
            throw new InvalidOperationException(
                $"Developer confirmation artifact at {artifactRef} does not contain any clarify items.");
        }
    }

    private static void ValidateBestPracticeReviewArtifact(string domain, BestPracticeReviewArtifact artifact)
    {
        if (!string.Equals(artifact.Domain, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Best-practice review artifact domain '{artifact.Domain}' does not match requested domain '{domain}'.");
        }
    }

    private static IReadOnlyList<string> BuildReasons(
        BestPracticeReviewArtifact reviewArtifact,
        IReadOnlyList<string> clarifyItems)
    {
        if (reviewArtifact.RecommendedClarifications.Count == 0)
        {
            return clarifyItems
                .Select(item => item.StartsWith("clarify:", StringComparison.Ordinal)
                    ? item["clarify:".Length..].TrimStart()
                    : item)
                .ToArray();
        }

        return clarifyItems
            .Select((item, index) => index < reviewArtifact.RecommendedClarifications.Count
                ? reviewArtifact.RecommendedClarifications[index]
                : item.StartsWith("clarify:", StringComparison.Ordinal)
                    ? item["clarify:".Length..].TrimStart()
                    : item)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildBlockingness(
        IReadOnlyList<string> clarifyItems,
        DeveloperConfirmationArtifact artifact)
    {
        var isBlocking = !string.Equals(artifact.DownstreamReadiness, "ready", StringComparison.Ordinal);
        return clarifyItems
            .Select(item => artifact.BlockedItems.Contains(item, StringComparer.Ordinal) || isBlocking
                ? "blocking"
                : "nonblocking")
            .ToArray();
    }

    private static string ResolvePath(string repoRoot, string artifactRef)
    {
        return Path.GetFullPath(Path.Combine(repoRoot, artifactRef.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string WriteArtifact(string repoRoot, string domain, string yaml)
    {
        var artifactPath = Path.Combine(
            repoRoot,
            ".intent-cli",
            "intake",
            $"{domain}.clarification-return.yaml");
        var directoryPath = Path.GetDirectoryName(artifactPath)
            ?? throw new InvalidOperationException("Clarification-return artifact path did not contain a directory.");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(artifactPath, yaml);
        return artifactPath;
    }

    private static string ToRelativePath(string repoRoot, string absolutePath)
    {
        return Path.GetRelativePath(repoRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
    }
}
