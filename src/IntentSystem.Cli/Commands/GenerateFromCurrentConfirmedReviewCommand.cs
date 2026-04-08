namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentConfirmedReviewCommand
{
    public static Func<CliContext, string[], TextWriter, GenerateFromCurrentConfirmedSubmitResult> ConfirmedSubmitExecutor { get; set; } =
        (context, args, writer) => GenerateFromCurrentConfirmedSubmitCommand.ExecuteCore(context, args, writer);

    public static Func<CliContext, string, ReviewRunResult> ReviewRunExecutor { get; set; } =
        (context, executionUnit) => ReviewRunCommand.ExecuteCore(context, executionUnit);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args, writer);
            GenerateFromCurrentConfirmedReviewRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentConfirmedReviewResult ExecuteCore(
        CliContext context,
        string[] args,
        TextWriter writer)
    {
        var domain = ParseDomain(args);
        var confirmedSubmitResult = ConfirmedSubmitExecutor(context, args, writer);

        if (!string.Equals(confirmedSubmitResult.Domain, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Confirmed submit result domain '{confirmedSubmitResult.Domain}' does not match requested domain '{domain}'.");
        }

        if (string.Equals(confirmedSubmitResult.Route, "clarification-return", StringComparison.Ordinal))
        {
            return CreateResult(domain, "clarification-return", confirmedSubmitResult, []);
        }

        if (!string.Equals(confirmedSubmitResult.DownstreamReadiness, "ready", StringComparison.Ordinal))
        {
            return CreateResult(domain, "reconciliation-required", confirmedSubmitResult, []);
        }

        var reviewResults = confirmedSubmitResult.ReviewExecutionUnits
            .Select(executionUnit => ReviewRunExecutor(context, executionUnit))
            .ToArray();

        return CreateResult(
            domain,
            "confirmed-review",
            confirmedSubmitResult,
            reviewResults.Select(result => result.ArtifactPath).ToArray());
    }

    private static GenerateFromCurrentConfirmedReviewResult CreateResult(
        string domain,
        string route,
        GenerateFromCurrentConfirmedSubmitResult confirmedSubmitResult,
        IReadOnlyList<string> reviewRequestArtifactPaths)
    {
        return new GenerateFromCurrentConfirmedReviewResult
        {
            Domain = domain,
            Route = route,
            ClarificationReturnArtifactPath = confirmedSubmitResult.ClarificationReturnArtifactPath,
            ConfirmedReconstructionArtifactPath = confirmedSubmitResult.ConfirmedReconstructionArtifactPath,
            UpdatedSourceFilePaths = confirmedSubmitResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = confirmedSubmitResult.UpdatedExecutionFilePaths,
            RegeneratedArtifactPaths = confirmedSubmitResult.RegeneratedArtifactPaths,
            StartedExecutionUnits = confirmedSubmitResult.StartedExecutionUnits,
            CreatedIssueRefs = confirmedSubmitResult.CreatedIssueRefs,
            WorktreePaths = confirmedSubmitResult.WorktreePaths,
            ImplementRequestArtifactPaths = confirmedSubmitResult.ImplementRequestArtifactPaths,
            CreatedPrRefs = confirmedSubmitResult.CreatedPrRefs,
            ReviewExecutionUnits = confirmedSubmitResult.ReviewExecutionUnits,
            ReviewRequestArtifactPaths = reviewRequestArtifactPaths,
            ConfirmedItems = confirmedSubmitResult.ConfirmedItems,
            BlockedItems = confirmedSubmitResult.BlockedItems,
            DownstreamReadiness = confirmedSubmitResult.DownstreamReadiness
        };
    }

    private static string ParseDomain(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current confirmed-review requires a domain.");
        }

        return args[0].Trim();
    }
}
