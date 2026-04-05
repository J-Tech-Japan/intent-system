using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class RunImplementRendererTests
{
    [Fact]
    public void RenderMarkdown_GivenImplementRequest_RendersDeterministicHandoffArtifact()
    {
        var markdown = RunImplementRenderer.RenderMarkdown(CreateRequest());

        Assert.Contains("# Execution Worker Handoff", markdown, StringComparison.Ordinal);
        Assert.Contains("`G19`", markdown, StringComparison.Ordinal);
        Assert.Contains("- implement: Claude", markdown, StringComparison.Ordinal);
        Assert.Contains("- queue_worker_role: coder", markdown, StringComparison.Ordinal);
        Assert.Contains("- queue_review_role: reviewer", markdown, StringComparison.Ordinal);
        Assert.Contains("- worktree_path: /repo/.intent-cli/worktrees/G19", markdown, StringComparison.Ordinal);
        Assert.Contains("- latest_linked_pr: https://github.com/J-Tech-Japan/intent-system/pull/67", markdown, StringComparison.Ordinal);
        Assert.Contains("- packet_ref: .intent-cli/issues/G19/packet.yaml", markdown, StringComparison.Ordinal);
        Assert.Contains("- review_context_ref: .intent-cli/issues/G19/review-context.md", markdown, StringComparison.Ordinal);
        Assert.Contains("## Deterministic Review Checks", markdown, StringComparison.Ordinal);
        Assert.Contains("- command remains read/write only for handoff artifact generation", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderMarkdown_GivenNoLatestLinkedPrOrEvidence_OmitsLatestPrAndRendersNoneEvidence()
    {
        var markdown = RunImplementRenderer.RenderMarkdown(CreateRequest() with
        {
            LatestLinkedPr = null,
            ExpectedEvidence = []
        });

        Assert.DoesNotContain("latest_linked_pr", markdown, StringComparison.Ordinal);
        Assert.Contains("## Expected Evidence", markdown, StringComparison.Ordinal);
        Assert.Contains("- none", markdown, StringComparison.Ordinal);
    }

    private static RunImplementRequest CreateRequest()
    {
        return new RunImplementRequest
        {
            ExecutionUnit = "G19",
            State = "active",
            ImplementRole = "Claude",
            QueueWorkerRole = "coder",
            QueueReviewRole = "reviewer",
            WorktreePath = "/repo/.intent-cli/worktrees/G19",
            ChildRepoPath = "/repo/submodules/intent-system",
            Branch = "issue-66-g19",
            LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/66",
            LatestLinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/67",
            PacketRef = ".intent-cli/issues/G19/packet.yaml",
            ReviewContextRef = ".intent-cli/issues/G19/review-context.md",
            IssueTitle = "[G19] Run Implement Command",
            Goal = "Generate an execution worker handoff artifact.",
            TargetPart = "cli run implement command",
            TargetRepo = "submodules/intent-system",
            TargetPath = ".",
            InScope = ["run implement command", "handoff artifact generation"],
            OutOfScope = ["queue mutation"],
            AcceptanceCriteria = ["handoff artifact generated"],
            DeterministicReviewChecks = ["command remains read/write only for handoff artifact generation"],
            ExpectedEvidence = ["dotnet test"]
        };
    }
}
