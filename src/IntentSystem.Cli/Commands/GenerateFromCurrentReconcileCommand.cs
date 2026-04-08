namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentReconcileCommand
{
    public static Func<CliContext, string[], GenerateFromCurrentBestPracticeResult> BestPracticeExecutor { get; set; } =
        (context, args) => GenerateFromCurrentBestPracticeCommand.ExecuteCore(context, args);

    public static Func<CliContext, string[], GenerateFromCurrentClarifyResult> ClarifyExecutor { get; set; } =
        (context, args) => GenerateFromCurrentClarifyCommand.ExecuteCore(context, args);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            GenerateFromCurrentReconcileRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentReconcileResult ExecuteCore(CliContext context, string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current reconcile requires a domain.");
        }

        var domain = args[0].Trim();
        var bestPracticeResult = BestPracticeExecutor(context, args);
        var confirmationArtifactRef = $".intent-cli/intake/{domain}.developer-confirmation.yaml";
        var confirmationArtifactPath = ResolvePath(context.RepoRoot, confirmationArtifactRef);

        if (!File.Exists(confirmationArtifactPath))
        {
            throw new InvalidOperationException($"Developer confirmation artifact was not found at {confirmationArtifactPath}");
        }

        var confirmationArtifact = DeveloperConfirmationArtifactYaml.Deserialize(File.ReadAllText(confirmationArtifactPath));
        ValidateDeveloperConfirmationArtifact(domain, bestPracticeResult, confirmationArtifact);

        if (confirmationArtifact.ClarifyItems.Count > 0)
        {
            var clarifyResult = ClarifyExecutor(context, args);
            return new GenerateFromCurrentReconcileResult
            {
                Domain = domain,
                Route = "clarification-return",
                SourceBundleArtifactPath = clarifyResult.SourceBundleArtifactPath,
                ReconstructedArtifactPaths = clarifyResult.ReconstructedArtifactPaths,
                ReviewArtifactPath = clarifyResult.ReviewArtifactPath,
                DeveloperConfirmationArtifactPath = clarifyResult.DeveloperConfirmationArtifactPath,
                ConfirmedItems = confirmationArtifact.ConfirmedItems,
                RejectedItems = confirmationArtifact.RejectedItems,
                DeferredItems = confirmationArtifact.DeferredItems,
                BlockedItems = confirmationArtifact.BlockedItems,
                ClarifyItems = clarifyResult.ClarifyItems,
                ReturnToIntentPaths = clarifyResult.ReturnToIntentPaths,
                DownstreamReadiness = confirmationArtifact.DownstreamReadiness,
                ClarificationReturnArtifactPath = clarifyResult.ClarificationReturnArtifactPath
            };
        }

        var confirmedArtifact = new ConfirmedReconstructionArtifact
        {
            DomainSlug = domain,
            SourceBundleArtifactPath = bestPracticeResult.SourceBundleArtifactPath,
            ReconstructedArtifactPaths = bestPracticeResult.ReconstructedArtifactPaths,
            ReviewArtifactPath = bestPracticeResult.ReviewArtifactPath,
            DeveloperConfirmationArtifactPath = confirmationArtifactRef,
            ConfirmedItems = confirmationArtifact.ConfirmedItems,
            RejectedItems = confirmationArtifact.RejectedItems,
            DeferredItems = confirmationArtifact.DeferredItems,
            BlockedItems = confirmationArtifact.BlockedItems,
            ReturnToIntentPaths = confirmationArtifact.ReturnToIntentPaths,
            DownstreamReadiness = confirmationArtifact.DownstreamReadiness
        };

        var artifactPath = WriteArtifact(context.RepoRoot, domain, ConfirmedReconstructionArtifactYaml.Serialize(confirmedArtifact));

        return new GenerateFromCurrentReconcileResult
        {
            Domain = domain,
            Route = "confirmed-handoff",
            SourceBundleArtifactPath = bestPracticeResult.SourceBundleArtifactPath,
            ReconstructedArtifactPaths = bestPracticeResult.ReconstructedArtifactPaths,
            ReviewArtifactPath = bestPracticeResult.ReviewArtifactPath,
            DeveloperConfirmationArtifactPath = confirmationArtifactRef,
            ConfirmedReconstructionArtifactPath = ToRelativePath(context.RepoRoot, artifactPath),
            ConfirmedItems = confirmationArtifact.ConfirmedItems,
            RejectedItems = confirmationArtifact.RejectedItems,
            DeferredItems = confirmationArtifact.DeferredItems,
            BlockedItems = confirmationArtifact.BlockedItems,
            ClarifyItems = Array.Empty<string>(),
            ReturnToIntentPaths = confirmationArtifact.ReturnToIntentPaths,
            DownstreamReadiness = confirmationArtifact.DownstreamReadiness
        };
    }

    private static void ValidateDeveloperConfirmationArtifact(
        string domain,
        GenerateFromCurrentBestPracticeResult bestPracticeResult,
        DeveloperConfirmationArtifact artifact)
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
            $"{domain}.confirmed-reconstruction.yaml");
        var directoryPath = Path.GetDirectoryName(artifactPath)
            ?? throw new InvalidOperationException("Confirmed reconstruction artifact path did not contain a directory.");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(artifactPath, yaml);
        return artifactPath;
    }

    private static string ToRelativePath(string repoRoot, string absolutePath)
    {
        return Path.GetRelativePath(repoRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
    }
}
