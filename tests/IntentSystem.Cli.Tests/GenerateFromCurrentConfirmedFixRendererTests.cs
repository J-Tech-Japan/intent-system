using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentConfirmedFixRendererTests
{
    [Fact]
    public void WriteSummary_GivenConfirmedFix_WritesFixRequestArtifactPaths()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentConfirmedFixRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentConfirmedFixResult
            {
                Domain = "auth",
                Route = "confirmed-fix",
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
                FixRequestArtifactPaths = [".intent-cli/fix/AUTH-01.request.md"],
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = [],
                DownstreamReadiness = "ready"
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current confirmed-fix processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Fix request artifact paths:", output, StringComparison.Ordinal);
        Assert.Contains("- .intent-cli/fix/AUTH-01.request.md", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenClarificationReturnRoute_WritesStopReason()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentConfirmedFixRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentConfirmedFixResult
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
                FixRequestArtifactPaths = [],
                ConfirmedItems = [],
                BlockedItems = ["clarify: resolve auth boundary before repair worker handoff treatment."],
                DownstreamReadiness = "not-ready"
            });

        var output = writer.ToString();
        Assert.Contains("Clarification-return artifact path: .intent-cli/intake/auth.clarification-return.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Downstream readiness: not-ready", output, StringComparison.Ordinal);
    }
}
