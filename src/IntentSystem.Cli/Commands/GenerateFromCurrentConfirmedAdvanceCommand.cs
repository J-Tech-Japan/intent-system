namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentConfirmedAdvanceCommand
{
    public static Func<CliContext, string[], GenerateFromCurrentConfirmedBridgeResult> ConfirmedBridgeExecutor { get; set; } =
        (context, args) => GenerateFromCurrentConfirmedBridgeCommand.ExecuteCore(context, args);

    public static Func<CliContext, string, IntakeAdvanceResult> IntakeAdvanceExecutor { get; set; } =
        (context, domain) => IntakeAdvanceCommand.ExecuteCore(context, domain);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            GenerateFromCurrentConfirmedAdvanceRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentConfirmedAdvanceResult ExecuteCore(CliContext context, string[] args)
    {
        var domain = ParseDomain(args);
        var confirmedBridgeResult = ConfirmedBridgeExecutor(context, args);

        if (!string.Equals(confirmedBridgeResult.Domain, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Confirmed bridge result domain '{confirmedBridgeResult.Domain}' does not match requested domain '{domain}'.");
        }

        if (string.Equals(confirmedBridgeResult.Route, "clarification-return", StringComparison.Ordinal))
        {
            return new GenerateFromCurrentConfirmedAdvanceResult
            {
                Domain = domain,
                Route = "clarification-return",
                ClarificationReturnArtifactPath = confirmedBridgeResult.ClarificationReturnArtifactPath,
                ConfirmedReconstructionArtifactPath = confirmedBridgeResult.ConfirmedReconstructionArtifactPath,
                UpdatedSourceFilePaths = [],
                UpdatedExecutionFilePaths = [],
                RegeneratedArtifactPaths = confirmedBridgeResult.RegeneratedArtifactPaths,
                ConfirmedItems = confirmedBridgeResult.ConfirmedItems,
                BlockedItems = confirmedBridgeResult.BlockedItems,
                DownstreamReadiness = confirmedBridgeResult.DownstreamReadiness
            };
        }

        if (!string.Equals(confirmedBridgeResult.DownstreamReadiness, "ready", StringComparison.Ordinal))
        {
            return new GenerateFromCurrentConfirmedAdvanceResult
            {
                Domain = domain,
                Route = "reconciliation-required",
                ClarificationReturnArtifactPath = confirmedBridgeResult.ClarificationReturnArtifactPath,
                ConfirmedReconstructionArtifactPath = confirmedBridgeResult.ConfirmedReconstructionArtifactPath,
                UpdatedSourceFilePaths = [],
                UpdatedExecutionFilePaths = [],
                RegeneratedArtifactPaths = confirmedBridgeResult.RegeneratedArtifactPaths,
                ConfirmedItems = confirmedBridgeResult.ConfirmedItems,
                BlockedItems = confirmedBridgeResult.BlockedItems,
                DownstreamReadiness = confirmedBridgeResult.DownstreamReadiness
            };
        }

        var intakeAdvanceResult = IntakeAdvanceExecutor(context, domain);
        var regeneratedArtifactPaths = confirmedBridgeResult.RegeneratedArtifactPaths
            .Concat(intakeAdvanceResult.RegeneratedArtifactPaths)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new GenerateFromCurrentConfirmedAdvanceResult
        {
            Domain = domain,
            Route = "confirmed-advance",
            ClarificationReturnArtifactPath = confirmedBridgeResult.ClarificationReturnArtifactPath,
            ConfirmedReconstructionArtifactPath = confirmedBridgeResult.ConfirmedReconstructionArtifactPath,
            UpdatedSourceFilePaths = intakeAdvanceResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = intakeAdvanceResult.UpdatedExecutionFilePaths,
            RegeneratedArtifactPaths = regeneratedArtifactPaths,
            ConfirmedItems = confirmedBridgeResult.ConfirmedItems,
            BlockedItems = confirmedBridgeResult.BlockedItems,
            DownstreamReadiness = intakeAdvanceResult.ReadinessStatus
        };
    }

    private static string ParseDomain(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current confirmed-advance requires a domain.");
        }

        return args[0].Trim();
    }
}
