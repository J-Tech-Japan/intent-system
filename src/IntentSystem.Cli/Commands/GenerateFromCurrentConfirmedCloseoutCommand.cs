namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentConfirmedCloseoutCommand
{
    private const string AcceptedCloseoutPath = "accepted-closeout";
    private const string RepairAcceptedCloseoutPath = "repair-in-place-accepted-closeout";

    public static Func<CliContext, string[], TextWriter, GenerateFromCurrentConfirmedAcceptResult> ConfirmedAcceptExecutor { get; set; } =
        (context, args, writer) => GenerateFromCurrentConfirmedAcceptCommand.ExecuteCore(context, args, writer);

    public static Func<CliContext, string[], TextWriter, GenerateFromCurrentConfirmedReacceptResult> ConfirmedReacceptExecutor { get; set; } =
        (context, args, writer) => GenerateFromCurrentConfirmedReacceptCommand.ExecuteCore(context, args, writer);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args, writer);
            GenerateFromCurrentConfirmedCloseoutRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentConfirmedCloseoutResult ExecuteCore(
        CliContext context,
        string[] args,
        TextWriter writer)
    {
        var (pipelineArgs, hasPreparedRepairComment) = ParseArgs(args);
        if (hasPreparedRepairComment)
        {
            var confirmedReacceptResult = ConfirmedReacceptExecutor(context, args, writer);
            return new GenerateFromCurrentConfirmedCloseoutResult
            {
                Domain = confirmedReacceptResult.Domain,
                ClarificationReturnArtifactPath = confirmedReacceptResult.ClarificationReturnArtifactPath,
                ConfirmedReconstructionArtifactPath = confirmedReacceptResult.ConfirmedReconstructionArtifactPath,
                UpdatedSourceFilePaths = confirmedReacceptResult.UpdatedSourceFilePaths,
                UpdatedExecutionFilePaths = confirmedReacceptResult.UpdatedExecutionFilePaths,
                RegeneratedArtifactPaths = confirmedReacceptResult.RegeneratedArtifactPaths,
                StartedExecutionUnits = confirmedReacceptResult.StartedExecutionUnits,
                CreatedIssueRefs = confirmedReacceptResult.CreatedIssueRefs,
                WorktreePaths = confirmedReacceptResult.WorktreePaths,
                ImplementRequestArtifactPaths = confirmedReacceptResult.ImplementRequestArtifactPaths,
                CreatedPrRefs = confirmedReacceptResult.CreatedPrRefs,
                ReviewExecutionUnits = confirmedReacceptResult.ReviewExecutionUnits,
                ReviewRequestArtifactPaths = confirmedReacceptResult.ReviewRequestArtifactPaths,
                PostedCommentArtifactPaths = confirmedReacceptResult.PostedCommentArtifactPaths,
                CommentRefs = confirmedReacceptResult.CommentRefs,
                FixingExecutionUnits = confirmedReacceptResult.FixingExecutionUnits,
                FixRequestArtifactPaths = confirmedReacceptResult.FixRequestArtifactPaths,
                ResubmittedExecutionUnits = confirmedReacceptResult.ResubmittedExecutionUnits,
                ResubmittedPrRefs = confirmedReacceptResult.ResubmittedPrRefs,
                RereviewedExecutionUnits = confirmedReacceptResult.RereviewedExecutionUnits,
                RereviewedPrRefs = confirmedReacceptResult.RereviewedPrRefs,
                CompletedExecutionUnits = confirmedReacceptResult.CompletedExecutionUnits,
                ClosedIssueRefs = confirmedReacceptResult.ClosedIssueRefs,
                MergedPrRefs = confirmedReacceptResult.MergedPrRefs,
                ConfirmedItems = confirmedReacceptResult.ConfirmedItems,
                BlockedItems = confirmedReacceptResult.BlockedItems,
                DownstreamReadiness = confirmedReacceptResult.DownstreamReadiness,
                SelectedCloseoutPath = RepairAcceptedCloseoutPath,
                SkippedStages = [AcceptedCloseoutPath]
            };
        }

        var confirmedAcceptResult = ConfirmedAcceptExecutor(context, pipelineArgs, writer);
        return new GenerateFromCurrentConfirmedCloseoutResult
        {
            Domain = confirmedAcceptResult.Domain,
            ClarificationReturnArtifactPath = confirmedAcceptResult.ClarificationReturnArtifactPath,
            ConfirmedReconstructionArtifactPath = confirmedAcceptResult.ConfirmedReconstructionArtifactPath,
            UpdatedSourceFilePaths = confirmedAcceptResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = confirmedAcceptResult.UpdatedExecutionFilePaths,
            RegeneratedArtifactPaths = confirmedAcceptResult.RegeneratedArtifactPaths,
            StartedExecutionUnits = confirmedAcceptResult.StartedExecutionUnits,
            CreatedIssueRefs = confirmedAcceptResult.CreatedIssueRefs,
            WorktreePaths = confirmedAcceptResult.WorktreePaths,
            ImplementRequestArtifactPaths = confirmedAcceptResult.ImplementRequestArtifactPaths,
            CreatedPrRefs = confirmedAcceptResult.CreatedPrRefs,
            ReviewExecutionUnits = confirmedAcceptResult.ReviewExecutionUnits,
            ReviewRequestArtifactPaths = confirmedAcceptResult.ReviewRequestArtifactPaths,
            PostedCommentArtifactPaths = [],
            CommentRefs = [],
            FixingExecutionUnits = [],
            FixRequestArtifactPaths = [],
            ResubmittedExecutionUnits = [],
            ResubmittedPrRefs = [],
            RereviewedExecutionUnits = [],
            RereviewedPrRefs = [],
            CompletedExecutionUnits = confirmedAcceptResult.CompletedExecutionUnits,
            ClosedIssueRefs = confirmedAcceptResult.ClosedIssueRefs,
            MergedPrRefs = confirmedAcceptResult.MergedPrRefs,
            ConfirmedItems = confirmedAcceptResult.ConfirmedItems,
            BlockedItems = confirmedAcceptResult.BlockedItems,
            DownstreamReadiness = confirmedAcceptResult.DownstreamReadiness,
            SelectedCloseoutPath = AcceptedCloseoutPath,
            SkippedStages = [RepairAcceptedCloseoutPath]
        };
    }

    private static (string[] PipelineArgs, bool HasPreparedRepairComment) ParseArgs(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current confirmed-closeout requires a domain.");
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
