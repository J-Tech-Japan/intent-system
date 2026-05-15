using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G356 / G358: Tests for <c>intent-cli automation closeout-drift-check</c>. Covers:
/// - Merged PR with non-Completed queue item → safe-repair.
/// - Unmerged PR → skipped (no drift).
/// - Multiple queue items sharing the same PR number → unsafe-stop.
/// - No queue-state present → empty result.
/// - --write: marks item Completed and appends pr-merged/closeout-recorded events.
/// - G358: linked_issue-only item + single merged closing PR → safe-repair, infer linked_pr.
/// - G358: linked_issue-only item + multiple closing PRs → unsafe-stop (ambiguous).
/// - G358: linked_issue-only item + no closing PRs → skipped.
/// - G358: linked_issue-only item + lookup failure → skipped with warning.
/// - G358: host-loop-next-action surfaces repair-host-metadata when drift repairs available.
/// - Diagnostics returns closeout-drift-repair instead of true-idle when count > 0.
/// </summary>
public sealed class AutomationCloseoutDriftCheckCommandTests : IDisposable
{
    public AutomationCloseoutDriftCheckCommandTests()
    {
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = null;
        AutomationCloseoutDriftCheckCommand.IssueLookupFactory = null;
        AutomationCloseoutDriftCheckCommand.UtcNowFactory =
            () => new DateTimeOffset(2026, 5, 15, 10, 0, 0, TimeSpan.Zero);
        // Reset all diagnostics-command static seams so tests that exercise the
        // full diagnostics command do not leak state into other test classes.
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = null;
        AutomationHostReviewDiagnosticsCommand.NestedProviderLauncher = null;
        AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = null;
        AutomationHostReviewDiagnosticsCommand.PublishRecoveryProbeFactory = null;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null;
    }

