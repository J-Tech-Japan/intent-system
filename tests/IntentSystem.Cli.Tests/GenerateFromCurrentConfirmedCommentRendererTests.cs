using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentConfirmedCommentRendererTests
{
    [Fact]
    public void WriteSummary_GivenConfirmedComment_WritesCommentArtifactsAndRefs()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentConfirmedCommentRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentConfirmedCommentResult
            {
                Domain = "auth",
                Route = "confirmed-comment",
                UpdatedSourceFilePaths = ["intents/intent-cli/concepts/auth-oauth2.md"],
                UpdatedExecutionFilePaths = ["intents/intent-cli/execution/05-post-mvp-sub-slices.md"],
                RegeneratedArtifactPaths =
                [
                    ".intent-cli/intake/auth.concept.yaml",
                    ".intent-cli/intake/auth.execution.md",
                    ".intent-cli/issues/AUTH-01/packet.yaml"
                ],
                StartedExecutionUnits = ["AUTH-01"],
                CreatedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/501"],
                WorktreePaths = ["/tmp/worktrees/AUTH-01"],
                ImplementRequestArtifactPaths = [".intent-cli/implement/AUTH-01.request.md"],
                CreatedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/501"],
                ReviewExecutionUnits = ["AUTH-01"],
                ReviewRequestArtifactPaths = [".intent-cli/reviews/AUTH-01.request.json"],
                PostedCommentArtifactPaths = [".intent-cli/reviews/AUTH-01.comment.json"],
                CommentRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/501#issuecomment-1"],
                FixingExecutionUnits = ["AUTH-01"],
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = [],
                DownstreamReadiness = "ready"
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current confirmed-comment processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Posted comment artifact paths:", output, StringComparison.Ordinal);
        Assert.Contains("- .intent-cli/reviews/AUTH-01.comment.json", output, StringComparison.Ordinal);
        Assert.Contains("Comment refs:", output, StringComparison.Ordinal);
        Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/pull/501#issuecomment-1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenClarificationReturnRoute_WritesStopReason()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentConfirmedCommentRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentConfirmedCommentResult
            {
                Domain = "auth",
                Route = "clarification-return",
                ClarificationReturnArtifactPath = ".intent-cli/intake/auth.clarification-return.yaml",
                UpdatedSourceFilePaths = [],
                UpdatedExecutionFilePaths = [],
                RegeneratedArtifactPaths = [],
                StartedExecutionUnits = [],
                CreatedIssueRefs = [],
                WorktreePaths = [],
                ImplementRequestArtifactPaths = [],
                CreatedPrRefs = [],
                ReviewExecutionUnits = [],
                ReviewRequestArtifactPaths = [],
                PostedCommentArtifactPaths = [],
                CommentRefs = [],
                FixingExecutionUnits = [],
                ConfirmedItems = [],
                BlockedItems = ["clarify: resolve auth boundary before repair-in-place review comment treatment."],
                DownstreamReadiness = "not-ready"
            });

        var output = writer.ToString();
        Assert.Contains("Clarification-return artifact path: .intent-cli/intake/auth.clarification-return.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Downstream readiness: not-ready", output, StringComparison.Ordinal);
    }
}
