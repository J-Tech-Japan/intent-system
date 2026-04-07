namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentFixCommand
{
    private static readonly string[] DeferredFixStages =
    [
        "fix-handoff"
    ];

    public static Func<CliContext, string[], GenerateFromCurrentCommentResult> CommentExecutor { get; set; } =
        (context, args) => GenerateFromCurrentCommentCommand.ExecuteCore(context, args);

    public static Func<CliContext, string, RunFixResult> RunFixExecutor { get; set; } =
        (context, executionUnit) => RunFixCommand.ExecuteCore(context, executionUnit);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            GenerateFromCurrentFixRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentFixResult ExecuteCore(CliContext context, string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current fix requires a domain.");
        }

        var commentResult = CommentExecutor(context, args);
        if (!string.Equals(commentResult.ReadinessStatus, "ready", StringComparison.Ordinal))
        {
            return new GenerateFromCurrentFixResult
            {
                Domain = commentResult.Domain,
                SourceBundleArtifactPath = commentResult.SourceBundleArtifactPath,
                ReconstructedArtifactPaths = commentResult.ReconstructedArtifactPaths,
                StandardIntakeArtifactPaths = commentResult.StandardIntakeArtifactPaths,
                UpdatedSourceFilePaths = commentResult.UpdatedSourceFilePaths,
                UpdatedExecutionFilePaths = commentResult.UpdatedExecutionFilePaths,
                GeneratedIssueArtifactPaths = commentResult.GeneratedIssueArtifactPaths,
                CreatedIssueRefs = commentResult.CreatedIssueRefs,
                WorktreePaths = commentResult.WorktreePaths,
                StartedExecutionUnits = commentResult.StartedExecutionUnits,
                ImplementRequestArtifactPaths = commentResult.ImplementRequestArtifactPaths,
                CreatedPrRefs = commentResult.CreatedPrRefs,
                ReviewExecutionUnits = commentResult.ReviewExecutionUnits,
                ReviewRequestArtifactPaths = commentResult.ReviewRequestArtifactPaths,
                PostedCommentArtifactPaths = commentResult.PostedCommentArtifactPaths,
                CommentRefs = commentResult.CommentRefs,
                FixingExecutionUnits = commentResult.FixingExecutionUnits,
                FixRequestArtifactPaths = [],
                ReadinessStatus = commentResult.ReadinessStatus,
                SkippedStages = commentResult.SkippedStages.Concat(DeferredFixStages).ToArray()
            };
        }

        var fixResults = commentResult.FixingExecutionUnits
            .Select(executionUnit => RunFixExecutor(context, executionUnit))
            .ToArray();

        var skippedStages = new List<string>(commentResult.SkippedStages);
        if (fixResults.Length == 0)
        {
            skippedStages.Add("fix-handoff");
        }

        return new GenerateFromCurrentFixResult
        {
            Domain = commentResult.Domain,
            SourceBundleArtifactPath = commentResult.SourceBundleArtifactPath,
            ReconstructedArtifactPaths = commentResult.ReconstructedArtifactPaths,
            StandardIntakeArtifactPaths = commentResult.StandardIntakeArtifactPaths,
            UpdatedSourceFilePaths = commentResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = commentResult.UpdatedExecutionFilePaths,
            GeneratedIssueArtifactPaths = commentResult.GeneratedIssueArtifactPaths,
            CreatedIssueRefs = commentResult.CreatedIssueRefs,
            WorktreePaths = commentResult.WorktreePaths,
            StartedExecutionUnits = commentResult.StartedExecutionUnits,
            ImplementRequestArtifactPaths = commentResult.ImplementRequestArtifactPaths,
            CreatedPrRefs = commentResult.CreatedPrRefs,
            ReviewExecutionUnits = commentResult.ReviewExecutionUnits,
            ReviewRequestArtifactPaths = commentResult.ReviewRequestArtifactPaths,
            PostedCommentArtifactPaths = commentResult.PostedCommentArtifactPaths,
            CommentRefs = commentResult.CommentRefs,
            FixingExecutionUnits = commentResult.FixingExecutionUnits,
            FixRequestArtifactPaths = fixResults.Select(result => result.ArtifactPath).ToArray(),
            ReadinessStatus = commentResult.ReadinessStatus,
            SkippedStages = skippedStages
        };
    }
}
