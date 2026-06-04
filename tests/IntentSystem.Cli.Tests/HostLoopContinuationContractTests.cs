using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G454: the canonical host-loop continuation contract must teach LLM
/// agents (Claude / Codex / Copilot / OpenCode / Cursor) the complete
/// continuation rules without relying on stale conversation history:
/// approval is intermediate (continue to merge + closeout), recoverable
/// blockers carry a repair + resume + retry contract, and a wake that
/// stopped after a partial transition is classified terminal/non-terminal.
/// </summary>
public sealed class HostLoopContinuationContractTests
{
    [Fact]
    public void Default_ApprovalIsNotTerminal_AndContinuesToMergeAndCloseout()
    {
        var model = HostLoopContinuationContract.Default;

        Assert.Equal("host-loop-continuation/v1", model.ContractId);
        Assert.False(model.ApprovalIsTerminal,
            "intent-pr-approved must be intermediate, not terminal, unless a concrete gate blocks merge.");

        // The continuation names merge, merged-state verification, and closeout.
        Assert.Contains(model.ContinuationAfterApproval,
            s => s.Contains("merged", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(model.ContinuationAfterApproval,
            s => s.Contains("closeout pr", StringComparison.Ordinal));
        Assert.Contains(model.ContinuationAfterApproval,
            s => s.Contains("--pr-merged", StringComparison.Ordinal));
    }

    [Fact]
    public void Default_ApprovedButUnmergedFixture_ClassifiesNonTerminalWithMergeCloseoutNext()
    {
        // Fixture: a wake stopped at the `intent-pr-approved` label without
        // merging. The contract must classify that stop as NOT terminal and
        // point at merge + closeout as the next command.
        var approved = HostLoopContinuationContract.Default.StopClassifications
            .Single(s => s.StopState == HostLoopContinuationContract.StopApproved);

        Assert.False(approved.Terminal,
            "approved-but-unmerged is an incomplete wake — it must not be terminal.");
        Assert.Contains("closeout pr", approved.NextCommand, StringComparison.Ordinal);
        Assert.Contains("merge", approved.NextCommand, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Default_ReviewStartStop_IsNonTerminal_RequestUpdateStop_IsTerminalForWake()
    {
        var stops = HostLoopContinuationContract.Default.StopClassifications;

        var reviewStart = stops.Single(s => s.StopState == HostLoopContinuationContract.StopReviewStart);
        Assert.False(reviewStart.Terminal,
            "review-start means review in progress — the wake must continue to a decision.");

        var requestUpdate = stops.Single(s => s.StopState == HostLoopContinuationContract.StopRequestUpdate);
        Assert.True(requestUpdate.Terminal,
            "request-update legitimately ends the wake; the child worker owns the next step.");
    }

    [Fact]
    public void Default_RecoverableMetadataBlockerFixture_CarriesRepairResumeRetryAndMutationFlag()
    {
        // Fixture: a selected review PR is blocked by a recoverable metadata
        // condition (missing linked_pr). The contract must give the LLM a
        // machine-usable repair command, the stage to resume from, the retry
        // command, and that mutation is allowed — so it repairs and retries
        // once before declaring blocked.
        var linkageGap = HostLoopContinuationContract.Default.BlockerContracts
            .Single(b => b.BlockerCategory == HostLoopContinuationContract.BlockerReviewLinkageGap);

        Assert.True(linkageGap.Recoverable);
        Assert.False(linkageGap.Terminal);
        Assert.True(linkageGap.MutationAllowed);
        Assert.False(string.IsNullOrWhiteSpace(linkageGap.RepairCommand));
        Assert.Contains("write-recovered-linkage", linkageGap.RepairCommand!, StringComparison.Ordinal);
        Assert.Equal("closeout", linkageGap.ResumeStage);
        Assert.False(string.IsNullOrWhiteSpace(linkageGap.RetryCommand));
        Assert.Null(linkageGap.OperatorActionRequired);
    }

    [Fact]
    public void Default_EveryRecoverableBlocker_HasFullRepairContract_AndUnrecoverableHasOperatorAction()
    {
        foreach (var blocker in HostLoopContinuationContract.Default.BlockerContracts)
        {
            if (blocker.Recoverable)
            {
                Assert.False(blocker.Terminal,
                    $"recoverable blocker '{blocker.BlockerCategory}' must not be terminal.");
                Assert.False(string.IsNullOrWhiteSpace(blocker.RepairCommand),
                    $"recoverable blocker '{blocker.BlockerCategory}' must name a repair_command.");
                Assert.False(string.IsNullOrWhiteSpace(blocker.ResumeStage),
                    $"recoverable blocker '{blocker.BlockerCategory}' must name a resume_stage.");
                Assert.False(string.IsNullOrWhiteSpace(blocker.RetryCommand),
                    $"recoverable blocker '{blocker.BlockerCategory}' must name a retry_command.");
            }
            else
            {
                Assert.True(blocker.Terminal,
                    $"unrecoverable blocker '{blocker.BlockerCategory}' must be terminal.");
                Assert.False(blocker.MutationAllowed,
                    $"unrecoverable blocker '{blocker.BlockerCategory}' must not allow mutation.");
                Assert.False(string.IsNullOrWhiteSpace(blocker.OperatorActionRequired),
                    $"unrecoverable blocker '{blocker.BlockerCategory}' must name operator_action_required.");
            }
        }
    }

    [Fact]
    public void Default_RetryOncePolicy_IsExplicitAboutRepairThenRetryOnce()
    {
        var policy = HostLoopContinuationContract.Default.RetryOncePolicy;
        Assert.Contains("ONCE", policy, StringComparison.Ordinal);
        Assert.Contains("repair", policy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderMarkdown_SubstitutesTargetRepo_AndContainsRailRecovery()
    {
        var markdown = HostLoopContinuationContract.RenderMarkdown("J-Tech-Japan/intent-system");

        Assert.Contains("host-loop-continuation/v1", markdown, StringComparison.Ordinal);
        Assert.Contains("Approval is not terminal", markdown, StringComparison.Ordinal);
        Assert.Contains("Rail recovery", markdown, StringComparison.Ordinal);
        // <r> placeholder resolved to the concrete repo, never leaked.
        Assert.Contains("J-Tech-Japan/intent-system", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("--repo <r>", markdown, StringComparison.Ordinal);
    }
}
