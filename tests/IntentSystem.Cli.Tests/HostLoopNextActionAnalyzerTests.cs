using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class HostLoopNextActionAnalyzerTests
{
    [Fact]
    public void StaleCli_BeatsEverything_AndDisallowsMutation()
    {
        var input = NewInput() with
        {
            StaleCli = true,
            ActionableReviewPr = new ActionableReviewPr { Number = 706, Url = "https://github.com/owner/repo/pull/706" },
            NextSliceIssueCutReady = true,
            PublishNextSliceExecutionUnit = "G300",
            HardClarificationOpen = true
        };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.Equal("stale-cli", result.Classification);
        Assert.False(result.MutationAllowed);
        Assert.Equal("intent-cli automation doctor --format json", result.RecommendedCommand);
    }

    [Fact]
    public void DirtyHostState_BeatsActionableReviewPr_AndDisallowsMutation()
    {
        var input = NewInput() with
        {
            SyncClassification = "dirty-host-durable-state",
            ActionableReviewPr = new ActionableReviewPr { Number = 706, Url = "https://github.com/owner/repo/pull/706" }
        };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.Equal("dirty-host-state", result.Classification);
        Assert.False(result.MutationAllowed);
        Assert.Contains("host-sync-preflight", result.RecommendedCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void DirtyMixed_AlsoMapsToDirtyHostState()
    {
        var input = NewInput() with { SyncClassification = "dirty-mixed" };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.Equal("dirty-host-state", result.Classification);
    }

    [Fact]
    public void DirtyUnrelatedSubmodule_RecommendsSafeStashLane()
    {
        var input = NewInput() with { SyncClassification = "dirty-unrelated-submodule" };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.Equal("safe-stash", result.Classification);
        Assert.True(result.MutationAllowed);
        Assert.Contains("workspace-guard --mode begin --write", result.RecommendedCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void ActionableReviewPr_RecommendsCloseoutPlan_WithPrUrl()
    {
        var input = NewInput() with
        {
            ActionableReviewPr = new ActionableReviewPr { Number = 706, Url = "https://github.com/J-Tech-Japan/intent-system/pull/706" }
        };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.Equal("review-pr", result.Classification);
        Assert.True(result.MutationAllowed);
        Assert.Contains("--pr 706", result.RecommendedCommand!, StringComparison.Ordinal);
        Assert.Contains("https://github.com/J-Tech-Japan/intent-system/pull/706", result.Evidence[0], StringComparison.Ordinal);
    }

    [Fact]
    public void PublishRecoveryRepairs_RecommendG303_NotPrComment()
    {
        var input = NewInput() with { PublishRecoveryRepairsAvailable = 1 };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.Equal("repair-host-metadata", result.Classification);
        Assert.True(result.MutationAllowed);
        Assert.Contains("publish-recovery", result.RecommendedCommand!, StringComparison.Ordinal);
        Assert.Contains(result.Evidence, e => e.Contains("MUST NOT become PR repair comments", StringComparison.Ordinal));
    }

    [Fact]
    public void PublishLifecycleDrift_RecommendG307Repair()
    {
        var input = NewInput() with { PublishLifecycleDriftCount = 2 };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.Equal("repair-host-metadata", result.Classification);
        Assert.Contains("publish-lifecycle-repair", result.RecommendedCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishNextIssue_OnlyWhenWipCapEmpty()
    {
        var input = NewInput() with
        {
            NextSliceIssueCutReady = true,
            PublishNextSliceExecutionUnit = "G310",
            OpenIntentTargetPrOrIssueExists = false
        };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.Equal("publish-next-issue", result.Classification);
        Assert.True(result.MutationAllowed);
        Assert.Contains("--execution-unit G310", result.RecommendedCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishNextIssue_GatedByOpenIntentTarget()
    {
        // WIP cap NOT empty; should NOT recommend publish-next-issue.
        var input = NewInput() with
        {
            NextSliceIssueCutReady = true,
            PublishNextSliceExecutionUnit = "G310",
            OpenIntentTargetPrOrIssueExists = true,
            AnyChildWorkerLeaseHeld = true
        };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.NotEqual("publish-next-issue", result.Classification);
        Assert.Equal("wait-for-child", result.Classification);
    }

    [Fact]
    public void HardClarification_RecommendsClarificationFlow_NoMutation()
    {
        var input = NewInput() with
        {
            HardClarificationOpen = true,
            Domain = "intent-cli"
        };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.Equal("hard-clarification", result.Classification);
        Assert.False(result.MutationAllowed);
        Assert.Contains("clarification next", result.RecommendedCommand!, StringComparison.Ordinal);
        Assert.Contains("--domain intent-cli", result.RecommendedCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void WaitForChild_WhenLeaseHeld_AndNoOtherSignal()
    {
        var input = NewInput() with
        {
            OpenIntentTargetPrOrIssueExists = true,
            AnyChildWorkerLeaseHeld = true
        };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.Equal("wait-for-child", result.Classification);
        Assert.False(result.MutationAllowed);
    }

    [Fact]
    public void TrueIdle_WhenNoSignals()
    {
        var result = HostLoopNextActionAnalyzer.Analyze(NewInput());

        Assert.Equal("true-idle", result.Classification);
        Assert.False(result.MutationAllowed);
        Assert.Null(result.RecommendedCommand);
    }

    [Fact]
    public void WipCapBlocked_WhenOpenIntentTargetButNoLease()
    {
        var input = NewInput() with
        {
            OpenIntentTargetPrOrIssueExists = true,
            AnyChildWorkerLeaseHeld = false
        };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.Equal("wip-cap-blocked", result.Classification);
        Assert.False(result.MutationAllowed);
        Assert.Contains("host-review-diagnostics", result.RecommendedCommand!, StringComparison.Ordinal);
    }

    private static HostLoopNextActionInput NewInput() =>
        new()
        {
            Repo = "J-Tech-Japan/intent-system",
            Domain = null,
            StaleCli = false,
            SyncClassification = "clean",
            SafeStashRequired = false,
            ActionableReviewPr = null,
            PublishRecoveryRepairsAvailable = 0,
            PublishLifecycleDriftCount = 0,
            NextSliceIssueCutReady = false,
            PublishNextSliceExecutionUnit = null,
            OpenIntentTargetPrOrIssueExists = false,
            AnyChildWorkerLeaseHeld = false,
            HardClarificationOpen = false
        };
}
