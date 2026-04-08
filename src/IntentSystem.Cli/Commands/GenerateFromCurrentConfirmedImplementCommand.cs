namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentConfirmedImplementCommand
{
    public static Func<CliContext, string[], TextWriter, GenerateFromCurrentConfirmedActivateResult> ConfirmedActivateExecutor { get; set; } =
        (context, args, writer) => GenerateFromCurrentConfirmedActivateCommand.ExecuteCore(context, args, writer);

    public static Func<CliContext, string, RunImplementResult> RunImplementExecutor { get; set; } =
        (context, executionUnit) => RunImplementCommand.ExecuteCore(context, executionUnit);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args, writer);
            GenerateFromCurrentConfirmedImplementRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentConfirmedImplementResult ExecuteCore(
        CliContext context,
        string[] args,
        TextWriter writer)
    {
        var domain = ParseDomain(args);
        var confirmedActivateResult = ConfirmedActivateExecutor(context, args, writer);

        if (!string.Equals(confirmedActivateResult.Domain, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Confirmed activate result domain '{confirmedActivateResult.Domain}' does not match requested domain '{domain}'.");
        }

        if (string.Equals(confirmedActivateResult.Route, "clarification-return", StringComparison.Ordinal))
        {
            return CreateStopResult(domain, "clarification-return", confirmedActivateResult, []);
        }

        if (!string.Equals(confirmedActivateResult.DownstreamReadiness, "ready", StringComparison.Ordinal))
        {
            return CreateStopResult(domain, "reconciliation-required", confirmedActivateResult, []);
        }

        var implementArtifactPaths = confirmedActivateResult.StartedExecutionUnits
            .Select(executionUnit => RunImplementExecutor(context, executionUnit).ArtifactPath)
            .ToArray();

        return CreateStopResult(domain, "confirmed-implement", confirmedActivateResult, implementArtifactPaths);
    }

    private static GenerateFromCurrentConfirmedImplementResult CreateStopResult(
        string domain,
        string route,
        GenerateFromCurrentConfirmedActivateResult confirmedActivateResult,
        IReadOnlyList<string> implementArtifactPaths)
    {
        return new GenerateFromCurrentConfirmedImplementResult
        {
            Domain = domain,
            Route = route,
            ClarificationReturnArtifactPath = confirmedActivateResult.ClarificationReturnArtifactPath,
            ConfirmedReconstructionArtifactPath = confirmedActivateResult.ConfirmedReconstructionArtifactPath,
            UpdatedSourceFilePaths = confirmedActivateResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = confirmedActivateResult.UpdatedExecutionFilePaths,
            RegeneratedArtifactPaths = confirmedActivateResult.RegeneratedArtifactPaths,
            StartedExecutionUnits = confirmedActivateResult.StartedExecutionUnits,
            CreatedIssueRefs = confirmedActivateResult.CreatedIssueRefs,
            WorktreePaths = confirmedActivateResult.WorktreePaths,
            ImplementRequestArtifactPaths = implementArtifactPaths,
            ConfirmedItems = confirmedActivateResult.ConfirmedItems,
            BlockedItems = confirmedActivateResult.BlockedItems,
            DownstreamReadiness = confirmedActivateResult.DownstreamReadiness
        };
    }

    private static string ParseDomain(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current confirmed-implement requires a domain.");
        }

        return args[0].Trim();
    }
}
