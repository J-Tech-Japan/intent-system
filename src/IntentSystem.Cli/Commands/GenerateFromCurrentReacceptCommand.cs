namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentReacceptCommand
{
    private static readonly string[] DeferredReacceptStages =
    [
        "accepted-closeout"
    ];

    public static Func<CliContext, string[], GenerateFromCurrentRereviewResult> RereviewExecutor { get; set; } =
        (context, args) => GenerateFromCurrentRereviewCommand.ExecuteCore(context, args);

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
            GenerateFromCurrentReacceptRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentReacceptResult ExecuteCore(CliContext context, string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current reaccept requires a domain.");
        }

        var rereviewResult = RereviewExecutor(context, args);
        if (!string.Equals(rereviewResult.ReadinessStatus, "ready", StringComparison.Ordinal))
        {
            return new GenerateFromCurrentReacceptResult
            {
                Domain = rereviewResult.Domain,
                SourceBundleArtifactPath = rereviewResult.SourceBundleArtifactPath,
                ReconstructedArtifactPaths = rereviewResult.ReconstructedArtifactPaths,
                StandardIntakeArtifactPaths = rereviewResult.StandardIntakeArtifactPaths,
                UpdatedSourceFilePaths = rereviewResult.UpdatedSourceFilePaths,
                UpdatedExecutionFilePaths = rereviewResult.UpdatedExecutionFilePaths,
                GeneratedIssueArtifactPaths = rereviewResult.GeneratedIssueArtifactPaths,
                CreatedIssueRefs = rereviewResult.CreatedIssueRefs,
                WorktreePaths = rereviewResult.WorktreePaths,
                StartedExecutionUnits = rereviewResult.StartedExecutionUnits,
                ImplementRequestArtifactPaths = rereviewResult.ImplementRequestArtifactPaths,
                CreatedPrRefs = rereviewResult.CreatedPrRefs,
                ReviewExecutionUnits = rereviewResult.ReviewExecutionUnits,
                ReviewRequestArtifactPaths = rereviewResult.ReviewRequestArtifactPaths,
                PostedCommentArtifactPaths = rereviewResult.PostedCommentArtifactPaths,
                CommentRefs = rereviewResult.CommentRefs,
                FixingExecutionUnits = rereviewResult.FixingExecutionUnits,
                FixRequestArtifactPaths = rereviewResult.FixRequestArtifactPaths,
                ResubmittedExecutionUnits = rereviewResult.ResubmittedExecutionUnits,
                ResubmittedPrRefs = rereviewResult.ResubmittedPrRefs,
                RereviewedExecutionUnits = rereviewResult.RereviewedExecutionUnits,
                RereviewedPrRefs = rereviewResult.RereviewedPrRefs,
                CompletedExecutionUnits = [],
                ClosedIssueRefs = [],
                MergedPrRefs = [],
                ReadinessStatus = rereviewResult.ReadinessStatus,
                SkippedStages = rereviewResult.SkippedStages.Concat(DeferredReacceptStages).ToArray()
            };
        }

        var acceptResults = rereviewResult.RereviewedExecutionUnits
            .Select(executionUnit => ReviewAcceptExecutor(context, executionUnit))
            .ToArray();

        var skippedStages = new List<string>(rereviewResult.SkippedStages);
        if (acceptResults.Length == 0)
        {
            skippedStages.Add("accepted-closeout");
        }

        return new GenerateFromCurrentReacceptResult
        {
            Domain = rereviewResult.Domain,
            SourceBundleArtifactPath = rereviewResult.SourceBundleArtifactPath,
            ReconstructedArtifactPaths = rereviewResult.ReconstructedArtifactPaths,
            StandardIntakeArtifactPaths = rereviewResult.StandardIntakeArtifactPaths,
            UpdatedSourceFilePaths = rereviewResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = rereviewResult.UpdatedExecutionFilePaths,
            GeneratedIssueArtifactPaths = rereviewResult.GeneratedIssueArtifactPaths,
            CreatedIssueRefs = rereviewResult.CreatedIssueRefs,
            WorktreePaths = rereviewResult.WorktreePaths,
            StartedExecutionUnits = rereviewResult.StartedExecutionUnits,
            ImplementRequestArtifactPaths = rereviewResult.ImplementRequestArtifactPaths,
            CreatedPrRefs = rereviewResult.CreatedPrRefs,
            ReviewExecutionUnits = rereviewResult.ReviewExecutionUnits,
            ReviewRequestArtifactPaths = rereviewResult.ReviewRequestArtifactPaths,
            PostedCommentArtifactPaths = rereviewResult.PostedCommentArtifactPaths,
            CommentRefs = rereviewResult.CommentRefs,
            FixingExecutionUnits = rereviewResult.FixingExecutionUnits,
            FixRequestArtifactPaths = rereviewResult.FixRequestArtifactPaths,
            ResubmittedExecutionUnits = rereviewResult.ResubmittedExecutionUnits,
            ResubmittedPrRefs = rereviewResult.ResubmittedPrRefs,
            RereviewedExecutionUnits = rereviewResult.RereviewedExecutionUnits,
            RereviewedPrRefs = rereviewResult.RereviewedPrRefs,
            CompletedExecutionUnits = acceptResults.Select(result => result.ExecutionUnit).ToArray(),
            ClosedIssueRefs = acceptResults.Select(result => result.ClosedIssueRef).ToArray(),
            MergedPrRefs = acceptResults.Select(result => result.MergedPrRef).ToArray(),
            ReadinessStatus = rereviewResult.ReadinessStatus,
            SkippedStages = skippedStages
        };
    }
}
