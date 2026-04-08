namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentConfirmedFixCommand
{
    public static Func<CliContext, string[], TextWriter, GenerateFromCurrentConfirmedCommentResult> ConfirmedCommentExecutor { get; set; } =
        (context, args, writer) => GenerateFromCurrentConfirmedCommentCommand.ExecuteCore(context, args, writer);

    public static Func<CliContext, string, RunFixResult> RunFixExecutor { get; set; } =
        (context, executionUnit) => RunFixCommand.ExecuteCore(context, executionUnit);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args, writer);
            GenerateFromCurrentConfirmedFixRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentConfirmedFixResult ExecuteCore(
        CliContext context,
        string[] args,
        TextWriter writer)
    {
        var domain = ParseDomain(args);
        var confirmedCommentResult = ConfirmedCommentExecutor(context, args, writer);

        if (!string.Equals(confirmedCommentResult.Domain, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Confirmed comment result domain '{confirmedCommentResult.Domain}' does not match requested domain '{domain}'.");
        }

        if (string.Equals(confirmedCommentResult.Route, "clarification-return", StringComparison.Ordinal))
        {
            return CreateResult(domain, "clarification-return", confirmedCommentResult, []);
        }

        if (!string.Equals(confirmedCommentResult.DownstreamReadiness, "ready", StringComparison.Ordinal))
        {
            return CreateResult(domain, "reconciliation-required", confirmedCommentResult, []);
        }

        var fixResults = confirmedCommentResult.FixingExecutionUnits
            .Select(executionUnit => RunFixExecutor(context, executionUnit))
            .ToArray();

        return CreateResult(
            domain,
            "confirmed-fix",
            confirmedCommentResult,
            fixResults.Select(result => result.ArtifactPath).ToArray());
    }

    private static GenerateFromCurrentConfirmedFixResult CreateResult(
        string domain,
        string route,
        GenerateFromCurrentConfirmedCommentResult confirmedCommentResult,
        IReadOnlyList<string> fixRequestArtifactPaths)
    {
        return new GenerateFromCurrentConfirmedFixResult
        {
            Domain = domain,
            Route = route,
            ClarificationReturnArtifactPath = confirmedCommentResult.ClarificationReturnArtifactPath,
            ConfirmedReconstructionArtifactPath = confirmedCommentResult.ConfirmedReconstructionArtifactPath,
            UpdatedSourceFilePaths = confirmedCommentResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = confirmedCommentResult.UpdatedExecutionFilePaths,
            RegeneratedArtifactPaths = confirmedCommentResult.RegeneratedArtifactPaths,
            StartedExecutionUnits = confirmedCommentResult.StartedExecutionUnits,
            CreatedIssueRefs = confirmedCommentResult.CreatedIssueRefs,
            WorktreePaths = confirmedCommentResult.WorktreePaths,
            ImplementRequestArtifactPaths = confirmedCommentResult.ImplementRequestArtifactPaths,
            CreatedPrRefs = confirmedCommentResult.CreatedPrRefs,
            ReviewExecutionUnits = confirmedCommentResult.ReviewExecutionUnits,
            ReviewRequestArtifactPaths = confirmedCommentResult.ReviewRequestArtifactPaths,
            PostedCommentArtifactPaths = confirmedCommentResult.PostedCommentArtifactPaths,
            CommentRefs = confirmedCommentResult.CommentRefs,
            FixingExecutionUnits = confirmedCommentResult.FixingExecutionUnits,
            FixRequestArtifactPaths = fixRequestArtifactPaths,
            ConfirmedItems = confirmedCommentResult.ConfirmedItems,
            BlockedItems = confirmedCommentResult.BlockedItems,
            DownstreamReadiness = confirmedCommentResult.DownstreamReadiness
        };
    }

    private static string ParseDomain(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current confirmed-fix requires a domain.");
        }

        return args[0].Trim();
    }
}
