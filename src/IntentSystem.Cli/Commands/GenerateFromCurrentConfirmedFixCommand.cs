using IntentSystem.Review;
using IntentSystem.Review.Serialization;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentConfirmedFixCommand
{
    public static Func<CliContext, string[], TextWriter, GenerateFromCurrentConfirmedReviewResult> ConfirmedReviewExecutor { get; set; } =
        (context, args, writer) => GenerateFromCurrentConfirmedReviewCommand.ExecuteCore(context, args, writer);

    public static Func<CliContext, IReadOnlyList<string>, ConfirmedCommentHandoff> ExistingCommentHandoffResolver { get; set; } =
        ResolveExistingCommentHandoff;

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
        var confirmedReviewResult = ConfirmedReviewExecutor(context, args, writer);

        if (!string.Equals(confirmedReviewResult.Domain, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Confirmed review result domain '{confirmedReviewResult.Domain}' does not match requested domain '{domain}'.");
        }

        if (string.Equals(confirmedReviewResult.Route, "clarification-return", StringComparison.Ordinal))
        {
            return CreateResult(
                domain,
                "clarification-return",
                confirmedReviewResult,
                new ConfirmedCommentHandoff
                {
                    PostedCommentArtifactPaths = [],
                    CommentRefs = [],
                    FixingExecutionUnits = []
                },
                []);
        }

        if (!string.Equals(confirmedReviewResult.DownstreamReadiness, "ready", StringComparison.Ordinal))
        {
            return CreateResult(
                domain,
                "reconciliation-required",
                confirmedReviewResult,
                new ConfirmedCommentHandoff
                {
                    PostedCommentArtifactPaths = [],
                    CommentRefs = [],
                    FixingExecutionUnits = []
                },
                []);
        }

        var commentHandoff = ExistingCommentHandoffResolver(context, confirmedReviewResult.ReviewExecutionUnits);

        var fixResults = commentHandoff.FixingExecutionUnits
            .Select(executionUnit => RunFixExecutor(context, executionUnit))
            .ToArray();

        return CreateResult(
            domain,
            "confirmed-fix",
            confirmedReviewResult,
            commentHandoff,
            fixResults.Select(result => result.ArtifactPath).ToArray());
    }

    private static GenerateFromCurrentConfirmedFixResult CreateResult(
        string domain,
        string route,
        GenerateFromCurrentConfirmedReviewResult confirmedReviewResult,
        ConfirmedCommentHandoff commentHandoff,
        IReadOnlyList<string> fixRequestArtifactPaths)
    {
        return new GenerateFromCurrentConfirmedFixResult
        {
            Domain = domain,
            Route = route,
            ClarificationReturnArtifactPath = confirmedReviewResult.ClarificationReturnArtifactPath,
            ConfirmedReconstructionArtifactPath = confirmedReviewResult.ConfirmedReconstructionArtifactPath,
            UpdatedSourceFilePaths = confirmedReviewResult.UpdatedSourceFilePaths,
            UpdatedExecutionFilePaths = confirmedReviewResult.UpdatedExecutionFilePaths,
            RegeneratedArtifactPaths = confirmedReviewResult.RegeneratedArtifactPaths,
            StartedExecutionUnits = confirmedReviewResult.StartedExecutionUnits,
            CreatedIssueRefs = confirmedReviewResult.CreatedIssueRefs,
            WorktreePaths = confirmedReviewResult.WorktreePaths,
            ImplementRequestArtifactPaths = confirmedReviewResult.ImplementRequestArtifactPaths,
            CreatedPrRefs = confirmedReviewResult.CreatedPrRefs,
            ReviewExecutionUnits = confirmedReviewResult.ReviewExecutionUnits,
            ReviewRequestArtifactPaths = confirmedReviewResult.ReviewRequestArtifactPaths,
            PostedCommentArtifactPaths = commentHandoff.PostedCommentArtifactPaths,
            CommentRefs = commentHandoff.CommentRefs,
            FixingExecutionUnits = commentHandoff.FixingExecutionUnits,
            FixRequestArtifactPaths = fixRequestArtifactPaths,
            ConfirmedItems = confirmedReviewResult.ConfirmedItems,
            BlockedItems = confirmedReviewResult.BlockedItems,
            DownstreamReadiness = confirmedReviewResult.DownstreamReadiness
        };
    }

    private static ConfirmedCommentHandoff ResolveExistingCommentHandoff(
        CliContext context,
        IReadOnlyList<string> reviewExecutionUnits)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reviewExecutionUnits);

        if (reviewExecutionUnits.Count == 0)
        {
            return new ConfirmedCommentHandoff
            {
                PostedCommentArtifactPaths = [],
                CommentRefs = [],
                FixingExecutionUnits = []
            };
        }

        var queueState = QueueCommandSupport.LoadQueueState(context, TextWriter.Null)
            ?? throw new InvalidOperationException($"No queue state found at {context.GetQueueStatePath()}");

        var postedCommentArtifactPaths = new List<string>();
        var commentRefs = new List<string>();
        var fixingExecutionUnits = new List<string>();

        foreach (var executionUnit in reviewExecutionUnits)
        {
            var queueItem = queueState.Items.FirstOrDefault(item =>
                string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));

            if (queueItem is null)
            {
                throw new InvalidOperationException(
                    $"Execution unit '{executionUnit}' was not found in queue state for confirmed-fix.");
            }

            if (queueItem.State is not QueueItemState.Fixing)
            {
                throw new InvalidOperationException(
                    $"Execution unit '{executionUnit}' must be fixing before confirmed-fix can continue.");
            }

            var artifactRef = ReviewCommentArtifactPathResolver.Resolve(executionUnit);
            var artifactPath = Path.Combine(
                context.RepoRoot,
                artifactRef.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(artifactPath))
            {
                throw new InvalidOperationException(
                    $"Review comment artifact was not found at {artifactPath}");
            }

            var artifact = ReviewCommentArtifactSerializer.Deserialize(File.ReadAllText(artifactPath));
            if (!string.Equals(artifact.ExecutionUnit, executionUnit, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Review comment artifact execution unit '{artifact.ExecutionUnit}' must match '{executionUnit}'.");
            }

            postedCommentArtifactPaths.Add(artifactRef);
            commentRefs.Add(artifact.CommentRef);
            fixingExecutionUnits.Add(executionUnit);
        }

        return new ConfirmedCommentHandoff
        {
            PostedCommentArtifactPaths = postedCommentArtifactPaths,
            CommentRefs = commentRefs,
            FixingExecutionUnits = fixingExecutionUnits
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

internal sealed record ConfirmedCommentHandoff
{
    public required IReadOnlyList<string> PostedCommentArtifactPaths { get; init; }

    public required IReadOnlyList<string> CommentRefs { get; init; }

    public required IReadOnlyList<string> FixingExecutionUnits { get; init; }
}
