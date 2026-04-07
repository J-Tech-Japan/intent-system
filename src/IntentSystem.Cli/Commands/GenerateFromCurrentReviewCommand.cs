namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentReviewCommand
{
    private static readonly string[] DeferredReviewRequestStages =
    [
        "review-request"
    ];

    public static Func<CliContext, string[], GenerateFromCurrentSubmitResult> SubmitExecutor { get; set; } =
        (context, args) => GenerateFromCurrentSubmitCommand.ExecuteCore(context, args);

    public static Func<CliContext, string, ReviewRunResult> ReviewRunExecutor { get; set; } =
        (context, executionUnit) => ReviewRunCommand.ExecuteCore(context, executionUnit);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            GenerateFromCurrentReviewRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentReviewResult ExecuteCore(CliContext context, string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current review requires a domain.");
        }

        var submitResult = SubmitExecutor(context, args);
        if (!string.Equals(submitResult.ReadinessStatus, "ready", StringComparison.Ordinal))
        {
            return new GenerateFromCurrentReviewResult
            {
                Domain = submitResult.Domain,
                SourceBundleArtifactPath = submitResult.SourceBundleArtifactPath,
                ReconstructedArtifactPaths = submitResult.ReconstructedArtifactPaths,
                StandardIntakeArtifactPaths = submitResult.StandardIntakeArtifactPaths,
                UpdatedSourceFilePaths = submitResult.UpdatedSourceFilePaths,
                UpdatedExecutionFilePaths = submitResult.UpdatedExecutionFilePaths,
                GeneratedIssueArtifactPaths = submitResult.GeneratedIssueArtifactPaths,
                CreatedIssueRefs = submitResult.CreatedIssueRefs,
                WorktreePaths = submitResult.WorktreePaths,
                StartedExecutionUnits = submitResult.StartedExecutionUnits,
                ImplementRequestArtifactPaths = submitResult.ImplementRequestArtifactPaths,
                CreatedPrRefs = submitResult.CreatedPrRefs,
                ReviewExecutionUnits = submitResult.ReviewExecutionUnits,
                ReviewRequestArtifactPaths = [],
                ReadinessStatus = submitResult.ReadinessStatus,
                SkippedStages = submitResult.SkippedStages.Concat(DeferredReviewRequestStages).ToArray()
            };
        }

        var reviewResults = submitResult.ReviewExecutionUnits
            .Select(executionUnit => ReviewRunExecutor(context, executionUnit))
            .ToArray();

        var skippedStages = new List<string>(submitResult.SkippedStages);
        if (reviewResults.Length == 0)
        {
            skippedStages.Add("review-request");
        }

        return new GenerateFromCurrentReviewResult
        {
            Domain = submitResult.Domain,
            SourceBundleArtifactPath = submitResult.SourceBundleArtifactPath,
            ReconstructedArtifactPaths = submitResult.ReconstructedArtifactPaths,
            StandardIntakeArtifactPaths = submitResult.StandardIntakeArtifactPaths,
            UpdatedSourceFilePaths = submitResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = submitResult.UpdatedExecutionFilePaths,
            GeneratedIssueArtifactPaths = submitResult.GeneratedIssueArtifactPaths,
            CreatedIssueRefs = submitResult.CreatedIssueRefs,
            WorktreePaths = submitResult.WorktreePaths,
            StartedExecutionUnits = submitResult.StartedExecutionUnits,
            ImplementRequestArtifactPaths = submitResult.ImplementRequestArtifactPaths,
            CreatedPrRefs = submitResult.CreatedPrRefs,
            ReviewExecutionUnits = submitResult.ReviewExecutionUnits,
            ReviewRequestArtifactPaths = reviewResults.Select(result => result.ArtifactPath).ToArray(),
            ReadinessStatus = submitResult.ReadinessStatus,
            SkippedStages = skippedStages
        };
    }
}
