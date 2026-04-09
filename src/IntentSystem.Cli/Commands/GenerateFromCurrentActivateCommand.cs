namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentActivateCommand
{
    private static readonly string[] DeferredActivationStages =
    [
        "issue-generation",
        "launch"
    ];

    public static Func<CliContext, string[], GenerateFromCurrentAdvanceResult> AdvanceExecutor { get; set; } =
        (context, args) => GenerateFromCurrentAdvanceCommand.ExecuteCore(context, args);

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
            GenerateFromCurrentActivateRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentActivateResult ExecuteCore(CliContext context, string[] args, TextWriter writer)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current activate requires a domain.");
        }

        var domain = args[0].Trim();
        var advanceResult = AdvanceExecutor(context, args);

        if (!string.Equals(advanceResult.ReadinessStatus, "ready", StringComparison.Ordinal))
        {
            return new GenerateFromCurrentActivateResult
            {
                Domain = domain,
                SourceBundleArtifactPath = advanceResult.SourceBundleArtifactPath,
                ReconstructedArtifactPaths = advanceResult.ReconstructedArtifactPaths,
                StandardIntakeArtifactPaths = advanceResult.StandardIntakeArtifactPaths,
                UpdatedSourceFilePaths = advanceResult.UpdatedSourceFilePaths,
                UpdatedExecutionFilePaths = advanceResult.UpdatedExecutionFilePaths,
                GeneratedIssueArtifactPaths = [],
                CreatedIssueRefs = [],
                WorktreePaths = [],
                StartedExecutionUnits = [],
                ReadinessStatus = advanceResult.ReadinessStatus,
                SkippedStages = advanceResult.SkippedStages.Concat(DeferredActivationStages).ToArray()
            };
        }

        var startResult = IntakeStartExecutor(context, domain, writer);

        return new GenerateFromCurrentActivateResult
        {
            Domain = domain,
            SourceBundleArtifactPath = advanceResult.SourceBundleArtifactPath,
            ReconstructedArtifactPaths = advanceResult.ReconstructedArtifactPaths,
            StandardIntakeArtifactPaths = advanceResult.StandardIntakeArtifactPaths,
            UpdatedSourceFilePaths = advanceResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = advanceResult.UpdatedExecutionFilePaths,
            GeneratedIssueArtifactPaths = startResult.GeneratedArtifactPaths,
            CreatedIssueRefs = startResult.CreatedIssueRefs,
            WorktreePaths = startResult.WorktreePaths,
            StartedExecutionUnits = startResult.StartedExecutionUnits,
            ReadinessStatus = advanceResult.ReadinessStatus,
            SkippedStages = advanceResult.SkippedStages
        };
    }
}
