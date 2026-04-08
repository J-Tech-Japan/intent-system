namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentConfirmedRereviewCommand
{
    public static Func<CliContext, string[], TextWriter, GenerateFromCurrentConfirmedResubmitResult> ConfirmedResubmitExecutor { get; set; } =
        (context, args, writer) => GenerateFromCurrentConfirmedResubmitCommand.ExecuteCore(context, args, writer);

    public static Func<CliContext, string, RunRereviewResult> RunRereviewExecutor { get; set; } =
        (context, executionUnit) => RunRereviewCommand.ExecuteCore(context, executionUnit);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args, writer);
            GenerateFromCurrentConfirmedRereviewRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentConfirmedRereviewResult ExecuteCore(
        CliContext context,
        string[] args,
        TextWriter writer)
    {
        var domain = ParseDomain(args);
        var confirmedResubmitResult = ConfirmedResubmitExecutor(context, args, writer);

        if (!string.Equals(confirmedResubmitResult.Domain, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Confirmed resubmit result domain '{confirmedResubmitResult.Domain}' does not match requested domain '{domain}'.");
        }

        if (string.Equals(confirmedResubmitResult.Route, "clarification-return", StringComparison.Ordinal))
        {
            return CreateResult(domain, "clarification-return", confirmedResubmitResult, [], []);
        }

        if (!string.Equals(confirmedResubmitResult.DownstreamReadiness, "ready", StringComparison.Ordinal))
        {
            return CreateResult(domain, "reconciliation-required", confirmedResubmitResult, [], []);
        }

        var rereviewResults = confirmedResubmitResult.ResubmittedExecutionUnits
            .Select(executionUnit => RunRereviewExecutor(context, executionUnit))
            .ToArray();

        return CreateResult(
            domain,
            "confirmed-rereview",
            confirmedResubmitResult,
            rereviewResults.Select(result => result.ExecutionUnit).ToArray(),
            rereviewResults.Select(result => result.LinkedPr).ToArray());
    }

    private static GenerateFromCurrentConfirmedRereviewResult CreateResult(
        string domain,
        string route,
        GenerateFromCurrentConfirmedResubmitResult confirmedResubmitResult,
        IReadOnlyList<string> rereviewedExecutionUnits,
        IReadOnlyList<string> rereviewedPrRefs)
    {
        return new GenerateFromCurrentConfirmedRereviewResult
        {
            Domain = domain,
            Route = route,
            ClarificationReturnArtifactPath = confirmedResubmitResult.ClarificationReturnArtifactPath,
            ConfirmedReconstructionArtifactPath = confirmedResubmitResult.ConfirmedReconstructionArtifactPath,
            UpdatedSourceFilePaths = confirmedResubmitResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = confirmedResubmitResult.UpdatedExecutionFilePaths,
            RegeneratedArtifactPaths = confirmedResubmitResult.RegeneratedArtifactPaths,
            StartedExecutionUnits = confirmedResubmitResult.StartedExecutionUnits,
            CreatedIssueRefs = confirmedResubmitResult.CreatedIssueRefs,
            WorktreePaths = confirmedResubmitResult.WorktreePaths,
            ImplementRequestArtifactPaths = confirmedResubmitResult.ImplementRequestArtifactPaths,
            CreatedPrRefs = confirmedResubmitResult.CreatedPrRefs,
            ReviewExecutionUnits = confirmedResubmitResult.ReviewExecutionUnits,
            ReviewRequestArtifactPaths = confirmedResubmitResult.ReviewRequestArtifactPaths,
            PostedCommentArtifactPaths = confirmedResubmitResult.PostedCommentArtifactPaths,
            CommentRefs = confirmedResubmitResult.CommentRefs,
            FixingExecutionUnits = confirmedResubmitResult.FixingExecutionUnits,
            FixRequestArtifactPaths = confirmedResubmitResult.FixRequestArtifactPaths,
            ResubmittedExecutionUnits = confirmedResubmitResult.ResubmittedExecutionUnits,
            ResubmittedPrRefs = confirmedResubmitResult.ResubmittedPrRefs,
            RereviewedExecutionUnits = rereviewedExecutionUnits,
            RereviewedPrRefs = rereviewedPrRefs,
            ConfirmedItems = confirmedResubmitResult.ConfirmedItems,
            BlockedItems = confirmedResubmitResult.BlockedItems,
            DownstreamReadiness = confirmedResubmitResult.DownstreamReadiness
        };
    }

    private static string ParseDomain(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current confirmed-rereview requires a domain.");
        }

        return args[0].Trim();
    }
}
