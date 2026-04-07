namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentConfirmCommand
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
            GenerateFromCurrentConfirmRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentConfirmResult ExecuteCore(CliContext context, string[] args)
    {
        var (pipelineArgs, decisionFilePathArgument) = ParseArgs(args);
        var domain = pipelineArgs[0].Trim();
        var bestPracticeResult = BestPracticeExecutor(context, pipelineArgs);
        var decisionFilePath = ResolveInputPath(context.RepoRoot, decisionFilePathArgument);
        if (!File.Exists(decisionFilePath))
        {
            throw new InvalidOperationException($"Prepared developer decision file was not found at {decisionFilePath}");
        }

        var decisionFile = DeveloperDecisionFileMarkdown.Deserialize(File.ReadAllText(decisionFilePath));
        ValidateDecisions(bestPracticeResult.DeveloperConfirmationItems, decisionFile);

        var decidedItems = decisionFile.ConfirmedItems
            .Concat(decisionFile.RejectedItems)
            .Concat(decisionFile.ClarifyItems)
            .Concat(decisionFile.DeferredItems)
            .ToHashSet(StringComparer.Ordinal);
        var blockedItems = BuildBlockedItems(bestPracticeResult, decisionFile, decidedItems);
        var downstreamReadiness = string.Equals(bestPracticeResult.ReadinessStatus, "ready", StringComparison.Ordinal)
            && blockedItems.Count == 0
            ? "ready"
            : "not-ready";

        var artifact = new DeveloperConfirmationArtifact
        {
            DomainSlug = domain,
            SourceBundleArtifactPath = bestPracticeResult.SourceBundleArtifactPath,
            ReconstructedArtifactPaths = bestPracticeResult.ReconstructedArtifactPaths,
            ReviewArtifactPath = bestPracticeResult.ReviewArtifactPath,
            DecisionFilePath = ToDisplayPath(context.RepoRoot, decisionFilePath),
            ConfirmedItems = decisionFile.ConfirmedItems,
            RejectedItems = decisionFile.RejectedItems,
            ClarifyItems = decisionFile.ClarifyItems,
            DeferredItems = decisionFile.DeferredItems,
            BlockedItems = blockedItems,
            DownstreamReadiness = downstreamReadiness,
            ReturnToIntentPaths = bestPracticeResult.ReturnToIntentPaths
        };

        var artifactPath = WriteArtifact(context.RepoRoot, domain, DeveloperConfirmationArtifactYaml.Serialize(artifact));

        return new GenerateFromCurrentConfirmResult
        {
            Domain = domain,
            SourceBundleArtifactPath = bestPracticeResult.SourceBundleArtifactPath,
            ReconstructedArtifactPaths = bestPracticeResult.ReconstructedArtifactPaths,
            ReviewArtifactPath = bestPracticeResult.ReviewArtifactPath,
            DecisionFilePath = artifact.DecisionFilePath,
            ConfirmationArtifactPath = ToRelativePath(context.RepoRoot, artifactPath),
            ConfirmedItems = decisionFile.ConfirmedItems,
            RejectedItems = decisionFile.RejectedItems,
            ClarifyItems = decisionFile.ClarifyItems,
            DeferredItems = decisionFile.DeferredItems,
            BlockedItems = blockedItems,
            DownstreamReadiness = downstreamReadiness,
            ReturnToIntentPaths = bestPracticeResult.ReturnToIntentPaths
        };
    }

    private static (string[] PipelineArgs, string DecisionFilePath) ParseArgs(string[] args)
    {
        if (args.Length < 3 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException(
                "Generate-from-current confirm requires a domain, source selection args, and '--from-file <path>'.");
        }

        string? decisionFilePath = null;
        var pipelineArgs = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, "--from-file", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    throw new InvalidOperationException("--from-file requires a value.");
                }

                decisionFilePath = args[index + 1];
                index++;
                continue;
            }

            pipelineArgs.Add(argument);
        }

        if (string.IsNullOrWhiteSpace(decisionFilePath))
        {
            throw new InvalidOperationException("Generate-from-current confirm requires '--from-file <path>'.");
        }

        return (pipelineArgs.ToArray(), decisionFilePath);
    }

    private static void ValidateDecisions(
        IReadOnlyList<string> currentItems,
        DeveloperDecisionFile decisionFile)
    {
        var knownItems = currentItems.ToHashSet(StringComparer.Ordinal);
        var seenItems = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in decisionFile.ConfirmedItems
                     .Concat(decisionFile.RejectedItems)
                     .Concat(decisionFile.ClarifyItems)
                     .Concat(decisionFile.DeferredItems))
        {
            if (!knownItems.Contains(item))
            {
                throw new InvalidOperationException(
                    $"Prepared developer decision item '{item}' does not match a current developer confirmation item.");
            }

            if (!seenItems.Add(item))
            {
                throw new InvalidOperationException(
                    $"Prepared developer decision item '{item}' must not appear in more than one decision bucket.");
            }
        }
    }

    private static IReadOnlyList<string> BuildBlockedItems(
        GenerateFromCurrentBestPracticeResult bestPracticeResult,
        DeveloperDecisionFile decisionFile,
        IReadOnlySet<string> decidedItems)
    {
        var blockedItems = new List<string>();

        blockedItems.AddRange(decisionFile.ClarifyItems);
        blockedItems.AddRange(decisionFile.DeferredItems);
        blockedItems.AddRange(bestPracticeResult.DeveloperConfirmationItems
            .Where(item => !decidedItems.Contains(item)));

        if (!string.Equals(bestPracticeResult.ReadinessStatus, "ready", StringComparison.Ordinal))
        {
            blockedItems.Add($"Resolve upstream reconstruction review readiness '{bestPracticeResult.ReadinessStatus}' before downstream activation.");
        }

        blockedItems.AddRange(bestPracticeResult.SkippedStages
            .Select(stage => $"Resolve upstream skipped stage '{stage}' before downstream activation."));

        return blockedItems.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string ResolveInputPath(string repoRoot, string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(repoRoot, path));
    }

    private static string ToDisplayPath(string repoRoot, string absolutePath)
    {
        var repoRootWithSeparator = repoRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return absolutePath.StartsWith(repoRootWithSeparator, StringComparison.Ordinal)
            ? ToRelativePath(repoRoot, absolutePath)
            : absolutePath;
    }

    private static string WriteArtifact(string repoRoot, string domain, string yaml)
    {
        var artifactPath = Path.Combine(
            repoRoot,
            ".intent-cli",
            "intake",
            $"{domain}.developer-confirmation.yaml");
        var directoryPath = Path.GetDirectoryName(artifactPath)
            ?? throw new InvalidOperationException("Developer confirmation artifact path did not contain a directory.");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(artifactPath, yaml);
        return artifactPath;
    }

    private static string ToRelativePath(string repoRoot, string absolutePath)
    {
        return Path.GetRelativePath(repoRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
    }
}
