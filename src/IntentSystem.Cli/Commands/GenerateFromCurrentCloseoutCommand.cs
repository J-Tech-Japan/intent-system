namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentCloseoutCommand
{
    private const string AcceptedCloseoutPath = "accepted-closeout";
    private const string RepairAcceptedCloseoutPath = "repair-in-place-accepted-closeout";

    public static Func<CliContext, string[], GenerateFromCurrentAcceptResult> AcceptExecutor { get; set; } =
        (context, args) => GenerateFromCurrentAcceptCommand.ExecuteCore(context, args);

    public static Func<CliContext, string[], GenerateFromCurrentReacceptResult> ReacceptExecutor { get; set; } =
        (context, args) => GenerateFromCurrentReacceptCommand.ExecuteCore(context, args);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            GenerateFromCurrentCloseoutRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentCloseoutResult ExecuteCore(CliContext context, string[] args)
    {
        var (pipelineArgs, hasPreparedRepairComment) = ParseArgs(args);
        if (hasPreparedRepairComment)
        {
            var reacceptResult = ReacceptExecutor(context, args);
            return new GenerateFromCurrentCloseoutResult
            {
                Domain = reacceptResult.Domain,
                SourceBundleArtifactPath = reacceptResult.SourceBundleArtifactPath,
                ReconstructedArtifactPaths = reacceptResult.ReconstructedArtifactPaths,
                StandardIntakeArtifactPaths = reacceptResult.StandardIntakeArtifactPaths,
                UpdatedSourceFilePaths = reacceptResult.UpdatedSourceFilePaths,
                UpdatedExecutionFilePaths = reacceptResult.UpdatedExecutionFilePaths,
                GeneratedIssueArtifactPaths = reacceptResult.GeneratedIssueArtifactPaths,
                CreatedIssueRefs = reacceptResult.CreatedIssueRefs,
                WorktreePaths = reacceptResult.WorktreePaths,
                StartedExecutionUnits = reacceptResult.StartedExecutionUnits,
                ImplementRequestArtifactPaths = reacceptResult.ImplementRequestArtifactPaths,
                CreatedPrRefs = reacceptResult.CreatedPrRefs,
                ReviewExecutionUnits = reacceptResult.ReviewExecutionUnits,
                ReviewRequestArtifactPaths = reacceptResult.ReviewRequestArtifactPaths,
                PostedCommentArtifactPaths = reacceptResult.PostedCommentArtifactPaths,
                CommentRefs = reacceptResult.CommentRefs,
                FixingExecutionUnits = reacceptResult.FixingExecutionUnits,
                FixRequestArtifactPaths = reacceptResult.FixRequestArtifactPaths,
                ResubmittedExecutionUnits = reacceptResult.ResubmittedExecutionUnits,
                ResubmittedPrRefs = reacceptResult.ResubmittedPrRefs,
                RereviewedExecutionUnits = reacceptResult.RereviewedExecutionUnits,
                CompletedExecutionUnits = reacceptResult.CompletedExecutionUnits,
                ClosedIssueRefs = reacceptResult.ClosedIssueRefs,
                MergedPrRefs = reacceptResult.MergedPrRefs,
                ReadinessStatus = reacceptResult.ReadinessStatus,
                SelectedCloseoutPath = RepairAcceptedCloseoutPath,
                SkippedStages = reacceptResult.SkippedStages
            };
        }

        var acceptResult = AcceptExecutor(context, pipelineArgs);
        return new GenerateFromCurrentCloseoutResult
        {
            Domain = acceptResult.Domain,
            SourceBundleArtifactPath = acceptResult.SourceBundleArtifactPath,
            ReconstructedArtifactPaths = acceptResult.ReconstructedArtifactPaths,
            StandardIntakeArtifactPaths = acceptResult.StandardIntakeArtifactPaths,
            UpdatedSourceFilePaths = acceptResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = acceptResult.UpdatedExecutionFilePaths,
            GeneratedIssueArtifactPaths = acceptResult.GeneratedIssueArtifactPaths,
            CreatedIssueRefs = acceptResult.CreatedIssueRefs,
            WorktreePaths = acceptResult.WorktreePaths,
            StartedExecutionUnits = acceptResult.StartedExecutionUnits,
            ImplementRequestArtifactPaths = acceptResult.ImplementRequestArtifactPaths,
            CreatedPrRefs = acceptResult.CreatedPrRefs,
            ReviewExecutionUnits = acceptResult.ReviewExecutionUnits,
            ReviewRequestArtifactPaths = acceptResult.ReviewRequestArtifactPaths,
            PostedCommentArtifactPaths = [],
            CommentRefs = [],
            FixingExecutionUnits = [],
            FixRequestArtifactPaths = [],
            ResubmittedExecutionUnits = [],
            ResubmittedPrRefs = [],
            RereviewedExecutionUnits = [],
            CompletedExecutionUnits = acceptResult.CompletedExecutionUnits,
            ClosedIssueRefs = acceptResult.ClosedIssueRefs,
            MergedPrRefs = acceptResult.MergedPrRefs,
            ReadinessStatus = acceptResult.ReadinessStatus,
            SelectedCloseoutPath = AcceptedCloseoutPath,
            SkippedStages = acceptResult.SkippedStages
        };
    }

    private static (string[] PipelineArgs, bool HasPreparedRepairComment) ParseArgs(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current closeout requires a domain.");
        }

        var pipelineArgs = new List<string>();
        var hasPreparedRepairComment = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, "--from-file", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    throw new InvalidOperationException("--from-file requires a value.");
                }

                hasPreparedRepairComment = true;
                index++;
                continue;
            }

            pipelineArgs.Add(argument);
        }

        return (pipelineArgs.ToArray(), hasPreparedRepairComment);
    }
}
