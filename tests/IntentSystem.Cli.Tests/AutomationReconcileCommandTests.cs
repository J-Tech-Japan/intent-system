using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class AutomationReconcileCommandTests : IDisposable
{
    public AutomationReconcileCommandTests()
    {
        AutomationReconcileCommand.CandidateListerFactory = null;
        AutomationReconcileCommand.MutatorFactory = null;
        AutomationReconcileCommand.NestedProviderLauncher = null;
    }

    public void Dispose()
    {
        AutomationReconcileCommand.CandidateListerFactory = null;
        AutomationReconcileCommand.MutatorFactory = null;
        AutomationReconcileCommand.NestedProviderLauncher = null;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null;
    }

    [Fact]
    public void Execute_DryRun_DetectsMissingPrIntentTargetAndMissingIssueIntentPrCreated()
    {
        using var workspace = new ReconcileWorkspace();
        var lister = new FakeLister
        {
            AllPrs =
            [
                BuildPr(420, "child impl", "https://github.com/J-Tech-Japan/intent-system/pull/420",
                    body: "Closes #559", labels: Array.Empty<string>()),
            ],
            PublishedIssues =
            [
                BuildIssue(559, "G227 some unit", "https://github.com/J-Tech-Japan/intent-system/issues/559",
                    labels: ["intent-target"]),
            ],
        };
        AutomationReconcileCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        Assert.Equal("host-review", result.Lane);
        Assert.Equal("dry-run", result.Mode);
        Assert.True(result.HostOnly);
        Assert.Empty(result.UnsafeStops);

        Assert.Contains(result.SafeRepairs, repair =>
            string.Equals(repair.Type, AutomationReconcileRepairTypes.MissingPrIntentTarget, StringComparison.Ordinal)
            && repair.TargetNumber == 420
            && repair.AddLabels.Contains("intent-target", StringComparer.Ordinal)
            && string.Equals(repair.Confidence, AutomationReconcileConfidence.High, StringComparison.Ordinal)
            && !repair.Applied);

        Assert.Contains(result.SafeRepairs, repair =>
            string.Equals(repair.Type, AutomationReconcileRepairTypes.MissingIssueIntentPrCreated, StringComparison.Ordinal)
            && repair.TargetNumber == 559
            && repair.AddLabels.Contains("intent-pr-created", StringComparer.Ordinal)
            && string.Equals(repair.Confidence, AutomationReconcileConfidence.High, StringComparison.Ordinal)
            && !repair.Applied);

        Assert.Contains(result.SafeRepairs, repair =>
            string.Equals(repair.Type, AutomationReconcileRepairTypes.MissingLinkedPrMetadata, StringComparison.Ordinal)
            && string.Equals(repair.Confidence, AutomationReconcileConfidence.Advisory, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(repair.RequiresFollowupCommand));
    }

    [Fact]
    public void Execute_DryRun_DetectsMisplacedIntentPrCreatedOnPr()
    {
        using var workspace = new ReconcileWorkspace();
        var lister = new FakeLister
        {
            AllPrs =
            [
                BuildPr(421, "should not have intent-pr-created",
                    "https://github.com/J-Tech-Japan/intent-system/pull/421",
                    body: "Closes #560",
                    labels: ["intent-target", "intent-pr-created"]),
            ],
            PublishedIssues =
            [
                BuildIssue(560, "G228", "https://github.com/J-Tech-Japan/intent-system/issues/560",
                    labels: ["intent-target", "intent-pr-created"]),
            ],
        };
        AutomationReconcileCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;

        Assert.Contains(result.SafeRepairs, repair =>
            string.Equals(repair.Type, AutomationReconcileRepairTypes.MisplacedPrIntentPrCreated, StringComparison.Ordinal)
            && repair.TargetNumber == 421
            && repair.RemoveLabels.Contains("intent-pr-created", StringComparer.Ordinal)
            && string.Equals(repair.Confidence, AutomationReconcileConfidence.High, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_DryRun_DetectsApprovedPrStillCarryingRereviewReady_G503()
    {
        using var workspace = new ReconcileWorkspace();
        var lister = new FakeLister
        {
            AllPrs =
            [
                BuildPr(543, "approved but still rereview-ready",
                    "https://github.com/J-Tech-Japan/intent-system/pull/543",
                    body: "Closes #561",
                    labels: ["intent-target", "intent-pr-approved", "intent-pr-rereview-ready"]),
            ],
            PublishedIssues =
            [
                BuildIssue(561, "G229", "https://github.com/J-Tech-Japan/intent-system/issues/561",
                    labels: ["intent-target", "intent-pr-created"]),
            ],
        };
        AutomationReconcileCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;

        Assert.Contains(result.SafeRepairs, repair =>
            string.Equals(repair.Type, AutomationReconcileRepairTypes.ApprovedPrStaleReviewLabel, StringComparison.Ordinal)
            && repair.TargetNumber == 543
            && repair.RemoveLabels.Contains("intent-pr-rereview-ready", StringComparer.Ordinal)
            && !repair.AddLabels.Any()
            && string.Equals(repair.Confidence, AutomationReconcileConfidence.High, StringComparison.Ordinal)
            && !repair.Applied);
    }

    [Fact]
    public void Execute_DryRun_ApprovedPrWithoutStaleReviewLabel_NoApprovedStaleRepair_G503()
    {
        using var workspace = new ReconcileWorkspace();
        var lister = new FakeLister
        {
            AllPrs =
            [
                BuildPr(544, "cleanly approved",
                    "https://github.com/J-Tech-Japan/intent-system/pull/544",
                    body: "Closes #562",
                    labels: ["intent-target", "intent-pr-approved"]),
            ],
            PublishedIssues =
            [
                BuildIssue(562, "G230", "https://github.com/J-Tech-Japan/intent-system/issues/562",
                    labels: ["intent-target", "intent-pr-created"]),
            ],
        };
        AutomationReconcileCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;

        Assert.DoesNotContain(result.SafeRepairs, repair =>
            string.Equals(repair.Type, AutomationReconcileRepairTypes.ApprovedPrStaleReviewLabel, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_NoDriftReturnsCleanPlanWithSummaryAndZeroExit()
    {
        using var workspace = new ReconcileWorkspace();
        var lister = new FakeLister
        {
            AllPrs =
            [
                BuildPr(500, "clean", "https://github.com/J-Tech-Japan/intent-system/pull/500",
                    body: "Closes #999",
                    labels: ["intent-target"]),
            ],
            PublishedIssues =
            [
                BuildIssue(999, "G300", "https://github.com/J-Tech-Japan/intent-system/issues/999",
                    labels: ["intent-target", "intent-pr-created"]),
            ],
        };
        AutomationReconcileCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;

        Assert.DoesNotContain(result.SafeRepairs, repair =>
            string.Equals(repair.Confidence, AutomationReconcileConfidence.High, StringComparison.Ordinal));
        Assert.Empty(result.UnsafeStops);
    }

    [Fact]
    public void Execute_DryRunNeverInvokesMutator()
    {
        using var workspace = new ReconcileWorkspace();
        var lister = new FakeLister
        {
            AllPrs =
            [
                BuildPr(600, "needs intent-target", "https://github.com/J-Tech-Japan/intent-system/pull/600",
                    body: "Closes #559", labels: Array.Empty<string>()),
            ],
            PublishedIssues =
            [
                BuildIssue(559, "G227", "https://github.com/J-Tech-Japan/intent-system/issues/559",
                    labels: ["intent-target"]),
            ],
        };
        AutomationReconcileCommand.CandidateListerFactory = () => lister;
        var mutator = new RecordingMutator();
        AutomationReconcileCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Empty(mutator.Reconciles);
    }

    [Fact]
    public void Execute_WriteAppliesOnlyHighConfidenceRepairs()
    {
        using var workspace = new ReconcileWorkspace();
        var lister = new FakeLister
        {
            AllPrs =
            [
                BuildPr(700, "missing target", "https://github.com/J-Tech-Japan/intent-system/pull/700",
                    body: "Closes #559", labels: Array.Empty<string>()),
            ],
            PublishedIssues =
            [
                BuildIssue(559, "G227", "https://github.com/J-Tech-Japan/intent-system/issues/559",
                    labels: ["intent-target"]),
            ],
        };
        AutomationReconcileCommand.CandidateListerFactory = () => lister;
        var mutator = new RecordingMutator();
        AutomationReconcileCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, mutator.Reconciles.Count);
        Assert.Contains(mutator.Reconciles, t =>
            t.Kind == "pr" && t.Number == 700
            && t.AddLabels.Contains("intent-target", StringComparer.Ordinal));
        Assert.Contains(mutator.Reconciles, t =>
            t.Kind == "issue" && t.Number == 559
            && t.AddLabels.Contains("intent-pr-created", StringComparer.Ordinal));

        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        Assert.Equal("write", result.Mode);
        Assert.Contains(result.SafeRepairs, repair =>
            string.Equals(repair.Confidence, AutomationReconcileConfidence.High, StringComparison.Ordinal)
            && repair.Applied);
        Assert.DoesNotContain(result.SafeRepairs, repair =>
            string.Equals(repair.Confidence, AutomationReconcileConfidence.Advisory, StringComparison.Ordinal)
            && repair.Applied);
    }

    [Fact]
    public void Execute_AmbiguousIssueLinkProducesUnsafeStop()
    {
        using var workspace = new ReconcileWorkspace();
        var lister = new FakeLister
        {
            AllPrs =
            [
                BuildPr(800, "no closes keyword",
                    "https://github.com/J-Tech-Japan/intent-system/pull/800",
                    body: "free-form notes — no Closes keyword",
                    labels: ["intent-target"]),
            ],
            PublishedIssues = Array.Empty<GitHubAutomationIssueCandidate>(),
        };
        AutomationReconcileCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        Assert.Contains(result.UnsafeStops, stop =>
            string.Equals(stop.Kind, AutomationReconcileUnsafeStopKinds.AmbiguousIssueLink, StringComparison.Ordinal)
            && stop.TargetNumber == 800);
    }

    [Fact]
    public void Execute_ChildLoopContextRefusesEarlyAndExitsTwo()
    {
        using var workspace = new ReconcileWorkspace();
        var lister = new ThrowingLister();
        AutomationReconcileCommand.CandidateListerFactory = () => lister;
        var mutator = new RecordingMutator();
        AutomationReconcileCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--child-loop-context", "--write", "--format", "json"],
            writer);

        Assert.Equal(2, exitCode);
        Assert.Empty(mutator.Reconciles);

        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        Assert.Empty(result.SafeRepairs);
        Assert.Contains(result.UnsafeStops, stop =>
            string.Equals(stop.Kind, AutomationReconcileUnsafeStopKinds.ChildLoopProhibited, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_NextSliceLane_StaleCacheClassifiedAsAdvisory()
    {
        using var workspace = new ReconcileWorkspace();
        AutomationReconcileCommand.CandidateListerFactory = () => new ThrowingLister();

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            [
                "--lane", "next-slice",
                "--repo", "J-Tech-Japan/intent-system",
                "--next-slice-clarification-required",
                "--clarifications-all-resolved",
                "--format", "json"
            ],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        Assert.Empty(result.UnsafeStops);
        Assert.Contains(result.SafeRepairs, repair =>
            string.Equals(repair.Type, AutomationReconcileRepairTypes.StaleNextSliceCandidateCache, StringComparison.Ordinal)
            && string.Equals(repair.Confidence, AutomationReconcileConfidence.Advisory, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(repair.RequiresFollowupCommand));
    }

    [Fact]
    public void Execute_NextSliceLane_OpenClarificationProducesUnsafeStop()
    {
        using var workspace = new ReconcileWorkspace();
        AutomationReconcileCommand.CandidateListerFactory = () => new ThrowingLister();

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            [
                "--lane", "next-slice",
                "--repo", "J-Tech-Japan/intent-system",
                "--next-slice-clarification-required",
                "--format", "json"
            ],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        Assert.DoesNotContain(result.SafeRepairs, repair =>
            string.Equals(repair.Type, AutomationReconcileRepairTypes.StaleNextSliceCandidateCache, StringComparison.Ordinal));
        Assert.Contains(result.UnsafeStops, stop =>
            string.Equals(stop.Kind, "open-clarification-present", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_StaleHostCliReturnsStructuredStopAndDoesNotRunLister()
    {
        using var workspace = new ReconcileWorkspace();
        workspace.WriteInstalledCliScript(stalePrTransition: true);
        AutomationReconcileCommand.CandidateListerFactory = () => new ThrowingLister();

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        Assert.Contains(result.UnsafeStops, stop =>
            string.Equals(stop.Kind, "stale-host-cli", StringComparison.Ordinal));
        Assert.Empty(result.SafeRepairs);
    }

    [Fact]
    public void CommandRouter_RegistersAutomationReconcile()
    {
        using var workspace = new ReconcileWorkspace();
        var lister = new FakeLister();
        AutomationReconcileCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["automation", "reconcile", "--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            workspace.Context,
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        Assert.True(result.HostOnly);
    }

    [Fact]
    public void CommandRouter_HelpListsAutomationReconcile()
    {
        using var workspace = new ReconcileWorkspace();

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute([], workspace.Context, writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("automation reconcile", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GuidePromptMatrix_ChildLoopDoesNotMentionReconcile()
    {
        using var workspace = new ReconcileWorkspace();
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            workspace.Context,
            ["--mode", "child-loop", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.DoesNotContain("automation reconcile", output, StringComparison.Ordinal);
    }

    [Fact]
    public void GuidePromptMatrix_ChildOneshotDoesNotMentionReconcile()
    {
        using var workspace = new ReconcileWorkspace();
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            workspace.Context,
            ["--mode", "child-oneshot", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("automation reconcile", writer.ToString(), StringComparison.Ordinal);
    }

    // ── G284: selected-PR linkage recovery ─────────────────────────────

    [Fact]
    public void Execute_DryRun_PromotesMissingLinkedPrToHighWhenUniqueQueueItemMatches()
    {
        using var workspace = new ReconcileWorkspace();
        workspace.SeedQueueState(
            ("G284", "G284 source unit", "J-Tech-Japan/intent-system", 491, null));

        AutomationReconcileCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(492, "selected for review", "https://github.com/J-Tech-Japan/intent-system/pull/492",
                    body: "Closes #491", labels: ["intent-target"]),
            ],
            PublishedIssues =
            [
                BuildIssue(491, "G284 src", "https://github.com/J-Tech-Japan/intent-system/issues/491",
                    labels: ["intent-target", "intent-pr-created"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;

        var linkedPrRepair = Assert.Single(result.SafeRepairs.Where(r =>
            string.Equals(r.Type, AutomationReconcileRepairTypes.MissingLinkedPrMetadata, StringComparison.Ordinal)));
        Assert.Equal(AutomationReconcileConfidence.High, linkedPrRepair.Confidence);
        Assert.Equal("queue-state", linkedPrRepair.TargetKind);
        Assert.Equal("G284", linkedPrRepair.QueueStateExecutionUnit);
        Assert.Equal(492, linkedPrRepair.PrNumberToLink);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/492", linkedPrRepair.QueueStateLinkedPrUrl);
        Assert.False(linkedPrRepair.Applied);
        Assert.Empty(result.UnsafeStops.Where(s =>
            string.Equals(s.Kind, AutomationReconcileUnsafeStopKinds.AmbiguousQueueLinkage, StringComparison.Ordinal)));
    }

    [Fact]
    public void Execute_Write_PatchesQueueStateLinkedPrAndMarksRepairApplied()
    {
        using var workspace = new ReconcileWorkspace();
        workspace.SeedQueueState(
            ("G284", "G284 source unit", "J-Tech-Japan/intent-system", 491, null));

        AutomationReconcileCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(492, "ready", "https://github.com/J-Tech-Japan/intent-system/pull/492",
                    body: "Closes #491", labels: ["intent-target"]),
            ],
            PublishedIssues =
            [
                BuildIssue(491, "G284 src", "https://github.com/J-Tech-Japan/intent-system/issues/491",
                    labels: ["intent-target", "intent-pr-created"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        var linkedPrRepair = Assert.Single(result.SafeRepairs.Where(r =>
            string.Equals(r.Type, AutomationReconcileRepairTypes.MissingLinkedPrMetadata, StringComparison.Ordinal)));
        Assert.True(linkedPrRepair.Applied);

        var patched = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        var item = Assert.Single(patched.Items);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/492", item.LinkedPr);
    }

    [Fact]
    public void Execute_AmbiguousMultipleQueueItemsForSameIssue_ReportsUnsafeStopAndDoesNotWrite()
    {
        using var workspace = new ReconcileWorkspace();
        // Two queue items both reference issue #491 — operator must dedupe before reconcile can write.
        workspace.SeedQueueState(
            ("G284-a", "first", "J-Tech-Japan/intent-system", 491, null),
            ("G284-b", "second", "J-Tech-Japan/intent-system", 491, null));

        AutomationReconcileCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(492, "ambiguous", "https://github.com/J-Tech-Japan/intent-system/pull/492",
                    body: "Closes #491", labels: ["intent-target"]),
            ],
            PublishedIssues =
            [
                BuildIssue(491, "G284 src", "https://github.com/J-Tech-Japan/intent-system/issues/491",
                    labels: ["intent-target", "intent-pr-created"]),
            ],
        };

        var beforeBytes = File.ReadAllBytes(workspace.QueueStatePath);

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        var stop = Assert.Single(result.UnsafeStops.Where(s =>
            string.Equals(s.Kind, AutomationReconcileUnsafeStopKinds.AmbiguousQueueLinkage, StringComparison.Ordinal)));
        Assert.Equal(491, stop.TargetNumber);
        Assert.Contains("G284-a", stop.Reason, StringComparison.Ordinal);
        Assert.Contains("G284-b", stop.Reason, StringComparison.Ordinal);

        Assert.DoesNotContain(result.SafeRepairs, r =>
            string.Equals(r.Type, AutomationReconcileRepairTypes.MissingLinkedPrMetadata, StringComparison.Ordinal));

        // queue-state.json was not mutated.
        Assert.Equal(beforeBytes, File.ReadAllBytes(workspace.QueueStatePath));
    }

    [Fact]
    public void Execute_QueueItemAlreadyHasMatchingLinkedPr_NoLinkedPrRepairEmitted()
    {
        using var workspace = new ReconcileWorkspace();
        workspace.SeedQueueState(
            ("G284", "already-linked", "J-Tech-Japan/intent-system", 491,
                "https://github.com/J-Tech-Japan/intent-system/pull/492"));

        AutomationReconcileCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(492, "already-linked", "https://github.com/J-Tech-Japan/intent-system/pull/492",
                    body: "Closes #491", labels: ["intent-target"]),
            ],
            PublishedIssues =
            [
                BuildIssue(491, "G284 src", "https://github.com/J-Tech-Japan/intent-system/issues/491",
                    labels: ["intent-target", "intent-pr-created"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        Assert.DoesNotContain(result.SafeRepairs, r =>
            string.Equals(r.Type, AutomationReconcileRepairTypes.MissingLinkedPrMetadata, StringComparison.Ordinal));
    }

    // ── G291: conflicting linked_pr ────────────────────────────────────

    [Fact]
    public void Execute_G291_QueueItemHasDifferentLinkedPr_EmitsConflictingLinkedPrUnsafeStop()
    {
        // Queue item already points at a DIFFERENT PR than the one closing the
        // source issue. Two PRs claiming the same queue row is unsafe to
        // overwrite — operator must clarify.
        using var workspace = new ReconcileWorkspace();
        workspace.SeedQueueState(
            ("G289", "G289 source unit", "J-Tech-Japan/intent-system", 681,
                "https://github.com/J-Tech-Japan/intent-system/pull/600"));

        AutomationReconcileCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(682, "selected for review", "https://github.com/J-Tech-Japan/intent-system/pull/682",
                    body: "Closes #681", labels: ["intent-target"]),
            ],
            PublishedIssues =
            [
                BuildIssue(681, "G289 src", "https://github.com/J-Tech-Japan/intent-system/issues/681",
                    labels: ["intent-target", "intent-pr-created"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;

        var conflict = Assert.Single(result.UnsafeStops, s =>
            string.Equals(s.Kind, AutomationReconcileUnsafeStopKinds.ConflictingLinkedPr, StringComparison.Ordinal));
        Assert.Equal("queue-state", conflict.TargetKind);
        Assert.Equal(681, conflict.TargetNumber);
        Assert.Contains("/pull/600", conflict.Reason, StringComparison.Ordinal);
        // No high-confidence linked_pr write must be emitted alongside the conflict.
        Assert.DoesNotContain(result.SafeRepairs, r =>
            string.Equals(r.Type, AutomationReconcileRepairTypes.MissingLinkedPrMetadata, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_G291_PR682Shaped_EmptyLinkedPrAndUniqueQueueItem_EmitsHighConfidenceRepair()
    {
        // Replays the PR #682 / issue #681 / G289 queue item scenario: PR
        // closes a single published intent-target issue uniquely linked to
        // exactly one queue item with empty linked_pr → high-confidence
        // queue-state write, no conflicting-linked-pr unsafe stop.
        using var workspace = new ReconcileWorkspace();
        workspace.SeedQueueState(
            ("G289", "G289 source unit", "J-Tech-Japan/intent-system", 681, null));

        AutomationReconcileCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(682, "G289 PR", "https://github.com/J-Tech-Japan/intent-system/pull/682",
                    body: "Closes #681", labels: ["intent-target"]),
            ],
            PublishedIssues =
            [
                BuildIssue(681, "G289 src", "https://github.com/J-Tech-Japan/intent-system/issues/681",
                    labels: ["intent-target", "intent-pr-created"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;

        var repair = Assert.Single(result.SafeRepairs, r =>
            string.Equals(r.Type, AutomationReconcileRepairTypes.MissingLinkedPrMetadata, StringComparison.Ordinal));
        Assert.Equal(AutomationReconcileConfidence.High, repair.Confidence);
        Assert.Equal("G289", repair.QueueStateExecutionUnit);
        Assert.Equal(682, repair.PrNumberToLink);
        Assert.Contains("no current linked_pr (empty)", string.Join(" | ", repair.Evidence), StringComparison.Ordinal);
        Assert.DoesNotContain(result.UnsafeStops, s =>
            string.Equals(s.Kind, AutomationReconcileUnsafeStopKinds.ConflictingLinkedPr, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_G291_LinkedPrAlreadyMatchesPr_NoRepairOrUnsafeStop()
    {
        // Idempotent regression guard: when the queue item's linked_pr already
        // matches the closing PR, no repair AND no conflict are emitted.
        using var workspace = new ReconcileWorkspace();
        workspace.SeedQueueState(
            ("G289", "already linked", "J-Tech-Japan/intent-system", 681,
                "https://github.com/J-Tech-Japan/intent-system/pull/682"));

        AutomationReconcileCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(682, "G289 PR", "https://github.com/J-Tech-Japan/intent-system/pull/682",
                    body: "Closes #681", labels: ["intent-target"]),
            ],
            PublishedIssues =
            [
                BuildIssue(681, "G289 src", "https://github.com/J-Tech-Japan/intent-system/issues/681",
                    labels: ["intent-target", "intent-pr-created"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        Assert.DoesNotContain(result.SafeRepairs, r =>
            string.Equals(r.Type, AutomationReconcileRepairTypes.MissingLinkedPrMetadata, StringComparison.Ordinal));
        Assert.DoesNotContain(result.UnsafeStops, s =>
            string.Equals(s.Kind, AutomationReconcileUnsafeStopKinds.ConflictingLinkedPr, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_WithoutQueueState_KeepsLinkedPrRepairAdvisory()
    {
        using var workspace = new ReconcileWorkspace();
        // intentionally no SeedQueueState call — backward compatibility check.
        Assert.False(File.Exists(workspace.QueueStatePath));

        AutomationReconcileCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(492, "no queue evidence", "https://github.com/J-Tech-Japan/intent-system/pull/492",
                    body: "Closes #491", labels: ["intent-target"]),
            ],
            PublishedIssues =
            [
                BuildIssue(491, "G284 src", "https://github.com/J-Tech-Japan/intent-system/issues/491",
                    labels: ["intent-target", "intent-pr-created"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        var advisory = Assert.Single(result.SafeRepairs.Where(r =>
            string.Equals(r.Type, AutomationReconcileRepairTypes.MissingLinkedPrMetadata, StringComparison.Ordinal)));
        Assert.Equal(AutomationReconcileConfidence.Advisory, advisory.Confidence);
        Assert.NotNull(advisory.RequiresFollowupCommand);
        Assert.Contains("intent-cli closeout pr", advisory.RequiresFollowupCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void GuidePromptMatrix_HostLoopMentionsSelectedPrLinkageRecoveryAndRetryOnce()
    {
        using var workspace = new ReconcileWorkspace();
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            workspace.Context,
            ["--mode", "host-loop", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Selected-PR linkage recovery", output, StringComparison.Ordinal);
        Assert.Contains("retry the same selected PR exactly once", output, StringComparison.Ordinal);
        Assert.Contains("ambiguous-queue-linkage", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Adapter_ApplyReconcileTransitions_RejectsAddingIntentPrCreatedToPr()
    {
        var mutator = new GhCliGitHubLabelMutator();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            mutator.ApplyReconcileTransitions(
                "J-Tech-Japan/intent-system", "pr", 421,
                new[] { "intent-pr-created" },
                Array.Empty<string>()));
        Assert.Contains("issue-only", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GuidePromptMatrix_HostLoopMentionsReconcile()
    {
        using var workspace = new ReconcileWorkspace();
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            workspace.Context,
            ["--mode", "host-loop", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("automation reconcile", writer.ToString(), StringComparison.Ordinal);
    }

    private static GitHubAutomationPrCandidate BuildPr(
        int number,
        string title,
        string url,
        string body,
        IReadOnlyList<string> labels) =>
        new()
        {
            Number = number,
            Title = title,
            Url = url,
            Body = body,
            CreatedAt = "2026-05-06T00:00:00Z",
            UpdatedAt = "2026-05-06T00:00:00Z",
            Labels = labels.Select(label => new GitHubAutomationLabel { Name = label }).ToArray(),
        };

    private static GitHubAutomationIssueCandidate BuildIssue(
        int number,
        string title,
        string url,
        IReadOnlyList<string> labels) =>
        new()
        {
            Number = number,
            Title = title,
            Url = url,
            CreatedAt = "2026-05-06T00:00:00Z",
            Labels = labels.Select(label => new GitHubAutomationLabel { Name = label }).ToArray(),
        };

    private sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> AllPrs { get; init; } = Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationIssueCandidate> PublishedIssues { get; init; } = Array.Empty<GitHubAutomationIssueCandidate>();

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels) =>
            AllPrs;

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo,
            IReadOnlyCollection<string> requiredLabels) =>
            PublishedIssues;
    }

    private sealed class ThrowingLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels) =>
            throw new InvalidOperationException("lister should not be invoked in this test");

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo,
            IReadOnlyCollection<string> requiredLabels) =>
            throw new InvalidOperationException("lister should not be invoked in this test");
    }

    private sealed class RecordingMutator : IGitHubLabelMutator
    {
        public List<RecordedTransition> Transitions { get; } = new();
        public List<RecordedTransition> Reconciles { get; } = new();

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            Array.Empty<GitHubAutomationLabel>();

        public void ApplyLabelTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels) =>
            Transitions.Add(new RecordedTransition(repo, kind, number, addLabels.ToArray(), removeLabels.ToArray()));

        public void ApplyReconcileTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels) =>
            Reconciles.Add(new RecordedTransition(repo, kind, number, addLabels.ToArray(), removeLabels.ToArray()));
    }

    private sealed record RecordedTransition(
        string Repo,
        string Kind,
        int Number,
        IReadOnlyList<string> AddLabels,
        IReadOnlyList<string> RemoveLabels);

    private sealed class ReconcileWorkspace : IDisposable
    {
        public string QueueStatePath => Path.Combine(RootPath, ".intent-cli", "queue-state.json");

        /// <summary>G284: seed queue-state.json with an arbitrary set of items
        /// so the linked_pr promotion / ambiguous-queue-linkage paths are
        /// covered without coupling tests to the production publish-flow.</summary>
        public void SeedQueueState(params (string ExecutionUnit, string Title, string IssueRepo, int IssueNumber, string? LinkedPrUrl)[] items)
        {
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            var state = new QueueState
            {
                SchemaVersion = "1",
                UpdatedAt = new DateTimeOffset(2026, 5, 7, 0, 0, 0, TimeSpan.Zero),
                Items = items.Select(item => new QueueItem
                {
                    ExecutionUnit = item.ExecutionUnit,
                    Title = item.Title,
                    State = QueueItemState.Queued,
                    Dependencies = Array.Empty<string>(),
                    BlockedBy = Array.Empty<string>(),
                    ClarificationReturnPath = string.Empty,
                    PacketPaths = new PacketPaths
                    {
                        Implementation = $".intent-cli/issues/{item.ExecutionUnit}/implementation.md",
                        ReviewContext = $".intent-cli/issues/{item.ExecutionUnit}/review-context.md",
                        Yaml = $".intent-cli/issues/{item.ExecutionUnit}/packet.yaml",
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = item.IssueRepo,
                        Number = item.IssueNumber,
                        Url = $"https://github.com/{item.IssueRepo}/issues/{item.IssueNumber}",
                    },
                    LinkedPr = item.LinkedPrUrl,
                    WorkerRole = "child-impl",
                    ReviewRole = "host-review",
                    Priority = "normal",
                }).ToArray(),
            };
            File.WriteAllText(QueueStatePath, QueueStateSerializer.Serialize(state));
        }

        public ReconcileWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("automation-reconcile-tests-").FullName;
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

        public void WriteInstalledCliScript(bool stalePrTransition)
        {
            var binPath = Path.Combine(RootPath, ".intent-cli", "bin");
            Directory.CreateDirectory(binPath);
            var scriptPath = Path.Combine(binPath, "intent-cli");
            // On Linux, overwriting an executable in-place with WriteAllText triggers
            // ETXTBSY (Text file busy) if the inode is still open for execution. Unlinking
            // the file first lets any running process keep its inode while the new file
            // gets a fresh one.
            if (!OperatingSystem.IsWindows() && File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
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
