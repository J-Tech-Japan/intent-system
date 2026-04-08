namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentConfirmedSubmitCommand
{
    public static Func<CliContext, string[], TextWriter, GenerateFromCurrentConfirmedImplementResult> ConfirmedImplementExecutor { get; set; } =
        (context, args, writer) => GenerateFromCurrentConfirmedImplementCommand.ExecuteCore(context, args, writer);

    public static Func<CliContext, string, RunSubmitResult> RunSubmitExecutor { get; set; } =
        (context, executionUnit) => RunSubmitCommand.ExecuteCore(context, executionUnit);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args, writer);
            GenerateFromCurrentConfirmedSubmitRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentConfirmedSubmitResult ExecuteCore(
        CliContext context,
        string[] args,
        TextWriter writer)
    {
        var domain = ParseDomain(args);
        var confirmedImplementResult = ConfirmedImplementExecutor(context, args, writer);

        if (!string.Equals(confirmedImplementResult.Domain, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Confirmed implement result domain '{confirmedImplementResult.Domain}' does not match requested domain '{domain}'.");
        }

        if (string.Equals(confirmedImplementResult.Route, "clarification-return", StringComparison.Ordinal))
        {
            return CreateResult(domain, "clarification-return", confirmedImplementResult, [], []);
        }

        if (!string.Equals(confirmedImplementResult.DownstreamReadiness, "ready", StringComparison.Ordinal))
        {
            return CreateResult(domain, "reconciliation-required", confirmedImplementResult, [], []);
        }

        var submitResults = confirmedImplementResult.StartedExecutionUnits
            .Select(executionUnit => RunSubmitExecutor(context, executionUnit))
            .ToArray();

        return CreateResult(
            domain,
            "confirmed-submit",
            confirmedImplementResult,
            submitResults.Select(result => result.LinkedPr).ToArray(),
            submitResults.Select(result => result.ExecutionUnit).ToArray());
    }

    private static GenerateFromCurrentConfirmedSubmitResult CreateResult(
        string domain,
        string route,
        GenerateFromCurrentConfirmedImplementResult confirmedImplementResult,
        IReadOnlyList<string> createdPrRefs,
        IReadOnlyList<string> reviewExecutionUnits)
    {
        return new GenerateFromCurrentConfirmedSubmitResult
        {
            Domain = domain,
            Route = route,
            ClarificationReturnArtifactPath = confirmedImplementResult.ClarificationReturnArtifactPath,
            ConfirmedReconstructionArtifactPath = confirmedImplementResult.ConfirmedReconstructionArtifactPath,
            UpdatedSourceFilePaths = confirmedImplementResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = confirmedImplementResult.UpdatedExecutionFilePaths,
            RegeneratedArtifactPaths = confirmedImplementResult.RegeneratedArtifactPaths,
            StartedExecutionUnits = confirmedImplementResult.StartedExecutionUnits,
            CreatedIssueRefs = confirmedImplementResult.CreatedIssueRefs,
            WorktreePaths = confirmedImplementResult.WorktreePaths,
            ImplementRequestArtifactPaths = confirmedImplementResult.ImplementRequestArtifactPaths,
            CreatedPrRefs = createdPrRefs,
            ReviewExecutionUnits = reviewExecutionUnits,
            ConfirmedItems = confirmedImplementResult.ConfirmedItems,
            BlockedItems = confirmedImplementResult.BlockedItems,
            DownstreamReadiness = confirmedImplementResult.DownstreamReadiness
        };
    }

    private static string ParseDomain(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current confirmed-submit requires a domain.");
        }

        return args[0].Trim();
    }
}
