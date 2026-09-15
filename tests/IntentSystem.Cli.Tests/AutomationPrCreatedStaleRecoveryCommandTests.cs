using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G836: comprehensive coverage for <c>automation pr-created-stale-recovery</c> —
/// identity-bound linkage, PR state, open-closing-PR and claim checks,
/// write-ahead audit events, abort/supersede/completion paths, and the
/// fail-soft <c>worker claim</c> hint.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class AutomationPrCreatedStaleRecoveryCommandTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 15, 16, 0, 0, TimeSpan.Zero);

    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G836";
    private const int Issue = 836;
    private const int Pr = 163;
    private const string Team = "intent-cli-dev";
    private const string Ruling = "stale intent-pr-created after unmerged PR close";

    public AutomationPrCreatedStaleRecoveryCommandTests()
    {
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () => new ThrowingIssueLookup();
        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () => new ThrowingPrLookup();
        AutomationPrCreatedStaleRecoveryCommand.CandidateListerFactory = () => new ThrowingLister();
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => new ThrowingLabelMutator();
        AutomationPrCreatedStaleRecoveryCommand.ClaimVerifierFactory = null;
        AutomationPrCreatedStaleRecoveryCommand.UtcNowFactory = () => FixedNow;
    }

    public void Dispose()
    {
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = null;
        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = null;
        AutomationPrCreatedStaleRecoveryCommand.CandidateListerFactory = null;
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = null;
        AutomationPrCreatedStaleRecoveryCommand.ClaimVerifierFactory = null;
        AutomationPrCreatedStaleRecoveryCommand.UtcNowFactory = null;
        WorkerClaimCommand.MutatorFactory = null;
        WorkerClaimCommand.IssueLookupFactory = null;
    }

    // ── proceed / real-case shape ───────────────────────────────────────

    [Fact]
    public void Execute_DryRun_ReportsPlan_RemovesOnlyIntentPrCreated_NoMutations()
    {
        using var workspace = CreateProceedWorkspace(linkedPr: Pr);
        var labelMutator = new RecordingLabelMutator("intent-target", "intent-pr-created");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;

        var (exitCode, result) = Execute(workspace, write: false);

        Assert.Equal(0, exitCode);
        Assert.Equal("proceed", result.Outcome);
        Assert.False(result.Applied);
        Assert.Equal(Pr, result.LinkedPr);
        Assert.Contains(result.PlannedMutations, m => m.Contains("intent-pr-created", StringComparison.Ordinal));
        Assert.DoesNotContain(result.PlannedMutations, m => m.Contains("intent-target", StringComparison.Ordinal));
        Assert.Empty(labelMutator.Transitions);
        Assert.False(File.Exists(workspace.Context.GetRunLogPath()));
        workspace.AssertHostArtifactsUnchanged();
    }

    [Fact]
    public void Execute_Write_AppendsStartedAndRecovered_RemovesOnlyIntentPrCreated()
    {
        using var workspace = CreateProceedWorkspace(linkedPr: Pr);
        var labelMutator = new RecordingLabelMutator("intent-target", "intent-pr-created");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;

        var (exitCode, result) = Execute(workspace, write: true);

        Assert.Equal(0, exitCode);
        Assert.Equal("recovered", result.Outcome);
        Assert.True(result.Applied);
        Assert.Equal(Pr, result.LinkedPr);

        var transition = Assert.Single(labelMutator.Transitions);
        Assert.Equal("issue", transition.Kind);
        Assert.Equal(Issue, transition.Number);
        Assert.Empty(transition.AddLabels);
        Assert.Equal(new[] { WorkerNextActionConstants.Labels.IntentPrCreated }, transition.RemoveLabels);

        var events = workspace.ReadRunEvents();
        Assert.Equal(2, events.Count);
        Assert.Equal(AutomationPrCreatedStaleRecoveryCommand.EventStarted, events[0].Event);
        Assert.Equal(AutomationPrCreatedStaleRecoveryCommand.EventRecovered, events[1].Event);
        Assert.Equal(Unit, events[0].ExecutionUnit);
        Assert.Equal(Repo, events[0].Repo);
        Assert.Equal(Issue, ParseIssueNumber(events[0].LinkedIssue));
        Assert.Equal(Pr, events[0].Pr);
        Assert.Contains("ruling:", events[0].Reason, StringComparison.Ordinal);
        Assert.Contains("claim:", events[0].Reason, StringComparison.Ordinal);
        Assert.Equal(AutomationPrCreatedStaleRecoveryCommand.By, events[0].By);
        workspace.AssertHostArtifactsUnchanged();
    }

    [Fact]
    public void Execute_RealCaseShape_Queue174_Publish163_ActsOn174_WarnsPublishYamlDiffers()
    {
        const int queuePr = 174;
        using var workspace = CreateProceedWorkspace(linkedPr: queuePr, publishPr: Pr);
        var labelMutator = new RecordingLabelMutator("intent-target", "intent-pr-created");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;
        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () =>
            new FakePrLookup(ClosedUnmerged(queuePr));

        var (exitCode, result) = Execute(workspace, write: false);

        Assert.Equal(0, exitCode);
        Assert.Equal(queuePr, result.LinkedPr);
        Assert.Contains(result.Warnings, w => w.Contains("publish_yaml_linked_pr_differs", StringComparison.Ordinal));

        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () => new FakePrLookup(Merged(queuePr));
        var (mergedExit, mergedResult) = Execute(workspace, write: false);
        Assert.Equal(1, mergedExit);
        Assert.Equal("pr-merged", mergedResult.Cause);
    }

    // ── refusal causes (checks 1–12) ────────────────────────────────────

    [Fact]
    public void Refuse_RulingMissing()
    {
        using var workspace = CreateProceedWorkspace();
        var (exitCode, result) = Execute(workspace, write: false, ruling: "   ");
        AssertRefusal(exitCode, result, "ruling-missing", workspace);
    }

    [Fact]
    public void Refuse_HostStateMissing()
    {
        using var workspace = new RecoveryWorkspace();
        SeedGitHubFakes(workspace, Pr);
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "host-state-missing", workspace);
    }

    [Fact]
    public void Refuse_RunsLogUnreadable()
    {
        using var workspace = CreateProceedWorkspace();
        workspace.WriteUnreadableRunsLog();
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "runs-log-unreadable", workspace);
    }

    [Fact]
    public void Refuse_IssueUnavailable_OnLookupFailure()
    {
        using var workspace = CreateProceedWorkspace();
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(new InvalidOperationException("issue lookup failed"));
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "issue-unavailable", workspace);
    }

    [Fact]
    public void Refuse_IssueNotOpen()
    {
        using var workspace = CreateProceedWorkspace();
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(ClosedIssue());
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "issue-not-open", workspace);
    }

    [Fact]
    public void Refuse_TargetAbsent()
    {
        using var workspace = CreateProceedWorkspace();
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(OpenIssue(labels: "intent-pr-created"));
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "target-absent", workspace);
    }

    [Fact]
    public void Refuse_QueueItemMissing()
    {
        using var workspace = CreateProceedWorkspace(includeQueueItem: false);
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "queue-item-missing", workspace);
    }

    [Fact]
    public void Refuse_IssueNotOpen_PrecedesQueueItemMissing()
    {
        using var workspace = CreateProceedWorkspace(includeQueueItem: false);
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(ClosedIssue());
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "issue-not-open", workspace);
    }

    [Fact]
    public void SameKeyWriteResume_QueueItemMissing_NoAbortBecauseCurrentKeyUnresolved()
    {
        using var workspace = CreateProceedWorkspace(includeQueueItem: false);
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, Pr));
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(OpenIssue("intent-target", "intent-pr-created"));

        var before = workspace.ReadRunEvents().Count;
        var (exitCode, result) = Execute(workspace, write: true);

        Assert.Equal(1, exitCode);
        Assert.Equal("queue-item-missing", result.Cause);
        Assert.Equal(before, workspace.ReadRunEvents().Count);
    }

    [Fact]
    public void Refuse_QueueItemAmbiguous()
    {
        using var workspace = CreateProceedWorkspace(duplicateQueueItem: true);
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "queue-item-ambiguous", workspace);
    }

    [Fact]
    public void Refuse_UnitMismatch()
    {
        using var workspace = CreateProceedWorkspace(executionUnit: "G999");
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "unit-mismatch", workspace);
    }

    [Fact]
    public void Refuse_RepoMismatch_FromCreatedIssueUrl()
    {
        using var workspace = CreateProceedWorkspace(createdIssueRepo: "Other-Org/other-repo");
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "repo-mismatch", workspace);
    }

    [Fact]
    public void Refuse_RepoMismatch_FromLinkedPr()
    {
        using var workspace = CreateProceedWorkspace(linkedPrRepo: "Other-Org/other-repo");
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "repo-mismatch", workspace);
    }

    [Fact]
    public void Refuse_PrLinkageMissing_StringAbsent()
    {
        using var workspace = CreateProceedWorkspace(linkedPr: null);
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "pr-linkage-missing", workspace);
    }

    [Fact]
    public void Refuse_PrLinkageMissing_ObjectWithoutUrl()
    {
        using var workspace = CreateProceedWorkspace(linkedPrObjectWithoutUrl: true);
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "pr-linkage-missing", workspace);
    }

    [Fact]
    public void Refuse_PrStateUnavailable()
    {
        using var workspace = CreateProceedWorkspace();
        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () =>
            new FakePrLookup(new InvalidOperationException("PR lookup failed"));
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "pr-state-unavailable", workspace);
    }

    [Fact]
    public void Refuse_PrOpen()
    {
        using var workspace = CreateProceedWorkspace();
        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () =>
            new FakePrLookup(OpenPr(Pr));
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "pr-open", workspace);
    }

    [Theory]
    [InlineData("MERGED", null, true)]
    [InlineData("CLOSED", "2026-01-01T00:00:00Z", false)]
    public void Refuse_PrMerged(string state, string? mergedAt, bool mergedFlag)
    {
        using var workspace = CreateProceedWorkspace();
        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () =>
            new FakePrLookup(new GitHubPrLookupResult
            {
                Number = Pr,
                State = state,
                MergedAt = mergedAt,
                Merged = mergedFlag,
            });
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "pr-merged", workspace);
    }

    [Fact]
    public void Refuse_OpenClosingPr()
    {
        using var workspace = CreateProceedWorkspace();
        AutomationPrCreatedStaleRecoveryCommand.CandidateListerFactory = () =>
            new FakeLister(prs: [OpenClosingPr(900, Issue)]);
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "open-closing-pr", workspace);
    }

    [Fact]
    public void Refuse_OpenPrListUnavailable()
    {
        using var workspace = CreateProceedWorkspace();
        AutomationPrCreatedStaleRecoveryCommand.CandidateListerFactory = () =>
            new FakeLister(listFailure: new InvalidOperationException("gh pr list failed"));
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "open-pr-list-unavailable", workspace);
    }

    [Theory]
    [InlineData("owned")]
    [InlineData("held-by-other-team")]
    public void Refuse_ClaimHeld(string scenario)
    {
        using var workspace = CreateProceedWorkspace();
        if (scenario == "owned")
        {
            workspace.WriteClaim("builder", Team);
        }
        else
        {
            workspace.WriteClaim("other-builder", "other-team");
        }

        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "claim-held", workspace);
    }

    [Fact]
    public void Refuse_ClaimUnavailable_NotConfigured()
    {
        using var workspace = CreateProceedWorkspace(configureClaimsStore: false);
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "claim-unavailable", workspace);
    }

    [Fact]
    public void Refuse_ClaimUnavailable_Invalid()
    {
        using var workspace = CreateProceedWorkspace();
        workspace.WriteRawClaim("not-json\n");
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "claim-unavailable", workspace);
    }

    [Fact]
    public void Refuse_ClaimUnavailable_CanonicalUnavailable()
    {
        using var workspace = CreateProceedWorkspace();
        workspace.InitializeBrokenOrigin();
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "claim-unavailable", workspace);
    }

    [Fact]
    public void Refuse_ClaimUnavailable_MetadataBranchOnly()
    {
        using var repos = new MetadataBranchRepos();
        using var workspace = RecoveryWorkspace.At(repos.FirstClone);
        workspace.WriteProceedHostState(linkedPr: Pr);
        SeedGitHubFakes(workspace, Pr);

        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "claim-unavailable", workspace);
    }

    [Fact]
    public void Refuse_InProgressPresent()
    {
        using var workspace = CreateProceedWorkspace();
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(OpenIssue("intent-target", "intent-pr-created", "intent-issue-in-progress"));
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "in-progress-present", workspace);
    }

    [Fact]
    public void Refuse_LinkageStale()
    {
        using var workspace = CreateProceedWorkspace();
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventRecovered, Pr));
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "linkage-stale", workspace);
        Assert.Contains("host-queue-item-recovery", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_StartedAmbiguous_LabelAbsent_MultipleDistinctPrKeys()
    {
        using var workspace = CreateProceedWorkspace();
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, Pr));
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, 174,
            ts: FixedNow.AddMinutes(1)));
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(OpenIssue("intent-target"));
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "started-ambiguous", workspace);
    }

    [Fact]
    public void Refuse_StartedAmbiguous_MultipleOpenStartedForSameKey()
    {
        using var workspace = CreateProceedWorkspace();
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, Pr));
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, Pr,
            ts: FixedNow.AddMinutes(1)));
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "started-ambiguous", workspace);
    }

    // ── write re-check refusals (exactly one aborted) ─────────────────────

    [Theory]
    [InlineData("issue-not-open")]
    [InlineData("target-absent")]
    [InlineData("in-progress-present")]
    [InlineData("label-changed")]
    [InlineData("pr-open")]
    [InlineData("pr-merged")]
    [InlineData("open-closing-pr")]
    public void WriteRecheckRefusal_AppendsExactlyOneAborted_NoMutation(string scenario)
    {
        using var workspace = CreateProceedWorkspace();
        var labelMutator = new RecordingLabelMutator("intent-target", "intent-pr-created");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;
        ConfigureRecheckScenario(scenario, labelMutator);

        var beforeRuns = workspace.ReadRunEvents();
        var (exitCode, result) = Execute(workspace, write: true);

        Assert.Equal(1, exitCode);
        Assert.Equal(scenario, result.Cause);
        Assert.Empty(labelMutator.Transitions);
        var after = workspace.ReadRunEvents();
        Assert.Equal(beforeRuns.Count + 2, after.Count);
        Assert.Equal(AutomationPrCreatedStaleRecoveryCommand.EventStarted, after[^2].Event);
        Assert.Equal(AutomationPrCreatedStaleRecoveryCommand.EventAborted, after[^1].Event);
        Assert.Contains("re-check", after[^1].Reason, StringComparison.OrdinalIgnoreCase);
        workspace.AssertHostArtifactsUnchanged();
    }

    [Fact]
    public void WriteRecheckRefusal_ClaimHeld_AppendsAborted()
    {
        using var workspace = CreateProceedWorkspace();
        var labelMutator = new RecordingLabelMutator("intent-target", "intent-pr-created");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;
        BindClaimVerifier(new SequencedClaimVerifier(UnheldClaim(), HeldClaim()));

        var (exitCode, result) = Execute(workspace, write: true);
        Assert.Equal(1, exitCode);
        Assert.Equal("claim-held", result.Cause);
        Assert.Empty(labelMutator.Transitions);
        var events = workspace.ReadRunEvents();
        Assert.Equal(2, events.Count);
        Assert.Equal(AutomationPrCreatedStaleRecoveryCommand.EventStarted, events[0].Event);
        Assert.Equal(AutomationPrCreatedStaleRecoveryCommand.EventAborted, events[1].Event);
    }

    [Fact]
    public void WriteRecheckRefusal_ClaimUnavailable_AppendsAborted()
    {
        using var workspace = CreateProceedWorkspace();
        var labelMutator = new RecordingLabelMutator("intent-target", "intent-pr-created");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;
        BindClaimVerifier(new SequencedClaimVerifier(
            UnheldClaim(),
            ClaimUnavailable(ClaimOwnershipVerification.StatusNotConfigured)));

        var (exitCode, result) = Execute(workspace, write: true);
        Assert.Equal(1, exitCode);
        Assert.Equal("claim-unavailable", result.Cause);
        Assert.Empty(labelMutator.Transitions);
        Assert.Equal(AutomationPrCreatedStaleRecoveryCommand.EventAborted,
            workspace.ReadRunEvents().Last().Event);
    }

    [Fact]
    public void WriteRecheckRefusal_IssueUnavailable_AppendsAborted()
    {
        using var workspace = CreateProceedWorkspace();
        var labelMutator = new RecordingLabelMutator("intent-target", "intent-pr-created");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;
        BindIssueLookup(new SequencedIssueLookup(
            OpenIssue("intent-target", "intent-pr-created"),
            new InvalidOperationException("issue read failed")));

        var (exitCode, result) = Execute(workspace, write: true);
        Assert.Equal(1, exitCode);
        Assert.Equal("issue-unavailable", result.Cause);
        Assert.Equal(AutomationPrCreatedStaleRecoveryCommand.EventAborted,
            workspace.ReadRunEvents().Last().Event);
    }

    [Fact]
    public void WriteRecheckRefusal_PrStateUnavailable_AppendsAborted()
    {
        using var workspace = CreateProceedWorkspace();
        var labelMutator = new RecordingLabelMutator("intent-target", "intent-pr-created");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;
        BindPrLookup(new SequencedPrLookup(
            ClosedUnmerged(Pr), new InvalidOperationException("PR lookup failed")));

        var (exitCode, result) = Execute(workspace, write: true);
        Assert.Equal(1, exitCode);
        Assert.Equal("pr-state-unavailable", result.Cause);
        Assert.Equal(AutomationPrCreatedStaleRecoveryCommand.EventAborted,
            workspace.ReadRunEvents().Last().Event);
    }

    [Fact]
    public void WriteRecheckRefusal_OpenPrListUnavailable_AppendsAborted()
    {
        using var workspace = CreateProceedWorkspace();
        var labelMutator = new RecordingLabelMutator("intent-target", "intent-pr-created");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;
        BindLister(new SequencedLister(
            Array.Empty<GitHubAutomationPrCandidate>(),
            failure: new GitHubApiRequestException(
                "github-transport-error",
                $"list PRs in {Repo}",
                $"[github-transport-error] `gh` failed to list PRs in {Repo} with exit 1: fake gh refused")));

        var (exitCode, result) = Execute(workspace, write: true);
        Assert.Equal(1, exitCode);
        Assert.Equal("open-pr-list-unavailable", result.Cause);
        Assert.Equal(AutomationPrCreatedStaleRecoveryCommand.EventAborted,
            workspace.ReadRunEvents().Last().Event);
    }

    [Fact]
    public void SameKeyWriteResumeRefusal_AppendsAborted_NoSecondStarted()
    {
        using var workspace = CreateProceedWorkspace();
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, Pr));
        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () => new FakePrLookup(Merged(Pr));

        var (exitCode, result) = Execute(workspace, write: true);

        Assert.Equal(1, exitCode);
        Assert.Equal("pr-merged", result.Cause);
        var events = workspace.ReadRunEvents();
        Assert.Equal(AutomationPrCreatedStaleRecoveryCommand.EventAborted, events[^1].Event);
        Assert.Equal(1, events.Count(e =>
            e.Event == AutomationPrCreatedStaleRecoveryCommand.EventStarted));
    }

    [Theory]
    [InlineData("issue-unavailable")]
    [InlineData("issue-not-open")]
    [InlineData("target-absent")]
    [InlineData("queue-item-ambiguous")]
    [InlineData("unit-mismatch")]
    [InlineData("repo-mismatch-created-issue-url")]
    [InlineData("pr-state-unavailable")]
    [InlineData("pr-open")]
    [InlineData("pr-merged")]
    [InlineData("open-closing-pr")]
    [InlineData("open-pr-list-unavailable")]
    [InlineData("claim-held")]
    [InlineData("claim-unavailable")]
    [InlineData("in-progress-present")]
    public void SameKeyWriteResume_Checks5Through11_AppendsExactlyOneAborted(string cause)
    {
        using var workspace = CreateSameKeyResumeWorkspace(cause);
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, Pr));
        ConfigureSameKeyResumeScenario(workspace, cause);

        var beforeCount = workspace.ReadRunEvents().Count;
        var (exitCode, result) = Execute(workspace, write: true);

        Assert.Equal(1, exitCode);
        Assert.Equal(cause switch
        {
            "repo-mismatch-created-issue-url" => "repo-mismatch",
            _ => cause,
        }, result.Cause);
        var events = workspace.ReadRunEvents();
        Assert.Equal(beforeCount + 1, events.Count);
        Assert.Equal(AutomationPrCreatedStaleRecoveryCommand.EventAborted, events[^1].Event);
        workspace.AssertHostArtifactsUnchanged();
    }

    [Theory]
    [InlineData("issue-unavailable")]
    [InlineData("issue-not-open")]
    [InlineData("target-absent")]
    [InlineData("pr-state-unavailable")]
    [InlineData("pr-merged")]
    [InlineData("claim-held")]
    [InlineData("in-progress-present")]
    public void SameKeyWriteResume_DryRun_WritesNothing(string cause)
    {
        using var workspace = CreateSameKeyResumeWorkspace(cause);
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, Pr));
        ConfigureSameKeyResumeScenario(workspace, cause);

        var before = workspace.ReadRunEvents();
        var (exitCode, _) = Execute(workspace, write: false);

        Assert.Equal(1, exitCode);
        Assert.Equal(before, workspace.ReadRunEvents());
    }

    [Fact]
    public void ClosedThenLabelRemoved_AfterLabelChangedRecheckAbort_ExitsZeroWritesNoEvent()
    {
        using var workspace = CreateProceedWorkspace();
        var labelMutator = new RecordingLabelMutator("intent-target", "intent-pr-created");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;
        BindIssueLookup(new SequencedIssueLookup(
            OpenIssue("intent-target", "intent-pr-created"),
            OpenIssue("intent-target")));

        var (abortExit, abortResult) = Execute(workspace, write: true);
        Assert.Equal(1, abortExit);
        Assert.Equal("label-changed", abortResult.Cause);
        Assert.Equal(AutomationPrCreatedStaleRecoveryCommand.EventAborted,
            workspace.ReadRunEvents().Last().Event);

        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(OpenIssue("intent-target"));
        var (exitCode, result) = Execute(workspace, write: true);

        Assert.Equal(0, exitCode);
        Assert.Equal("closed-then-label-removed", result.Outcome);
        Assert.False(result.Applied);
        Assert.Equal(2, workspace.ReadRunEvents().Count);
        Assert.DoesNotContain(workspace.ReadRunEvents(),
            e => e.Event == AutomationPrCreatedStaleRecoveryCommand.EventRecovered);
    }

    [Fact]
    public void ClosedThenLabelRemoved_AfterSameKeyResumePrMergedAbort_NeverWritesRecovered()
    {
        using var workspace = CreateProceedWorkspace();
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, Pr));
        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () => new FakePrLookup(Merged(Pr));

        var (abortExit, _) = Execute(workspace, write: true);
        Assert.Equal(1, abortExit);

        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(OpenIssue("intent-target"));
        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () => new FakePrLookup(ClosedUnmerged(Pr));
        var (exitCode, result) = Execute(workspace, write: true);

        Assert.Equal(0, exitCode);
        Assert.Equal("closed-then-label-removed", result.Outcome);
        Assert.DoesNotContain(workspace.ReadRunEvents(),
            e => e.Event == AutomationPrCreatedStaleRecoveryCommand.EventRecovered);
    }

    [Fact]
    public void ClosedThenLabelRemoved_SupersededKeyOnly_LabelAbsent_ExitsZeroWritesNoEvent()
    {
        const int oldPr = 163;
        const int movedPr = 174;
        using var workspace = CreateProceedWorkspace(linkedPr: oldPr, publishPr: movedPr);
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventSuperseded, oldPr,
            reason: "outcome of the earlier run unknown"));
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(OpenIssue("intent-target"));

        var (exitCode, result) = Execute(workspace, write: false);

        Assert.Equal(0, exitCode);
        Assert.Equal("closed-then-label-removed", result.Outcome);
        Assert.Single(workspace.ReadRunEvents());
    }

    [Fact]
    public void ClosedThenLabelRemoved_CloserWithDifferentRepoCasing_StillClosesStarted()
    {
        using var workspace = CreateProceedWorkspace();
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, Pr));
        var aborted = BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventAborted, Pr,
            ts: FixedNow.AddMinutes(-5), reason: "re-check refused: label-changed");
        workspace.AppendRunEvent(aborted with
        {
            Repo = Repo.ToUpperInvariant(),
            LinkedIssue = IssueUrl(Issue).ToUpperInvariant(),
            LinkedPr = PrUrl(Pr).ToUpperInvariant(),
        });
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(OpenIssue("intent-target"));

        var (exitCode, result) = Execute(workspace, write: true);

        Assert.Equal(0, exitCode);
        Assert.Equal("closed-then-label-removed", result.Outcome);
        Assert.Equal(2, workspace.ReadRunEvents().Count);
    }

    [Fact]
    public void SupersededKeyOnly_LabelPresent_ProceedsWithNewStarted()
    {
        const int oldPr = 163;
        const int movedPr = 174;
        using var workspace = CreateProceedWorkspace(linkedPr: oldPr, publishPr: movedPr);
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventSuperseded, oldPr,
            reason: "outcome of the earlier run unknown"));
        var labelMutator = new RecordingLabelMutator("intent-target", "intent-pr-created");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;

        var (exitCode, result) = Execute(workspace, write: true);

        Assert.Equal(0, exitCode);
        Assert.Equal("recovered", result.Outcome);
        var events = workspace.ReadRunEvents();
        Assert.Contains(events, e => e.Event == AutomationPrCreatedStaleRecoveryCommand.EventStarted && e.Pr == oldPr);
        Assert.Contains(events, e => e.Event == AutomationPrCreatedStaleRecoveryCommand.EventRecovered && e.Pr == oldPr);
    }

    [Fact]
    public void Superseded_LabelAbsentRunNeverCompletesSupersededKey()
    {
        const int oldPr = 163;
        const int currentPr = 174;
        using var workspace = CreateProceedWorkspace(linkedPr: currentPr, publishPr: oldPr);
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, oldPr));
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventSuperseded, oldPr,
            reason: "outcome of the earlier run unknown"));
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, currentPr));
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventRecovered, currentPr));
        workspace.WriteQueueState(linkedPr: oldPr);
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(OpenIssue("intent-target"));
        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () =>
            new FakePrLookup(ClosedUnmerged(oldPr), ClosedUnmerged(currentPr));

        var (exitCode, result) = Execute(workspace, write: false);

        Assert.Equal(0, exitCode);
        Assert.Equal("closed-then-label-removed", result.Outcome);
        Assert.Equal(4, workspace.ReadRunEvents().Count);
    }

    [Fact]
    public void Superseded_FailedAppend_ExitsNonzeroBeforeStarted()
    {
        const int oldPr = 163;
        const int currentPr = 174;
        using var workspace = CreateProceedWorkspace(linkedPr: currentPr, publishPr: oldPr);
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, oldPr));
        var runsPath = workspace.Context.GetRunLogPath();
        File.SetAttributes(runsPath, FileAttributes.ReadOnly);
        var labelMutator = new RecordingLabelMutator("intent-target", "intent-pr-created");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;
        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () =>
            new FakePrLookup(ClosedUnmerged(currentPr));

        using var writer = new StringWriter();
        var exitCode = AutomationPrCreatedStaleRecoveryCommand.Execute(
            workspace.Context,
            [
                "--repo", Repo,
                "--issue", Issue.ToString(),
                "--execution-unit", Unit,
                "--team", Team,
                "--ruling", Ruling,
                "--write",
                "--format", "json",
            ],
            writer);

        File.SetAttributes(runsPath, FileAttributes.Normal);
        Assert.Equal(1, exitCode);
        Assert.Contains("failed to append superseded event", writer.ToString(), StringComparison.Ordinal);
        Assert.Single(workspace.ReadRunEvents());
        Assert.DoesNotContain(workspace.ReadRunEvents(),
            e => e.Event == AutomationPrCreatedStaleRecoveryCommand.EventSuperseded);
        Assert.DoesNotContain(workspace.ReadRunEvents(),
            e => e.Event == AutomationPrCreatedStaleRecoveryCommand.EventStarted && e.Pr == currentPr);
    }

    [Fact]
    public void Refuse_StartedAmbiguous_LabelAbsent_TwoOpenStartedKeys()
    {
        using var workspace = CreateProceedWorkspace();
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, Pr));
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, 174,
            ts: FixedNow.AddMinutes(1)));
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(OpenIssue("intent-target"));
        var (exitCode, result) = Execute(workspace, write: false);
        AssertRefusal(exitCode, result, "started-ambiguous", workspace);
    }

    // ── completion / audit / supersede / second rebuild ─────────────────

    [Fact]
    public void CompletionPath_LabelAbsent_DryRun_ReportsRecoveryCompleted_NoEvents()
    {
        using var workspace = CreateProceedWorkspace();
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, Pr));
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(OpenIssue("intent-target"));

        var (exitCode, result) = Execute(workspace, write: false);

        Assert.Equal(0, exitCode);
        Assert.Equal("recovery-completed", result.Outcome);
        Assert.False(result.Applied);
        Assert.Single(workspace.ReadRunEvents());
    }

    [Fact]
    public void CompletionPath_LabelAbsent_Write_AppendsRecoveredOnly_NoGitHubMutation()
    {
        using var workspace = CreateProceedWorkspace();
        workspace.AppendRunEvent(BuildRecoveryEvent(
            AutomationPrCreatedStaleRecoveryCommand.EventStarted,
            Pr,
            reason: "ruling: earlier; claim: unheld-available"));
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(OpenIssue("intent-target"));
        var labelMutator = new RecordingLabelMutator("intent-target");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;

        var (exitCode, result) = Execute(workspace, write: true);

        Assert.Equal(0, exitCode);
        Assert.Equal("recovery-completed", result.Outcome);
        Assert.True(result.Applied);
        Assert.Empty(labelMutator.Transitions);
        var events = workspace.ReadRunEvents();
        Assert.Equal(2, events.Count);
        Assert.Equal(AutomationPrCreatedStaleRecoveryCommand.EventRecovered, events[^1].Event);
        Assert.Contains("resumed: true", events[^1].Reason, StringComparison.Ordinal);
        Assert.Contains("label absent at completion", events[^1].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditOnly_LabelAbsent_NoPriorEvents_DryRun_EventCompleted()
    {
        using var workspace = CreateProceedWorkspace();
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(OpenIssue("intent-target"));

        var (exitCode, result) = Execute(workspace, write: false);

        Assert.Equal(0, exitCode);
        Assert.Equal("event-completed", result.Outcome);
        Assert.False(File.Exists(workspace.Context.GetRunLogPath()));
    }

    [Fact]
    public void AuditOnly_LabelAbsent_NoPriorEvents_Write_AppendsRecoveredOnly()
    {
        using var workspace = CreateProceedWorkspace();
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(OpenIssue("intent-target"));
        var labelMutator = new RecordingLabelMutator("intent-target");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;

        var (exitCode, result) = Execute(workspace, write: true);

        Assert.Equal(0, exitCode);
        Assert.Equal("event-completed", result.Outcome);
        Assert.True(result.Applied);
        Assert.Empty(labelMutator.Transitions);
        var recovered = Assert.Single(workspace.ReadRunEvents());
        Assert.Equal(AutomationPrCreatedStaleRecoveryCommand.EventRecovered, recovered.Event);
        Assert.Contains("resumed: true", recovered.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Superseded_DryRun_WarnsAndEvaluatesCurrentLinkedPr()
    {
        const int oldPr = 163;
        const int currentPr = 174;
        using var workspace = CreateProceedWorkspace(linkedPr: currentPr, publishPr: oldPr);
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, oldPr));
        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () =>
            new FakePrLookup(ClosedUnmerged(currentPr));

        var (exitCode, result) = Execute(workspace, write: false);

        Assert.Equal(0, exitCode);
        Assert.Equal(currentPr, result.LinkedPr);
        Assert.Contains(result.Warnings, w => w.Contains("superseded_started_event", StringComparison.Ordinal));
        Assert.Single(workspace.ReadRunEvents());
    }

    [Fact]
    public void Superseded_Write_ClosedUnmerged_AppendsSupersededStartedRecovered_InOrder()
    {
        const int oldPr = 163;
        const int currentPr = 174;
        using var workspace = CreateProceedWorkspace(linkedPr: currentPr, publishPr: oldPr);
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, oldPr));
        var labelMutator = new RecordingLabelMutator("intent-target", "intent-pr-created");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;
        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () =>
            new FakePrLookup(ClosedUnmerged(currentPr));

        var (exitCode, result) = Execute(workspace, write: true);

        Assert.Equal(0, exitCode);
        Assert.Equal("recovered", result.Outcome);
        var events = workspace.ReadRunEvents();
        Assert.Equal(4, events.Count);
        Assert.Equal(AutomationPrCreatedStaleRecoveryCommand.EventSuperseded, events[1].Event);
        Assert.Equal(oldPr, events[1].Pr);
        Assert.Equal(AutomationPrCreatedStaleRecoveryCommand.EventStarted, events[2].Event);
        Assert.Equal(currentPr, events[2].Pr);
        Assert.Equal(AutomationPrCreatedStaleRecoveryCommand.EventRecovered, events[3].Event);
        Assert.Equal(currentPr, events[3].Pr);
    }

    [Fact]
    public void SecondRebuild_AfterRecoveryFor163_ProceedsFor200()
    {
        using var workspace = CreateProceedWorkspace(linkedPr: Pr);
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventRecovered, Pr));
        workspace.WriteQueueState(linkedPr: 200);
        var labelMutator = new RecordingLabelMutator("intent-target", "intent-pr-created");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(OpenIssue("intent-target", "intent-pr-created"));
        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () =>
            new FakePrLookup(ClosedUnmerged(200));

        var (exitCode, result) = Execute(workspace, write: true);

        Assert.Equal(0, exitCode);
        Assert.Equal(200, result.LinkedPr);
        var newKeyEvents = workspace.ReadRunEvents()
            .Where(e => e.Pr == 200)
            .Select(e => e.Event)
            .ToArray();
        Assert.Equal(
            new[]
            {
                AutomationPrCreatedStaleRecoveryCommand.EventStarted,
                AutomationPrCreatedStaleRecoveryCommand.EventRecovered,
            },
            newKeyEvents);
    }

    [Fact]
    public void EventMatching_CompletedLinkageRepairOnSameKey_DoesNotBlockProceed()
    {
        using var workspace = CreateProceedWorkspace();
        workspace.AppendRunEvent(new RunEvent
        {
            Ts = FixedNow.AddHours(-2),
            ExecutionUnit = Unit,
            Event = "completed-linkage-repair",
            By = "automation-host-queue-item-recovery",
            Repo = Repo,
            LinkedIssue = IssueUrl(Issue),
            LinkedPr = PrUrl(Pr),
            Pr = Pr,
            Reason = "earlier linkage repair",
        });
        var labelMutator = new RecordingLabelMutator("intent-target", "intent-pr-created");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;

        var (exitCode, result) = Execute(workspace, write: true);

        Assert.Equal(0, exitCode);
        Assert.Equal("recovered", result.Outcome);
        Assert.Contains(workspace.ReadRunEvents(),
            e => e.Event == AutomationPrCreatedStaleRecoveryCommand.EventStarted);
    }

    [Fact]
    public void EndToEnd_ClaimReleaseRecovery_ThenWorkerClaimPasses()
    {
        using var workspace = CreateProceedWorkspace();
        workspace.WriteClaim("builder", Team);
        var labelMutator = new RecordingLabelMutator("intent-target", "intent-pr-created");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;

        workspace.ReleaseClaim();
        WorkerClaimCommand.MutatorFactory = () => new RecordingLabelMutator("intent-target", "intent-pr-created");
        WorkerClaimCommand.IssueLookupFactory = () => new FakeIssueLookup(OpenIssue("intent-target", "intent-pr-created"));

        using var claimWriter = new StringWriter();
        var claimExit = WorkerClaimCommand.Execute(
            workspace.Context,
            [
                "--repo", Repo, "--kind", "issue", "--number", Issue.ToString(),
                "--team", Team, "--dry-run", "--github-only", "--format", "json",
            ],
            claimWriter);
        var claimResult = JsonSerializer.Deserialize<WorkerClaimResult>(claimWriter.ToString())!;
        Assert.Equal(2, claimExit);
        Assert.Contains(claimResult.Errors, e =>
            e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.AlreadyCompleted, StringComparison.Ordinal));
        Assert.Contains(claimResult.Warnings, w =>
            w.Contains("pr-created-stale-recovery", StringComparison.Ordinal));

        var (recoveryExit, recoveryResult) = Execute(workspace, write: true);
        Assert.Equal(0, recoveryExit);
        Assert.Equal("recovered", recoveryResult.Outcome);

        WorkerClaimCommand.MutatorFactory = () => new RecordingLabelMutator("intent-target");
        using var afterWriter = new StringWriter();
        var afterExit = WorkerClaimCommand.Execute(
            workspace.Context,
            [
                "--repo", Repo, "--kind", "issue", "--number", Issue.ToString(),
                "--team", Team, "--dry-run", "--github-only", "--format", "json",
            ],
            afterWriter);
        var afterResult = JsonSerializer.Deserialize<WorkerClaimResult>(afterWriter.ToString())!;
        Assert.Equal(0, afterExit);
        Assert.True(afterResult.Proceed);
    }

    // ── router / mutation guards / worker hint ──────────────────────────

    [Fact]
    public void CommandRouter_HelpListsPrCreatedStaleRecovery()
    {
        var router = typeof(CommandRouter);
        var helpField = router.GetField("AutomationCommandHelp",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(helpField);
        var lines = (IReadOnlyList<string>?)helpField!.GetValue(null);
        Assert.NotNull(lines);
        Assert.Contains(lines!, l => l.Contains("pr-created-stale-recovery", StringComparison.Ordinal));
    }

    [Fact]
    public void CommandRouter_DispatchesPrCreatedStaleRecoveryToCommand()
    {
        var router = typeof(CommandRouter);
        var commandsField = router.GetField("ImplementedCommands",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(commandsField);
        var groups = commandsField!.GetValue(null);
        Assert.NotNull(groups);

        var outer = (System.Collections.IDictionary)groups!;
        Assert.True(outer.Contains("automation"));
        var inner = (System.Collections.IDictionary)outer["automation"]!;
        Assert.True(inner.Contains("pr-created-stale-recovery"));
        var handler = (Delegate)inner["pr-created-stale-recovery"]!;
        Assert.Equal(
            typeof(AutomationPrCreatedStaleRecoveryCommand).GetMethod(
                "Execute",
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static)!,
            handler.Method);
    }

    [Fact]
    public void WorkerClaim_UnheldAlreadyCompleted_IncludesStaleRecoveryHint()
    {
        using var workspace = CreateProceedWorkspace(configureClaimsStore: true);
        WorkerClaimCommand.MutatorFactory = () =>
            new RecordingLabelMutator("intent-target", "intent-pr-created");
        WorkerClaimCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(OpenIssue("intent-target", "intent-pr-created"));

        using var writer = new StringWriter();
        var exitCode = WorkerClaimCommand.Execute(
            workspace.Context,
            [
                "--repo", Repo, "--kind", "issue", "--number", Issue.ToString(),
                "--team", Team, "--dry-run", "--github-only", "--format", "json",
            ],
            writer);

        var result = JsonSerializer.Deserialize<WorkerClaimResult>(writer.ToString())!;
        Assert.Equal(2, exitCode);
        Assert.Contains(result.Warnings, w =>
            w.Contains("automation pr-created-stale-recovery", StringComparison.Ordinal)
            && w.Contains($"--execution-unit {Unit}", StringComparison.Ordinal)
            && w.Contains($"--team {Team}", StringComparison.Ordinal)
            && w.Contains("<ruling>", StringComparison.Ordinal));
    }

    [Fact]
    public void WorkerClaim_OtherPaths_DoNotGainStaleRecoveryHint()
    {
        using var workspace = CreateProceedWorkspace(configureClaimsStore: true);

        var scenarios = new (string Name, string[] Labels, bool WriteClaim, int ExpectedExitCode)[]
        {
            ("held-pr-created", new[] { "intent-target", "intent-pr-created" }, true, 2),
            ("missing-target", Array.Empty<string>(), false, 2),
            ("unheld-in-progress-shadow", new[] { "intent-target", "intent-issue-in-progress" }, false, 0),
        };

        foreach (var scenario in scenarios)
        {
            if (scenario.WriteClaim)
            {
                workspace.WriteClaim("builder", Team);
            }
            else
            {
                workspace.ReleaseClaim();
                workspace.EnsureClaimsStore();
            }

            WorkerClaimCommand.MutatorFactory = () => new RecordingLabelMutator(scenario.Labels);
            WorkerClaimCommand.IssueLookupFactory = () =>
                new FakeIssueLookup(OpenIssue(scenario.Labels));

            using var writer = new StringWriter();
            var exitCode = WorkerClaimCommand.Execute(
                workspace.Context,
                [
                    "--repo", Repo, "--kind", "issue", "--number", Issue.ToString(),
                    "--team", Team, "--dry-run", "--format", "json",
                ],
                writer);

            var result = JsonSerializer.Deserialize<WorkerClaimResult>(writer.ToString())!;
            Assert.DoesNotContain(result.Warnings, w =>
                w.Contains("pr-created-stale-recovery", StringComparison.Ordinal));
            Assert.Equal(scenario.ExpectedExitCode, exitCode);
        }
    }

    // Head-only evidence hook; canonical base-vs-head output lives in g836-verify/wc.
    [Fact]
    public void WorkerClaim_Evidence_WriteBaseVsHeadOutputs_HeadOnly()
    {
        var evidenceDir = Environment.GetEnvironmentVariable("G836_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(evidenceDir))
        {
            return;
        }

        Directory.CreateDirectory(evidenceDir);
        using var workspace = CreateProceedWorkspace(configureClaimsStore: true);
        var cases = new Dictionary<string, string[]>
        {
            ["unheld-already-completed-normal"] =
            [
                "--repo", Repo, "--kind", "issue", "--number", Issue.ToString(),
                "--team", Team, "--dry-run", "--format", "json",
            ],
            ["unheld-already-completed-github-only"] =
            [
                "--repo", Repo, "--kind", "issue", "--number", Issue.ToString(),
                "--team", Team, "--dry-run", "--github-only", "--format", "json",
            ],
            ["held-pr-created"] =
            [
                "--repo", Repo, "--kind", "issue", "--number", Issue.ToString(),
                "--team", Team, "--dry-run", "--format", "json",
            ],
        };

        WorkerClaimCommand.MutatorFactory = () =>
            new RecordingLabelMutator("intent-target", "intent-pr-created");
        WorkerClaimCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(OpenIssue("intent-target", "intent-pr-created"));
        foreach (var (name, args) in cases)
        {
            if (name == "held-pr-created")
            {
                workspace.WriteClaim("builder", Team);
            }
            else
            {
                workspace.ReleaseClaim();
                workspace.EnsureClaimsStore();
            }

            using var writer = new StringWriter();
            WorkerClaimCommand.Execute(workspace.Context, args, writer);
            File.WriteAllText(Path.Combine(evidenceDir, $"worker-claim-head-{name}.json"), writer.ToString());
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static RecoveryWorkspace CreateSameKeyResumeWorkspace(string cause) =>
        cause switch
        {
            "queue-item-ambiguous" => CreateProceedWorkspace(duplicateQueueItem: true),
            "unit-mismatch" => CreateProceedWorkspace(executionUnit: "G999"),
            "repo-mismatch-created-issue-url" => CreateProceedWorkspace(createdIssueRepo: "Other-Org/other-repo"),
            _ => CreateProceedWorkspace(),
        };

    private static RecoveryWorkspace CreateProceedWorkspace(
        int? linkedPr = Pr,
        int? publishPr = null,
        string? executionUnit = null,
        string? createdIssueRepo = null,
        string? linkedPrRepo = null,
        bool includeQueueItem = true,
        bool duplicateQueueItem = false,
        bool configureClaimsStore = true,
        bool linkedPrObjectWithoutUrl = false)
    {
        var workspace = new RecoveryWorkspace();
        workspace.WriteProceedHostState(
            linkedPr: linkedPr,
            publishPr: publishPr ?? linkedPr ?? Pr,
            createdIssueRepo: createdIssueRepo ?? Repo,
            linkedPrRepo: linkedPrRepo ?? Repo,
            includeQueueItem: includeQueueItem,
            duplicateQueueItem: duplicateQueueItem,
            linkedPrObjectWithoutUrl: linkedPrObjectWithoutUrl,
            executionUnit: executionUnit);
        if (configureClaimsStore)
        {
            workspace.EnsureClaimsStore();
        }

        SeedGitHubFakes(workspace, linkedPr ?? Pr);
        return workspace;
    }

    private static void SeedGitHubFakes(RecoveryWorkspace workspace, int linkedPr)
    {
        var issueLookup = new FakeIssueLookup(OpenIssue("intent-target", "intent-pr-created"));
        var prLookup = new FakePrLookup(ClosedUnmerged(linkedPr));
        var lister = new FakeLister();
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () => issueLookup;
        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () => prLookup;
        AutomationPrCreatedStaleRecoveryCommand.CandidateListerFactory = () => lister;
    }

    private static (int ExitCode, AutomationPrCreatedStaleRecoveryResult Result) Execute(
        RecoveryWorkspace workspace,
        bool write,
        string? ruling = null)
    {
        var args = new List<string>
        {
            "--repo", Repo,
            "--issue", Issue.ToString(),
            "--execution-unit", Unit,
            "--team", Team,
            "--ruling", ruling ?? Ruling,
            "--format", "json",
        };
        if (write)
        {
            args.Add("--write");
        }

        using var writer = new StringWriter();
        var exitCode = AutomationPrCreatedStaleRecoveryCommand.Execute(workspace.Context, args.ToArray(), writer);
        return (exitCode, JsonSerializer.Deserialize<AutomationPrCreatedStaleRecoveryResult>(writer.ToString())!);
    }

    private static void AssertRefusal(
        int exitCode,
        AutomationPrCreatedStaleRecoveryResult result,
        string cause,
        RecoveryWorkspace workspace)
    {
        Assert.Equal(1, exitCode);
        Assert.Equal("refused", result.Outcome);
        Assert.Equal(cause, result.Cause);
        Assert.False(result.Applied);
        workspace.AssertHostArtifactsUnchanged();
    }

    private static void ConfigureSameKeyResumeScenario(RecoveryWorkspace workspace, string cause)
    {
        switch (cause)
        {
            case "issue-unavailable":
                BindIssueLookup(new SequencedIssueLookup(
                    OpenIssue("intent-target", "intent-pr-created"),
                    new InvalidOperationException("issue lookup failed")));
                break;
            case "issue-not-open":
                BindIssueLookup(new SequencedIssueLookup(
                    OpenIssue("intent-target", "intent-pr-created"),
                    ClosedIssue()));
                break;
            case "target-absent":
                BindIssueLookup(new SequencedIssueLookup(
                    OpenIssue("intent-target", "intent-pr-created"),
                    OpenIssue("intent-pr-created")));
                break;
            case "queue-item-ambiguous":
            case "unit-mismatch":
            case "repo-mismatch-created-issue-url":
                SeedGitHubFakes(workspace, Pr);
                break;
            case "pr-state-unavailable":
                AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () =>
                    new FakePrLookup(new InvalidOperationException("PR lookup failed"));
                break;
            case "pr-open":
                AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () =>
                    new FakePrLookup(OpenPr(Pr));
                break;
            case "pr-merged":
                AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () =>
                    new FakePrLookup(Merged(Pr));
                break;
            case "open-closing-pr":
                AutomationPrCreatedStaleRecoveryCommand.CandidateListerFactory = () =>
                    new FakeLister(prs: [OpenClosingPr(902, Issue)]);
                break;
            case "open-pr-list-unavailable":
                AutomationPrCreatedStaleRecoveryCommand.CandidateListerFactory = () =>
                    new FakeLister(listFailure: new InvalidOperationException("gh pr list failed"));
                break;
            case "claim-held":
                BindClaimVerifier(new SequencedClaimVerifier(HeldClaim()));
                break;
            case "claim-unavailable":
                BindClaimVerifier(new SequencedClaimVerifier(
                    ClaimUnavailable(ClaimOwnershipVerification.StatusNotConfigured)));
                break;
            case "in-progress-present":
                AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
                    new FakeIssueLookup(OpenIssue("intent-target", "intent-pr-created", "intent-issue-in-progress"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(cause), cause, null);
        }
    }

    private static void ConfigureRecheckScenario(string scenario, RecordingLabelMutator labelMutator)
    {
        switch (scenario)
        {
            case "issue-not-open":
                BindIssueLookup(new SequencedIssueLookup(
                    OpenIssue("intent-target", "intent-pr-created"), ClosedIssue()));
                break;
            case "target-absent":
                BindIssueLookup(new SequencedIssueLookup(
                    OpenIssue("intent-target", "intent-pr-created"),
                    OpenIssue("intent-pr-created")));
                break;
            case "in-progress-present":
                BindIssueLookup(new SequencedIssueLookup(
                    OpenIssue("intent-target", "intent-pr-created"),
                    OpenIssue("intent-target", "intent-pr-created", "intent-issue-in-progress")));
                break;
            case "label-changed":
                BindIssueLookup(new SequencedIssueLookup(
                    OpenIssue("intent-target", "intent-pr-created"),
                    OpenIssue("intent-target")));
                labelMutator.SetLabels(["intent-target"]);
                break;
            case "pr-open":
                BindPrLookup(new SequencedPrLookup(ClosedUnmerged(Pr), OpenPr(Pr)));
                break;
            case "pr-merged":
                BindPrLookup(new SequencedPrLookup(ClosedUnmerged(Pr), Merged(Pr)));
                break;
            case "open-closing-pr":
                BindLister(new SequencedLister(
                    Array.Empty<GitHubAutomationPrCandidate>(),
                    [OpenClosingPr(901, Issue)]));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }
    }

    private static void BindIssueLookup(IGitHubIssueLookup lookup) =>
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () => lookup;

    private static void BindPrLookup(IGitHubPrLookup lookup) =>
        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () => lookup;

    private static void BindLister(IGitHubAutomationCandidateLister lister) =>
        AutomationPrCreatedStaleRecoveryCommand.CandidateListerFactory = () => lister;

    private static void BindClaimVerifier(SequencedClaimVerifier verifier) =>
        AutomationPrCreatedStaleRecoveryCommand.ClaimVerifierFactory =
            verifier.Verify;

    private static ClaimOwnershipVerification UnheldClaim() => new(
        Passed: true,
        Status: ClaimOwnershipVerification.StatusUnheldAvailable,
        Scope: $"execution-unit:{Unit}",
        StoreConfigured: true,
        InvokingTeam: Team,
        Holder: null,
        HolderTeam: null,
        Detail: "unheld");

    private static ClaimOwnershipVerification HeldClaim() => new(
        Passed: false,
        Status: ClaimOwnershipVerification.StatusOwned,
        Scope: $"execution-unit:{Unit}",
        StoreConfigured: true,
        InvokingTeam: Team,
        Holder: "builder",
        HolderTeam: Team,
        Detail: "owned");

    private static ClaimOwnershipVerification ClaimUnavailable(string status) => new(
        Passed: false,
        Status: status,
        Scope: $"execution-unit:{Unit}",
        StoreConfigured: false,
        InvokingTeam: Team,
        Holder: null,
        HolderTeam: null,
        Detail: "unavailable");

    private static GitHubIssueLookupResult OpenIssue(params string[] labels) => new()
    {
        Number = Issue,
        State = "OPEN",
        Title = $"{Unit}: stale pr-created recovery fixture",
        Labels = labels.Select(name => new GitHubIssueLabel { Name = name }).ToArray(),
    };

    private static GitHubIssueLookupResult ClosedIssue() => new()
    {
        Number = Issue,
        State = "CLOSED",
        Title = $"{Unit}: closed",
        Labels = Array.Empty<GitHubIssueLabel>(),
    };

    private static GitHubPrLookupResult ClosedUnmerged(int pr) => new()
    {
        Number = pr,
        State = "CLOSED",
        Merged = false,
        MergedAt = null,
    };

    private static GitHubPrLookupResult OpenPr(int pr) => new()
    {
        Number = pr,
        State = "OPEN",
        Merged = false,
    };

    private static GitHubPrLookupResult Merged(int pr) => new()
    {
        Number = pr,
        State = "MERGED",
        Merged = true,
        MergedAt = "2026-01-01T00:00:00Z",
    };

    private static GitHubAutomationPrCandidate OpenClosingPr(int prNumber, int closingIssue) => new()
    {
        Number = prNumber,
        Title = $"closing PR #{prNumber}",
        Url = $"{Repo}/pull/{prNumber}",
        CreatedAt = FixedNow.AddDays(-1).ToString("O"),
        UpdatedAt = FixedNow.AddDays(-1).ToString("O"),
        State = "OPEN",
        ClosingIssuesReferences =
        [
            new GitHubPrClosingIssueReference
            {
                Number = closingIssue,
                Repository = new GitHubPrClosingIssueRepository
                {
                    Name = "intent-system",
                    Owner = new GitHubPrClosingIssueRepositoryOwner { Login = "J-Tech-Japan" },
                },
            },
        ],
    };

    private static RunEvent BuildRecoveryEvent(
        string eventName,
        int pr,
        DateTimeOffset? ts = null,
        string? reason = null) => new()
    {
        Ts = ts ?? FixedNow.AddMinutes(-10),
        ExecutionUnit = Unit,
        Event = eventName,
        By = AutomationPrCreatedStaleRecoveryCommand.By,
        Repo = Repo,
        LinkedIssue = IssueUrl(Issue),
        LinkedPr = PrUrl(pr),
        Pr = pr,
        Reason = reason ?? $"ruling: {Ruling}; claim: unheld-available",
    };

    private static string IssueUrl(int issue) => $"https://github.com/{Repo}/issues/{issue}";

    private static string PrUrl(int pr) => $"https://github.com/{Repo}/pull/{pr}";

    private static int ParseIssueNumber(string? url)
    {
        var match = System.Text.RegularExpressions.Regex.Match(url ?? string.Empty, @"/issues/(\d+)");
        return int.Parse(match.Groups[1].Value);
    }

    // ── workspace ───────────────────────────────────────────────────────

    private sealed class RecoveryWorkspace : IDisposable
    {
        private string publishYamlSnapshot = string.Empty;
        private string queueStateSnapshot = string.Empty;
        private string? claimSnapshot;

        public RecoveryWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("g836-recovery-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            };
        }

        private RecoveryWorkspace(string rootPath)
        {
            RootPath = rootPath;
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            };
        }

        public static RecoveryWorkspace At(string rootPath) => new(rootPath);

        public string RootPath { get; }

        public CliContext Context { get; }

        public void WriteProceedHostState(
            int? linkedPr = Pr,
            int publishPr = Pr,
            string createdIssueRepo = Repo,
            string linkedPrRepo = Repo,
            bool includeQueueItem = true,
            bool duplicateQueueItem = false,
            bool linkedPrObjectWithoutUrl = false,
            string? executionUnit = null)
        {
            if (includeQueueItem)
            {
                WriteQueueState(
                    linkedPr: linkedPr,
                    linkedPrRepo: linkedPrRepo,
                    duplicateQueueItem: duplicateQueueItem,
                    linkedPrObjectWithoutUrl: linkedPrObjectWithoutUrl,
                    executionUnit: executionUnit);
            }
            else
            {
                WriteEmptyQueueState();
            }

            WritePublishYaml(publishPr, createdIssueRepo);
            CaptureSnapshots();
        }

        public void WriteEmptyQueueState()
        {
            File.WriteAllText(
                Context.GetQueueStatePath(),
                QueueStateSerializer.Serialize(new QueueState
                {
                    SchemaVersion = "1",
                    UpdatedAt = FixedNow,
                    Items = Array.Empty<QueueItem>(),
                }));
        }

        public void WriteQueueState(
            int? linkedPr = Pr,
            string linkedPrRepo = Repo,
            bool duplicateQueueItem = false,
            bool linkedPrObjectWithoutUrl = false,
            string? executionUnit = null)
        {
            var unit = executionUnit ?? Unit;
            string? linkedPrValue = linkedPrObjectWithoutUrl || linkedPr is null
                ? null
                : $"https://github.com/{linkedPrRepo}/pull/{linkedPr}";

            if (linkedPrObjectWithoutUrl)
            {
                var objectJson = JsonSerializer.Serialize(new
                {
                    repo = linkedPrRepo,
                    number = linkedPr,
                });
                File.WriteAllText(
                    Context.GetQueueStatePath(),
                    BuildQueueStateRawJson(unit, objectJson, duplicateQueueItem));
                return;
            }

            File.WriteAllText(
                Context.GetQueueStatePath(),
                BuildQueueStateJson(unit, linkedPrValue, duplicateQueueItem: duplicateQueueItem));
        }

        public void WritePublishYaml(int publishPr, string createdIssueRepo)
        {
            var issueDir = Path.Combine(RootPath, ".intent-cli", "issues", Unit);
            Directory.CreateDirectory(issueDir);
            var artifact = new IssuePublishArtifact
            {
                ExecutionUnit = Unit,
                PublishStatus = "published",
                PacketPath = $".intent-cli/issues/{Unit}/packet.yaml",
                IssueBodyPath = $".intent-cli/issues/{Unit}/issue-body.md",
                CreatedIssueNumber = Issue,
                CreatedIssueUrl = $"https://github.com/{createdIssueRepo}/issues/{Issue}",
                PublishedLabelName = "intent-target",
                LinkedPrNumber = publishPr,
                LinkedPrUrl = $"https://github.com/{Repo}/pull/{publishPr}",
            };
            File.WriteAllText(
                Path.Combine(issueDir, "publish.yaml"),
                IssuePublishArtifactYaml.Serialize(artifact));
        }

        public void EnsureClaimsStore()
        {
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli", "claims"));
        }

        public void WriteClaim(string actor, string team)
        {
            EnsureClaimsStore();
            WriteRawClaim(JsonSerializer.Serialize(new ClaimRecord(
                "1",
                $"execution-unit:{Unit}",
                actor,
                team,
                DateTimeOffset.UtcNow,
                "g836-fixture")));
        }

        public void WriteRawClaim(string json)
        {
            EnsureClaimsStore();
            var scope = $"execution-unit:{Unit}";
            var relative = ClaimCommand.ClaimPath(scope).Replace('/', Path.DirectorySeparatorChar);
            var path = Path.Combine(RootPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }

        public void ReleaseClaim()
        {
            var scope = $"execution-unit:{Unit}";
            var relative = ClaimCommand.ClaimPath(scope).Replace('/', Path.DirectorySeparatorChar);
            var path = Path.Combine(RootPath, relative);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public void InitializeBrokenOrigin()
        {
            RunGit(RootPath, "init", "--quiet");
            RunGit(RootPath, "remote", "add", "origin", Path.Combine(RootPath, "missing-origin.git"));
        }

        public void WriteUnreadableRunsLog()
        {
            var runsPath = Context.GetRunLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(runsPath)!);
            File.WriteAllText(runsPath, "{ not valid jsonl\n");
        }

        public void AppendRunEvent(RunEvent runEvent)
        {
            var runsPath = Context.GetRunLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(runsPath)!);
            File.AppendAllText(runsPath, RunLogSerializer.SerializeLine(runEvent) + "\n");
        }

        public IReadOnlyList<RunEvent> ReadRunEvents()
        {
            var runsPath = Context.GetRunLogPath();
            if (!File.Exists(runsPath))
            {
                return Array.Empty<RunEvent>();
            }

            return RunLogSerializer.DeserializeAll(File.ReadAllText(runsPath));
        }

        public void AssertHostArtifactsUnchanged()
        {
            if (!string.IsNullOrEmpty(queueStateSnapshot))
            {
                Assert.Equal(queueStateSnapshot, File.ReadAllText(Context.GetQueueStatePath()));
            }

            var publishPath = Path.Combine(RootPath, ".intent-cli", "issues", Unit, "publish.yaml");
            if (!string.IsNullOrEmpty(publishYamlSnapshot) && File.Exists(publishPath))
            {
                Assert.Equal(publishYamlSnapshot, File.ReadAllText(publishPath));
            }

            if (claimSnapshot is not null)
            {
                var scope = $"execution-unit:{Unit}";
                var relative = ClaimCommand.ClaimPath(scope).Replace('/', Path.DirectorySeparatorChar);
                var claimPath = Path.Combine(RootPath, relative);
                if (File.Exists(claimPath))
                {
                    Assert.Equal(claimSnapshot, File.ReadAllText(claimPath));
                }
            }
        }

        private void CaptureSnapshots()
        {
            if (File.Exists(Context.GetQueueStatePath()))
            {
                queueStateSnapshot = File.ReadAllText(Context.GetQueueStatePath());
            }

            var publishPath = Path.Combine(RootPath, ".intent-cli", "issues", Unit, "publish.yaml");
            if (File.Exists(publishPath))
            {
                publishYamlSnapshot = File.ReadAllText(publishPath);
            }

            var scope = $"execution-unit:{Unit}";
            var relative = ClaimCommand.ClaimPath(scope).Replace('/', Path.DirectorySeparatorChar);
            var claimPath = Path.Combine(RootPath, relative);
            if (File.Exists(claimPath))
            {
                claimSnapshot = File.ReadAllText(claimPath);
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private static string BuildQueueStateJson(
            string unit,
            string? linkedPr,
            bool duplicateQueueItem = false)
        {
            var items = new List<QueueItem> { BuildQueueItem(unit, linkedPr) };
            if (duplicateQueueItem)
            {
                items.Add(BuildQueueItem($"{unit}-dup", linkedPr));
            }

            return QueueStateSerializer.Serialize(new QueueState
            {
                SchemaVersion = "1",
                UpdatedAt = FixedNow,
                Items = items,
            });
        }

        private static string BuildQueueStateRawJson(string unit, string linkedPrObjectJson, bool duplicateQueueItem)
        {
            var secondItem = duplicateQueueItem
                ? $$"""
                    ,{
                      "execution_unit": "{{unit}}-dup",
                      "title": "{{unit}}-dup title",
                      "state": "queued",
                      "dependencies": [],
                      "blocked_by": [],
                      "clarification_return_path": "",
                      "packet_paths": {
                        "yaml": ".intent-cli/issues/{{unit}}-dup/packet.yaml",
                        "implementation": ".intent-cli/issues/{{unit}}-dup/implementation.md",
                        "review_context": ".intent-cli/issues/{{unit}}-dup/review-context.md"
                      },
                      "linked_issue": {
                        "repo": "{{Repo}}",
                        "number": {{Issue}},
                        "url": "https://github.com/{{Repo}}/issues/{{Issue}}"
                      },
                      "linked_pr": {{linkedPrObjectJson}},
                      "worker_role": "{{LogicalRoleNormalizer.Builder}}",
                      "review_role": "{{LogicalRoleNormalizer.Reviewer}}",
                      "priority": "normal"
                    }
                    """
                : string.Empty;

            return $$"""
                {
                  "schema_version": "1",
                  "updated_at": "{{FixedNow:O}}",
                  "items": [
                    {
                      "execution_unit": "{{unit}}",
                      "title": "{{unit}} title",
                      "state": "queued",
                      "dependencies": [],
                      "blocked_by": [],
                      "clarification_return_path": "",
                      "packet_paths": {
                        "yaml": ".intent-cli/issues/{{unit}}/packet.yaml",
                        "implementation": ".intent-cli/issues/{{unit}}/implementation.md",
                        "review_context": ".intent-cli/issues/{{unit}}/review-context.md"
                      },
                      "linked_issue": {
                        "repo": "{{Repo}}",
                        "number": {{Issue}},
                        "url": "https://github.com/{{Repo}}/issues/{{Issue}}"
                      },
                      "linked_pr": {{linkedPrObjectJson}},
                      "worker_role": "{{LogicalRoleNormalizer.Builder}}",
                      "review_role": "{{LogicalRoleNormalizer.Reviewer}}",
                      "priority": "normal"
                    }{{secondItem}}
                  ]
                }
                """;
        }

        private static QueueItem BuildQueueItem(string unit, string? linkedPr)
        {
            return new QueueItem
            {
                ExecutionUnit = unit,
                Title = $"{unit} title",
                State = QueueItemState.Queued,
                Dependencies = Array.Empty<string>(),
                BlockedBy = Array.Empty<string>(),
                ClarificationReturnPath = string.Empty,
                PacketPaths = new PacketPaths
                {
                    Yaml = $".intent-cli/issues/{unit}/packet.yaml",
                    Implementation = $".intent-cli/issues/{unit}/implementation.md",
                    ReviewContext = $".intent-cli/issues/{unit}/review-context.md",
                },
                LinkedIssue = new LinkedIssue
                {
                    Repo = Repo,
                    Number = Issue,
                    Url = IssueUrl(Issue),
                },
                LinkedPr = linkedPr,
                WorkerRole = LogicalRoleNormalizer.Builder,
                ReviewRole = LogicalRoleNormalizer.Reviewer,
                Priority = "normal",
            };
        }
    }

    private sealed class MetadataBranchRepos : IDisposable
    {
        private readonly string temp = Directory.CreateTempSubdirectory("g836-metadata-").FullName;

        public MetadataBranchRepos()
        {
            Bare = Path.Combine(temp, "origin.git");
            FirstClone = Path.Combine(temp, "first");
            Directory.CreateDirectory(Bare);
            RunGit(Bare, "init", "--bare", "--quiet");
            var seed = Path.Combine(temp, "seed");
            Directory.CreateDirectory(seed);
            RunGit(seed, "init", "--quiet", "--initial-branch=main");
            RunGit(seed, "config", "user.name", "g836-fixture");
            RunGit(seed, "config", "user.email", "g836-fixture@example.invalid");
            File.WriteAllText(Path.Combine(seed, "README.md"), "fixture\n");
            RunGit(seed, "add", "README.md");
            RunGit(seed, "commit", "--quiet", "-m", "seed");
            RunGit(seed, "remote", "add", "origin", Bare);
            RunGit(seed, "push", "--quiet", "-u", "origin", "main");
            RunGit(seed, "switch", "--quiet", "-c", "main-metadata");
            var claimDir = Path.Combine(seed, ClaimCommand.ClaimsDirectory.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(claimDir);
            var claimPath = Path.Combine(
                claimDir,
                ClaimCommand.ClaimPath($"execution-unit:{Unit}").Split('/').Last());
            File.WriteAllText(
                claimPath,
                JsonSerializer.Serialize(new ClaimRecord(
                    "1",
                    $"execution-unit:{Unit}",
                    "builder",
                    Team,
                    DateTimeOffset.UtcNow,
                    "g836-metadata-fixture")));
            RunGit(seed, "add", "--", ClaimCommand.ClaimsDirectory);
            RunGit(seed, "commit", "--quiet", "-m", "metadata claims");
            RunGit(seed, "push", "--quiet", "-u", "origin", "main-metadata");
            RunGit(Bare, "symbolic-ref", "HEAD", "refs/heads/main");
            RunGit(temp, "clone", "--quiet", Bare, FirstClone);
            Directory.CreateDirectory(Path.Combine(FirstClone, ".intent-cli"));
            File.WriteAllText(
                Path.Combine(FirstClone, ".intent-cli", "config.toml"),
                "[project]\n"
                + "domain = \"intent-cli\"\n"
                + "artifact_root = \".intent-cli\"\n"
                + "metadata_source_branch = \"main-metadata\"\n");
        }

        public string Bare { get; }

        public string FirstClone { get; }

        public void Dispose()
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var error = process!.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
    }

    // ── GitHub fakes ────────────────────────────────────────────────────

    private sealed class ThrowingIssueLookup : IGitHubIssueLookup
    {
        public GitHubIssueLookupResult Lookup(string repo, int issueNumber) =>
            throw new InvalidOperationException("IssueLookupFactory was not overridden in test.");
    }

    private sealed class ThrowingPrLookup : IGitHubPrLookup
    {
        public GitHubPrLookupResult Lookup(string repo, int prNumber) =>
            throw new InvalidOperationException("PrLookupFactory was not overridden in test.");
    }

    private sealed class ThrowingLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) =>
            throw new InvalidOperationException("CandidateListerFactory was not overridden in test.");

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) =>
            throw new InvalidOperationException("CandidateListerFactory was not overridden in test.");
    }

    private sealed class ThrowingLabelMutator : IGitHubLabelMutator
    {
        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            throw new InvalidOperationException("LabelMutatorFactory was not overridden in test.");

        public void ApplyLabelTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new InvalidOperationException("LabelMutatorFactory was not overridden in test.");

        public void ApplyReconcileTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();
    }

    private sealed class FakeIssueLookup : IGitHubIssueLookup
    {
        private readonly GitHubIssueLookupResult? snapshot;
        private readonly Exception? failure;

        public FakeIssueLookup(GitHubIssueLookupResult snapshot) => this.snapshot = snapshot;

        public FakeIssueLookup(Exception failure) => this.failure = failure;

        public GitHubIssueLookupResult Lookup(string repo, int issueNumber)
        {
            if (failure is not null)
            {
                throw failure;
            }

            return snapshot!;
        }
    }

    private sealed class SequencedIssueLookup : IGitHubIssueLookup
    {
        private readonly Queue<object> sequence = new();

        public SequencedIssueLookup(params object[] steps)
        {
            foreach (var step in steps)
            {
                sequence.Enqueue(step);
            }
        }

        public GitHubIssueLookupResult Lookup(string repo, int issueNumber)
        {
            if (sequence.Count == 0)
            {
                throw new InvalidOperationException("no more sequenced issue lookup results");
            }

            var next = sequence.Dequeue();
            if (next is Exception exception)
            {
                throw exception;
            }

            return (GitHubIssueLookupResult)next;
        }
    }

    private sealed class FakePrLookup : IGitHubPrLookup
    {
        private readonly Dictionary<int, GitHubPrLookupResult> map = new();
        private readonly Exception? failure;

        public FakePrLookup(GitHubPrLookupResult result) => map[result.Number] = result;

        public FakePrLookup(params GitHubPrLookupResult[] results)
        {
            foreach (var result in results)
            {
                map[result.Number] = result;
            }
        }

        public FakePrLookup(Exception failure) => this.failure = failure;

        public GitHubPrLookupResult Lookup(string repo, int prNumber)
        {
            if (failure is not null)
            {
                throw failure;
            }

            if (map.TryGetValue(prNumber, out var result))
            {
                return result;
            }

            throw new InvalidOperationException($"no fake PR for #{prNumber}");
        }
    }

    private sealed class SequencedPrLookup : IGitHubPrLookup
    {
        private readonly Queue<object> sequence = new();

        public SequencedPrLookup(params object[] steps)
        {
            foreach (var step in steps)
            {
                sequence.Enqueue(step);
            }
        }

        public GitHubPrLookupResult Lookup(string repo, int prNumber)
        {
            if (sequence.Count == 0)
            {
                throw new InvalidOperationException("no more sequenced PR lookup results");
            }

            var next = sequence.Dequeue();
            if (next is Exception exception)
            {
                throw exception;
            }

            return (GitHubPrLookupResult)next;
        }
    }

    private sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        private readonly IReadOnlyList<GitHubAutomationPrCandidate> prs;
        private readonly Exception? listFailure;

        public FakeLister(
            IReadOnlyList<GitHubAutomationPrCandidate>? prs = null,
            Exception? listFailure = null)
        {
            this.prs = prs ?? Array.Empty<GitHubAutomationPrCandidate>();
            this.listFailure = listFailure;
        }

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels)
        {
            if (listFailure is not null)
            {
                throw listFailure;
            }

            return prs;
        }

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationIssueCandidate>();
    }

    private sealed class SequencedClaimVerifier
    {
        private readonly Queue<ClaimOwnershipVerification> sequence = new();

        public SequencedClaimVerifier(params ClaimOwnershipVerification[] steps)
        {
            foreach (var step in steps)
            {
                sequence.Enqueue(step);
            }
        }

        public ClaimOwnershipVerification Verify(string repoRoot, string scope, string? team, bool allowUnheld)
        {
            if (sequence.Count == 0)
            {
                throw new InvalidOperationException("no more sequenced claim verification results");
            }

            return sequence.Dequeue();
        }
    }

    private sealed class SequencedLister : IGitHubAutomationCandidateLister
    {
        private readonly Queue<object> sequence = new();

        public SequencedLister(
            IReadOnlyList<GitHubAutomationPrCandidate> first,
            IReadOnlyList<GitHubAutomationPrCandidate>? second = null,
            Exception? failure = null)
        {
            sequence.Enqueue(first);
            if (failure is not null)
            {
                sequence.Enqueue(failure);
            }
            else if (second is not null)
            {
                sequence.Enqueue(second);
            }
        }

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels)
        {
            if (sequence.Count == 0)
            {
                throw new InvalidOperationException("no more sequenced PR list results");
            }

            var next = sequence.Dequeue();
            if (next is Exception exception)
            {
                throw exception;
            }

            return (IReadOnlyList<GitHubAutomationPrCandidate>)next;
        }

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationIssueCandidate>();
    }

    private sealed class RecordingLabelMutator : IGitHubLabelMutator
    {
        private List<string> labels;

        public RecordingLabelMutator(params string[] labels) => this.labels = labels.ToList();

        public List<LabelTransition> Transitions { get; } = new();

        public void SetLabels(IEnumerable<string> values) => labels = values.ToList();

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();

        public void ApplyLabelTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels)
        {
            foreach (var remove in removeLabels)
            {
                labels.Remove(remove);
            }

            foreach (var add in addLabels)
            {
                if (!labels.Contains(add))
                {
                    labels.Add(add);
                }
            }

            Transitions.Add(new LabelTransition(kind, number, addLabels.ToArray(), removeLabels.ToArray()));
        }

        public void ApplyReconcileTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();
    }

    private sealed record LabelTransition(
        string Kind,
        int Number,
        IReadOnlyList<string> AddLabels,
        IReadOnlyList<string> RemoveLabels);
}
