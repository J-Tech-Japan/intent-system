namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentRereviewCommand
{
    private static readonly string[] DeferredRereviewStages =
    [
        "rereview-entry"
    ];

    public static Func<CliContext, string[], GenerateFromCurrentResubmitResult> ResubmitExecutor { get; set; } =
        (context, args) => GenerateFromCurrentResubmitCommand.ExecuteCore(context, args);

    public static Func<CliContext, string, RunRereviewResult> RunRereviewExecutor { get; set; } =
        (context, executionUnit) => RunRereviewCommand.ExecuteCore(context, executionUnit);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            GenerateFromCurrentRereviewRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentRereviewResult ExecuteCore(CliContext context, string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current rereview requires a domain.");
        }

        var resubmitResult = ResubmitExecutor(context, args);
        if (!string.Equals(resubmitResult.ReadinessStatus, "ready", StringComparison.Ordinal))
        {
            return new GenerateFromCurrentRereviewResult
            {
                Domain = resubmitResult.Domain,
                SourceBundleArtifactPath = resubmitResult.SourceBundleArtifactPath,
                ReconstructedArtifactPaths = resubmitResult.ReconstructedArtifactPaths,
                StandardIntakeArtifactPaths = resubmitResult.StandardIntakeArtifactPaths,
                UpdatedSourceFilePaths = resubmitResult.UpdatedSourceFilePaths,
                UpdatedExecutionFilePaths = resubmitResult.UpdatedExecutionFilePaths,
                GeneratedIssueArtifactPaths = resubmitResult.GeneratedIssueArtifactPaths,
                CreatedIssueRefs = resubmitResult.CreatedIssueRefs,
                WorktreePaths = resubmitResult.WorktreePaths,
                StartedExecutionUnits = resubmitResult.StartedExecutionUnits,
                ImplementRequestArtifactPaths = resubmitResult.ImplementRequestArtifactPaths,
                CreatedPrRefs = resubmitResult.CreatedPrRefs,
                ReviewExecutionUnits = resubmitResult.ReviewExecutionUnits,
                ReviewRequestArtifactPaths = resubmitResult.ReviewRequestArtifactPaths,
                PostedCommentArtifactPaths = resubmitResult.PostedCommentArtifactPaths,
                CommentRefs = resubmitResult.CommentRefs,
                FixingExecutionUnits = resubmitResult.FixingExecutionUnits,
                FixRequestArtifactPaths = resubmitResult.FixRequestArtifactPaths,
                ResubmittedExecutionUnits = resubmitResult.ResubmittedExecutionUnits,
                ResubmittedPrRefs = resubmitResult.ResubmittedPrRefs,
                RereviewedExecutionUnits = [],
                RereviewedPrRefs = [],
                ReadinessStatus = resubmitResult.ReadinessStatus,
                SkippedStages = resubmitResult.SkippedStages.Concat(DeferredRereviewStages).ToArray()
            };
        }

        var rereviewResults = resubmitResult.ResubmittedExecutionUnits
            .Select(executionUnit => RunRereviewExecutor(context, executionUnit))
            .ToArray();

        var skippedStages = new List<string>(resubmitResult.SkippedStages);
        if (rereviewResults.Length == 0)
        {
            skippedStages.Add("rereview-entry");
        }

        return new GenerateFromCurrentRereviewResult
        {
            Domain = resubmitResult.Domain,
            SourceBundleArtifactPath = resubmitResult.SourceBundleArtifactPath,
            ReconstructedArtifactPaths = resubmitResult.ReconstructedArtifactPaths,
            StandardIntakeArtifactPaths = resubmitResult.StandardIntakeArtifactPaths,
            UpdatedSourceFilePaths = resubmitResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = resubmitResult.UpdatedExecutionFilePaths,
            GeneratedIssueArtifactPaths = resubmitResult.GeneratedIssueArtifactPaths,
            CreatedIssueRefs = resubmitResult.CreatedIssueRefs,
            WorktreePaths = resubmitResult.WorktreePaths,
            StartedExecutionUnits = resubmitResult.StartedExecutionUnits,
            ImplementRequestArtifactPaths = resubmitResult.ImplementRequestArtifactPaths,
            CreatedPrRefs = resubmitResult.CreatedPrRefs,
            ReviewExecutionUnits = resubmitResult.ReviewExecutionUnits,
            ReviewRequestArtifactPaths = resubmitResult.ReviewRequestArtifactPaths,
            PostedCommentArtifactPaths = resubmitResult.PostedCommentArtifactPaths,
            CommentRefs = resubmitResult.CommentRefs,
            FixingExecutionUnits = resubmitResult.FixingExecutionUnits,
            FixRequestArtifactPaths = resubmitResult.FixRequestArtifactPaths,
            ResubmittedExecutionUnits = resubmitResult.ResubmittedExecutionUnits,
            ResubmittedPrRefs = resubmitResult.ResubmittedPrRefs,
            RereviewedExecutionUnits = rereviewResults.Select(result => result.ExecutionUnit).ToArray(),
            RereviewedPrRefs = rereviewResults.Select(result => result.LinkedPr).ToArray(),
            ReadinessStatus = resubmitResult.ReadinessStatus,
            SkippedStages = skippedStages
        };
    }
}
