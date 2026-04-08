namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentConfirmedAcceptCommand
{
    public static Func<CliContext, string[], TextWriter, GenerateFromCurrentConfirmedReviewResult> ConfirmedReviewExecutor { get; set; } =
        (context, args, writer) => GenerateFromCurrentConfirmedReviewCommand.ExecuteCore(context, args, writer);

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
            GenerateFromCurrentConfirmedAcceptRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentConfirmedAcceptResult ExecuteCore(
        CliContext context,
        string[] args,
        TextWriter writer)
    {
        var domain = ParseDomain(args);
        var confirmedReviewResult = ConfirmedReviewExecutor(context, args, writer);

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

        var acceptResults = confirmedReviewResult.ReviewExecutionUnits
            .Select(executionUnit => ReviewAcceptExecutor(context, executionUnit))
            .ToArray();

        return CreateResult(
            domain,
            "confirmed-accept",
            confirmedReviewResult,
            acceptResults.Select(result => result.MergedPrRef).ToArray(),
            acceptResults.Select(result => result.ClosedIssueRef).ToArray(),
            acceptResults.Select(result => result.ExecutionUnit).ToArray());
    }

    private static GenerateFromCurrentConfirmedAcceptResult CreateResult(
        string domain,
        string route,
        GenerateFromCurrentConfirmedReviewResult confirmedReviewResult,
        IReadOnlyList<string> mergedPrRefs,
        IReadOnlyList<string> closedIssueRefs,
        IReadOnlyList<string> completedExecutionUnits)
    {
        return new GenerateFromCurrentConfirmedAcceptResult
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
            MergedPrRefs = mergedPrRefs,
            ClosedIssueRefs = closedIssueRefs,
            CompletedExecutionUnits = completedExecutionUnits,
            ConfirmedItems = confirmedReviewResult.ConfirmedItems,
            BlockedItems = confirmedReviewResult.BlockedItems,
            DownstreamReadiness = confirmedReviewResult.DownstreamReadiness
        };
    }

    private static string ParseDomain(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current confirmed-accept requires a domain.");
        }

        return args[0].Trim();
    }
}
