namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentAdvanceCommand
{
    public static Func<CliContext, string[], GenerateFromCurrentResult> SourceBundleExecutor { get; set; } =
        (context, args) => GenerateFromCurrentCommand.ExecuteSourceBundleCore(context, args);

    public static Func<CliContext, string, GenerateFromCurrentReconstructionResult> ReconstructionExecutor { get; set; } =
        (context, domain) => GenerateFromCurrentReconstructionCommand.ExecuteCore(context, [domain]);

    public static Func<CliContext, string, GenerateFromCurrentBridgeResult> BridgeExecutor { get; set; } =
        (context, domain) => GenerateFromCurrentBridgeCommand.ExecuteCore(context, [domain]);

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
            GenerateFromCurrentAdvanceRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentAdvanceResult ExecuteCore(CliContext context, string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current advance requires a domain.");
        }

        var domain = args[0].Trim();
        var sourceBundleResult = SourceBundleExecutor(context, args);
        var reconstructionResult = ReconstructionExecutor(context, domain);
        var bridgeResult = BridgeExecutor(context, domain);
        var intakeAdvanceResult = IntakeAdvanceExecutor(context, domain);

        return new GenerateFromCurrentAdvanceResult
        {
            Domain = domain,
            SourceBundleArtifactPath = sourceBundleResult.ArtifactPath,
            ReconstructedArtifactPaths =
            [
                reconstructionResult.ConceptArtifactPath,
                reconstructionResult.InterviewArtifactPath
            ],
            StandardIntakeArtifactPaths =
            [
                bridgeResult.ConceptArtifactPath,
                .. bridgeResult.InterviewArtifactPaths
            ],
            UpdatedSourceFilePaths = intakeAdvanceResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = intakeAdvanceResult.UpdatedExecutionFilePaths,
            ReadinessStatus = intakeAdvanceResult.ReadinessStatus,
            SkippedStages = BuildSkippedStages(bridgeResult, intakeAdvanceResult)
        };
    }

    private static IReadOnlyList<string> BuildSkippedStages(
        GenerateFromCurrentBridgeResult bridgeResult,
        IntakeAdvanceResult intakeAdvanceResult)
    {
        var skippedStages = new List<string>();
        if (bridgeResult.InterviewArtifactPaths.Count == 0)
        {
            skippedStages.Add("bridge-interviews");
        }

        skippedStages.AddRange(intakeAdvanceResult.SkippedStages);
        return skippedStages;
    }
}
