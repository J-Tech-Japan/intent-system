namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentAcceptCommand
{
    private static readonly string[] DeferredAcceptStages =
    [
        "accepted-closeout"
    ];

    public static Func<CliContext, string[], GenerateFromCurrentReviewResult> ReviewExecutor { get; set; } =
        (context, args) => GenerateFromCurrentReviewCommand.ExecuteCore(context, args);

    public static Func<CliContext, string, ReviewAcceptResult> ReviewAcceptExecutor { get; set; } =
        (context, executionUnit) => ReviewAcceptCommand.ExecuteCore(context, executionUnit);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            GenerateFromCurrentAcceptRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentAcceptResult ExecuteCore(CliContext context, string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current accept requires a domain.");
        }

        var reviewResult = ReviewExecutor(context, args);
        if (!string.Equals(reviewResult.ReadinessStatus, "ready", StringComparison.Ordinal))
        {
            return new GenerateFromCurrentAcceptResult
            {
                Domain = reviewResult.Domain,
                SourceBundleArtifactPath = reviewResult.SourceBundleArtifactPath,
                ReconstructedArtifactPaths = reviewResult.ReconstructedArtifactPaths,
                StandardIntakeArtifactPaths = reviewResult.StandardIntakeArtifactPaths,
                UpdatedSourceFilePaths = reviewResult.UpdatedSourceFilePaths,
                UpdatedExecutionFilePaths = reviewResult.UpdatedExecutionFilePaths,
                GeneratedIssueArtifactPaths = reviewResult.GeneratedIssueArtifactPaths,
                CreatedIssueRefs = reviewResult.CreatedIssueRefs,
                WorktreePaths = reviewResult.WorktreePaths,
                StartedExecutionUnits = reviewResult.StartedExecutionUnits,
                ImplementRequestArtifactPaths = reviewResult.ImplementRequestArtifactPaths,
                CreatedPrRefs = reviewResult.CreatedPrRefs,
                ReviewExecutionUnits = reviewResult.ReviewExecutionUnits,
                ReviewRequestArtifactPaths = reviewResult.ReviewRequestArtifactPaths,
                MergedPrRefs = [],
                ClosedIssueRefs = [],
                CompletedExecutionUnits = [],
                ReadinessStatus = reviewResult.ReadinessStatus,
                SkippedStages = reviewResult.SkippedStages.Concat(DeferredAcceptStages).ToArray()
            };
        }

        var acceptResults = reviewResult.ReviewExecutionUnits
            .Select(executionUnit => ReviewAcceptExecutor(context, executionUnit))
            .ToArray();

        var skippedStages = new List<string>(reviewResult.SkippedStages);
        if (acceptResults.Length == 0)
        {
            skippedStages.Add("accepted-closeout");
        }

        return new GenerateFromCurrentAcceptResult
        {
            Domain = reviewResult.Domain,
            SourceBundleArtifactPath = reviewResult.SourceBundleArtifactPath,
            ReconstructedArtifactPaths = reviewResult.ReconstructedArtifactPaths,
            StandardIntakeArtifactPaths = reviewResult.StandardIntakeArtifactPaths,
            UpdatedSourceFilePaths = reviewResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = reviewResult.UpdatedExecutionFilePaths,
            GeneratedIssueArtifactPaths = reviewResult.GeneratedIssueArtifactPaths,
            CreatedIssueRefs = reviewResult.CreatedIssueRefs,
            WorktreePaths = reviewResult.WorktreePaths,
            StartedExecutionUnits = reviewResult.StartedExecutionUnits,
            ImplementRequestArtifactPaths = reviewResult.ImplementRequestArtifactPaths,
            CreatedPrRefs = reviewResult.CreatedPrRefs,
            ReviewExecutionUnits = reviewResult.ReviewExecutionUnits,
            ReviewRequestArtifactPaths = reviewResult.ReviewRequestArtifactPaths,
            MergedPrRefs = acceptResults.Select(result => result.MergedPrRef).ToArray(),
            ClosedIssueRefs = acceptResults.Select(result => result.ClosedIssueRef).ToArray(),
            CompletedExecutionUnits = acceptResults.Select(result => result.ExecutionUnit).ToArray(),
            ReadinessStatus = reviewResult.ReadinessStatus,
            SkippedStages = skippedStages
        };
    }
}