    public void Dispose()
    {
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = null;
        AutomationCloseoutDriftCheckCommand.IssueLookupFactory = null;
        AutomationCloseoutDriftCheckCommand.UtcNowFactory = null;
        // Restore diagnostics-command static seams after each test.
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = null;
        AutomationHostReviewDiagnosticsCommand.NestedProviderLauncher = null;
        AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = null;
        AutomationHostReviewDiagnosticsCommand.PublishRecoveryProbeFactory = null;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Basic drift detection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_MergedPrWithNonCompletedItem_ReturnsSafeRepair()
    {
        using var workspace = new DriftWorkspace();
        // linkedIssueNumber=815: the queue item is for issue #815
        workspace.WriteQueueState(BuildQueueState("G330", "review",
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/780",
            linkedIssueNumber: 815));
        // PR #780 is merged and closes issue #815 (matches the queue item)
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: true, closingIssueNumbers: [815]);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal("dry-run", result.Mode);
        Assert.Equal(1, result.SafeRepairCount);
        Assert.Equal(0, result.UnsafeStopCount);
        Assert.Equal(0, result.AppliedCount);

        var record = Assert.Single(result.Records);
        Assert.Equal("G330", record.ExecutionUnit);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ResultSafeRepair, record.Result);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ReasonPrMerged, record.ReasonCode);
        Assert.Equal(780, record.LinkedPrNumber);
        Assert.Equal(815, record.LinkedIssueNumber);
    }

    [Fact]
    public void Execute_UnmergedPr_ReturnsSkipped()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueState("G330", "review",
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/780"));
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: false, state: "OPEN");

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(0, result.SafeRepairCount);
        Assert.Equal(0, result.UnsafeStopCount);

        var record = Assert.Single(result.Records);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ResultSkipped, record.Result);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ReasonPrNotMerged, record.ReasonCode);
    }

    [Fact]
    public void Execute_AlreadyCompletedItem_NotIncludedInCandidates()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueState("G330", "completed",
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/780"));
        // PrLookup should never be called because completed items are excluded.
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: true);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(0, result.SafeRepairCount);
        Assert.Equal(0, result.UnsafeStopCount);
        Assert.Empty(result.Records);
    }

    [Fact]
    public void Execute_ItemWithNullLinkedPr_ReturnsEmpty()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueState("G330", "review", linkedPr: null));
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(0, merged: false);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        // No candidates because linked_pr is null.
        Assert.Equal(0, result.SafeRepairCount);
        Assert.Empty(result.Records);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Ambiguity guard (G356 unsafe-stop for multiple items sharing one PR)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_TwoItemsShareSamePrNumber_ReturnsUnsafeStop()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueStateWithTwoItems(
            "G330", "review", "https://github.com/J-Tech-Japan/intent-system/pull/780", linkedIssue1: 815,
            "G331", "active", "https://github.com/J-Tech-Japan/intent-system/pull/780", linkedIssue2: 816));
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: true, closingIssueNumbers: [815]);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode); // unsafe-stop causes non-zero exit
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(0, result.SafeRepairCount);
        Assert.Equal(2, result.UnsafeStopCount);

        Assert.All(result.Records, r =>
        {
            Assert.Equal(AutomationCloseoutDriftCheckCommand.ResultUnsafeStop, r.Result);
            Assert.Equal(AutomationCloseoutDriftCheckCommand.ReasonAmbiguousMapping, r.ReasonCode);
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // No queue-state
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_NoQueueState_ReturnsEmptyAndExitsZero()
    {
        using var workspace = new DriftWorkspace();
        // No queue-state written.
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(0, merged: false);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(0, result.SafeRepairCount);
        Assert.Equal(0, result.UnsafeStopCount);
        Assert.Empty(result.Records);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Write path: marks queue item Completed + appends run events
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_WriteWithMergedPr_MarksQueueItemCompleted()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueState("G330", "review",
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/780",
            linkedIssueNumber: 815));
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: true, closingIssueNumbers: [815]);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal("write", result.Mode);
        Assert.Equal(1, result.SafeRepairCount);
        Assert.Equal(1, result.AppliedCount);

        // Queue item must now be Completed.
        var queueOnDisk = workspace.QueueStateOnDisk();
        Assert.Contains("\"state\": \"completed\"", queueOnDisk, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_WriteWithMergedPr_AppendsPrMergedAndCloseoutRecordedEvents()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueState("G330", "review",
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/780",
            linkedIssueNumber: 815));
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: true, closingIssueNumbers: [815]);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);

        var runsLines = workspace.RunsLines();
        Assert.Equal(2, runsLines.Length);

        // Both events must be deserializable via RunLogSerializer.
        var event0 = RunLogSerializer.DeserializeLine(runsLines[0]);
        var event1 = RunLogSerializer.DeserializeLine(runsLines[1]);

        Assert.Equal("pr-merged", event0.Event);
        Assert.Equal("closeout-recorded", event1.Event);

        Assert.Equal("G330", event0.ExecutionUnit);
        Assert.Equal("G330", event1.ExecutionUnit);

        Assert.Equal("J-Tech-Japan/intent-system", event0.Repo);
        Assert.Equal(780, event0.Pr);

        Assert.Contains("closeout-drift-check", event0.By, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_WriteWithUnmergedPr_DoesNotMutateQueueOrRunsLog()
    {
        using var workspace = new DriftWorkspace();
        var originalQueueText = BuildQueueState("G330", "review",
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/780");
        workspace.WriteQueueState(originalQueueText);
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: false, state: "OPEN");

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(0, result.AppliedCount);

        // Queue-state must be unchanged.
        Assert.Equal(originalQueueText, workspace.QueueStateOnDisk());
        // No runs.jsonl created.
        Assert.Empty(workspace.RunsLines());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Closing-issue linkage verification (G356 AC)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_MergedPrWithNoLinkedIssueOnQueueItem_ReturnsUnsafeStop()
    {
        using var workspace = new DriftWorkspace();
        // Queue item has no linked_issue (null) → cannot verify closing-issue linkage
        workspace.WriteQueueState(BuildQueueState("G330", "review",
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/780",
            linkedIssueNumber: null));
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: true, closingIssueNumbers: [815]);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(0, result.SafeRepairCount);
        Assert.Equal(1, result.UnsafeStopCount);

        var record = Assert.Single(result.Records);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ResultUnsafeStop, record.Result);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ReasonMissingClosingIssueLinkage, record.ReasonCode);
    }

    [Fact]
    public void Execute_MergedPrWithNoClosingIssues_ReturnsUnsafeStop()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueState("G330", "review",
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/780",
            linkedIssueNumber: 815));
        // PR is merged but GitHub reports no closing-issue references
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: true, closingIssueNumbers: []);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(0, result.SafeRepairCount);
        Assert.Equal(1, result.UnsafeStopCount);

        var record = Assert.Single(result.Records);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ResultUnsafeStop, record.Result);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ReasonMissingClosingIssueLinkage, record.ReasonCode);
    }

    [Fact]
    public void Execute_MergedPrWithClosingIssueMismatch_ReturnsUnsafeStop()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueState("G330", "review",
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/780",
            linkedIssueNumber: 815));
        // PR is merged but closes a different issue (#999), not #815
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: true, closingIssueNumbers: [999]);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(0, result.SafeRepairCount);
        Assert.Equal(1, result.UnsafeStopCount);

        var record = Assert.Single(result.Records);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ResultUnsafeStop, record.Result);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ReasonClosingIssueMismatch, record.ReasonCode);
    }

    [Fact]
    public void Execute_MixedSafeAndUnsafe_WriteBlockedReturnsNonZero()
    {
        // Two items with DIFFERENT PR numbers:
        // item1: PR #780, linked issue #815, PR merged and closes #815 → safe-repair candidate
        // item2: PR #781, linked issue #820, PR merged but closes nothing → missing-closing-issue-linkage → unsafe-stop
        // --write must NOT mutate queue-state when any unsafe stop is present.
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueStateWithTwoItems(
            "G330", "review",
            "https://github.com/J-Tech-Japan/intent-system/pull/780", linkedIssue1: 815,
            "G331", "review",
            "https://github.com/J-Tech-Japan/intent-system/pull/781", linkedIssue2: 820));
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () => new FakeDictPrLookup(
            new Dictionary<int, (bool Merged, int[] Closing)>
            {
                [780] = (true, [815]),   // safe-repair: merged + closes matching issue
                [781] = (true, []),       // unsafe-stop: merged but no closing issues
            });

        var originalQueueText = workspace.QueueStateOnDisk();
        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        // Must return non-zero and NOT mutate queue-state.
        Assert.Equal(1, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(0, result.AppliedCount);
        Assert.Equal(1, result.SafeRepairCount);
        Assert.Equal(1, result.UnsafeStopCount);
        Assert.Equal("write", result.Mode);
        // Queue must be unchanged (fail-closed: no mutations when any unsafe stop present).
        Assert.Equal(originalQueueText, workspace.QueueStateOnDisk());
        // No runs.jsonl created.
        Assert.Empty(workspace.RunsLines());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Diagnostics integration: closeout-drift-repair classification
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyzer_WithCloseoutDriftRepairsAvailable_ClassifiesCloseoutDriftRepairNotTrueIdle()
    {
        // Snapshot: diagnostics returns closeout-drift-repair instead of
        // true-idle when closeoutDriftRepairsAvailable > 0 and no other
        // terminal class applies.
        var result = AutomationHostReviewDiagnosticsAnalyzer.Analyze(
            repo: "J-Tech-Japan/intent-system",
            openPrs: Array.Empty<GitHubAutomationPrCandidate>(),
            publishedIntentTargetIssues: Array.Empty<GitHubAutomationIssueCandidate>(),
            clarificationRequired: false,
            candidateExecutionUnit: null,
            closeoutDriftRepairsAvailable: 1);

        Assert.Equal(
            AutomationHostReviewDiagnosticsClassifications.CloseoutDriftRepair,
            result.Classification);
        Assert.True(result.SafeRepairAvailable);
        Assert.Equal(SafeRepairCategories.CloseoutDriftRepair, result.SafeRepairCategory);
        Assert.NotNull(result.RecommendedNextCommand);
        Assert.Contains("closeout-drift-check", result.RecommendedNextCommand!, StringComparison.Ordinal);
        Assert.Contains("--write", result.RecommendedNextCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyzer_CloseoutDriftRepairPrecedesTrueIdle_WhenNoOtherBlockers()
    {
        // With zero drift repairs, should return true-idle.
        var idleResult = AutomationHostReviewDiagnosticsAnalyzer.Analyze(
            repo: "J-Tech-Japan/intent-system",
            openPrs: Array.Empty<GitHubAutomationPrCandidate>(),
            publishedIntentTargetIssues: Array.Empty<GitHubAutomationIssueCandidate>(),
            clarificationRequired: false,
            candidateExecutionUnit: null,
            closeoutDriftRepairsAvailable: 0);
        Assert.Equal(AutomationHostReviewDiagnosticsClassifications.TrueIdle, idleResult.Classification);

        // With one drift repair, should return closeout-drift-repair.
        var driftResult = AutomationHostReviewDiagnosticsAnalyzer.Analyze(
            repo: "J-Tech-Japan/intent-system",
            openPrs: Array.Empty<GitHubAutomationPrCandidate>(),
            publishedIntentTargetIssues: Array.Empty<GitHubAutomationIssueCandidate>(),
            clarificationRequired: false,
            candidateExecutionUnit: null,
            closeoutDriftRepairsAvailable: 1);
        Assert.Equal(
            AutomationHostReviewDiagnosticsClassifications.CloseoutDriftRepair,
            driftResult.Classification);
    }

    [Fact]
    public void DiagnosticsCommand_WithCloseoutDriftRepairsAvailableFlag_ClassifiesCloseoutDriftRepair()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteInstalledCliScript();
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null; // use real check or stub
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeEmptyLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system",
             "--closeout-drift-repairs-available", "2",
             "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal(AutomationHostReviewDiagnosticsClassifications.CloseoutDriftRepair, result.Classification);
        Assert.True(result.SafeRepairAvailable);
        Assert.Equal(SafeRepairCategories.CloseoutDriftRepair, result.SafeRepairCategory);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Argument validation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_MissingRepo_ReturnsUsageError()
    {
        using var workspace = new DriftWorkspace();
        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context, [], writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("--repo is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ReturnsUsageError()
    {
        using var workspace = new DriftWorkspace();
        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--unknown-flag"],
            writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var workspace = new DriftWorkspace();
        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context, ["--help"], writer);
        Assert.Equal(0, exitCode);
        Assert.Contains("closeout-drift-check", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_InvalidFormatValue_ReturnsError()
    {
        using var workspace = new DriftWorkspace();
        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "xml"],
            writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'json' or 'text'", writer.ToString(), StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Text format smoke test
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_TextFormat_PrintsReadableOutput()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueState("G330", "review",
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/780",
            linkedIssueNumber: 815));
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: true, closingIssueNumbers: [815]);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("closeout-drift-check", output, StringComparison.Ordinal);
        Assert.Contains("Safe repairs", output, StringComparison.Ordinal);
        Assert.Contains("G330", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Negative test: PR number in URL as numeric string
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_LinkedPrAsRawNumber_ParsesAndChecks()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueState("G330", "review", linkedPr: "780", linkedIssueNumber: 815));
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: true, closingIssueNumbers: [815]);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(1, result.SafeRepairCount);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SafeRepairCategories constant
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SafeRepairCategories_CloseoutDriftRepair_HasExpectedValue()
    {
        Assert.Equal("closeout-drift-repair", SafeRepairCategories.CloseoutDriftRepair);
    }

    [Fact]
    public void AutomationHostReviewDiagnosticsClassifications_CloseoutDriftRepair_HasExpectedValue()
    {
        Assert.Equal("closeout-drift-repair",
            AutomationHostReviewDiagnosticsClassifications.CloseoutDriftRepair);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // G358: linked_issue-only pass (no linked_pr in queue-state)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_G358_LinkedIssueOnly_SingleMergedClosingPr_ReturnsSafeRepair()
    {
        // G358: queue item has linked_issue but no linked_pr; GitHub reports exactly
        // one merged PR that closed the issue → safe-repair with inferred linked_pr.
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueStateLinkedIssueOnly("G819", "review", linkedIssueNumber: 819));
        AutomationCloseoutDriftCheckCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(819, closingPrs: [(820, true, "J-Tech-Japan", "intent-system")]);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(1, result.SafeRepairCount);
        Assert.Equal(0, result.UnsafeStopCount);
        var record = Assert.Single(result.Records);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ResultSafeRepair, record.Result);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ReasonLinkedIssuePrInferred, record.ReasonCode);
        Assert.Equal(820, record.LinkedPrNumber);
        Assert.Equal(819, record.LinkedIssueNumber);
        Assert.NotNull(record.InferredLinkedPrUrl);
        Assert.Contains("820", record.InferredLinkedPrUrl!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G358_LinkedIssueOnly_NoClosingPr_ReturnsSkipped()
    {
        // G358: queue item has linked_issue but no linked_pr; GitHub reports no
        // merged closing PR → skipped (issue may not be closed yet).
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueStateLinkedIssueOnly("G819", "review", linkedIssueNumber: 819));
        AutomationCloseoutDriftCheckCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(819, closingPrs: []);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(0, result.SafeRepairCount);
        Assert.Equal(0, result.UnsafeStopCount);
        var record = Assert.Single(result.Records);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ResultSkipped, record.Result);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ReasonLinkedIssueNoMergedClosingPr, record.ReasonCode);
    }

    [Fact]
    public void Execute_G358_LinkedIssueOnly_MultipleClosingPrs_ReturnsUnsafeStop()
    {
        // G358: queue item has linked_issue but no linked_pr; GitHub reports two
        // merged PRs that closed the issue → unsafe-stop (ambiguous mapping).
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueStateLinkedIssueOnly("G819", "review", linkedIssueNumber: 819));
        AutomationCloseoutDriftCheckCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(819, closingPrs:
            [
                (820, true, "J-Tech-Japan", "intent-system"),
                (821, true, "J-Tech-Japan", "intent-system"),
            ]);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(0, result.SafeRepairCount);
        Assert.Equal(1, result.UnsafeStopCount);
        var record = Assert.Single(result.Records);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ResultUnsafeStop, record.Result);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ReasonLinkedIssueAmbiguousClosingPrs, record.ReasonCode);
    }

    [Fact]
    public void Execute_G358_LinkedIssueOnly_LookupFailed_ReturnsSkippedWithWarning()
    {
        // G358: issue lookup throws → skipped with a warning.
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueStateLinkedIssueOnly("G819", "review", linkedIssueNumber: 819));
        AutomationCloseoutDriftCheckCommand.IssueLookupFactory = () =>
            new FakeFailingIssueLookup("gh api graphql returned error 403");

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode); // warnings cause non-zero exit
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(0, result.SafeRepairCount);
        Assert.Equal(0, result.UnsafeStopCount);
        var record = Assert.Single(result.Records);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ResultSkipped, record.Result);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ReasonLinkedIssueLookupFailed, record.ReasonCode);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Execute_G358_CrossRepoPrFiltered_NoSameRepoMergedPr_Skipped()
    {
        // G358: closing PR belongs to a different repo → filtered out; result is skipped.
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueStateLinkedIssueOnly("G819", "review", linkedIssueNumber: 819));
        AutomationCloseoutDriftCheckCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(819, closingPrs:
            [
                (820, true, "OtherOwner", "other-repo"), // cross-repo — must be filtered out
            ]);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(0, result.SafeRepairCount);
        Assert.Equal(0, result.UnsafeStopCount);
        var record = Assert.Single(result.Records);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ResultSkipped, record.Result);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ReasonLinkedIssueNoMergedClosingPr, record.ReasonCode);
    }

    [Fact]
    public void Execute_G358_WriteWithInferredPr_PopulatesLinkedPrAndMarksCompleted()
    {
        // G358 write path: linked_pr is inferred and written into queue-state
        // alongside the state=Completed transition.
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueStateLinkedIssueOnly("G819", "review", linkedIssueNumber: 819));
        AutomationCloseoutDriftCheckCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(819, closingPrs: [(820, true, "J-Tech-Japan", "intent-system")]);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(1, result.SafeRepairCount);
        Assert.Equal(1, result.AppliedCount);

        // Queue-state must have linked_pr set to inferred URL and state=completed.
        var queueOnDisk = workspace.QueueStateOnDisk();
        Assert.Contains("pull/820", queueOnDisk, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"state\": \"completed\"", queueOnDisk, StringComparison.OrdinalIgnoreCase);

        // Run events must be appended.
        var runsLines = workspace.RunsLines();
        Assert.Equal(2, runsLines.Length);
        var event0 = RunLogSerializer.DeserializeLine(runsLines[0]);
        var event1 = RunLogSerializer.DeserializeLine(runsLines[1]);
        Assert.Equal("pr-merged", event0.Event);
        Assert.Equal("closeout-recorded", event1.Event);
        Assert.Equal("G819", event0.ExecutionUnit);
        Assert.Equal(820, event0.Pr);
    }

    [Fact]
    public void Execute_G358_CompletedItemExcluded_NoRepair()
    {
        // G358: item in state=completed is excluded from the second pass.
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueStateLinkedIssueOnly("G819", "completed", linkedIssueNumber: 819));
        AutomationCloseoutDriftCheckCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(819, closingPrs: [(820, true, "J-Tech-Japan", "intent-system")]);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(0, result.SafeRepairCount);
        Assert.Empty(result.Records);
    }

    [Fact]
    public void Analyzer_G358_CloseoutDriftRepairsAvailable_SurfacesRepairHostMetadata()
    {
        // G358: HostLoopNextActionAnalyzer emits repair-host-metadata with the
        // closeout-drift-check --write recommendation when CloseoutDriftRepairsAvailable > 0.
        var result = HostLoopNextActionAnalyzer.Analyze(new HostLoopNextActionInput
        {
            Repo = "J-Tech-Japan/intent-system",
            CloseoutDriftRepairsAvailable = 1,
        });

        Assert.Equal(HostLoopNextActionAnalyzer.ClassificationRepairHostMetadata, result.Classification);
        Assert.True(result.MutationAllowed);
        Assert.NotNull(result.RecommendedCommand);
        Assert.Contains("closeout-drift-check", result.RecommendedCommand!, StringComparison.Ordinal);
        Assert.Contains("--write", result.RecommendedCommand!, StringComparison.Ordinal);
        Assert.Contains(result.Evidence, e => e.Contains("G358", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyzer_G358_CloseoutDriftRepairsAvailable_PrecedesTrueIdle()
    {
        // G358: with zero drift repairs → true-idle; with one → repair-host-metadata.
        var idleResult = HostLoopNextActionAnalyzer.Analyze(new HostLoopNextActionInput
        {
            Repo = "J-Tech-Japan/intent-system",
            CloseoutDriftRepairsAvailable = 0,
        });
        Assert.Equal(HostLoopNextActionAnalyzer.ClassificationTrueIdle, idleResult.Classification);

        var repairResult = HostLoopNextActionAnalyzer.Analyze(new HostLoopNextActionInput
        {
            Repo = "J-Tech-Japan/intent-system",
            CloseoutDriftRepairsAvailable = 1,
        });
        Assert.Equal(HostLoopNextActionAnalyzer.ClassificationRepairHostMetadata, repairResult.Classification);
    }

    [Fact]
    public void Execute_G358_BothPass1AndPass2_BothRepairsDetected()
    {
        // G358: a queue with one pass-1 item (has linked_pr) and one pass-2 item
        // (has linked_issue only) → both safe-repair records are emitted.
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueStateMixed(
            unit1: "G330", state1: "review",
            linkedPr1: "https://github.com/J-Tech-Japan/intent-system/pull/780", linkedIssue1: 815,
            unit2: "G819", state2: "review",
            linkedIssue2: 819));
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: true, closingIssueNumbers: [815]);
        AutomationCloseoutDriftCheckCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(819, closingPrs: [(820, true, "J-Tech-Japan", "intent-system")]);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(2, result.SafeRepairCount);
        Assert.Equal(0, result.UnsafeStopCount);
        Assert.Equal(2, result.Records.Count);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helper types
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// G358: build a queue-state JSON where the item has <c>linked_issue</c>
    /// but NO <c>linked_pr</c> (the canonical G358 candidate shape).
    /// </summary>
    private static string BuildQueueStateLinkedIssueOnly(
        string executionUnit,
        string state,
        int linkedIssueNumber)
    {
        return $$"""
            {
              "schema_version": "1",
              "updated_at": "2026-05-15T10:00:00Z",
              "items": [
                {
                  "execution_unit": "{{executionUnit}}",
                  "title": "title",
                  "state": "{{state}}",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": null,
                  "linked_issue": {"repo": "J-Tech-Japan/intent-system", "number": {{linkedIssueNumber}}, "url": "https://github.com/J-Tech-Japan/intent-system/issues/{{linkedIssueNumber}}"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """;
    }

    /// <summary>
    /// G358: build a queue-state JSON with two items — one pass-1 item (has
    /// <c>linked_pr</c>) and one pass-2 item (has <c>linked_issue</c> only,
    /// no <c>linked_pr</c>).
    /// </summary>
    private static string BuildQueueStateMixed(
        string unit1, string state1, string linkedPr1, int? linkedIssue1,
        string unit2, string state2, int linkedIssue2)
    {
        var linkedIssueBlock1 = linkedIssue1.HasValue
            ? $"\"linked_issue\": {{\"repo\": \"J-Tech-Japan/intent-system\", \"number\": {linkedIssue1.Value}}},"
            : "\"linked_issue\": null,";
        return $$"""
            {
              "schema_version": "1",
              "updated_at": "2026-05-15T10:00:00Z",
              "items": [
                {
                  "execution_unit": "{{unit1}}",
                  "title": "pass1 item",
                  "state": "{{state1}}",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": "{{linkedPr1}}",
                  {{linkedIssueBlock1}}
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "{{unit2}}",
                  "title": "pass2 item",
                  "state": "{{state2}}",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": null,
                  "linked_issue": {"repo": "J-Tech-Japan/intent-system", "number": {{linkedIssue2}}, "url": "https://github.com/J-Tech-Japan/intent-system/issues/{{linkedIssue2}}"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """;
    }

    private static string BuildQueueState(
        string executionUnit,
        string state,
        string? linkedPr,
        int? linkedIssueNumber = null)
    {
        var linked = linkedPr is null ? "null" : $"\"{linkedPr}\"";
        var linkedIssueBlock = linkedIssueNumber.HasValue
            ? $$"""
              "linked_issue": {"repo": "J-Tech-Japan/intent-system", "number": {{linkedIssueNumber.Value}}, "url": "https://github.com/J-Tech-Japan/intent-system/issues/{{linkedIssueNumber.Value}}"},
              """
            : "\"linked_issue\": null,\n              ";
        return $$"""
            {
              "schema_version": "1",
              "updated_at": "2026-05-15T10:00:00Z",
              "items": [
                {
                  "execution_unit": "{{executionUnit}}",
                  "title": "title",
                  "state": "{{state}}",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": {{linked}},
                  {{linkedIssueBlock}}"worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """;
    }

    private static string BuildQueueStateWithTwoItems(
        string unit1, string state1, string linkedPr1, int? linkedIssue1,
        string unit2, string state2, string linkedPr2, int? linkedIssue2)
    {
        var linkedIssueBlock1 = linkedIssue1.HasValue
            ? $"\"linked_issue\": {{\"repo\": \"J-Tech-Japan/intent-system\", \"number\": {linkedIssue1.Value}}},"
            : "\"linked_issue\": null,";
        var linkedIssueBlock2 = linkedIssue2.HasValue
            ? $"\"linked_issue\": {{\"repo\": \"J-Tech-Japan/intent-system\", \"number\": {linkedIssue2.Value}}},"
            : "\"linked_issue\": null,";
        return $$"""
            {
              "schema_version": "1",
              "updated_at": "2026-05-15T10:00:00Z",
              "items": [
                {
                  "execution_unit": "{{unit1}}",
                  "title": "first",
                  "state": "{{state1}}",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": "{{linkedPr1}}",
                  {{linkedIssueBlock1}}
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "{{unit2}}",
                  "title": "second",
                  "state": "{{state2}}",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": "{{linkedPr2}}",
                  {{linkedIssueBlock2}}
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """;
    }

    private sealed class FakePrLookup : IGitHubPrLookup
    {
        private readonly int prNumber;
        private readonly bool merged;
        private readonly string state;
        private readonly IReadOnlyList<int> closingIssueNumbers;

        /// <param name="prNumber">Expected PR number (throws on mismatch).</param>
        /// <param name="merged">Whether the PR is merged.</param>
        /// <param name="state">GitHub state string; ignored when merged=true (forced to MERGED).</param>
        /// <param name="closingIssueNumbers">Issue numbers to report in ClosingIssuesReferences.</param>
        public FakePrLookup(
            int prNumber,
            bool merged,
            string state = "MERGED",
            int[]? closingIssueNumbers = null)
        {
            this.prNumber = prNumber;
            this.merged = merged;
            this.state = merged ? "MERGED" : state;
            this.closingIssueNumbers = closingIssueNumbers ?? Array.Empty<int>();
        }

        public GitHubPrLookupResult Lookup(string repo, int number)
        {
            if (number != prNumber)
            {
                throw new InvalidOperationException(
                    $"FakePrLookup: unexpected PR number {number} (expected {prNumber})");
            }
            return new GitHubPrLookupResult
            {
                Number = number,
                State = this.state,
                Merged = merged,
                ClosingIssuesReferences = closingIssueNumbers
                    .Select(n => new GitHubPrClosingIssueReference { Number = n })
                    .ToArray(),
            };
        }
    }

    /// <summary>
    /// Multi-PR variant of the fake lookup: accepts a dictionary of
    /// prNumber → (merged, closingIssueNumbers) to support tests that
    /// probe more than one PR number in a single Execute call.
    /// </summary>
    private sealed class FakeDictPrLookup : IGitHubPrLookup
    {
        private readonly Dictionary<int, (bool Merged, int[] Closing)> entries;

        public FakeDictPrLookup(Dictionary<int, (bool Merged, int[] Closing)> entries)
        {
            this.entries = entries;
        }

        public GitHubPrLookupResult Lookup(string repo, int number)
        {
            if (!entries.TryGetValue(number, out var entry))
            {
                throw new InvalidOperationException(
                    $"FakeDictPrLookup: unexpected PR number {number}");
            }
            return new GitHubPrLookupResult
            {
                Number = number,
                State = entry.Merged ? "MERGED" : "OPEN",
                Merged = entry.Merged,
                ClosingIssuesReferences = entry.Closing
                    .Select(n => new GitHubPrClosingIssueReference { Number = n })
                    .ToArray(),
            };
        }
    }

    private sealed class FakeEmptyLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo, IReadOnlyCollection<string> requiredLabels)
            => Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo, IReadOnlyCollection<string> requiredLabels)
            => Array.Empty<GitHubAutomationIssueCandidate>();
    }

    /// <summary>
    /// G358: fake <see cref="IGitHubIssueClosingPrLookup"/> for tests. Returns a
    /// canned list of closing PR refs. Each entry is (prNumber, merged, repoOwner, repoName).
    /// </summary>
    private sealed class FakeIssueLookup : IGitHubIssueClosingPrLookup
    {
        private readonly int issueNumber;
        private readonly IReadOnlyList<(int Pr, bool Merged, string Owner, string Repo)> closingPrs;

        public FakeIssueLookup(
            int issueNumber,
            IReadOnlyList<(int Pr, bool Merged, string Owner, string Repo)> closingPrs)
        {
            this.issueNumber = issueNumber;
            this.closingPrs = closingPrs;
        }

        public GitHubIssueClosingPrLookupResult Lookup(string repo, int number)
        {
            if (number != issueNumber)
            {
                throw new InvalidOperationException(
                    $"FakeIssueLookup: unexpected issue number {number} (expected {issueNumber})");
            }
            return new GitHubIssueClosingPrLookupResult
            {
                IssueNumber = number,
                State = closingPrs.Any(p => p.Merged) ? "CLOSED" : "OPEN",
                ClosingPullRequests = closingPrs
                    .Select(p => new GitHubIssueClosingPrRef
                    {
                        Number = p.Pr,
                        State = p.Merged ? "MERGED" : "OPEN",
                        Merged = p.Merged,
                        BaseRefName = "main",
                        RepoOwner = p.Owner,
                        RepoName = p.Repo,
                    })
                    .ToArray()
            };
        }
    }

    /// <summary>G358: fake that always throws, simulating a gh lookup failure.</summary>
    private sealed class FakeFailingIssueLookup : IGitHubIssueClosingPrLookup
    {
        private readonly string message;
        public FakeFailingIssueLookup(string message) { this.message = message; }

        public GitHubIssueClosingPrLookupResult Lookup(string repo, int issueNumber) =>
            throw new InvalidOperationException(message);
    }

    private sealed class DriftWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("closeout-drift-check-tests-")
            .FullName;

        public DriftWorkspace()
        {
            Directory.CreateDirectory(Path.Combine(rootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = rootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    }
                }
            };
        }

        public CliContext Context { get; }

        public void WriteQueueState(string content) =>
            File.WriteAllText(Context.GetQueueStatePath(), content);

        public string QueueStateOnDisk() =>
            File.ReadAllText(Context.GetQueueStatePath());

        public string[] RunsLines() => File.Exists(Context.GetRunLogPath())
            ? File.ReadAllLines(Context.GetRunLogPath())
            : Array.Empty<string>();

        /// <summary>
        /// Write a minimal installed-cli script so
        /// <see cref="AutomationInstalledCliSurfaceProbe"/> passes for tests
        /// that exercise the full diagnostics command.
        /// </summary>
        public void WriteInstalledCliScript()
        {
            var binPath = Path.Combine(rootPath, ".intent-cli", "bin");
            Directory.CreateDirectory(binPath);
            var scriptPath = Path.Combine(binPath, "intent-cli");
            File.WriteAllText(
                scriptPath,
                "#!/bin/sh\n"
                + "case \"$*\" in\n"
                + "  'automation summary') echo '--domain is required.'; exit 1 ;;\n"
                + "  'automation host-review-preflight') echo '--repo is required.'; exit 1 ;;\n"
                + "  'automation issue-publish') echo '--issue is required.'; exit 1 ;;\n"
                + "  'automation pr-transition') echo '--transition is required (review-start, request-update, or approved).'; exit 1 ;;\n"
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
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
