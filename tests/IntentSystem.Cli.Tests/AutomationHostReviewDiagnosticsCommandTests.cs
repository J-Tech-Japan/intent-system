using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

// G358: serialise with AutomationCloseoutDriftCheckCommandTests so that
// CandidateListerFactory resets in this class's ctor/Dispose cannot race
// with the FakeEmptyLister assignment in
// DiagnosticsCommand_WithCloseoutDriftRepairsAvailableFlag_ClassifiesCloseoutDriftRepair.
[Collection("HostReviewDiagnostics")]
public sealed class AutomationHostReviewDiagnosticsCommandTests : IDisposable
{
    public AutomationHostReviewDiagnosticsCommandTests()
    {
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = null;
        AutomationHostReviewDiagnosticsCommand.NestedProviderLauncher = null;
    }

    public void Dispose()
    {
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = null;
        AutomationHostReviewDiagnosticsCommand.NestedProviderLauncher = null;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null;
    }

    [Fact]
    public void Execute_NoPrsNoIssues_ClassifiesTrueIdle()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("true-idle", result.Classification);
        Assert.True(result.ReadOnly);
        Assert.Null(result.RecommendedNextCommand);
    }

    [Fact]
    public void Execute_StuckIntentPrReviewingWithoutExitTransition_ClassifiesStuckReviewingAndRecommendsTransition()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(490, "stuck reviewing", "https://github.com/J-Tech-Japan/intent-system/pull/490",
                    body: "Closes #559", labels: ["intent-target", "intent-pr-reviewing"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("stuck-reviewing", result.Classification);
        Assert.NotNull(result.RecommendedNextCommand);
        Assert.Contains("pr-transition", result.RecommendedNextCommand!, StringComparison.Ordinal);
        Assert.Contains(result.Details, d => d.TargetNumber == 490);
    }

    [Fact]
    public void Execute_PrLinksPublishedIssueWithoutIntentTarget_ClassifiesMissingTargetOnPr()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(420, "missing target", "https://github.com/J-Tech-Japan/intent-system/pull/420",
                    body: "Closes #559", labels: Array.Empty<string>()),
            ],
            PublishedIssues =
            [
                BuildIssue(559, "G227", "https://github.com/J-Tech-Japan/intent-system/issues/559",
                    labels: ["intent-target"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("missing-target-on-pr", result.Classification);
        Assert.Contains("automation reconcile", result.RecommendedNextCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_PrCarriesBothRequestUpdateAndRereviewReady_ClassifiesConflictWithStructuredClarification()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(500, "conflict", "https://github.com/J-Tech-Japan/intent-system/pull/500",
                    body: "Closes #560", labels: ["intent-target", "intent-pr-request-update", "intent-pr-rereview-ready"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("request-update-rereview-conflict", result.Classification);
        Assert.NotNull(result.StructuredClarification);
        Assert.Equal(2, result.StructuredClarification!.Options.Count);
    }

    [Fact]
    public void Execute_OpenIntentTargetIssueButNoActionablePr_ClassifiesWipCapBlocked()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            PublishedIssues =
            [
                BuildIssue(700, "G300 in flight", "https://github.com/J-Tech-Japan/intent-system/issues/700",
                    labels: ["intent-target", "intent-issue-in-progress"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("wip-cap-blocked", result.Classification);
    }

    // ─── G289 tests ────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_G289_ClosedIntentTargetIssue_NotCountedAsWipBlocker()
    {
        // SekibanAsAService PR #498 / SKS-G189 closeout regression: a closed
        // GitHub issue that still carries `intent-target` label must not flip
        // a publish-ready candidate to wip-cap-blocked.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            PublishedIssues =
            [
                BuildIssue(497, "SKS-G189 (closed)", "https://github.com/J-Tech-Japan/intent-system/issues/497",
                    labels: ["intent-target", "intent-pr-created"], state: "CLOSED"),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--candidate", "SKS-G190", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.NotEqual("wip-cap-blocked", result.Classification);
        Assert.Equal("issue-publish-ready", result.Classification);
    }

    [Fact]
    public void Execute_G289_OpenIntentTargetIssue_StillBlocksAsWip()
    {
        // Regression guard: an actually-open `intent-target` issue still
        // produces wip-cap-blocked. The G289 filter only excludes closed
        // items.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            PublishedIssues =
            [
                BuildIssue(700, "G300 in flight (open)", "https://github.com/J-Tech-Japan/intent-system/issues/700",
                    labels: ["intent-target", "intent-issue-in-progress"], state: "OPEN"),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--candidate", "G301", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("wip-cap-blocked", result.Classification);
    }

    [Fact]
    public void Execute_G289_EmptyState_StillTreatedAsOpen_ForBackwardCompat()
    {
        // Backward compat: legacy callers that don't populate `state` (e.g.
        // tests written before G289) must continue to behave as if the issue
        // is open. Empty/missing state is NOT a free pass to ignore the
        // candidate — the existing wip-cap-blocked behavior is preserved.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            PublishedIssues =
            [
                BuildIssue(700, "G300 in flight (legacy)", "https://github.com/J-Tech-Japan/intent-system/issues/700",
                    labels: ["intent-target", "intent-issue-in-progress"], state: ""),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--candidate", "G301", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("wip-cap-blocked", result.Classification);
    }

    [Fact]
    public void Execute_G289_MergedPrAlsoExcludedFromWip()
    {
        // Defensive: a PR with state=MERGED (closed-after-merge) carrying
        // `intent-target` is no longer in flight either.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(498, "SKS-G189 merged", "https://github.com/J-Tech-Japan/intent-system/pull/498",
                    body: "Closes #497", labels: ["intent-target", "intent-pr-approved"], state: "MERGED"),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--candidate", "SKS-G190", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("issue-publish-ready", result.Classification);
    }

    // ─── G288 tests ────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_G288_WipBlockedDefault_StillReturnsWipCapBlocked()
    {
        // Regression: without --allow-wip-cap-override, default WIP cap
        // behavior is unchanged even when a candidate is provided.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            PublishedIssues =
            [
                BuildIssue(700, "G300 in flight", "https://github.com/J-Tech-Japan/intent-system/issues/700",
                    labels: ["intent-target", "intent-issue-in-progress"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--candidate", "G301", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("wip-cap-blocked", result.Classification);
        Assert.DoesNotContain("wip-cap-overridden", result.Warnings);
    }

    [Fact]
    public void Execute_G288_AllowWipCapOverrideWithCandidate_ClassifiesIssuePublishReadyAndWarns()
    {
        // With --allow-wip-cap-override AND a candidate, the wake routes to
        // issue-publish-ready and surfaces wip-cap-overridden in warnings so
        // the override is auditable. The publish chain is still emitted.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            PublishedIssues =
            [
                BuildIssue(700, "G300 in flight", "https://github.com/J-Tech-Japan/intent-system/issues/700",
                    labels: ["intent-target", "intent-issue-in-progress"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--candidate", "G301", "--allow-wip-cap-override", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("issue-publish-ready", result.Classification);
        Assert.Contains("wip-cap-overridden", result.Warnings);
        Assert.Contains("G301", result.RecommendedNextCommand!, StringComparison.Ordinal);
        Assert.Contains("packet draft", result.RecommendedNextCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G288_AllowWipCapOverrideWithoutCandidate_StillBlocked()
    {
        // The override only triggers a publish when a candidate is provided.
        // With WIP > 0 and no candidate, the wake remains wip-cap-blocked.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            PublishedIssues =
            [
                BuildIssue(700, "G300 in flight", "https://github.com/J-Tech-Japan/intent-system/issues/700",
                    labels: ["intent-target", "intent-issue-in-progress"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--allow-wip-cap-override", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("wip-cap-blocked", result.Classification);
        Assert.DoesNotContain("wip-cap-overridden", result.Warnings);
    }

    [Fact]
    public void Execute_G288_AllowWipCapOverrideWithClarificationRequired_StillStops()
    {
        // The override only bypasses WIP cap; clarification is unaffected.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            PublishedIssues =
            [
                BuildIssue(700, "G300 in flight", "https://github.com/J-Tech-Japan/intent-system/issues/700",
                    labels: ["intent-target", "intent-issue-in-progress"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--candidate", "G301", "--clarification-required", "--allow-wip-cap-override", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("clarification-required", result.Classification);
    }

    [Fact]
    public void Execute_ClarificationRequiredFlag_ClassifiesClarificationRequired()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--clarification-required", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("clarification-required", result.Classification);
    }

    [Fact]
    public void Execute_StaleHostCli_ClassifiesStaleHostCliWithoutCallingLister()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        workspace.WriteInstalledCliScript(stalePrTransition: true);
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new ThrowingLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("stale-host-cli", result.Classification);
        Assert.Contains("automation doctor", result.RecommendedNextCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ActionableReviewPrPresent_ClassifiesReviewPrActionable()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(800, "ready review", "https://github.com/J-Tech-Japan/intent-system/pull/800",
                    body: "Closes #100", labels: ["intent-target"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("review-pr-actionable", result.Classification);
    }

    [Fact]
    public void Execute_CandidateProvidedWithoutWipOrPr_ClassifiesIssuePublishReady_G286()
    {
        // G286: candidate-ready was renamed to issue-publish-ready, and the
        // recommended_next_command is now the deterministic publish chain
        // (`packet draft` → `issue publish-flow --write` → `automation
        // issue-publish --write`) so the host loop can converge without an
        // extra acceptance prompt.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--candidate", "G99", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("issue-publish-ready", result.Classification);
        Assert.Contains("G99", result.RecommendedNextCommand!, StringComparison.Ordinal);
        Assert.Contains("packet draft", result.RecommendedNextCommand!, StringComparison.Ordinal);
        Assert.Contains("issue publish-flow", result.RecommendedNextCommand!, StringComparison.Ordinal);
        Assert.Contains("automation issue-publish", result.RecommendedNextCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_StaleClarificationMetadata_SurfacesAsWarningWithoutFlippingClass_G286()
    {
        // G286: stale clarification metadata surfaces in `warnings` without
        // flipping the terminal class. issue-publish-ready remains.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--candidate", "G99", "--stale-clarification-metadata", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("issue-publish-ready", result.Classification);
        Assert.Contains("stale-clarification-metadata", result.Warnings);
    }

    [Fact]
    public void Execute_ReconcileUnsafeStop_ClassifiesUnsafeMetadata_G286()
    {
        // G286: an unsafe stop kind (e.g. ambiguous-queue-linkage) trumps every
        // candidate / WIP / true-idle classification — the host loop must stop
        // with structured clarification.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--candidate", "G99", "--reconcile-unsafe-stop", "ambiguous-queue-linkage", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("unsafe-metadata", result.Classification);
        Assert.Contains("ambiguous-queue-linkage", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ReconcileHighConfidenceRepairsAvailable_NoCandidate_ClassifiesRepairedAndRetry_G286()
    {
        // G286: when reconcile has unapplied high-confidence repairs and no
        // other terminal class fits, surface `repaired-and-retry` so the host
        // loop knows to apply the safe repair and retry the wake rather than
        // reporting a misleading `true-idle`.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--reconcile-repairs-available", "2", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("repaired-and-retry", result.Classification);
        Assert.Contains("reconcile", result.RecommendedNextCommand!, StringComparison.Ordinal);
        Assert.Contains("--write", result.RecommendedNextCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_PublishRecoveryRepairsAvailable_ClassifiesPublishRecoveryReady_G313()
    {
        // G313: when publish-recovery has unapplied high-confidence repairs
        // and no other terminal class fits, surface `publish-recovery-ready`
        // so the host loop runs publish-recovery (not generic reconcile)
        // first for missing-linked_pr blockers backed by publish artifacts.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--publish-recovery-repairs-available", "1", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("publish-recovery-ready", result.Classification);
        Assert.Contains("publish-recovery", result.RecommendedNextCommand!, StringComparison.Ordinal);
        Assert.Contains("--write", result.RecommendedNextCommand!, StringComparison.Ordinal);
        // The summary should mention publish.yaml so the operator can tell
        // this lane apart from generic reconcile.
        Assert.Contains("publish.yaml", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_PublishRecoveryAndReconcileBothAvailable_PrefersPublishRecoveryReady_G313()
    {
        // G313: when BOTH publish-recovery and reconcile have unapplied
        // high-confidence repairs, publish-recovery wins because its
        // evidence is stronger (publish-artifact-backed, single-issue
        // single-queue-item convergence).
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system",
             "--publish-recovery-repairs-available", "1",
             "--reconcile-repairs-available", "2",
             "--format", "json"],
            writer);

        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("publish-recovery-ready", result.Classification);
    }

    [Fact]
    public void Execute_ReconcileRepairsAvailableButCandidatePresent_PrefersIssuePublishReady_G286()
    {
        // G286: when both a publish-ready candidate AND unapplied repairs are
        // present, the candidate-publish path wins — repairs surface as
        // advisory follow-up. The host loop should publish first.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--candidate", "G99", "--reconcile-repairs-available", "1", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("issue-publish-ready", result.Classification);
    }

    [Fact]
    public void Execute_NeverWritesAnyFile()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        workspace.WriteSentinel();
        var snapshotBefore = workspace.SnapshotFiles();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(490, "stuck", "https://github.com/J-Tech-Japan/intent-system/pull/490",
                    body: "Closes #559", labels: ["intent-target", "intent-pr-reviewing"]),
            ],
        };

        using var writer = new StringWriter();
        AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        var snapshotAfter = workspace.SnapshotFiles();
        Assert.Equal(snapshotBefore, snapshotAfter);
    }

    [Fact]
    public void CommandRouter_RegistersAutomationHostReviewDiagnostics()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["automation", "host-review-diagnostics", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            workspace.Context,
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.True(result.ReadOnly);
    }

    [Fact]
    public void CommandRouter_HelpListsAutomationHostReviewDiagnostics()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute([], workspace.Context, writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("automation host-review-diagnostics", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GuidePromptMatrix_HostLoopMentionsHostReviewDiagnostics()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            workspace.Context,
            ["--mode", "host-loop", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("automation host-review-diagnostics", output, StringComparison.Ordinal);
        Assert.Contains("Stage 4", output, StringComparison.Ordinal);
    }

    // ── G297 draft-merge-blocked ────────────────────────────────────────

    [Fact]
    public void Execute_PrDraftTrue_OnIntentTargetPr_ClassifiesDraftMergeBlocked_AndRecommendsReviewRelease()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(523, "draft pr", "https://github.com/owner/repo/pull/523",
                    "Closes #515", new[] { "intent-target" })
            ],
            PublishedIssues =
            [
                BuildIssue(515, "issue", "https://github.com/owner/repo/issues/515",
                    new[] { "intent-target" })
            ]
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "owner/repo", "--pr-draft", "true", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("draft-merge-blocked", result.Classification);
        Assert.Contains("523", result.Summary, StringComparison.Ordinal);
        Assert.NotNull(result.RecommendedNextCommand);
        Assert.Contains("review-release", result.RecommendedNextCommand!, StringComparison.Ordinal);
        Assert.Contains("--pr 523", result.RecommendedNextCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_PrDraftFalse_DoesNotClassifyDraftMergeBlocked()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(523, "ready pr", "https://github.com/owner/repo/pull/523",
                    "Closes #515", new[] { "intent-target" })
            ],
            PublishedIssues =
            [
                BuildIssue(515, "issue", "https://github.com/owner/repo/issues/515",
                    new[] { "intent-target" })
            ]
        };

        using var writer = new StringWriter();
        AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "owner/repo", "--pr-draft", "false", "--format", "json"],
            writer);

        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.NotEqual("draft-merge-blocked", result.Classification);
        Assert.Equal("review-pr-actionable", result.Classification);
    }

    [Fact]
    public void Execute_RejectsInvalidPrDraftValue()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();
        using var writer = new StringWriter();

        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "owner/repo", "--pr-draft", "yes"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--pr-draft must be 'true' or 'false'", writer.ToString(), StringComparison.Ordinal);
    }

    // ── G376 draft-aware review decision ────────────────────────────────

    [Fact]
    public void Execute_PrDraftTrue_DraftReviewReady_ClassifiesDraftReadyToPromote_AndRecommendsMarkReady()
    {
        // Zero4Racer PR #203 shape: draft PR that is otherwise review-ready
        // (closeout ready, guide ready, base main, diff passed, no findings)
        // and not operator-intended. The host loop verifies readiness and
        // passes --draft-review-ready; the diagnostic must NOT release the
        // lease — it should recommend marking the PR ready and continuing.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(203, "draft but ready", "https://github.com/owner/repo/pull/203",
                    "Closes #199", new[] { "intent-target" })
            ],
            PublishedIssues =
            [
                BuildIssue(199, "issue", "https://github.com/owner/repo/issues/199",
                    new[] { "intent-target" })
            ]
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "owner/repo", "--pr-draft", "true", "--draft-review-ready", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("draft-ready-to-promote", result.Classification);
        Assert.NotNull(result.RecommendedNextCommand);
        Assert.Contains("gh pr ready 203", result.RecommendedNextCommand!, StringComparison.Ordinal);
        Assert.DoesNotContain("review-release", result.RecommendedNextCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_PrDraftTrue_FindingsPresent_ClassifiesDraftRequestUpdate()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(203, "draft with findings", "https://github.com/owner/repo/pull/203",
                    "Closes #199", new[] { "intent-target" })
            ],
            PublishedIssues =
            [
                BuildIssue(199, "issue", "https://github.com/owner/repo/issues/199",
                    new[] { "intent-target" })
            ]
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "owner/repo", "--pr-draft", "true", "--draft-findings-present", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("draft-request-update", result.Classification);
        Assert.NotNull(result.RecommendedNextCommand);
        Assert.Contains("request-update", result.RecommendedNextCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_PrDraftTrue_OperatorIntended_StaysDraftMergeBlocked_EvenWhenReviewReady()
    {
        // Fail-closed: an explicitly operator-intended draft is never
        // auto-promoted, even when review readiness is also asserted.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(203, "operator-held draft", "https://github.com/owner/repo/pull/203",
                    "Closes #199", new[] { "intent-target" })
            ],
            PublishedIssues =
            [
                BuildIssue(199, "issue", "https://github.com/owner/repo/issues/199",
                    new[] { "intent-target" })
            ]
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "owner/repo", "--pr-draft", "true", "--draft-review-ready", "--operator-intended-draft", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("draft-merge-blocked", result.Classification);
        Assert.NotNull(result.RecommendedNextCommand);
        Assert.Contains("review-release", result.RecommendedNextCommand!, StringComparison.Ordinal);
        Assert.Contains("operator-intended", result.Summary, StringComparison.Ordinal);
    }

    // ── G383 visible-verification AC decision lane ──────────────────────

    [Fact]
    public void Execute_ReviewVerificationAc_StandingPolicyWithEvidence_ClassifiesActionableProceed()
    {
        // No lister needed: the verification-policy lane is an isolated
        // early-return that never lists candidates.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--review-verification-ac", "--repo", "owner/repo", "--pr", "259",
             "--standing-policy", "--evidence", "source-mapping", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("review-pr-actionable", result.Classification);
        Assert.Contains("what was NOT run", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ReviewVerificationAc_ImplementationActionable_ClassifiesImplementationFinding_RoutesRequestUpdate()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--review-verification-ac", "--repo", "owner/repo", "--pr", "259",
             "--evidence", "none", "--implementation-actionable", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("implementation-finding", result.Classification);
        Assert.NotNull(result.RecommendedNextCommand);
        Assert.Contains("request-update", result.RecommendedNextCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ReviewVerificationAc_NoNormNotActionable_ClassifiesReviewPolicyGap_RecordsOnce()
    {
        // The Zero4Racer #259 shape: only a visible-verification policy is
        // unresolved, no encoded norm, not implementation-actionable → record
        // a host-owned policy gap once instead of re-asking the operator.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--review-verification-ac", "--repo", "owner/repo", "--pr", "259",
             "--evidence", "none", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("review-policy-gap", result.Classification);
        // Routed to a durable host clarification recorded once (not the child PR).
        Assert.NotNull(result.RecommendedNextCommand);
        Assert.Contains("clarify open", result.RecommendedNextCommand!, StringComparison.Ordinal);
        Assert.Contains(result.Details, d => d.Description.Contains("record_host_gap_once=true", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_ReviewVerificationAc_UnknownEvidence_RejectsInsteadOfSilentReviewPolicyGap()
    {
        // G383 (review follow-up): the diagnostics lane must also reject an
        // unknown/misspelled evidence token rather than silently classifying
        // it as review-policy-gap.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--review-verification-ac", "--repo", "owner/repo", "--pr", "259", "--evidence", "typo", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("--evidence must be one of", output, StringComparison.Ordinal);
        Assert.DoesNotContain("review-policy-gap", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ReviewVerificationAc_FalseRuntimeClaim_NeverProceedsToApprove()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--review-verification-ac", "--repo", "owner/repo", "--pr", "259",
             "--standing-policy", "--evidence", "source-mapping", "--false-runtime-claim", "--implementation-actionable", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        // Never the proceed verdict when a false runtime claim would be required.
        Assert.NotEqual("review-pr-actionable", result.Classification);
        Assert.Equal("implementation-finding", result.Classification);
    }

    // ── G384 redundant in-submodule-edit decision lane ─────────────────

    [Fact]
    public void Execute_InSubmoduleEdit_RedundantPrHeadMatch_ClassifiesRedundant_WithSafeRepair()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--in-submodule-edit", "--path", "submodules/X/.github/workflows/ci.yml",
             "--local-fingerprint", "abc123", "--pr-head-fingerprint", "abc123", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("redundant-in-submodule-edit", result.Classification);
        Assert.True(result.SafeRepairAvailable);
        Assert.NotNull(result.RecommendedNextCommand);
    }

    [Fact]
    public void Execute_InSubmoduleEdit_UniqueLocalContent_ClassifiesProtectedOperatorWork()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--in-submodule-edit", "--path", "submodules/X/f.cs",
             "--local-fingerprint", "abc", "--pr-head-fingerprint", "abc", "--unique-local-content", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("protected-operator-work", result.Classification);
        Assert.False(result.SafeRepairAvailable);
    }

    [Fact]
    public void Execute_InSubmoduleEdit_RedundantButRequiredCiFailing_SurfacesCiBlocker()
    {
        // Required CI failing must stay the visible implementation blocker
        // even when the local submodule edit is redundant-safe.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--in-submodule-edit", "--path", "submodules/X/ci.yml",
             "--local-fingerprint", "abc", "--pr-head-fingerprint", "abc",
             "--required-ci-failing", "--repo", "owner/repo", "--pr", "1090", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("required-ci-failing", result.Classification);
        Assert.NotNull(result.RecommendedNextCommand);
        Assert.Contains("request-update", result.RecommendedNextCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_InSubmoduleEdit_SameFingerprintAcrossWakes_DeduplicatesReport()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--in-submodule-edit", "--path", "submodules/X/ci.yml",
             "--local-fingerprint", "abc", "--pr-head-fingerprint", "abc", "--pr", "1090",
             "--prior-fingerprint", "submodules/X/ci.yml|local=abc|pr=1090|head=abc", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Contains("deduplicated-unchanged-report", result.Warnings);
        Assert.Contains("deduplicated", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_NextSliceProbe_AutoPopulatesCandidate_WhenDomainFlagOrConfigPresent()
    {
        // G341: when no `--candidate` is supplied, the command must
        // auto-probe `intent next-slice --dry-run`. A returned
        // `issue-cut-ready` outcome flips the classification from
        // `true-idle` to `issue-publish-ready`, mirroring the
        // SekibanAsAService SKS-G239 case in the G341 packet.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();
        var probeWasInvoked = false;
        AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "issue-cut-ready", ExecutionUnit = "SKS-G239" },
            onProbe: () => probeWasInvoked = true);

        try
        {
            using var writer = new StringWriter();
            var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
                workspace.Context, // configured domain = "intent-cli"
                ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.True(probeWasInvoked, "probe must run when --candidate omitted and config carries a domain");
            var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
            Assert.Equal("issue-publish-ready", result.Classification);
            Assert.NotNull(result.RecommendedNextCommand);
            Assert.Contains("SKS-G239", result.RecommendedNextCommand!, StringComparison.Ordinal);
            Assert.Contains("packet draft", result.RecommendedNextCommand!, StringComparison.Ordinal);
            Assert.Contains("issue publish-flow", result.RecommendedNextCommand!, StringComparison.Ordinal);
            Assert.Contains("automation issue-publish", result.RecommendedNextCommand!, StringComparison.Ordinal);
        }
        finally
        {
            AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_NextSliceProbe_DomainFlag_OverridesConfiguredDomain()
    {
        // G341: `--domain` wins over the configured domain so an
        // operator can probe a different domain on the same host.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();
        string? probedDomain = null;
        AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null },
            onProbeArgs: (_, d) => probedDomain = d);

        try
        {
            using var writer = new StringWriter();
            var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
                workspace.Context, // configured domain = "intent-cli"
                ["--repo", "owner/repo", "--domain", "sekiban-as-a-service", "--format", "json"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.Equal("sekiban-as-a-service", probedDomain);
        }
        finally
        {
            AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_NextSliceProbe_OperatorCandidateOverridesProbeResult()
    {
        // G341: when the operator pre-supplies `--candidate`, the
        // probe is skipped — the operator-supplied value wins so
        // upstream tooling that already computed the candidate can
        // route through `--candidate` and bypass the probe.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();
        var probeWasInvoked = false;
        AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "issue-cut-ready", ExecutionUnit = "OTHER" },
            onProbe: () => probeWasInvoked = true);

        try
        {
            using var writer = new StringWriter();
            var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
                workspace.Context,
                ["--repo", "owner/repo", "--candidate", "OPERATOR-G123", "--format", "json"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.False(probeWasInvoked, "probe must not run when --candidate is operator-supplied");
            var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
            Assert.Equal("issue-publish-ready", result.Classification);
            Assert.Contains("OPERATOR-G123", result.RecommendedNextCommand!, StringComparison.Ordinal);
        }
        finally
        {
            AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_TrueIdle_RemainsPossible_WhenNextSliceAlsoIdle()
    {
        // G341 acceptance: `true-idle` is still emitted when no
        // review PR, no WIP, no clarification, AND next-slice probe
        // reports `no-actionable-item`. Guards against an over-
        // aggressive fallback that would never report true idle.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();
        AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });

        try
        {
            using var writer = new StringWriter();
            var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
                workspace.Context,
                ["--repo", "owner/repo", "--format", "json"],
                writer);

            Assert.Equal(0, exitCode);
            var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
            Assert.Equal("true-idle", result.Classification);
        }
        finally
        {
            AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_G364_NextSliceProbe_ClarificationRequired_RoutesToClarificationLane()
    {
        // G364 AC: when `intent next-slice --dry-run` reports
        // `clarification-required`, the diagnostics must surface
        // `clarification-required` instead of falling through to
        // `true-idle`. This mirrors the host-loop-next-action probe
        // (which already maps the same outcome to hardClarificationOpen)
        // so both surfaces agree on the same next-slice signal. The
        // observed SekibanAsAService SKS-G403 case showed that without
        // this mapping the host loop sees `true-idle` on the diagnostics
        // surface while the explicit `intent next-slice` command reports
        // a real blocker — the two must not disagree.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();
        AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "clarification-required", ExecutionUnit = null });

        try
        {
            using var writer = new StringWriter();
            var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
                workspace.Context,
                ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
                writer);

            Assert.Equal(0, exitCode);
            var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
            Assert.Equal("clarification-required", result.Classification);
            Assert.NotEqual("true-idle", result.Classification);
        }
        finally
        {
            AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_G364_NextSliceProbe_IssueCutReady_AndExplicitCandidate_Agree()
    {
        // G364 AC: diagnostics with `--candidate SKS-G403` and diagnostics
        // without `--candidate` (where the auto-probe returns
        // `issue-cut-ready` for SKS-G403) must agree on classification.
        // Captures the observed SKS-G403 regression shape: explicit
        // candidate path returns `issue-publish-ready`; the implicit
        // path must do the same.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();

        try
        {
            // 1. Implicit candidate path: probe returns issue-cut-ready
            AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
                new NextSliceProbeResult { RecommendedOutcome = "issue-cut-ready", ExecutionUnit = "SKS-G403" });
            using var implicitWriter = new StringWriter();
            var implicitExit = AutomationHostReviewDiagnosticsCommand.Execute(
                workspace.Context,
                ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
                implicitWriter);
            Assert.Equal(0, implicitExit);
            var implicitResult = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(implicitWriter.ToString())!;

            // 2. Explicit candidate path: operator passes --candidate
            // SKS-G403 directly (probe is skipped by design)
            AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
                new NextSliceProbeResult { RecommendedOutcome = "issue-cut-ready", ExecutionUnit = "SKS-G403" });
            using var explicitWriter = new StringWriter();
            var explicitExit = AutomationHostReviewDiagnosticsCommand.Execute(
                workspace.Context,
                ["--repo", "J-Tech-Japan/SekibanAsAService", "--candidate", "SKS-G403", "--format", "json"],
                explicitWriter);
            Assert.Equal(0, explicitExit);
            var explicitResult = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(explicitWriter.ToString())!;

            // Both paths must agree on classification (issue-publish-ready)
            // and surface SKS-G403 in the recommended next command.
            Assert.Equal("issue-publish-ready", implicitResult.Classification);
            Assert.Equal("issue-publish-ready", explicitResult.Classification);
            Assert.Equal(implicitResult.Classification, explicitResult.Classification);
            Assert.Contains("SKS-G403", implicitResult.RecommendedNextCommand!, StringComparison.Ordinal);
            Assert.Contains("SKS-G403", explicitResult.RecommendedNextCommand!, StringComparison.Ordinal);
        }
        finally
        {
            AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_G364_NextSliceProbe_DesignNeeded_LeavesAnalyzerToClassify()
    {
        // G364 AC (negative): diagnostics does not own the
        // `design-needed` lane — that lane lives in the host-loop-next-action
        // analyzer. When next-slice reports `design-needed`, the
        // diagnostics probe must NOT promote it to a different
        // classification or clarification gate. The analyzer is allowed
        // to fall through to `true-idle` (its current behavior) because
        // diagnostics has no lane vocabulary for design-needed; the
        // host-loop-next-action surface is responsible for surfacing
        // design-needed to the operator. This test pins the contract so
        // a future "promote design-needed to clarification" change is
        // intentional.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();
        AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "design-needed", ExecutionUnit = null });

        try
        {
            using var writer = new StringWriter();
            var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
                workspace.Context,
                ["--repo", "owner/repo", "--format", "json"],
                writer);

            Assert.Equal(0, exitCode);
            var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
            Assert.NotEqual("clarification-required", result.Classification);
            Assert.NotEqual("issue-publish-ready", result.Classification);
        }
        finally
        {
            AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_PublishRecoveryProbe_SurfacesPublishRecoveryReady_WhenSafeRepairsAvailable()
    {
        // G342: when `automation publish-recovery --dry-run` reports
        // safe_repairs > 0, the diagnostics must surface
        // `publish-recovery-ready` (the `linked_pr` deterministic
        // recovery lane) instead of `true-idle`. Mirrors the
        // host-loop-next-action probe so both surfaces agree.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();
        AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });
        AutomationHostReviewDiagnosticsCommand.PublishRecoveryProbeFactory = _ => new FakePublishRecoveryProbe(
            new PublishRecoveryProbeResult { SafeRepairCount = 1, UnsafeStopCount = 0 });

        try
        {
            using var writer = new StringWriter();
            var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
                workspace.Context,
                ["--repo", "owner/repo", "--format", "json"],
                writer);

            Assert.Equal(0, exitCode);
            var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
            Assert.Equal("publish-recovery-ready", result.Classification);
            Assert.NotNull(result.RecommendedNextCommand);
            Assert.Contains("publish-recovery", result.RecommendedNextCommand!, StringComparison.Ordinal);
        }
        finally
        {
            AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = null;
            AutomationHostReviewDiagnosticsCommand.PublishRecoveryProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_PublishRecoveryProbe_LaneSuppressed_WhenUnsafeStopsPresent()
    {
        // G358: when the publish-recovery probe reports safe_repairs > 0 but
        // also unsafe_stop_count > 0, the diagnostics must NOT surface
        // `publish-recovery-ready` because the --write path refuses all
        // mutations when unsafe stops are present. The result must fall
        // through to `true-idle` (no work to do safely).
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();
        AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });
        AutomationHostReviewDiagnosticsCommand.PublishRecoveryProbeFactory = _ => new FakePublishRecoveryProbe(
            new PublishRecoveryProbeResult { SafeRepairCount = 1, UnsafeStopCount = 1 });

        try
        {
            using var writer = new StringWriter();
            var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
                workspace.Context,
                ["--repo", "owner/repo", "--format", "json"],
                writer);

            Assert.Equal(0, exitCode);
            var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
            Assert.NotEqual("publish-recovery-ready", result.Classification);
        }
        finally
        {
            AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = null;
            AutomationHostReviewDiagnosticsCommand.PublishRecoveryProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_OperatorSupplied_PublishRecoveryRepairs_BypassesProbe_InDiagnostics()
    {
        // G342: when the operator pre-supplies
        // `--publish-recovery-repairs-available <N>`, the diagnostics
        // skip the probe and use the operator value verbatim.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();
        AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });
        var probeInvoked = false;
        AutomationHostReviewDiagnosticsCommand.PublishRecoveryProbeFactory = _ =>
        {
            probeInvoked = true;
            return new FakePublishRecoveryProbe(new PublishRecoveryProbeResult { SafeRepairCount = 99, UnsafeStopCount = 0 });
        };

        try
        {
            using var writer = new StringWriter();
            var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
                workspace.Context,
                ["--repo", "owner/repo", "--publish-recovery-repairs-available", "1", "--format", "json"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.False(probeInvoked, "publish-recovery probe must not run when operator pre-supplied the count");
            var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
            Assert.Equal("publish-recovery-ready", result.Classification);
        }
        finally
        {
            AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = null;
            AutomationHostReviewDiagnosticsCommand.PublishRecoveryProbeFactory = null;
        }
    }

    // ── G355: safe_repair_available field ────────────────────────────────

    [Fact]
    public void Execute_G355_RepairedAndRetry_SetsSafeRepairAvailableTrue()
    {
        // G355 AC: When diagnostics classifies repaired-and-retry,
        // safe_repair_available must be true and safe_repair_category must
        // be host-artifact-repair.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--reconcile-repairs-available", "1", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("repaired-and-retry", result.Classification);
        Assert.True(result.SafeRepairAvailable);
        Assert.Equal("host-artifact-repair", result.SafeRepairCategory);
    }

    [Fact]
    public void Execute_G355_PublishRecoveryReady_SetsSafeRepairAvailableTrueWithReviewLinkageGap()
    {
        // G355 AC: When diagnostics classifies publish-recovery-ready,
        // safe_repair_available must be true and safe_repair_category must
        // be review-linkage-gap.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--publish-recovery-repairs-available", "1", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("publish-recovery-ready", result.Classification);
        Assert.True(result.SafeRepairAvailable);
        Assert.Equal("review-linkage-gap", result.SafeRepairCategory);
    }

    [Fact]
    public void Execute_G355_TrueIdle_SetsSafeRepairAvailableFalse()
    {
        // G355 AC: When diagnostics classifies true-idle,
        // safe_repair_available must be false — the loop should stop.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("true-idle", result.Classification);
        Assert.False(result.SafeRepairAvailable);
        Assert.Null(result.SafeRepairCategory);
    }

    [Fact]
    public void Execute_G355_UnsafeMetadata_SetsSafeRepairAvailableFalse()
    {
        // G355 AC: When diagnostics classifies unsafe-metadata,
        // safe_repair_available must be false — do NOT attempt a repair.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system",
             "--reconcile-unsafe-stop", "ambiguous-queue-linkage",
             "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("unsafe-metadata", result.Classification);
        Assert.False(result.SafeRepairAvailable);
        Assert.Null(result.SafeRepairCategory);
    }

    [Fact]
    public void Execute_G355_StuckReviewing_SetsSafeRepairAvailableTrueWithStaleReviewLease()
    {
        // G355 AC: When diagnostics classifies stuck-reviewing (stale review
        // lease), safe_repair_available must be true and safe_repair_category
        // must be stale-review-lease. The recommended_next_command must
        // reference pr-transition --transition review-release.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(490, "stuck reviewing", "https://github.com/J-Tech-Japan/intent-system/pull/490",
                    body: "Closes #559", labels: ["intent-target", "intent-pr-reviewing"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("stuck-reviewing", result.Classification);
        Assert.True(result.SafeRepairAvailable);
        Assert.Equal("stale-review-lease", result.SafeRepairCategory);
        Assert.NotNull(result.RecommendedNextCommand);
        Assert.Contains("review-release", result.RecommendedNextCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G355_WorkspaceSafeDirty_SetsSafeRepairAvailableTrue()
    {
        // G355 AC: When --workspace-safe-dirty is passed, diagnostics must
        // return repaired-and-retry with safe_repair_available: true and
        // safe_repair_category: workspace-safe-dirty. This signals the host
        // loop to run workspace-guard --mode begin --write before proceeding.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--workspace-safe-dirty", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("repaired-and-retry", result.Classification);
        Assert.True(result.SafeRepairAvailable);
        Assert.Equal("workspace-safe-dirty", result.SafeRepairCategory);
        Assert.NotNull(result.RecommendedNextCommand);
        Assert.Contains("workspace-guard", result.RecommendedNextCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G365_HostBinding_Match_DerivesDomainAndProbesAndReturnsIssuePublishReady()
    {
        // G365: when neither --candidate nor --domain is supplied and
        // the host-binding records target_repo matching --repo, the
        // resolver-supplied domain reaches the next-slice probe and a
        // returned `issue-cut-ready` candidate flips diagnostics from
        // `true-idle` to `issue-publish-ready`.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        string? probedDomain = null;
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();
        AutomationHostReviewDiagnosticsCommand.HostBindingDomainResolverDelegate = (_, repo) =>
            HostBindingDomainResolution.Match("sekiban-as-a-service", "/host/.intent-cli/host-binding.toml");
        AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "issue-cut-ready", ExecutionUnit = "SKS-G406" },
            onProbeArgs: (_, d) => probedDomain = d);

        try
        {
            using var writer = new StringWriter();
            var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
                workspace.Context, // configured domain = "intent-cli"
                ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.Equal("sekiban-as-a-service", probedDomain);
            var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
            Assert.Equal("issue-publish-ready", result.Classification);
            Assert.Contains("SKS-G406", result.RecommendedNextCommand!, StringComparison.Ordinal);
        }
        finally
        {
            AutomationHostReviewDiagnosticsCommand.HostBindingDomainResolverDelegate = null;
            AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_G365_HostBinding_Mismatch_EmitsMissingDomainBindingClassification()
    {
        // G365: when no --domain is supplied and the host-binding records
        // a target_repo that does not match --repo, diagnostics must
        // return `missing-domain-binding` with read_only true and a
        // recommendation to pass --domain explicitly. The next-slice
        // probe must NOT run.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        var probeWasInvoked = false;
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();
        AutomationHostReviewDiagnosticsCommand.HostBindingDomainResolverDelegate = (_, _) =>
            HostBindingDomainResolution.Mismatch(
                domain: "intent-cli",
                boundTargetRepo: "J-Tech-Japan/intent-system",
                bindingPath: "/host/.intent-cli/host-binding.toml");
        AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "issue-cut-ready", ExecutionUnit = "WRONG-DOMAIN" },
            onProbe: () => probeWasInvoked = true);

        try
        {
            using var writer = new StringWriter();
            var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
                workspace.Context,
                ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.False(probeWasInvoked, "next-slice probe must not run on host-binding mismatch");
            var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
            Assert.Equal("missing-domain-binding", result.Classification);
            Assert.True(result.ReadOnly);
            Assert.False(result.SafeRepairAvailable);
            Assert.Null(result.SafeRepairCategory);
            Assert.NotNull(result.RecommendedNextCommand);
            Assert.Contains("--domain <DOMAIN>", result.RecommendedNextCommand!, StringComparison.Ordinal);
            Assert.Single(result.Details);
            Assert.Contains("J-Tech-Japan/intent-system", result.Details[0].Description, StringComparison.Ordinal);
            Assert.Contains("J-Tech-Japan/SekibanAsAService", result.Details[0].Description, StringComparison.Ordinal);
        }
        finally
        {
            AutomationHostReviewDiagnosticsCommand.HostBindingDomainResolverDelegate = null;
            AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_G365_HostBinding_Missing_FallsBackToConfiguredDomain()
    {
        // G365 backward-compat: when no host-binding is present the
        // existing G341 fallback to context.Config.Project.Domain
        // continues to work unchanged.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        string? probedDomain = null;
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();
        AutomationHostReviewDiagnosticsCommand.HostBindingDomainResolverDelegate = (_, _) =>
            HostBindingDomainResolution.Missing("(no binding file)");
        AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null },
            onProbeArgs: (_, d) => probedDomain = d);

        try
        {
            using var writer = new StringWriter();
            var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
                workspace.Context, // configured domain = "intent-cli"
                ["--repo", "owner/repo", "--format", "json"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.Equal("intent-cli", probedDomain);
        }
        finally
        {
            AutomationHostReviewDiagnosticsCommand.HostBindingDomainResolverDelegate = null;
            AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_G365_ExplicitDomainFlag_BypassesHostBindingLookup()
    {
        // G365: when --domain is supplied, the host-binding resolver
        // must not be invoked. This preserves the pre-G365 contract
        // that an explicit domain flag wins.
        using var workspace = new HostReviewDiagnosticsWorkspace();
        var resolverInvoked = false;
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();
        AutomationHostReviewDiagnosticsCommand.HostBindingDomainResolverDelegate = (_, _) =>
        {
            resolverInvoked = true;
            return HostBindingDomainResolution.Mismatch(
                "wrong-domain", "wrong-repo", "/host/.intent-cli/host-binding.toml");
        };
        AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });

        try
        {
            using var writer = new StringWriter();
            var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
                workspace.Context,
                ["--repo", "owner/repo", "--domain", "operator-supplied", "--format", "json"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.False(resolverInvoked, "host-binding resolver must not run when --domain is supplied");
        }
        finally
        {
            AutomationHostReviewDiagnosticsCommand.HostBindingDomainResolverDelegate = null;
            AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = null;
        }
    }

    /// <summary>
    /// G342: deterministic stand-in for the publish-recovery probe.
    /// </summary>
    private sealed class FakePublishRecoveryProbe : IPublishRecoveryProbe
    {
        private readonly PublishRecoveryProbeResult? _canned;
        public FakePublishRecoveryProbe(PublishRecoveryProbeResult? canned) { _canned = canned; }
        public PublishRecoveryProbeResult? Probe(string repo) => _canned;
    }

    /// <summary>
    /// G341: deterministic stand-in for <see cref="INextSliceDryRunProbe"/>
    /// used by the host-review-diagnostics tests. Returns a canned
    /// <see cref="NextSliceProbeResult"/> and records the (repo, domain)
    /// pair the command passed in.
    /// </summary>
    private sealed class FakeNextSliceProbe : INextSliceDryRunProbe
    {
        private readonly NextSliceProbeResult? _canned;
        private readonly Action? _onProbe;
        private readonly Action<string, string>? _onProbeArgs;

        public FakeNextSliceProbe(
            NextSliceProbeResult? canned,
            Action? onProbe = null,
            Action<string, string>? onProbeArgs = null)
        {
            _canned = canned;
            _onProbe = onProbe;
            _onProbeArgs = onProbeArgs;
        }

        public NextSliceProbeResult? Probe(string repo, string domain)
        {
            _onProbe?.Invoke();
            _onProbeArgs?.Invoke(repo, domain);
            return _canned;
        }
    }

    private static GitHubAutomationPrCandidate BuildPr(
        int number,
        string title,
        string url,
        string body,
        IReadOnlyList<string> labels,
        string state = "OPEN") =>
        new()
        {
            Number = number,
            Title = title,
            Url = url,
            Body = body,
            CreatedAt = "2026-05-07T00:00:00Z",
            UpdatedAt = "2026-05-07T00:00:00Z",
            Labels = labels.Select(label => new GitHubAutomationLabel { Name = label }).ToArray(),
            State = state,
        };

    private static GitHubAutomationIssueCandidate BuildIssue(
        int number,
        string title,
        string url,
        IReadOnlyList<string> labels,
        string state = "OPEN") =>
        new()
        {
            Number = number,
            Title = title,
            Url = url,
            CreatedAt = "2026-05-07T00:00:00Z",
            Labels = labels.Select(label => new GitHubAutomationLabel { Name = label }).ToArray(),
            State = state,
        };

    private sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> AllPrs { get; init; } = Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationIssueCandidate> PublishedIssues { get; init; } = Array.Empty<GitHubAutomationIssueCandidate>();

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => AllPrs;

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) => PublishedIssues;
    }

    private sealed class ThrowingLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) =>
            throw new InvalidOperationException("lister should not be invoked when surface probe rejects");

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) =>
            throw new InvalidOperationException("lister should not be invoked when surface probe rejects");
    }

    // ── G390 review-lease preservation lane ────────────────────────────────

    [Fact]
    public void Execute_ReviewLeasePreservation_MetadataBlockedBeforeVerdict_RestoresRereviewReady()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--review-lease-preservation", "--rereview-ready-consumed", "--host-metadata-blocker",
             "--repo", "J-Tech-Japan/intent-system", "--pr", "3639", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("metadata-blocked-review-preserved", result.Classification);
        Assert.True(result.SafeRepairAvailable);
        Assert.Equal("stale-review-lease", result.SafeRepairCategory);
        Assert.NotNull(result.RecommendedNextCommand);
        // The recommended repair must use a SUPPORTED automation pr-transition
        // (review-release), not the non-existent `rereview-ready` transition.
        Assert.Contains("--transition review-release", result.RecommendedNextCommand!, StringComparison.Ordinal);
        Assert.DoesNotContain("--transition rereview-ready", result.RecommendedNextCommand!, StringComparison.Ordinal);
        Assert.Matches(@"--transition (review-start|request-update|approved|review-release)\b", result.RecommendedNextCommand!);
        // The host metadata blocker must not become an implementation comment.
        Assert.Contains("must NOT be posted as an implementation request-update comment", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ReviewLeasePreservation_ReviewVerdictProduced_NoRestore()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--review-lease-preservation", "--rereview-ready-consumed", "--review-verdict-produced",
             "--repo", "J-Tech-Japan/intent-system", "--pr", "3639", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.NotEqual("metadata-blocked-review-preserved", result.Classification);
        Assert.False(result.SafeRepairAvailable);
    }

    private sealed class HostReviewDiagnosticsWorkspace : IDisposable
    {
        public HostReviewDiagnosticsWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("automation-host-review-diagnostics-tests-").FullName;
            WriteInstalledCliScript(stalePrTransition: false);
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli"
                    }
                }
            };
        }

        public string RootPath { get; }

        public CliContext Context { get; }

        public void WriteSentinel() =>
            File.WriteAllText(Path.Combine(RootPath, "sentinel.txt"), "unchanged");

        public IReadOnlyDictionary<string, string> SnapshotFiles()
        {
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories))
            {
                snapshot[path] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
            }
            return snapshot;
        }

        public void WriteInstalledCliScript(bool stalePrTransition)
        {
            var binPath = Path.Combine(RootPath, ".intent-cli", "bin");
            Directory.CreateDirectory(binPath);
            var scriptPath = Path.Combine(binPath, "intent-cli");
            var prTransitionBlock = stalePrTransition
                ? "  echo \"Command 'automation pr-transition' is not yet implemented.\"\n  exit 1\n"
                : "  echo '--transition is required (review-start, request-update, or approved).'\n  exit 1\n";
            File.WriteAllText(
                scriptPath,
                "#!/bin/sh\n"
                + "case \"$*\" in\n"
                + "  'automation summary') echo '--domain is required.'; exit 1 ;;\n"
                + "  'automation host-review-preflight') echo '--repo is required.'; exit 1 ;;\n"
                + "  'automation issue-publish') echo '--issue is required.'; exit 1 ;;\n"
                + "  'automation pr-transition')\n"
                + prTransitionBlock
                + "    ;;\n"
                + "  *) echo \"unexpected probe: $*\"; exit 1 ;;\n"
                + "esac\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    scriptPath,
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute);
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
