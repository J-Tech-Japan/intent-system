namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentCommentCommand
{
    private static readonly string[] DeferredCommentStages =
    [
        "review-comment"
    ];

    public static Func<CliContext, string[], GenerateFromCurrentReviewResult> ReviewExecutor { get; set; } =
        (context, args) => GenerateFromCurrentReviewCommand.ExecuteCore(context, args);

    public static Func<CliContext, string, string, ReviewCommentResult> ReviewCommentExecutor { get; set; } =
        (context, executionUnit, bodyPath) => ReviewCommentCommand.ExecuteCore(context, executionUnit, bodyPath);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            GenerateFromCurrentCommentRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentCommentResult ExecuteCore(CliContext context, string[] args)
    {
        var (pipelineArgs, bodyPath) = ParseArgs(args);
        var reviewResult = ReviewExecutor(context, pipelineArgs);
        if (!string.Equals(reviewResult.ReadinessStatus, "ready", StringComparison.Ordinal))
        {
            return new GenerateFromCurrentCommentResult
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
                PostedCommentArtifactPaths = [],
                CommentRefs = [],
                FixingExecutionUnits = [],
                ReadinessStatus = reviewResult.ReadinessStatus,
                SkippedStages = reviewResult.SkippedStages.Concat(DeferredCommentStages).ToArray()
            };
        }

        var commentResults = reviewResult.ReviewExecutionUnits
            .Select(executionUnit => ReviewCommentExecutor(context, executionUnit, bodyPath))
            .ToArray();

        var skippedStages = new List<string>(reviewResult.SkippedStages);
        if (commentResults.Length == 0)
        {
            skippedStages.Add("review-comment");
        }

        return new GenerateFromCurrentCommentResult
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
            PostedCommentArtifactPaths = commentResults.Select(result => result.ArtifactPath).ToArray(),
            CommentRefs = commentResults.Select(result => result.CommentRef).ToArray(),
            FixingExecutionUnits = commentResults.Select(result => result.ExecutionUnit).ToArray(),
            ReadinessStatus = reviewResult.ReadinessStatus,
            SkippedStages = skippedStages
        };
    }

    private static (string[] PipelineArgs, string BodyPath) ParseArgs(string[] args)
    {
        if (args.Length < 3 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException(
                "Generate-from-current comment requires a domain, source selection args, and '--from-file <path>'.");
        }

        string? bodyPath = null;
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

                bodyPath = args[index + 1];
                index++;
                continue;
            }

            pipelineArgs.Add(argument);
        }

        if (string.IsNullOrWhiteSpace(bodyPath))
        {
            throw new InvalidOperationException("Generate-from-current comment requires '--from-file <path>'.");
        }

        return (pipelineArgs.ToArray(), bodyPath);
    }
}
