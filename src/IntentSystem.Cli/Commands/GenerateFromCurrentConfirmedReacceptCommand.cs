namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentConfirmedReacceptCommand
{
    public static Func<CliContext, string[], TextWriter, GenerateFromCurrentConfirmedRereviewResult> ConfirmedRereviewExecutor { get; set; } =
        (context, args, writer) => GenerateFromCurrentConfirmedRereviewCommand.ExecuteCore(context, args, writer);

    public static Func<CliContext, string, ReviewAcceptResult> ReviewAcceptExecutor { get; set; } =
        (context, executionUnit) => ReviewAcceptCommand.ExecuteCore(context, executionUnit);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args, writer);
            GenerateFromCurrentConfirmedReacceptRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentConfirmedReacceptResult ExecuteCore(
        CliContext context,
        string[] args,
        TextWriter writer)
    {
        var domain = ParseDomain(args);
        var confirmedRereviewResult = ConfirmedRereviewExecutor(context, args, writer);

        if (!string.Equals(confirmedRereviewResult.Domain, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Confirmed rereview result domain '{confirmedRereviewResult.Domain}' does not match requested domain '{domain}'.");
        }

        if (string.Equals(confirmedRereviewResult.Route, "clarification-return", StringComparison.Ordinal))
        {
            return CreateResult(domain, "clarification-return", confirmedRereviewResult, [], [], []);
        }

        if (!string.Equals(confirmedRereviewResult.DownstreamReadiness, "ready", StringComparison.Ordinal))
        {
            return CreateResult(domain, "reconciliation-required", confirmedRereviewResult, [], [], []);
        }

        var acceptResults = confirmedRereviewResult.RereviewedExecutionUnits
            .Select(executionUnit => ReviewAcceptExecutor(context, executionUnit))
            .ToArray();

        return CreateResult(
            domain,
            "confirmed-reaccept",
            confirmedRereviewResult,
            acceptResults.Select(result => result.MergedPrRef).ToArray(),
            acceptResults.Select(result => result.ClosedIssueRef).ToArray(),
            acceptResults.Select(result => result.ExecutionUnit).ToArray());
    }

    private static GenerateFromCurrentConfirmedReacceptResult CreateResult(
        string domain,
        string route,
        GenerateFromCurrentConfirmedRereviewResult confirmedRereviewResult,
        IReadOnlyList<string> mergedPrRefs,
        IReadOnlyList<string> closedIssueRefs,
        IReadOnlyList<string> completedExecutionUnits)
    {
        return new GenerateFromCurrentConfirmedReacceptResult
        {
            Domain = domain,
            Route = route,
            ClarificationReturnArtifactPath = confirmedRereviewResult.ClarificationReturnArtifactPath,
            ConfirmedReconstructionArtifactPath = confirmedRereviewResult.ConfirmedReconstructionArtifactPath,
            UpdatedSourceFilePaths = confirmedRereviewResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = confirmedRereviewResult.UpdatedExecutionFilePaths,
            RegeneratedArtifactPaths = confirmedRereviewResult.RegeneratedArtifactPaths,
            StartedExecutionUnits = confirmedRereviewResult.StartedExecutionUnits,
            CreatedIssueRefs = confirmedRereviewResult.CreatedIssueRefs,
            WorktreePaths = confirmedRereviewResult.WorktreePaths,
            ImplementRequestArtifactPaths = confirmedRereviewResult.ImplementRequestArtifactPaths,
            CreatedPrRefs = confirmedRereviewResult.CreatedPrRefs,
            ReviewExecutionUnits = confirmedRereviewResult.ReviewExecutionUnits,
            ReviewRequestArtifactPaths = confirmedRereviewResult.ReviewRequestArtifactPaths,
            PostedCommentArtifactPaths = confirmedRereviewResult.PostedCommentArtifactPaths,
            CommentRefs = confirmedRereviewResult.CommentRefs,
            FixingExecutionUnits = confirmedRereviewResult.FixingExecutionUnits,
            FixRequestArtifactPaths = confirmedRereviewResult.FixRequestArtifactPaths,
            ResubmittedExecutionUnits = confirmedRereviewResult.ResubmittedExecutionUnits,
            ResubmittedPrRefs = confirmedRereviewResult.ResubmittedPrRefs,
            RereviewedExecutionUnits = confirmedRereviewResult.RereviewedExecutionUnits,
            RereviewedPrRefs = confirmedRereviewResult.RereviewedPrRefs,
            CompletedExecutionUnits = completedExecutionUnits,
            ClosedIssueRefs = closedIssueRefs,
            MergedPrRefs = mergedPrRefs,
            ConfirmedItems = confirmedRereviewResult.ConfirmedItems,
            BlockedItems = confirmedRereviewResult.BlockedItems,
            DownstreamReadiness = confirmedRereviewResult.DownstreamReadiness
        };
    }

    private static string ParseDomain(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current confirmed-reaccept requires a domain.");
        }

        return args[0].Trim();
    }
}
