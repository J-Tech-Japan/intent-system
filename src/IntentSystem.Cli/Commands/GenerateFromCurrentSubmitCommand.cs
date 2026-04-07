namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentSubmitCommand
{
    private static readonly string[] DeferredSubmitStages =
    [
        "submit-review"
    ];

    public static Func<CliContext, string[], GenerateFromCurrentImplementResult> ImplementExecutor { get; set; } =
        (context, args) => GenerateFromCurrentImplementCommand.ExecuteCore(context, args);

    public static Func<CliContext, string, RunSubmitResult> RunSubmitExecutor { get; set; } =
        (context, executionUnit) => RunSubmitCommand.ExecuteCore(context, executionUnit);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            GenerateFromCurrentSubmitRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentSubmitResult ExecuteCore(CliContext context, string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current submit requires a domain.");
        }

        var implementResult = ImplementExecutor(context, args);
        if (!string.Equals(implementResult.ReadinessStatus, "ready", StringComparison.Ordinal))
        {
            return new GenerateFromCurrentSubmitResult
            {
                Domain = implementResult.Domain,
                SourceBundleArtifactPath = implementResult.SourceBundleArtifactPath,
                ReconstructedArtifactPaths = implementResult.ReconstructedArtifactPaths,
                StandardIntakeArtifactPaths = implementResult.StandardIntakeArtifactPaths,
                UpdatedSourceFilePaths = implementResult.UpdatedSourceFilePaths,
                UpdatedExecutionFilePaths = implementResult.UpdatedExecutionFilePaths,
                GeneratedIssueArtifactPaths = implementResult.GeneratedIssueArtifactPaths,
                CreatedIssueRefs = implementResult.CreatedIssueRefs,
                WorktreePaths = implementResult.WorktreePaths,
                StartedExecutionUnits = implementResult.StartedExecutionUnits,
                ImplementRequestArtifactPaths = implementResult.ImplementRequestArtifactPaths,
                CreatedPrRefs = [],
                ReviewExecutionUnits = [],
                ReadinessStatus = implementResult.ReadinessStatus,
                SkippedStages = implementResult.SkippedStages.Concat(DeferredSubmitStages).ToArray()
            };
        }

        var submitResults = implementResult.StartedExecutionUnits
            .Select(executionUnit => RunSubmitExecutor(context, executionUnit))
            .ToArray();

        var skippedStages = new List<string>(implementResult.SkippedStages);
        if (submitResults.Length == 0)
        {
            skippedStages.Add("submit-review");
        }

        return new GenerateFromCurrentSubmitResult
        {
            Domain = implementResult.Domain,
            SourceBundleArtifactPath = implementResult.SourceBundleArtifactPath,
            ReconstructedArtifactPaths = implementResult.ReconstructedArtifactPaths,
            StandardIntakeArtifactPaths = implementResult.StandardIntakeArtifactPaths,
            UpdatedSourceFilePaths = implementResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = implementResult.UpdatedExecutionFilePaths,
            GeneratedIssueArtifactPaths = implementResult.GeneratedIssueArtifactPaths,
            CreatedIssueRefs = implementResult.CreatedIssueRefs,
            WorktreePaths = implementResult.WorktreePaths,
            StartedExecutionUnits = implementResult.StartedExecutionUnits,
            ImplementRequestArtifactPaths = implementResult.ImplementRequestArtifactPaths,
            CreatedPrRefs = submitResults.Select(result => result.LinkedPr).ToArray(),
            ReviewExecutionUnits = submitResults.Select(result => result.ExecutionUnit).ToArray(),
            ReadinessStatus = implementResult.ReadinessStatus,
            SkippedStages = skippedStages
        };
    }
}
