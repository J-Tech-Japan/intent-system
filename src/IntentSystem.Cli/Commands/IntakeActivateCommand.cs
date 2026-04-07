namespace IntentSystem.Cli.Commands;

internal static class IntakeActivateCommand
{
    private static readonly string[] DeferredDownstreamStages =
    [
        "issue",
        "enqueue",
        "dispatch",
        "start"
    ];

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Intake activate command requires a domain.");
            return 1;
        }

        var domain = args[0].Trim();

        try
        {
            var result = ExecuteCore(context, domain, writer);
            IntakeActivateRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static IntakeActivateResult ExecuteCore(CliContext context, string domain, TextWriter writer)
    {
        var advanceResult = IntakeAdvanceCommand.ExecuteCore(context, domain);
        if (!string.Equals(advanceResult.ReadinessStatus, "ready", StringComparison.Ordinal))
        {
            return new IntakeActivateResult
            {
                Domain = domain,
                ReadinessStatus = advanceResult.ReadinessStatus,
                UpdatedSourceFilePaths = advanceResult.UpdatedSourceFilePaths,
                UpdatedExecutionFilePaths = advanceResult.UpdatedExecutionFilePaths,
                RegeneratedArtifactPaths = advanceResult.RegeneratedArtifactPaths,
                StartedExecutionUnits = [],
                GeneratedIssueArtifactPaths = [],
                CreatedIssueRefs = [],
                WorktreePaths = [],
                SkippedStages = advanceResult.SkippedStages.Concat(DeferredDownstreamStages).ToArray()
            };
        }

        var startResult = IntakeStartCommand.ExecuteCore(context, domain, writer);
        return new IntakeActivateResult
        {
            Domain = domain,
            ReadinessStatus = advanceResult.ReadinessStatus,
            UpdatedSourceFilePaths = advanceResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = advanceResult.UpdatedExecutionFilePaths,
            RegeneratedArtifactPaths = advanceResult.RegeneratedArtifactPaths,
            StartedExecutionUnits = startResult.StartedExecutionUnits,
            GeneratedIssueArtifactPaths = startResult.GeneratedArtifactPaths,
            CreatedIssueRefs = startResult.CreatedIssueRefs,
            WorktreePaths = startResult.WorktreePaths,
            SkippedStages = advanceResult.SkippedStages
        };
    }
}
