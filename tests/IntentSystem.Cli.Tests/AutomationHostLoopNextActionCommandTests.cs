using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G319: command-level regression tests for the approved-PR
/// continuation lane. Verifies the end-to-end path — the command layer
/// detects an approved PR from GitHub labels, plumbs it into the
/// analyzer with the correct `is_draft` + merge-state + metadata flags,
/// and the analyzer surfaces it BEFORE the wip-cap-blocked stop.
/// </summary>
public sealed class AutomationHostLoopNextActionCommandTests : IDisposable
{
    public AutomationHostLoopNextActionCommandTests()
    {
        AutomationHostLoopNextActionCommand.CandidateListerFactory = null;
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = null;
    }

    public void Dispose()
    {
        AutomationHostLoopNextActionCommand.CandidateListerFactory = null;
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = null;
    }

    [Fact]
    public void Execute_ApprovedPrOpenAndNonDraft_SelectsApprovedContinuation_NotWipCapBlocked()
    {
        // Mirrors SKS PR #571: open, non-draft, carries
        // intent-target + intent-pr-approved; the host loop previously
        // stopped at wip-cap-blocked for the open intent-target.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: new[]
            {
                NewPr(571, isDraft: false, state: "OPEN", labels: new[] { "intent-target", "intent-pr-approved" },
                      closingIssue: 570)
            },
            issues: new[] { NewIssue(570, labels: new[] { "intent-target", "intent-pr-created" }) });

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("approved-pr-merge-closeout", root.GetProperty("classification").GetString());
        Assert.True(root.GetProperty("mutation_allowed").GetBoolean());
        Assert.Contains("--pr 571", root.GetProperty("recommended_command").GetString()!, StringComparison.Ordinal);
        var evidence = root.GetProperty("evidence").EnumerateArray()
            .Select(e => e.GetString()!).ToArray();
        Assert.Contains(evidence, e => e.Contains("#571", StringComparison.Ordinal));
        Assert.Contains(evidence, e => e.Contains("Closes #570", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_ApprovedPrIsDraft_MapsToApprovedPrDraftBlocked()
    {
        // G297: an approved PR that is currently a draft cannot be
        // merged; the analyzer must classify draft-blocked, not the
        // happy path or generic wip-cap-blocked.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: new[]
            {
                NewPr(571, isDraft: true, state: "OPEN", labels: new[] { "intent-target", "intent-pr-approved" })
            },
            issues: Array.Empty<GitHubAutomationIssueCandidate>());

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("approved-pr-draft-blocked", doc.RootElement.GetProperty("classification").GetString());
        Assert.False(doc.RootElement.GetProperty("mutation_allowed").GetBoolean());
    }

    [Fact]
    public void Execute_ApprovedPrMergeStateOverride_MapsToApprovedPrMergeBlocked()
    {
        // The command exposes `--approved-pr-merge-state <state>` so
        // host-loop guidance can pre-fetch `gh pr view --json
        // mergeStateStatus` once and pipe it in. CONFLICTING → blocked.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: new[]
            {
                NewPr(571, isDraft: false, state: "OPEN", labels: new[] { "intent-target", "intent-pr-approved" })
            },
            issues: Array.Empty<GitHubAutomationIssueCandidate>());

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService",
             "--approved-pr-merge-state", "CONFLICTING", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("approved-pr-merge-blocked", doc.RootElement.GetProperty("classification").GetString());
        Assert.False(doc.RootElement.GetProperty("mutation_allowed").GetBoolean());
        Assert.Contains("CONFLICTING", doc.RootElement.GetProperty("summary").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ApprovedPrMetadataBlockedFlag_MapsToApprovedPrMetadataBlocked()
    {
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: new[]
            {
                NewPr(571, isDraft: false, state: "OPEN", labels: new[] { "intent-target", "intent-pr-approved" })
            },
            issues: Array.Empty<GitHubAutomationIssueCandidate>());

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService",
             "--approved-pr-metadata-blocked", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("approved-pr-metadata-blocked", doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_OpenIntentTargetPrWithoutApproved_FallsThroughToWipCapBlocked()
    {
        // Acceptance: when there is open intent-target work but NO
        // approved continuation pending, the existing wip-cap-blocked
        // classification is unchanged.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: new[] { NewIssue(700, labels: new[] { "intent-target" }) });

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("wip-cap-blocked", doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_ApprovedPrWithUpdateInProgress_NotSelectedForContinuation()
    {
        // An approved PR that is also `intent-pr-update-in-progress`
        // means a child worker is repairing it. The continuation lane
        // skips it; the existing wip-cap / wait-for-child lanes handle
        // the in-flight state.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: new[]
            {
                NewPr(571, isDraft: false, state: "OPEN",
                    labels: new[] { "intent-target", "intent-pr-approved", "intent-pr-update-in-progress" })
            },
            issues: Array.Empty<GitHubAutomationIssueCandidate>());

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var classification = doc.RootElement.GetProperty("classification").GetString();
        Assert.NotEqual("approved-pr-merge-closeout", classification);
        // Open intent-target + lease held → wait-for-child.
        Assert.Equal("wait-for-child", classification);
    }

    // --- helpers --------------------------------------------------------------

    private static GitHubAutomationPrCandidate NewPr(
        int number,
        bool isDraft,
        string state,
        IReadOnlyList<string> labels,
        int? closingIssue = null)
    {
        var refs = closingIssue is int issueNumber
            ? new[]
            {
                new GitHubPrClosingIssueReference { Number = issueNumber }
            }
            : Array.Empty<GitHubPrClosingIssueReference>();
        return new GitHubAutomationPrCandidate
        {
            Number = number,
            Title = $"PR {number}",
            Url = $"https://github.com/J-Tech-Japan/SekibanAsAService/pull/{number}",
            Body = "stub",
            CreatedAt = "2026-05-10T00:00:00Z",
            UpdatedAt = "2026-05-10T00:00:00Z",
            Labels = labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
            ClosingIssuesReferences = refs,
            State = state,
            IsDraft = isDraft
        };
    }

    private static GitHubAutomationIssueCandidate NewIssue(int number, IReadOnlyList<string> labels) =>
        new()
        {
            Number = number,
            Title = $"issue {number}",
            Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{number}",
            CreatedAt = "2026-05-10T00:00:00Z",
            Labels = labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
            State = "OPEN"
        };

    // --- G318: automatic intent next-slice --dry-run probe ----------------

    [Fact]
    public void Execute_NextSliceProbeReturnsIssueCutReady_SelectsPublishNextIssue_NotTrueIdle()
    {
        // Mirrors SKS-G224: empty repo (no review PR, no open intent-target,
        // no host-metadata drift), `intent next-slice --dry-run` reports
        // `issue-cut-ready`. Before G318 the host loop reported true-idle;
        // the command now auto-probes next-slice and surfaces the publish
        // action.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "issue-cut-ready", ExecutionUnit = "SKS-G224" });

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService",
             "--domain", "sekiban-as-a-service", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("publish-next-issue", root.GetProperty("classification").GetString());
        Assert.True(root.GetProperty("mutation_allowed").GetBoolean());
        var recommended = root.GetProperty("recommended_command").GetString()!;
        Assert.Contains("--execution-unit SKS-G224", recommended, StringComparison.Ordinal);
        Assert.Contains("--target-repo J-Tech-Japan/SekibanAsAService", recommended, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NextSliceProbeReturnsNoActionableItem_FallsThroughToTrueIdle()
    {
        // G318 acceptance: when next-slice has no candidate AND no other
        // host signal exists, true-idle remains a valid outcome.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService",
             "--domain", "sekiban-as-a-service", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("true-idle", doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_NextSliceProbe_IsNotInvoked_WhenOperatorPrePipesFlag()
    {
        // G318 backward-compat: pre-piped `--next-slice-issue-cut-ready`
        // wins; the probe is not invoked even when --domain is present.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        var probeWasInvoked = false;
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "ignored", ExecutionUnit = "ignored" },
            onProbe: () => probeWasInvoked = true);

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService",
             "--domain", "sekiban-as-a-service",
             "--next-slice-issue-cut-ready",
             "--publish-next-execution-unit", "OPERATOR-PIPED",
             "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        Assert.False(probeWasInvoked, "operator-supplied next-slice flag must short-circuit the probe");
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("publish-next-issue", doc.RootElement.GetProperty("classification").GetString());
        Assert.Contains("--execution-unit OPERATOR-PIPED",
            doc.RootElement.GetProperty("recommended_command").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NextSliceProbeReturnsIssueCutReady_ButWipCapActive_BlocksPublish()
    {
        // G318 acceptance: even when next-slice is `issue-cut-ready`, an
        // open intent-target issue/PR must keep the WIP-cap-blocked
        // classification (publish-next-issue requires WIP cap empty).
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: new[] { NewIssue(900, labels: new[] { "intent-target" }) });
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "issue-cut-ready", ExecutionUnit = "SKS-G225" });

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService",
             "--domain", "sekiban-as-a-service", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        Assert.Equal("wip-cap-blocked",
            JsonDocument.Parse(writer.ToString()).RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_NextSliceProbeReturnsClarificationRequired_SurfacesHardClarification_NotTrueIdle()
    {
        // G318 review fix (PR #744): a blocked next-slice outcome must not
        // collapse into `true-idle`. `clarification-required` from
        // `intent next-slice --dry-run` maps to the analyzer's
        // `hard-clarification` lane so the operator gets a precise stop
        // and a clarification-next pointer instead of an idle wake.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "clarification-required", ExecutionUnit = null });

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService",
             "--domain", "sekiban-as-a-service", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("hard-clarification", doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_NextSliceProbeReturnsSkipDueToWip_SurfacesWipCapBlocked_NotTrueIdle()
    {
        // G318 review fix (PR #744): when queue-state authoritatively
        // reports `skip-next-slice-due-to-wip` (e.g. labels and queue
        // diverge), the command MUST surface `wip-cap-blocked` instead of
        // collapsing into `true-idle` because the GitHub label listing
        // happens to look empty.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "skip-next-slice-due-to-wip", ExecutionUnit = null });

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService",
             "--domain", "sekiban-as-a-service", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("wip-cap-blocked", doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_NextSliceProbe_IsNotInvoked_WhenDomainMissingAndConfigEmpty()
    {
        // G318 safety rail + G341 update: `intent next-slice --dry-run`
        // requires a domain. The probe is skipped only when neither
        // `--domain` is supplied nor the CliContext config carries a
        // configured domain. G341 added the config-domain fallback so
        // operators can run `automation host-loop-next-action --repo X`
        // without re-typing the domain every time.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        var probeWasInvoked = false;
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "issue-cut-ready", ExecutionUnit = "IGNORED" },
            onProbe: () => probeWasInvoked = true);

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContextWithoutDomain(),
            ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        Assert.False(probeWasInvoked, "probe must not run when neither --domain nor config domain is available");
        Assert.Equal("true-idle",
            JsonDocument.Parse(writer.ToString()).RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_NextSliceProbe_FallsBackToConfiguredDomain_WhenDomainFlagMissing()
    {
        // G341: when the operator omits `--domain`, the command falls
        // back to `context.Config.Project.Domain` so a real
        // `issue-cut-ready` next-slice candidate is surfaced as
        // `publish-next-issue` instead of `true-idle`. Mirrors the
        // SekibanAsAService SKS-G239 case in the G341 packet.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        var probeWasInvoked = false;
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "issue-cut-ready", ExecutionUnit = "SKS-G239" },
            onProbe: () => probeWasInvoked = true);

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(), // configured domain = "intent-cli"
            ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        Assert.True(probeWasInvoked, "probe must run when --domain is omitted but config carries a domain");
        var root = JsonDocument.Parse(writer.ToString()).RootElement;
        Assert.Equal("publish-next-issue", root.GetProperty("classification").GetString());
        Assert.Contains("SKS-G239", root.GetProperty("recommended_command").GetString(), StringComparison.Ordinal);
        Assert.True(root.GetProperty("mutation_allowed").GetBoolean());
        // The emitted domain field should carry the fallback so the
        // operator can see which domain drove the classification.
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
    }

    [Fact]
    public void Execute_PublishRecoveryProbe_SurfacesRepairHostMetadata_WhenSafeRepairsAvailable()
    {
        // G342: when `automation publish-recovery --dry-run` reports
        // safe_repairs > 0 (the deterministic `linked_pr` recovery
        // case), the host loop must surface `repair-host-metadata`
        // instead of falling through to `true-idle`.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = _ => new FakePublishRecoveryProbe(
            new PublishRecoveryProbeResult { SafeRepairCount = 1, UnsafeStopCount = 0 });
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = _ => new FakeHostSyncPreflightProbe(
            new HostSyncPreflightProbeResult { Classification = HostSyncPreflightAnalyzer.ClassificationClean });

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                CreateContext(),
                ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            var root = JsonDocument.Parse(writer.ToString()).RootElement;
            Assert.Equal("repair-host-metadata", root.GetProperty("classification").GetString());
            Assert.True(root.GetProperty("mutation_allowed").GetBoolean());
            Assert.Contains("publish-recovery", root.GetProperty("recommended_command").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
            AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_HostSyncPreflightProbe_SurfacesSafeStash_WhenDirtyUnrelatedSubmodule()
    {
        // G342: when host-sync-preflight reports
        // `dirty-unrelated-submodule` (the recoverable workspace
        // case), the host loop must surface `safe-stash` with the
        // `workspace-guard --mode begin --write` recommendation
        // instead of falling through to `true-idle`.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = _ => new FakePublishRecoveryProbe(
            new PublishRecoveryProbeResult { SafeRepairCount = 0, UnsafeStopCount = 0 });
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = _ => new FakeHostSyncPreflightProbe(
            new HostSyncPreflightProbeResult { Classification = HostSyncPreflightAnalyzer.ClassificationDirtyUnrelatedSubmodule });

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                CreateContext(),
                ["--repo", "owner/repo", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            var root = JsonDocument.Parse(writer.ToString()).RootElement;
            Assert.Equal("safe-stash", root.GetProperty("classification").GetString());
            Assert.True(root.GetProperty("mutation_allowed").GetBoolean());
            Assert.Contains("workspace-guard", root.GetProperty("recommended_command").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
            AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_HostSyncPreflightProbe_SurfacesDirtyHostState_WhenDurableStateDirty()
    {
        // G342: when host-sync-preflight reports
        // `dirty-host-durable-state` (NOT recoverable — operator
        // must commit/revert), the host loop must surface
        // `dirty-host-state` with `mutation_allowed: false` and a
        // structured stop. This is the unambiguous-block lane that
        // protects against publishing on top of dirty durable state.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = _ => new FakePublishRecoveryProbe(
            new PublishRecoveryProbeResult { SafeRepairCount = 0, UnsafeStopCount = 0 });
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = _ => new FakeHostSyncPreflightProbe(
            new HostSyncPreflightProbeResult { Classification = HostSyncPreflightAnalyzer.ClassificationDirtyDurableState });

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                CreateContext(),
                ["--repo", "owner/repo", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            var root = JsonDocument.Parse(writer.ToString()).RootElement;
            Assert.Equal("dirty-host-state", root.GetProperty("classification").GetString());
            Assert.False(root.GetProperty("mutation_allowed").GetBoolean());
        }
        finally
        {
            AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
            AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_TrueIdle_RemainsPossible_WhenAllSafeRepairProbesIdle()
    {
        // G342 acceptance: `true-idle` is still emitted when every
        // safe-repair probe (next-slice, publish-recovery,
        // host-sync-preflight) reports no actionable signal.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = _ => new FakePublishRecoveryProbe(
            new PublishRecoveryProbeResult { SafeRepairCount = 0, UnsafeStopCount = 0 });
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = _ => new FakeHostSyncPreflightProbe(
            new HostSyncPreflightProbeResult { Classification = HostSyncPreflightAnalyzer.ClassificationClean });

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                CreateContext(),
                ["--repo", "owner/repo", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            Assert.Equal("true-idle",
                JsonDocument.Parse(writer.ToString()).RootElement.GetProperty("classification").GetString());
        }
        finally
        {
            AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
            AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_OperatorSupplied_PublishRecoveryRepairs_BypassesProbe()
    {
        // G342: when the operator pre-supplies
        // `--publish-recovery-repairs <N>`, the probe is skipped so
        // upstream tooling that already computed the count routes
        // through the operator value.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        var probeInvoked = false;
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = _ => new FakePublishRecoveryProbe(
            canned: null) { };
        // Use a probe that records invocation so we can assert it was bypassed.
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = _ =>
        {
            probeInvoked = true;
            return new FakePublishRecoveryProbe(new PublishRecoveryProbeResult { SafeRepairCount = 99, UnsafeStopCount = 0 });
        };

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                CreateContext(),
                ["--repo", "owner/repo", "--publish-recovery-repairs", "1", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            Assert.False(probeInvoked, "publish-recovery probe must not run when --publish-recovery-repairs is operator-supplied");
            // The operator-supplied value (1) wins, surfacing
            // repair-host-metadata regardless of probe.
            Assert.Equal("repair-host-metadata",
                JsonDocument.Parse(writer.ToString()).RootElement.GetProperty("classification").GetString());
        }
        finally
        {
            AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
        }
    }

    private static CliContext CreateContextWithoutDomain() =>
        new()
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = string.Empty,
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };

    /// <summary>
    /// G342: deterministic stand-in for the publish-recovery probe.
    /// Returns the canned safe-repair / unsafe-stop counts the host
    /// loop tests need to drive the analyzer's `repair-host-metadata`
    /// lane without touching live queue-state.
    /// </summary>
    private sealed class FakePublishRecoveryProbe : IPublishRecoveryProbe
    {
        private readonly PublishRecoveryProbeResult? _canned;
        public FakePublishRecoveryProbe(PublishRecoveryProbeResult? canned) { _canned = canned; }
        public PublishRecoveryProbeResult? Probe(string repo) => _canned;
    }

    /// <summary>
    /// G342: deterministic stand-in for the host-sync-preflight probe.
    /// Tests drive `safe-stash` / `dirty-host-state` lanes by canning
    /// classifications without touching the local git working tree.
    /// </summary>
    private sealed class FakeHostSyncPreflightProbe : IHostSyncPreflightProbe
    {
        private readonly HostSyncPreflightProbeResult? _canned;
        public FakeHostSyncPreflightProbe(HostSyncPreflightProbeResult? canned) { _canned = canned; }
        public HostSyncPreflightProbeResult? Probe() => _canned;
    }

    private sealed class FakeNextSliceProbe : INextSliceDryRunProbe
    {
        private readonly NextSliceProbeResult? _canned;
        private readonly Action? _onProbe;

        public FakeNextSliceProbe(NextSliceProbeResult? canned, Action? onProbe = null)
        {
            _canned = canned;
            _onProbe = onProbe;
        }

        public NextSliceProbeResult? Probe(string repo, string domain)
        {
            _onProbe?.Invoke();
            return _canned;
        }
    }

    private sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        private readonly IReadOnlyList<GitHubAutomationPrCandidate> prs;
        private readonly IReadOnlyList<GitHubAutomationIssueCandidate> issues;

        public FakeLister(
            IReadOnlyList<GitHubAutomationPrCandidate> prs,
            IReadOnlyList<GitHubAutomationIssueCandidate> issues)
        {
            this.prs = prs;
            this.issues = issues;
        }

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => prs;
        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) => issues;
    }

    private static CliContext CreateContext() =>
        new()
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };
}
