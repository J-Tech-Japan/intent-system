using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G449: coverage for the shared next-slice readiness engine — the five
/// required terminal cases (contract-missing, duplicate-existing, true-idle,
/// clarification-required, issue-cut-ready), the fail-closed precedence, and
/// the cross-surface agreement invariants (a publish-flow-rejected candidate
/// is never issue-cut-ready; diagnostics and host-loop-next-action agree).
/// </summary>
public sealed class NextSliceReadinessEvaluatorTests
{
    private const string Unit = "G500";

    [Fact]
    public void Evaluate_NoCandidate_IsTrueIdle()
    {
        var result = NextSliceReadinessEvaluator.Evaluate(new NextSliceReadinessInput
        {
            HasCandidate = false,
        });

        Assert.Equal(NextSliceReadinessClass.TrueIdle, result.ReadinessClass);
        Assert.True(result.TrueIdle);
        Assert.False(result.IssueCutReady);
        Assert.Equal(NextSliceClassification.NoActionableItem, result.ToNextSliceClassification());
        Assert.Equal(HostLoopNextActionAnalyzer.ClassificationTrueIdle, result.ToHostLoopClassification());
    }

    [Fact]
    public void Evaluate_ContractMissing_IsContractIncomplete_AndNeverIssueCutReady()
    {
        var result = NextSliceReadinessEvaluator.Evaluate(new NextSliceReadinessInput
        {
            HasCandidate = true,
            CandidateExecutionUnit = Unit,
            ContractComplete = false,
            MissingContractSections = ["Acceptance Criteria", "Base Branch Policy"],
        });

        Assert.Equal(NextSliceReadinessClass.ContractIncomplete, result.ReadinessClass);
        Assert.False(result.IssueCutReady);
        // Cross-surface agreement: a publish-flow-rejected (contract incomplete)
        // candidate is never reported issue-cut-ready by next-slice.
        Assert.NotEqual(NextSliceClassification.IssueCutReady, result.ToNextSliceClassification());
        Assert.Equal(NextSliceClassification.InspectManually, result.ToNextSliceClassification());
        Assert.Equal(HostLoopNextActionAnalyzer.ClassificationDesignNeeded, result.ToHostLoopClassification());
        Assert.Contains(result.Evidence, e => e.Contains("Acceptance Criteria", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_ClarificationRequired_IsClarificationRequired()
    {
        var result = NextSliceReadinessEvaluator.Evaluate(new NextSliceReadinessInput
        {
            HasCandidate = true,
            CandidateExecutionUnit = Unit,
            ContractComplete = true,
            OpenHardClarification = true,
            ClarificationSummary = "ambiguous target path",
        });

        Assert.Equal(NextSliceReadinessClass.ClarificationRequired, result.ReadinessClass);
        Assert.False(result.IssueCutReady);
        Assert.Equal(NextSliceClassification.ClarificationRequired, result.ToNextSliceClassification());
        Assert.Equal(HostLoopNextActionAnalyzer.ClassificationHardClarification, result.ToHostLoopClassification());
    }

    [Fact]
    public void Evaluate_DuplicateExisting_SuppressesPublish_RoutesToRecovery()
    {
        var result = NextSliceReadinessEvaluator.Evaluate(new NextSliceReadinessInput
        {
            HasCandidate = true,
            CandidateExecutionUnit = Unit,
            ContractComplete = true,
            OpenHardClarification = false,
            ExistingGitHubReference = new NextSliceExistingReference
            {
                Kind = "issue",
                Number = 742,
                Url = "https://github.com/J-Tech-Japan/intent-system/issues/742",
            },
        });

        Assert.Equal(NextSliceReadinessClass.DuplicateExisting, result.ReadinessClass);
        Assert.False(result.IssueCutReady);
        Assert.True(result.RoutesToRecovery);
        Assert.Equal(NextSliceClassification.ExistingIssueNeedsRepair, result.ToNextSliceClassification());
        // Host loop routes a duplicate to the G444 stale-next-slice reconcile
        // lane, NOT the publish lane.
        Assert.Equal(
            HostLoopNextActionAnalyzer.ClassificationStaleNextSliceReconcile,
            result.ToHostLoopClassification());
        Assert.Equal(742, result.ExistingReference!.Number);
    }

    [Fact]
    public void Evaluate_AllGreen_IsIssueCutReady_AndPublishable()
    {
        var result = NextSliceReadinessEvaluator.Evaluate(new NextSliceReadinessInput
        {
            HasCandidate = true,
            CandidateExecutionUnit = Unit,
            ContractComplete = true,
            OpenHardClarification = false,
            ExistingGitHubReference = null,
        });

        Assert.Equal(NextSliceReadinessClass.IssueCutReady, result.ReadinessClass);
        Assert.True(result.IssueCutReady);
        Assert.Equal(NextSliceClassification.IssueCutReady, result.ToNextSliceClassification());
        Assert.Equal(HostLoopNextActionAnalyzer.ClassificationPublishNextIssue, result.ToHostLoopClassification());
    }

    [Fact]
    public void Evaluate_ContractIncomplete_TakesPrecedenceOverClarificationAndDuplicate()
    {
        // Fail-closed precedence: even with a clarification AND a duplicate
        // present, an incomplete contract is the controlling (non-publishable)
        // verdict — publish-flow would reject it regardless.
        var result = NextSliceReadinessEvaluator.Evaluate(new NextSliceReadinessInput
        {
            HasCandidate = true,
            CandidateExecutionUnit = Unit,
            ContractComplete = false,
            MissingContractSections = ["Goal"],
            OpenHardClarification = true,
            ExistingGitHubReference = new NextSliceExistingReference { Kind = "pr", Number = 9 },
        });

        Assert.Equal(NextSliceReadinessClass.ContractIncomplete, result.ReadinessClass);
        Assert.False(result.IssueCutReady);
    }

    [Fact]
    public void Evaluate_DuplicateTakesPrecedenceOverReadyButNotOverClarification()
    {
        // Clarification outranks duplicate.
        var clarification = NextSliceReadinessEvaluator.Evaluate(new NextSliceReadinessInput
        {
            HasCandidate = true,
            CandidateExecutionUnit = Unit,
            ContractComplete = true,
            OpenHardClarification = true,
            ExistingGitHubReference = new NextSliceExistingReference { Kind = "issue", Number = 1 },
        });
        Assert.Equal(NextSliceReadinessClass.ClarificationRequired, clarification.ReadinessClass);

        // Duplicate outranks the otherwise-ready verdict.
        var duplicate = NextSliceReadinessEvaluator.Evaluate(new NextSliceReadinessInput
        {
            HasCandidate = true,
            CandidateExecutionUnit = Unit,
            ContractComplete = true,
            OpenHardClarification = false,
            ExistingGitHubReference = new NextSliceExistingReference { Kind = "issue", Number = 1 },
        });
        Assert.Equal(NextSliceReadinessClass.DuplicateExisting, duplicate.ReadinessClass);
    }

    // ---- real-surface wiring (G449 review fix) ------------------------------
    // These exercise the ACTUAL HostLoopNextActionAnalyzer surface to prove it
    // routes the publish-vs-duplicate decision through the shared evaluator,
    // not an independent inline branch.

    [Fact]
    public void HostLoopSurface_UnitAlreadyOnGitHub_RoutesToStaleReconcile_ViaSharedEvaluator()
    {
        var result = HostLoopNextActionAnalyzer.Analyze(new HostLoopNextActionInput
        {
            Repo = "J-Tech-Japan/intent-system",
            NextSliceIssueCutReady = true,
            PublishNextSliceExecutionUnit = Unit,
            NextSliceUnitAlreadyOnGitHub = true,
        });

        // duplicate-existing → ToHostLoopClassification → stale-next-slice-reconcile
        Assert.Equal(HostLoopNextActionAnalyzer.ClassificationStaleNextSliceReconcile, result.Classification);
        Assert.False(result.MutationAllowed);
    }

    [Fact]
    public void HostLoopSurface_CleanCandidate_RoutesToPublish_ViaSharedEvaluator()
    {
        var result = HostLoopNextActionAnalyzer.Analyze(new HostLoopNextActionInput
        {
            Repo = "J-Tech-Japan/intent-system",
            NextSliceIssueCutReady = true,
            PublishNextSliceExecutionUnit = Unit,
            NextSliceUnitAlreadyOnGitHub = false,
            OpenIntentTargetPrOrIssueExists = false,
        });

        // issue-cut-ready → ToHostLoopClassification → publish-next-issue
        Assert.Equal(HostLoopNextActionAnalyzer.ClassificationPublishNextIssue, result.Classification);
        Assert.True(result.MutationAllowed);
    }

    [Theory]
    [InlineData(false, true, false, false)] // no candidate
    [InlineData(true, false, false, false)] // contract incomplete
    [InlineData(true, true, true, false)]   // clarification
    [InlineData(true, true, false, true)]   // duplicate
    public void CrossSurfaceAgreement_PublishableOnlyWhenIssueCutReady(
        bool hasCandidate, bool contractComplete, bool clarification, bool duplicate)
    {
        var result = NextSliceReadinessEvaluator.Evaluate(new NextSliceReadinessInput
        {
            HasCandidate = hasCandidate,
            CandidateExecutionUnit = hasCandidate ? Unit : null,
            ContractComplete = contractComplete,
            OpenHardClarification = clarification,
            ExistingGitHubReference = duplicate
                ? new NextSliceExistingReference { Kind = "issue", Number = 5 }
                : null,
        });

        // None of these scenarios is publishable.
        Assert.False(result.IssueCutReady);
        // The two surface mappings must agree: neither reports the publishable
        // terminal class.
        Assert.NotEqual(NextSliceClassification.IssueCutReady, result.ToNextSliceClassification());
        Assert.NotEqual(
            HostLoopNextActionAnalyzer.ClassificationPublishNextIssue,
            result.ToHostLoopClassification());
    }

    // ---- shared adapter routes every publish-readiness surface (G449) -------

    [Theory]
    [InlineData(true, true)]   // complete contract → publishable
    [InlineData(false, false)] // incomplete contract → not publishable
    public void SharedAdapter_IsPublishable_ReflectsContractCompleteness(bool contractComplete, bool expected)
    {
        Assert.Equal(expected, NextSliceReadinessEvaluator.IsPublishable(Unit, contractComplete));
    }

    [Fact]
    public void HostReviewDiagnosticsSurface_IncompleteContract_NotIssuePublishReady_ViaSharedAdapter()
    {
        var result = AutomationHostReviewDiagnosticsAnalyzer.Analyze(
            repo: "J-Tech-Japan/intent-system",
            openPrs: Array.Empty<GitHubAutomationPrCandidate>(),
            publishedIntentTargetIssues: Array.Empty<GitHubAutomationIssueCandidate>(),
            clarificationRequired: false,
            candidateExecutionUnit: Unit,
            candidateMissingContractSections: ["Acceptance Criteria"]);

        // The same candidate that publish-flow / packet-draft reject for an
        // incomplete contract is NOT reported issue-publish-ready here.
        Assert.NotEqual(
            AutomationHostReviewDiagnosticsClassifications.IssuePublishReady,
            result.Classification);
        Assert.Equal(
            AutomationHostReviewDiagnosticsClassifications.ClarificationRequired,
            result.Classification);
    }

    [Fact]
    public void HostReviewDiagnosticsSurface_CompleteContract_IsIssuePublishReady_ViaSharedAdapter()
    {
        var result = AutomationHostReviewDiagnosticsAnalyzer.Analyze(
            repo: "J-Tech-Japan/intent-system",
            openPrs: Array.Empty<GitHubAutomationPrCandidate>(),
            publishedIntentTargetIssues: Array.Empty<GitHubAutomationIssueCandidate>(),
            clarificationRequired: false,
            candidateExecutionUnit: Unit,
            candidateMissingContractSections: Array.Empty<string>());

        Assert.Equal(
            AutomationHostReviewDiagnosticsClassifications.IssuePublishReady,
            result.Classification);
    }
}
