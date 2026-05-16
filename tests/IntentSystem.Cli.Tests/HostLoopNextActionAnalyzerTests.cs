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
    public void DesignNeeded_WhenProbeReportsDesignNeeded_AndNoOtherSignal()
    {
        // G328 acceptance: when the next-slice probe reports
        // `design-needed` (no prepared packet AND runtime creation is
        // not permitted), the analyzer surfaces `design-needed`
        // ABOVE true-idle so the host loop calls out the missing
        // design work explicitly.
        var input = NewInput() with
        {
            DesignNeeded = true
        };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.Equal("design-needed", result.Classification);
        Assert.False(result.MutationAllowed);
        Assert.Contains("intent next-slice", result.RecommendedCommand!, StringComparison.Ordinal);
        Assert.Contains("--runtime-creation-allowed", result.RecommendedCommand!, StringComparison.Ordinal);
        Assert.Contains("design-side packet draft",
            string.Join('\n', result.Evidence),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DesignNeeded_DoesNotOverride_HigherPrioritySignals()
    {
        // G328: design-needed sits between wait-for-child and
        // true-idle. Higher-priority lanes (review-pr, hard
        // clarification, wip-cap, wait-for-child) must still win
        // when present.
        var input = NewInput() with
        {
            DesignNeeded = true,
            HardClarificationOpen = true,
            Domain = "intent-cli"
        };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.Equal("hard-clarification", result.Classification);
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

    // --- G319: approved-PR continuation lane ---------------------------------

    [Fact]
    public void ApprovedPr_OutranksWipCapBlocked_AndRecommendsMergeCloseout()
    {
        // Mirrors the SKS-G219 incident: PR #571 is OPEN, non-draft,
        // mergeStateStatus=CLEAN, carries intent-target + intent-pr-approved;
        // host loop previously stopped at wip-cap-blocked. With G319 the
        // open approved PR is the next host action.
        var input = NewInput() with
        {
            OpenIntentTargetPrOrIssueExists = true,
            AnyChildWorkerLeaseHeld = false,
            ApprovedPrPendingMergeCloseout = new ApprovedPrContinuation
            {
                Number = 571,
                Url = "https://github.com/J-Tech-Japan/SekibanAsAService/pull/571",
                IsDraft = false,
                MergeStateStatus = "CLEAN",
                HostMetadataBlocked = false,
                LinkedIssueNumber = 570
            }
        };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.Equal("approved-pr-merge-closeout", result.Classification);
        Assert.NotEqual("wip-cap-blocked", result.Classification);
        Assert.True(result.MutationAllowed);
        Assert.Contains("--pr 571", result.RecommendedCommand!, StringComparison.Ordinal);
        Assert.Contains("review closeout-plan", result.RecommendedCommand!, StringComparison.Ordinal);
        // Evidence cites both the PR identity and the G311 closing reference reminder.
        Assert.Contains(result.Evidence, e => e.Contains("#571", StringComparison.Ordinal));
        Assert.Contains(result.Evidence, e => e.Contains("Closes #570", StringComparison.Ordinal));
        Assert.Contains(result.Evidence, e =>
            e.Contains("Approved continuation outranks wip-cap-blocked", StringComparison.Ordinal));
    }

    [Fact]
    public void ApprovedPr_WithoutLinkedIssue_StillSelectsMergeCloseout_NoG311Evidence()
    {
        var input = NewInput() with
        {
            OpenIntentTargetPrOrIssueExists = true,
            ApprovedPrPendingMergeCloseout = new ApprovedPrContinuation
            {
                Number = 700,
                Url = "https://github.com/owner/repo/pull/700",
                IsDraft = false,
                MergeStateStatus = null, // operator didn't pre-check; analyzer treats as CLEAN
                HostMetadataBlocked = false,
                LinkedIssueNumber = null
            }
        };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.Equal("approved-pr-merge-closeout", result.Classification);
        Assert.DoesNotContain(result.Evidence, e => e.Contains("Closes #", StringComparison.Ordinal));
    }

    [Fact]
    public void ApprovedPr_Draft_MapsToApprovedPrDraftBlocked_NotGenericWipCap()
    {
        var input = NewInput() with
        {
            OpenIntentTargetPrOrIssueExists = true,
            ApprovedPrPendingMergeCloseout = new ApprovedPrContinuation
            {
                Number = 571,
                Url = "https://github.com/J-Tech-Japan/SekibanAsAService/pull/571",
                IsDraft = true,
                MergeStateStatus = "CLEAN",
                HostMetadataBlocked = false
            }
        };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.Equal("approved-pr-draft-blocked", result.Classification);
        Assert.False(result.MutationAllowed);
        // Recommended command releases the review lease (G292) cleanly.
        Assert.Contains("pr-transition --transition review-release", result.RecommendedCommand!, StringComparison.Ordinal);
        Assert.Contains(result.Evidence, e => e.Contains("G297", StringComparison.Ordinal));
    }

    [Fact]
    public void ApprovedPr_DirtyMergeState_MapsToApprovedPrMergeBlocked()
    {
        var input = NewInput() with
        {
            OpenIntentTargetPrOrIssueExists = true,
            ApprovedPrPendingMergeCloseout = new ApprovedPrContinuation
            {
                Number = 571,
                Url = "https://github.com/J-Tech-Japan/SekibanAsAService/pull/571",
                IsDraft = false,
                MergeStateStatus = "CONFLICTING",
                HostMetadataBlocked = false
            }
        };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.Equal("approved-pr-merge-blocked", result.Classification);
        Assert.False(result.MutationAllowed);
        Assert.Contains("CONFLICTING", result.Summary, StringComparison.Ordinal);
        Assert.Contains(result.Evidence, e => e.Contains("CONFLICTING", StringComparison.Ordinal));
    }

    [Fact]
    public void ApprovedPr_HostMetadataBlocked_MapsToApprovedPrMetadataBlocked()
    {
        var input = NewInput() with
        {
            OpenIntentTargetPrOrIssueExists = true,
            ApprovedPrPendingMergeCloseout = new ApprovedPrContinuation
            {
                Number = 571,
                Url = "https://github.com/J-Tech-Japan/SekibanAsAService/pull/571",
                IsDraft = false,
                MergeStateStatus = "CLEAN",
                HostMetadataBlocked = true
            }
        };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.Equal("approved-pr-metadata-blocked", result.Classification);
        Assert.False(result.MutationAllowed);
        Assert.Contains("closeout-plan", result.RecommendedCommand!, StringComparison.Ordinal);
        Assert.Contains(result.Evidence, e =>
            e.Contains("MUST NOT become PR repair comments", StringComparison.Ordinal));
    }

    [Fact]
    public void ApprovedPr_BlockerPrecedence_DraftBeatsMergeBeatsMetadata()
    {
        // If multiple blockers are reported simultaneously, the analyzer
        // picks the most specific one in a stable order: draft > merge >
        // metadata. This keeps the operator-facing classification
        // deterministic.
        var input = NewInput() with
        {
            OpenIntentTargetPrOrIssueExists = true,
            ApprovedPrPendingMergeCloseout = new ApprovedPrContinuation
            {
                Number = 571,
                Url = "https://github.com/owner/repo/pull/571",
                IsDraft = true,
                MergeStateStatus = "CONFLICTING",
                HostMetadataBlocked = true
            }
        };

        Assert.Equal("approved-pr-draft-blocked",
            HostLoopNextActionAnalyzer.Analyze(input).Classification);

        var mergeOnly = input with
        {
            ApprovedPrPendingMergeCloseout = input.ApprovedPrPendingMergeCloseout! with { IsDraft = false }
        };
        Assert.Equal("approved-pr-merge-blocked",
            HostLoopNextActionAnalyzer.Analyze(mergeOnly).Classification);

        var metadataOnly = mergeOnly with
        {
            ApprovedPrPendingMergeCloseout = mergeOnly.ApprovedPrPendingMergeCloseout! with { MergeStateStatus = "CLEAN" }
        };
        Assert.Equal("approved-pr-metadata-blocked",
            HostLoopNextActionAnalyzer.Analyze(metadataOnly).Classification);
    }

    [Fact]
    public void WipCapBlocked_StillFires_WhenNoApprovedPrContinuation()
    {
        // Acceptance: "Given only an in-flight issue/PR without approved
        // closeout work, WIP-cap blocking remains unchanged." Open
        // intent-target exists but no approved PR is pending → fall
        // through to the existing wip-cap-blocked classification.
        var input = NewInput() with
        {
            OpenIntentTargetPrOrIssueExists = true,
            AnyChildWorkerLeaseHeld = false,
            ApprovedPrPendingMergeCloseout = null
        };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.Equal("wip-cap-blocked", result.Classification);
        Assert.False(result.MutationAllowed);
    }

    [Fact]
    public void ApprovedPr_DoesNotOverride_HigherPriorityBlockers()
    {
        // stale-cli, dirty-host-state, safe-stash, and review-pr all
        // outrank the approved-PR continuation lane — those signals
        // represent unsafe states / earlier review work that must be
        // resolved first.
        var approved = new ApprovedPrContinuation
        {
            Number = 571,
            Url = "https://github.com/owner/repo/pull/571",
            IsDraft = false,
            MergeStateStatus = "CLEAN"
        };

        Assert.Equal("stale-cli", HostLoopNextActionAnalyzer.Analyze(
            NewInput() with { StaleCli = true, ApprovedPrPendingMergeCloseout = approved }).Classification);

        Assert.Equal("dirty-host-state", HostLoopNextActionAnalyzer.Analyze(
            NewInput() with { SyncClassification = "dirty-host-durable-state", ApprovedPrPendingMergeCloseout = approved }).Classification);

        Assert.Equal("review-pr", HostLoopNextActionAnalyzer.Analyze(
            NewInput() with
            {
                ActionableReviewPr = new ActionableReviewPr { Number = 700, Url = "https://github.com/owner/repo/pull/700" },
                ApprovedPrPendingMergeCloseout = approved
            }).Classification);
    }

    [Fact]
    public void PreparedPacketCommitReady_OverridesDirtyHostStateWithSafeCommitLane()
    {
        // G361 AC5: when host-sync-preflight reports dirty-host-durable-state
        // BUT durable-state-preflight already classified the dirty surface
        // as a complete prepared packet directory (commit-ready), the
        // analyzer must NOT surface generic dirty-host-state. Instead it
        // routes to prepared-packet-commit-ready so the host loop commits
        // + pushes the packet before publication.
        var input = NewInput() with
        {
            SyncClassification = "dirty-host-durable-state",
            PreparedPacketCommitReadyAvailable = true,
            PreparedPacketExecutionUnit = "Z4R-G3",
            Domain = "zero4racer-mobile-revival",
        };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.Equal(HostLoopNextActionAnalyzer.ClassificationPreparedPacketCommitReady, result.Classification);
        Assert.True(result.MutationAllowed);
        Assert.Contains("Z4R-G3", result.Summary, StringComparison.Ordinal);
        Assert.Contains("durable-state-preflight", result.RecommendedCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void PreparedPacketCommitReady_RequiresDirtySyncClassification()
    {
        // Defensive: the lane only fires when host-sync-preflight is
        // actually dirty; otherwise the analyzer falls through to its
        // normal lanes. Without the dirty signal the prepared-packet
        // input is irrelevant (nothing to commit).
        var input = NewInput() with
        {
            SyncClassification = "clean",
            PreparedPacketCommitReadyAvailable = true,
            PreparedPacketExecutionUnit = "Z4R-G3",
        };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        Assert.NotEqual(HostLoopNextActionAnalyzer.ClassificationPreparedPacketCommitReady, result.Classification);
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
