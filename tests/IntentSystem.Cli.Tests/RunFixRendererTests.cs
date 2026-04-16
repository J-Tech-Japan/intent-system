using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class RunFixRendererTests
{
    [Fact]
    public void RenderMarkdown_GivenCompleteRequest_EmitsRepairHandoffShape()
    {
        var markdown = RunFixRenderer.RenderMarkdown(CreateRequest());

        Assert.Contains("# Repair Worker Handoff", markdown, StringComparison.Ordinal);
        Assert.Contains("- latest_linked_pr: https://github.com/J-Tech-Japan/intent-system/pull/69", markdown, StringComparison.Ordinal);
        Assert.Contains("- latest_comment_ref: https://github.com/J-Tech-Japan/intent-system/pull/69#issuecomment-2", markdown, StringComparison.Ordinal);
        Assert.Contains("- review_comment_artifact_ref: .intent-cli/reviews/G20.comment.json", markdown, StringComparison.Ordinal);
        Assert.Contains("- review_request_ref: .intent-cli/reviews/G20.request.json", markdown, StringComparison.Ordinal);
        Assert.Contains("- review_comment_body_path: /repo/prepared-comment.md", markdown, StringComparison.Ordinal);
        Assert.Contains("## Deterministic Review Checks", markdown, StringComparison.Ordinal);
        Assert.Contains("## Execution Contract", markdown, StringComparison.Ordinal);
        Assert.Contains("Continue beyond initial repository inspection", markdown, StringComparison.Ordinal);
        Assert.Contains("deterministic refusal or contract-gap explanation", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenRequest_WritesRepairSummary()
    {
        using var writer = new StringWriter();

        RunFixRenderer.WriteSummary(writer, CreateRequest(), "/repo/.intent-cli/fix/G20.request.md");

        var output = writer.ToString();
        Assert.Contains("Repair handoff artifact generated for G20", output, StringComparison.Ordinal);
        Assert.Contains("Artifact path: /repo/.intent-cli/fix/G20.request.md", output, StringComparison.Ordinal);
        Assert.Contains("Latest linked PR: https://github.com/J-Tech-Japan/intent-system/pull/69", output, StringComparison.Ordinal);
        Assert.Contains("Latest comment ref: https://github.com/J-Tech-Japan/intent-system/pull/69#issuecomment-2", output, StringComparison.Ordinal);
    }

    private static RunFixRequest CreateRequest()
    {
        return new RunFixRequest
        {
            ExecutionUnit = "G20",
            State = "fixing",
            ImplementRole = "Claude",
            QueueWorkerRole = "coder",
            QueueReviewRole = "reviewer",
            WorktreePath = "/repo/.intent-cli/worktrees/G20",
            ChildRepoPath = "/repo/submodules/intent-system",
            Branch = "issue-68-g20",
            LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/68",
            LatestLinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/69",
            LatestCommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/69#issuecomment-2",
            PacketRef = ".intent-cli/issues/G20/packet.yaml",
            ReviewContextRef = ".intent-cli/issues/G20/review-context.md",
            ReviewCommentArtifactRef = ".intent-cli/reviews/G20.comment.json",
            ReviewRequestRef = ".intent-cli/reviews/G20.request.json",
            ReviewCommentBodyPath = "/repo/prepared-comment.md",
            IssueTitle = "[G20] Run Fix Command",
            Goal = "Generate a repair worker handoff artifact.",
            TargetPart = "cli run fix command",
            TargetRepo = "submodules/intent-system",
            TargetPath = ".",
            InScope = ["run fix command", "repair handoff artifact generation"],
            OutOfScope = ["queue mutation", "worker start"],
            AcceptanceCriteria = ["repair handoff artifact generated"],
            DeterministicReviewChecks = ["run fix command remains handoff-only"],
            ExpectedEvidence = ["dotnet test IntentSystem.sln"]
        };
    }
}
