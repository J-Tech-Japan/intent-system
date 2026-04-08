namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentConfirmedActivateCommand
{
    public static Func<CliContext, string[], GenerateFromCurrentConfirmedAdvanceResult> ConfirmedAdvanceExecutor { get; set; } =
        (context, args) => GenerateFromCurrentConfirmedAdvanceCommand.ExecuteCore(context, args);

    public static Func<CliContext, string, TextWriter, IntakeStartResult> IntakeStartExecutor { get; set; } =
        (context, domain, writer) => IntakeStartCommand.ExecuteCore(context, domain, writer);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args, writer);
            GenerateFromCurrentConfirmedActivateRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentConfirmedActivateResult ExecuteCore(
        CliContext context,
        string[] args,
        TextWriter writer)
    {
        var domain = ParseDomain(args);
        var confirmedAdvanceResult = ConfirmedAdvanceExecutor(context, args);

        if (!string.Equals(confirmedAdvanceResult.Domain, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Confirmed advance result domain '{confirmedAdvanceResult.Domain}' does not match requested domain '{domain}'.");
        }

        if (string.Equals(confirmedAdvanceResult.Route, "clarification-return", StringComparison.Ordinal))
        {
            return new GenerateFromCurrentConfirmedActivateResult
            {
                Domain = domain,
                Route = "clarification-return",
                ClarificationReturnArtifactPath = confirmedAdvanceResult.ClarificationReturnArtifactPath,
                ConfirmedReconstructionArtifactPath = confirmedAdvanceResult.ConfirmedReconstructionArtifactPath,
                UpdatedSourceFilePaths = confirmedAdvanceResult.UpdatedSourceFilePaths,
                UpdatedExecutionFilePaths = confirmedAdvanceResult.UpdatedExecutionFilePaths,
                RegeneratedArtifactPaths = confirmedAdvanceResult.RegeneratedArtifactPaths,
                StartedExecutionUnits = [],
                CreatedIssueRefs = [],
                WorktreePaths = [],
                ConfirmedItems = confirmedAdvanceResult.ConfirmedItems,
                BlockedItems = confirmedAdvanceResult.BlockedItems,
                DownstreamReadiness = confirmedAdvanceResult.DownstreamReadiness
            };
        }

        if (!string.Equals(confirmedAdvanceResult.DownstreamReadiness, "ready", StringComparison.Ordinal))
        {
            return new GenerateFromCurrentConfirmedActivateResult
            {
                Domain = domain,
                Route = "reconciliation-required",
                ClarificationReturnArtifactPath = confirmedAdvanceResult.ClarificationReturnArtifactPath,
                ConfirmedReconstructionArtifactPath = confirmedAdvanceResult.ConfirmedReconstructionArtifactPath,
                UpdatedSourceFilePaths = confirmedAdvanceResult.UpdatedSourceFilePaths,
                UpdatedExecutionFilePaths = confirmedAdvanceResult.UpdatedExecutionFilePaths,
                RegeneratedArtifactPaths = confirmedAdvanceResult.RegeneratedArtifactPaths,
                StartedExecutionUnits = [],
                CreatedIssueRefs = [],
                WorktreePaths = [],
                ConfirmedItems = confirmedAdvanceResult.ConfirmedItems,
                BlockedItems = confirmedAdvanceResult.BlockedItems,
                DownstreamReadiness = confirmedAdvanceResult.DownstreamReadiness
            };
        }

        var startResult = IntakeStartExecutor(context, domain, writer);
        var regeneratedArtifactPaths = confirmedAdvanceResult.RegeneratedArtifactPaths
            .Concat(startResult.GeneratedArtifactPaths)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new GenerateFromCurrentConfirmedActivateResult
        {
            Domain = domain,
            Route = "confirmed-activate",
            ClarificationReturnArtifactPath = confirmedAdvanceResult.ClarificationReturnArtifactPath,
            ConfirmedReconstructionArtifactPath = confirmedAdvanceResult.ConfirmedReconstructionArtifactPath,
            UpdatedSourceFilePaths = confirmedAdvanceResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = confirmedAdvanceResult.UpdatedExecutionFilePaths,
            RegeneratedArtifactPaths = regeneratedArtifactPaths,
            StartedExecutionUnits = startResult.StartedExecutionUnits,
            CreatedIssueRefs = startResult.CreatedIssueRefs,
            WorktreePaths = startResult.WorktreePaths,
            ConfirmedItems = confirmedAdvanceResult.ConfirmedItems,
            BlockedItems = confirmedAdvanceResult.BlockedItems,
            DownstreamReadiness = confirmedAdvanceResult.DownstreamReadiness
        };
    }

    private static string ParseDomain(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current confirmed-activate requires a domain.");
        }

        return args[0].Trim();
    }
}
