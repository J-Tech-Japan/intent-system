namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentResubmitCommand
{
    private static readonly string[] DeferredResubmitStages =
    [
        "resubmit-trace"
    ];

    public static Func<CliContext, string[], GenerateFromCurrentFixResult> FixExecutor { get; set; } =
        (context, args) => GenerateFromCurrentFixCommand.ExecuteCore(context, args);

    public static Func<CliContext, string, RunResubmitResult> RunResubmitExecutor { get; set; } =
        (context, executionUnit) => RunResubmitCommand.ExecuteCore(context, executionUnit);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            GenerateFromCurrentResubmitRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentResubmitResult ExecuteCore(CliContext context, string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current resubmit requires a domain.");
        }

        var fixResult = FixExecutor(context, args);
        if (!string.Equals(fixResult.ReadinessStatus, "ready", StringComparison.Ordinal))
        {
            return new GenerateFromCurrentResubmitResult
            {
                Domain = fixResult.Domain,
                SourceBundleArtifactPath = fixResult.SourceBundleArtifactPath,
                ReconstructedArtifactPaths = fixResult.ReconstructedArtifactPaths,
                StandardIntakeArtifactPaths = fixResult.StandardIntakeArtifactPaths,
                UpdatedSourceFilePaths = fixResult.UpdatedSourceFilePaths,
                UpdatedExecutionFilePaths = fixResult.UpdatedExecutionFilePaths,
                GeneratedIssueArtifactPaths = fixResult.GeneratedIssueArtifactPaths,
                CreatedIssueRefs = fixResult.CreatedIssueRefs,
                WorktreePaths = fixResult.WorktreePaths,
                StartedExecutionUnits = fixResult.StartedExecutionUnits,
                ImplementRequestArtifactPaths = fixResult.ImplementRequestArtifactPaths,
                CreatedPrRefs = fixResult.CreatedPrRefs,
                ReviewExecutionUnits = fixResult.ReviewExecutionUnits,
                ReviewRequestArtifactPaths = fixResult.ReviewRequestArtifactPaths,
                PostedCommentArtifactPaths = fixResult.PostedCommentArtifactPaths,
                CommentRefs = fixResult.CommentRefs,
                FixingExecutionUnits = fixResult.FixingExecutionUnits,
                FixRequestArtifactPaths = fixResult.FixRequestArtifactPaths,
                ResubmittedExecutionUnits = [],
                ResubmittedPrRefs = [],
                ReadinessStatus = fixResult.ReadinessStatus,
                SkippedStages = fixResult.SkippedStages.Concat(DeferredResubmitStages).ToArray()
            };
        }

        var resubmitResults = fixResult.FixingExecutionUnits
            .Select(executionUnit => RunResubmitExecutor(context, executionUnit))
            .ToArray();

        var skippedStages = new List<string>(fixResult.SkippedStages);
        if (resubmitResults.Length == 0)
        {
            skippedStages.Add("resubmit-trace");
        }

        return new GenerateFromCurrentResubmitResult
        {
            Domain = fixResult.Domain,
            SourceBundleArtifactPath = fixResult.SourceBundleArtifactPath,
            ReconstructedArtifactPaths = fixResult.ReconstructedArtifactPaths,
            StandardIntakeArtifactPaths = fixResult.StandardIntakeArtifactPaths,
            UpdatedSourceFilePaths = fixResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = fixResult.UpdatedExecutionFilePaths,
            GeneratedIssueArtifactPaths = fixResult.GeneratedIssueArtifactPaths,
            CreatedIssueRefs = fixResult.CreatedIssueRefs,
            WorktreePaths = fixResult.WorktreePaths,
            StartedExecutionUnits = fixResult.StartedExecutionUnits,
            ImplementRequestArtifactPaths = fixResult.ImplementRequestArtifactPaths,
            CreatedPrRefs = fixResult.CreatedPrRefs,
            ReviewExecutionUnits = fixResult.ReviewExecutionUnits,
            ReviewRequestArtifactPaths = fixResult.ReviewRequestArtifactPaths,
            PostedCommentArtifactPaths = fixResult.PostedCommentArtifactPaths,
            CommentRefs = fixResult.CommentRefs,
            FixingExecutionUnits = fixResult.FixingExecutionUnits,
            FixRequestArtifactPaths = fixResult.FixRequestArtifactPaths,
            ResubmittedExecutionUnits = resubmitResults.Select(result => result.ExecutionUnit).ToArray(),
            ResubmittedPrRefs = resubmitResults.Select(result => result.LinkedPr).ToArray(),
            ReadinessStatus = fixResult.ReadinessStatus,
            SkippedStages = skippedStages
        };
    }
}
