namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentImplementCommand
{
    private static readonly string[] DeferredActivationStages =
    [
        "issue-generation",
        "launch",
        "implement-handoff"
    ];

    public static Func<CliContext, string[], GenerateFromCurrentAdvanceResult> AdvanceExecutor { get; set; } =
        (context, args) => GenerateFromCurrentAdvanceCommand.ExecuteCore(context, args);

    public static Func<CliContext, string, TextWriter, IntakeStartResult> StartExecutor { get; set; } =
        (context, domain, writer) => IntakeStartCommand.ExecuteCore(context, domain, writer);

    public static Func<CliContext, string, RunImplementResult> RunImplementExecutor { get; set; } =
        (context, executionUnit) => RunImplementCommand.ExecuteCore(context, executionUnit);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            GenerateFromCurrentImplementRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentImplementResult ExecuteCore(CliContext context, string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current implement requires a domain.");
        }

        var domain = args[0].Trim();
        var advanceResult = AdvanceExecutor(context, args);

        if (!string.Equals(advanceResult.ReadinessStatus, "ready", StringComparison.Ordinal))
        {
            return new GenerateFromCurrentImplementResult
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
                ImplementRequestArtifactPaths = [],
                ReadinessStatus = advanceResult.ReadinessStatus,
                SkippedStages = advanceResult.SkippedStages.Concat(DeferredActivationStages).ToArray()
            };
        }

        var startResult = StartExecutor(context, domain, TextWriter.Null);
        var implementArtifactPaths = startResult.StartedExecutionUnits
            .Select(executionUnit => RunImplementExecutor(context, executionUnit).ArtifactPath)
            .ToArray();

        var skippedStages = new List<string>(advanceResult.SkippedStages);
        if (implementArtifactPaths.Length == 0)
        {
            skippedStages.Add("implement-handoff");
        }

        return new GenerateFromCurrentImplementResult
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
            ImplementRequestArtifactPaths = implementArtifactPaths,
            ReadinessStatus = advanceResult.ReadinessStatus,
            SkippedStages = skippedStages
        };
    }
}
