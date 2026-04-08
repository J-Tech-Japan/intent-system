namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentConfirmedCommentCommand
{
    public static Func<CliContext, string[], TextWriter, GenerateFromCurrentConfirmedReviewResult> ConfirmedReviewExecutor { get; set; } =
        (context, args, writer) => GenerateFromCurrentConfirmedReviewCommand.ExecuteCore(context, args, writer);

    public static Func<CliContext, string, string, ReviewCommentResult> ReviewCommentExecutor { get; set; } =
        (context, executionUnit, bodyPath) => ReviewCommentCommand.ExecuteCore(context, executionUnit, bodyPath);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args, writer);
            GenerateFromCurrentConfirmedCommentRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentConfirmedCommentResult ExecuteCore(
        CliContext context,
        string[] args,
        TextWriter writer)
    {
        var (pipelineArgs, bodyPath) = ParseArgs(args);
        var domain = pipelineArgs[0];
        var confirmedReviewResult = ConfirmedReviewExecutor(context, pipelineArgs, writer);

        if (!string.Equals(confirmedReviewResult.Domain, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Confirmed review result domain '{confirmedReviewResult.Domain}' does not match requested domain '{domain}'.");
        }

        if (string.Equals(confirmedReviewResult.Route, "clarification-return", StringComparison.Ordinal))
        {
            return CreateResult(domain, "clarification-return", confirmedReviewResult, [], [], []);
        }

        if (!string.Equals(confirmedReviewResult.DownstreamReadiness, "ready", StringComparison.Ordinal))
        {
            return CreateResult(domain, "reconciliation-required", confirmedReviewResult, [], [], []);
        }

        var commentResults = confirmedReviewResult.ReviewExecutionUnits
            .Select(executionUnit => ReviewCommentExecutor(context, executionUnit, bodyPath))
            .ToArray();

        return CreateResult(
            domain,
            "confirmed-comment",
            confirmedReviewResult,
            commentResults.Select(result => result.ArtifactPath).ToArray(),
            commentResults.Select(result => result.CommentRef).ToArray(),
            commentResults.Select(result => result.ExecutionUnit).ToArray());
    }

    private static GenerateFromCurrentConfirmedCommentResult CreateResult(
        string domain,
        string route,
        GenerateFromCurrentConfirmedReviewResult confirmedReviewResult,
        IReadOnlyList<string> postedCommentArtifactPaths,
        IReadOnlyList<string> commentRefs,
        IReadOnlyList<string> fixingExecutionUnits)
    {
        return new GenerateFromCurrentConfirmedCommentResult
        {
            Domain = domain,
            Route = route,
            ClarificationReturnArtifactPath = confirmedReviewResult.ClarificationReturnArtifactPath,
            ConfirmedReconstructionArtifactPath = confirmedReviewResult.ConfirmedReconstructionArtifactPath,
            UpdatedSourceFilePaths = confirmedReviewResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = confirmedReviewResult.UpdatedExecutionFilePaths,
            RegeneratedArtifactPaths = confirmedReviewResult.RegeneratedArtifactPaths,
            StartedExecutionUnits = confirmedReviewResult.StartedExecutionUnits,
            CreatedIssueRefs = confirmedReviewResult.CreatedIssueRefs,
            WorktreePaths = confirmedReviewResult.WorktreePaths,
            ImplementRequestArtifactPaths = confirmedReviewResult.ImplementRequestArtifactPaths,
            CreatedPrRefs = confirmedReviewResult.CreatedPrRefs,
            ReviewExecutionUnits = confirmedReviewResult.ReviewExecutionUnits,
            ReviewRequestArtifactPaths = confirmedReviewResult.ReviewRequestArtifactPaths,
            PostedCommentArtifactPaths = postedCommentArtifactPaths,
            CommentRefs = commentRefs,
            FixingExecutionUnits = fixingExecutionUnits,
            ConfirmedItems = confirmedReviewResult.ConfirmedItems,
            BlockedItems = confirmedReviewResult.BlockedItems,
            DownstreamReadiness = confirmedReviewResult.DownstreamReadiness
        };
    }

    private static (string[] PipelineArgs, string BodyPath) ParseArgs(string[] args)
    {
        if (args.Length < 3 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException(
                "Generate-from-current confirmed-comment requires a domain, source selection args, and '--from-file <path>'.");
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

        if (pipelineArgs.Count == 0 || string.IsNullOrWhiteSpace(pipelineArgs[0]))
        {
            throw new InvalidOperationException("Generate-from-current confirmed-comment requires a domain.");
        }

        if (string.IsNullOrWhiteSpace(bodyPath))
        {
            throw new InvalidOperationException("Generate-from-current confirmed-comment requires '--from-file <path>'.");
        }

        return (pipelineArgs.ToArray(), bodyPath);
    }
}
