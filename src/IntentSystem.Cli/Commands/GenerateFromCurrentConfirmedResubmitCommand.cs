namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentConfirmedResubmitCommand
{
    public static Func<CliContext, string[], TextWriter, GenerateFromCurrentConfirmedFixResult> ConfirmedFixExecutor { get; set; } =
        (context, args, writer) => GenerateFromCurrentConfirmedFixCommand.ExecuteCore(context, args, writer);

    public static Func<CliContext, string, RunResubmitResult> RunResubmitExecutor { get; set; } =
        (context, executionUnit) => RunResubmitCommand.ExecuteCore(context, executionUnit);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args, writer);
            GenerateFromCurrentConfirmedResubmitRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentConfirmedResubmitResult ExecuteCore(
        CliContext context,
        string[] args,
        TextWriter writer)
    {
        var domain = ParseDomain(args);
        var confirmedFixResult = ConfirmedFixExecutor(context, args, writer);

        if (!string.Equals(confirmedFixResult.Domain, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Confirmed fix result domain '{confirmedFixResult.Domain}' does not match requested domain '{domain}'.");
        }

        if (string.Equals(confirmedFixResult.Route, "clarification-return", StringComparison.Ordinal))
        {
            return CreateResult(domain, "clarification-return", confirmedFixResult, [], []);
        }

        if (!string.Equals(confirmedFixResult.DownstreamReadiness, "ready", StringComparison.Ordinal))
        {
            return CreateResult(domain, "reconciliation-required", confirmedFixResult, [], []);
        }

        var resubmitResults = confirmedFixResult.FixingExecutionUnits
            .Select(executionUnit => RunResubmitExecutor(context, executionUnit))
            .ToArray();

        return CreateResult(
            domain,
            "confirmed-resubmit",
            confirmedFixResult,
            resubmitResults.Select(result => result.ExecutionUnit).ToArray(),
            resubmitResults.Select(result => result.LinkedPr).ToArray());
    }

    private static GenerateFromCurrentConfirmedResubmitResult CreateResult(
        string domain,
        string route,
        GenerateFromCurrentConfirmedFixResult confirmedFixResult,
        IReadOnlyList<string> resubmittedExecutionUnits,
        IReadOnlyList<string> resubmittedPrRefs)
    {
        return new GenerateFromCurrentConfirmedResubmitResult
        {
            Domain = domain,
            Route = route,
            ClarificationReturnArtifactPath = confirmedFixResult.ClarificationReturnArtifactPath,
            ConfirmedReconstructionArtifactPath = confirmedFixResult.ConfirmedReconstructionArtifactPath,
            UpdatedSourceFilePaths = confirmedFixResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = confirmedFixResult.UpdatedExecutionFilePaths,
            RegeneratedArtifactPaths = confirmedFixResult.RegeneratedArtifactPaths,
            StartedExecutionUnits = confirmedFixResult.StartedExecutionUnits,
            CreatedIssueRefs = confirmedFixResult.CreatedIssueRefs,
            WorktreePaths = confirmedFixResult.WorktreePaths,
            ImplementRequestArtifactPaths = confirmedFixResult.ImplementRequestArtifactPaths,
            CreatedPrRefs = confirmedFixResult.CreatedPrRefs,
            ReviewExecutionUnits = confirmedFixResult.ReviewExecutionUnits,
            ReviewRequestArtifactPaths = confirmedFixResult.ReviewRequestArtifactPaths,
            PostedCommentArtifactPaths = confirmedFixResult.PostedCommentArtifactPaths,
            CommentRefs = confirmedFixResult.CommentRefs,
            FixingExecutionUnits = confirmedFixResult.FixingExecutionUnits,
            FixRequestArtifactPaths = confirmedFixResult.FixRequestArtifactPaths,
            ResubmittedExecutionUnits = resubmittedExecutionUnits,
            ResubmittedPrRefs = resubmittedPrRefs,
            ConfirmedItems = confirmedFixResult.ConfirmedItems,
            BlockedItems = confirmedFixResult.BlockedItems,
            DownstreamReadiness = confirmedFixResult.DownstreamReadiness
        };
    }

    private static string ParseDomain(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current confirmed-resubmit requires a domain.");
        }

        return args[0].Trim();
    }
}
