using System.Security.Cryptography;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G205: Tests for <c>intent-cli worker result-summary</c>. Cover every
/// outcome named in the issue Acceptance Criteria, the status mapping, the
/// advisory label-cleanup actions, the warnings, the JSON shape, the
/// camelCase alias, and the no-mutation invariants.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class WorkerResultSummaryCommandTests : IDisposable
{
    public WorkerResultSummaryCommandTests()
    {
        WorkerResultSummaryCommand.NestedProviderLauncher = null;
        WorkerResultSummaryCommand.IssueLookupFactory = () => new NoEvidenceIssueLookup();
    }

    public void Dispose()
    {
        WorkerResultSummaryCommand.NestedProviderLauncher = null;
        WorkerResultSummaryCommand.IssueLookupFactory = null;
    }

    [Fact]
    public void Execute_GivenIssueToPrPrCreated_EmitsCompletedStatusAndIssueLabelActions()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--repo", "J-Tech-Japan/intent-system",
                "--issue", "515",
                "--pr", "999",
                "--outcome", "pr-created",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerResultSummaryResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.Equal(WorkerResultSummaryConstants.Kinds.IssueToPr, result!.Kind);
        Assert.Equal("J-Tech-Japan/intent-system", result.Repo);
        Assert.Equal(515, result.Issue);
        Assert.Equal(999, result.Pr);
        Assert.Equal(WorkerResultSummaryConstants.Outcomes.PrCreated, result.Outcome);
        Assert.Equal(WorkerResultSummaryConstants.Statuses.Completed, result.Status);

        // Recommended actions: remove in-progress on the issue, add intent-pr-created on the issue.
        Assert.Contains(result.RecommendedLabelActions, a =>
            a.Action == "remove" && a.Target == "issue"
            && a.Label == WorkerResultSummaryConstants.Labels.IntentIssueInProgress);
        Assert.Contains(result.RecommendedLabelActions, a =>
            a.Action == "add" && a.Target == "issue"
            && a.Label == WorkerResultSummaryConstants.Labels.IntentPrCreated);

        // Policy warning: intent-pr-created belongs on the issue, not the PR.
        Assert.Contains(result.Warnings, w =>
            w.Contains(WorkerResultSummaryConstants.Labels.IntentPrCreated, StringComparison.Ordinal)
            && w.Contains("source issue", StringComparison.Ordinal));

        Assert.Contains("PR #999", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenPrCommentFixRepairPushed_EmitsSwapActionToRereviewReady()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "pr-comment-fix",
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "514",
                "--outcome", "repair-pushed",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerResultSummaryResult>(writer.ToString())!;
        Assert.Equal(WorkerResultSummaryConstants.Statuses.Completed, result.Status);

        // Primary swap action: update-in-progress -> rereview-ready on the PR.
        Assert.Contains(result.RecommendedLabelActions, a =>
            a.Action == "swap"
            && a.Target == "pr"
            && a.Label.Contains(WorkerResultSummaryConstants.Labels.IntentPrUpdateInProgress, StringComparison.Ordinal)
            && a.Label.Contains(WorkerResultSummaryConstants.Labels.IntentPrRereviewReady, StringComparison.Ordinal));
        Assert.Contains(result.RecommendedLabelActions, a =>
            a.Action == "remove"
            && a.Target == "pr"
            && a.Label == WorkerResultSummaryConstants.Labels.IntentPrRequestUpdate);

        // No spurious "remove in-progress" action — the swap covers it.
        Assert.DoesNotContain(result.RecommendedLabelActions, a =>
            a.Action == "remove"
            && a.Label == WorkerResultSummaryConstants.Labels.IntentPrUpdateInProgress);
    }

    [Fact]
    public void Execute_GivenPrCommentFixAlreadyResolved_EmitsSwapActionToRereviewReady()
    {
        // G372: already-resolved on a pr-comment-fix now recommends the
        // SAME convergent label actions as repair-pushed (swap
        // update-in-progress -> rereview-ready, remove request-update) so
        // an agent that reports "fix already present" hands the PR back to
        // host re-review instead of leaving request-update behind for the
        // selector to re-pick.
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "pr-comment-fix",
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "842",
                "--outcome", "already-resolved",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerResultSummaryResult>(writer.ToString())!;
        Assert.Equal(WorkerResultSummaryConstants.Statuses.Completed, result.Status);
        Assert.Contains("ready for re-review", result.Summary, StringComparison.Ordinal);

        Assert.Contains(result.RecommendedLabelActions, a =>
            a.Action == "swap"
            && a.Target == "pr"
            && a.Label.Contains(WorkerResultSummaryConstants.Labels.IntentPrUpdateInProgress, StringComparison.Ordinal)
            && a.Label.Contains(WorkerResultSummaryConstants.Labels.IntentPrRereviewReady, StringComparison.Ordinal));
        Assert.Contains(result.RecommendedLabelActions, a =>
            a.Action == "remove"
            && a.Target == "pr"
            && a.Label == WorkerResultSummaryConstants.Labels.IntentPrRequestUpdate);

        // No spurious standalone "remove in-progress" action — the swap covers it.
        Assert.DoesNotContain(result.RecommendedLabelActions, a =>
            a.Action == "remove"
            && a.Label == WorkerResultSummaryConstants.Labels.IntentPrUpdateInProgress);
    }

    [Fact]
    public void Execute_GivenIssueToPrDeclinedContractIncomplete_EmitsDeclinedStatus()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--repo", "J-Tech-Japan/intent-system",
                "--issue", "999",
                "--outcome", "declined-contract-incomplete",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerResultSummaryResult>(writer.ToString())!;
        Assert.Equal(WorkerResultSummaryConstants.Statuses.Declined, result.Status);

        // Declined runs still recommend removing the worker's claim label.
        Assert.Contains(result.RecommendedLabelActions, a =>
            a.Action == "remove" && a.Target == "issue"
            && a.Label == WorkerResultSummaryConstants.Labels.IntentIssueInProgress);
    }

    [Fact]
    public void Execute_GivenClarificationRequiredForIssueKind_EmitsClarificationStatus()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--repo", "J-Tech-Japan/intent-system",
                "--issue", "999",
                "--outcome", "clarification-required",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerResultSummaryResult>(writer.ToString())!;
        Assert.Equal(WorkerResultSummaryConstants.Statuses.ClarificationRequired, result.Status);
        Assert.Contains("Clarification required", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenAlreadyResolvedForIssueKind_EmitsCompletedStatus()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--repo", "J-Tech-Japan/intent-system",
                "--issue", "999",
                "--outcome", "already-resolved",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerResultSummaryResult>(writer.ToString())!;
        Assert.Equal(WorkerResultSummaryConstants.Statuses.Completed, result.Status);
        Assert.Contains("Already resolved", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenNoActionableCommentsForPrFix_EmitsCompletedStatus()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "pr-comment-fix",
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "514",
                "--outcome", "no-actionable-comments",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerResultSummaryResult>(writer.ToString())!;
        Assert.Equal(WorkerResultSummaryConstants.Statuses.Completed, result.Status);

        // PR claim label release recommendation is present for non-repair-pushed terminal outcomes.
        Assert.Contains(result.RecommendedLabelActions, a =>
            a.Action == "remove" && a.Target == "pr"
            && a.Label == WorkerResultSummaryConstants.Labels.IntentPrUpdateInProgress);
    }

    [Fact]
    public void Execute_GivenFailedOutcome_EmitsFailedStatus()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--repo", "J-Tech-Japan/intent-system",
                "--issue", "999",
                "--outcome", "failed",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerResultSummaryResult>(writer.ToString())!;
        Assert.Equal(WorkerResultSummaryConstants.Statuses.Failed, result.Status);
    }

    [Fact]
    public void Execute_GivenLabelCleanupRequired_EmitsCleanupStatusAndWarning()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "pr-comment-fix",
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "514",
                "--outcome", "label-cleanup-required",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerResultSummaryResult>(writer.ToString())!;
        Assert.Equal(WorkerResultSummaryConstants.Statuses.LabelCleanupRequired, result.Status);
        Assert.Contains(result.Warnings, w =>
            w.Contains("label-cleanup-required", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenUnsupportedKindOutcomeCombo_ReturnsNonZero()
    {
        // pr-created is only valid for kind=issue-to-pr.
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "pr-comment-fix",
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "514",
                "--outcome", "pr-created",
                "--format", "json"
            },
            writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("not supported for kind", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownOutcome_ReturnsNonZero()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--repo", "J-Tech-Japan/intent-system",
                "--issue", "999",
                "--outcome", "made-up-outcome",
                "--format", "json"
            },
            writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("unrecognized outcome", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownKind_ReturnsNonZero()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "made-up-kind",
                "--repo", "J-Tech-Japan/intent-system",
                "--outcome", "pr-created",
                "--format", "json"
            },
            writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("unrecognized kind", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingKind_ReturnsNonZero()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--outcome", "pr-created"
            },
            writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--kind is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NonNumericIssue_ReturnsNonZero()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--repo", "J-Tech-Japan/intent-system",
                "--issue", "abc",
                "--outcome", "pr-created"
            },
            writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--issue must be a positive integer", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void JsonOutput_IncludesCamelCaseAliasForRecommendedLabelActions()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--repo", "J-Tech-Japan/intent-system",
                "--issue", "515",
                "--outcome", "pr-created",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);
        var raw = writer.ToString();
        // Both keys present, identical payloads.
        Assert.Contains("\"recommended_label_actions\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"recommendedLabelActions\"", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void TextOutput_RendersStableSectionsAndSummary()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--repo", "J-Tech-Japan/intent-system",
                "--issue", "515",
                "--pr", "999",
                "--outcome", "pr-created"
            },
            writer);

        Assert.Equal(0, exitCode);
        var raw = writer.ToString();
        Assert.Contains("# Worker result-summary for J-Tech-Japan/intent-system (issue-to-pr)", raw, StringComparison.Ordinal);
        Assert.Contains("## Recommended label actions (advisory only)", raw, StringComparison.Ordinal);
        Assert.Contains("## Warnings", raw, StringComparison.Ordinal);
        Assert.Contains("PR #999", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyzer_DeriveStatus_CoversEveryDocumentedOutcome()
    {
        // Lock the outcome→status map at the helper layer so a future
        // outcome cannot be added without a status mapping.
        foreach (var outcome in WorkerResultSummaryConstants.AllOutcomes)
        {
            var status = WorkerResultSummaryAnalyzer.DeriveStatus(outcome);
            Assert.False(string.IsNullOrWhiteSpace(status),
                $"DeriveStatus produced no status for outcome '{outcome}'");
        }
    }

    [Fact]
    public void Analyzer_IsSupportedKindOutcome_AcceptsDocumentedPairsAndRejectsOthers()
    {
        // Sample of supported pairs.
        Assert.True(WorkerResultSummaryAnalyzer.IsSupportedKindOutcome(
            WorkerResultSummaryConstants.Kinds.IssueToPr,
            WorkerResultSummaryConstants.Outcomes.PrCreated));
        Assert.True(WorkerResultSummaryAnalyzer.IsSupportedKindOutcome(
            WorkerResultSummaryConstants.Kinds.PrCommentFix,
            WorkerResultSummaryConstants.Outcomes.RepairPushed));
        Assert.True(WorkerResultSummaryAnalyzer.IsSupportedKindOutcome(
            WorkerResultSummaryConstants.Kinds.IssueToPr,
            WorkerResultSummaryConstants.Outcomes.ClarificationRequired));

        // Cross-kind invalids.
        Assert.False(WorkerResultSummaryAnalyzer.IsSupportedKindOutcome(
            WorkerResultSummaryConstants.Kinds.PrCommentFix,
            WorkerResultSummaryConstants.Outcomes.PrCreated));
        Assert.False(WorkerResultSummaryAnalyzer.IsSupportedKindOutcome(
            WorkerResultSummaryConstants.Kinds.IssueToPr,
            WorkerResultSummaryConstants.Outcomes.RepairPushed));
        Assert.False(WorkerResultSummaryAnalyzer.IsSupportedKindOutcome(
            WorkerResultSummaryConstants.Kinds.IssueToPr,
            WorkerResultSummaryConstants.Outcomes.NoActionableComments));
    }

    [Fact]
    public void Execute_NeverInvokesNestedProviderLauncher()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        var launcherInvoked = false;
        WorkerResultSummaryCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };

        using var writer = new StringWriter();
        // Walk every supported kind/outcome combination just once each, plus
        // a few invalid inputs, to lock the no-launch invariant across all
        // execution paths.
        ExecuteAndAssertExit(workspace, writer, "issue-to-pr", "515", null, "pr-created", expected: 0);
        ExecuteAndAssertExit(workspace, writer, "issue-to-pr", "999", null, "declined-contract-incomplete", expected: 0);
        ExecuteAndAssertExit(workspace, writer, "issue-to-pr", "999", null, "clarification-required", expected: 0);
        ExecuteAndAssertExit(workspace, writer, "issue-to-pr", "999", null, "already-resolved", expected: 0);
        ExecuteAndAssertExit(workspace, writer, "issue-to-pr", "999", null, "failed", expected: 0);
        ExecuteAndAssertExit(workspace, writer, "issue-to-pr", "999", null, "label-cleanup-required", expected: 0);
        ExecuteAndAssertExit(workspace, writer, "pr-comment-fix", null, "514", "repair-pushed", expected: 0);
        ExecuteAndAssertExit(workspace, writer, "pr-comment-fix", null, "514", "no-actionable-comments", expected: 0);
        ExecuteAndAssertExit(workspace, writer, "pr-comment-fix", null, "514", "already-resolved", expected: 0);
        ExecuteAndAssertExit(workspace, writer, "pr-comment-fix", null, "514", "clarification-required", expected: 0);
        ExecuteAndAssertExit(workspace, writer, "pr-comment-fix", null, "514", "failed", expected: 0);
        ExecuteAndAssertExit(workspace, writer, "pr-comment-fix", null, "514", "label-cleanup-required", expected: 0);
        ExecuteAndAssertExit(workspace, writer, "made-up", null, null, "pr-created", expected: 1);
        ExecuteAndAssertExit(workspace, writer, "issue-to-pr", "999", null, "made-up", expected: 1);

        Assert.False(launcherInvoked,
            "WorkerResultSummaryCommand must never invoke NestedProviderLauncher.");
    }

    [Fact]
    public void Execute_LeavesIntentCliWorkspaceByteEquivalent()
    {
        // Whole-workspace byte-snapshot — the command must not create any
        // file or modify any file on disk. Walks every supported (kind,
        // outcome) pair once.
        using var workspace = new WorkerResultSummaryWorkspace();
        var before = workspace.SnapshotWorkspace();

        using (var writer = new StringWriter())
        {
            ExecuteAndAssertExit(workspace, writer, "issue-to-pr", "515", "999", "pr-created", expected: 0);
            ExecuteAndAssertExit(workspace, writer, "pr-comment-fix", null, "514", "repair-pushed", expected: 0);
            ExecuteAndAssertExit(workspace, writer, "issue-to-pr", "999", null, "label-cleanup-required", expected: 0);
        }

        var after = workspace.SnapshotWorkspace();
        Assert.Equal(before.Count, after.Count);
        foreach (var (path, hash) in before)
        {
            Assert.True(after.TryGetValue(path, out var afterHash),
                $"file disappeared after run: {path}");
            Assert.Equal(hash, afterHash);
        }
    }

    [Fact]
    public void SourceScan_AnalyzerAndCommand_ContainNoProcessStartOrGitHubMutationLiterals()
    {
        // Locks the no-mutation guarantee at the source level: neither the
        // analyzer nor the command file may contain Process.Start, gh CLI
        // mutation verbs, or GraphQL review-thread mutations. Mirrors the
        // G204 source-scan pattern.
        var analyzer = File.ReadAllText(LocateSourceFile("WorkerResultSummaryAnalyzer.cs"));
        var command = File.ReadAllText(LocateSourceFile("WorkerResultSummaryCommand.cs"));

        var combined = analyzer + "\n" + command;

        Assert.DoesNotContain("Process.Start(", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh issue edit", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr edit", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr merge", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr close", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr reopen", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr comment", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr review", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("resolveReviewThread", combined, StringComparison.Ordinal);
    }

    // ----- G311: closing-reference annotation when caller pre-fetches PR body -----

    [Fact]
    public void Execute_G311_PrBodyMissingClosingReference_AddsWarning()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", WorkerResultSummaryConstants.Kinds.IssueToPr,
                "--repo", "J-Tech-Japan/intent-system",
                "--issue", "725",
                "--pr", "724",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr-draft", "false",
                "--pr-body", "## Summary\n- Implement G311 (no closing reference yet).",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerResultSummaryResult>(writer.ToString())!;
        Assert.Contains(result.Warnings, w => w.Contains("closing-reference (G311)", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("Closes #725", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_G311_PrBodyHasValidClosingReference_NoG311Warning()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", WorkerResultSummaryConstants.Kinds.IssueToPr,
                "--repo", "J-Tech-Japan/intent-system",
                "--issue", "725",
                "--pr", "724",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr-draft", "false",
                "--pr-body", "## Summary\n- Implement G311.\n\nCloses #725",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerResultSummaryResult>(writer.ToString())!;
        Assert.DoesNotContain(result.Warnings, w => w.Contains("closing-reference (G311)", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_G311_PrBodyHasMultipleDistinctRefs_AddsAmbiguityWarning()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", WorkerResultSummaryConstants.Kinds.IssueToPr,
                "--repo", "J-Tech-Japan/intent-system",
                "--issue", "725",
                "--pr", "724",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr-body", "Closes #725 and Fixes #800.",
                "--format", "json",
            },
            writer);

        var result = JsonSerializer.Deserialize<WorkerResultSummaryResult>(writer.ToString())!;
        Assert.Contains(result.Warnings, w =>
            w.Contains("closing-reference (G311)", StringComparison.Ordinal)
            && w.Contains("multiple", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_G311_PrBodyOmitted_SkipsAnnotation()
    {
        // When the operator does not supply --pr-body, the analyzer must
        // not run — result-summary stays purely declarative.
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", WorkerResultSummaryConstants.Kinds.IssueToPr,
                "--repo", "J-Tech-Japan/intent-system",
                "--issue", "725",
                "--pr", "724",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--format", "json",
            },
            writer);

        var result = JsonSerializer.Deserialize<WorkerResultSummaryResult>(writer.ToString())!;
        Assert.DoesNotContain(result.Warnings, w => w.Contains("closing-reference (G311)", StringComparison.Ordinal));
    }

    private static void ExecuteAndAssertExit(
        WorkerResultSummaryWorkspace workspace,
        StringWriter writer,
        string kind,
        string? issue,
        string? pr,
        string outcome,
        int expected)
    {
        writer.GetStringBuilder().Clear();
        var args = new List<string> { "--kind", kind, "--repo", "J-Tech-Japan/intent-system" };
        if (issue is not null)
        {
            args.Add("--issue");
            args.Add(issue);
        }
        if (pr is not null)
        {
            args.Add("--pr");
            args.Add(pr);
        }
        args.Add("--outcome");
        args.Add(outcome);
        args.Add("--format");
        args.Add("json");

        var exitCode = WorkerResultSummaryCommand.Execute(workspace.Context, args.ToArray(), writer);
        Assert.Equal(expected, exitCode);
    }

    private static string LocateSourceFile(string fileName)
    {
        var directory = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(directory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "src", "IntentSystem.Cli", "Commands", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate source file {fileName} from {directory}");
    }

    // ── G296 PR draft state ─────────────────────────────────────────────

    [Fact]
    public void Execute_GivenPrCreatedWithoutPrDraftFlag_OmitsPrDraftField()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--repo", "owner/repo",
                "--issue", "515",
                "--pr", "999",
                "--outcome", "pr-created",
                "--format", "json"
            },
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.TryGetProperty("pr_draft", out var draft));
        Assert.Equal(JsonValueKind.Null, draft.ValueKind);
        var summary = document.RootElement.GetProperty("summary").GetString()!;
        Assert.DoesNotContain("(draft)", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("(ready for review)", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenPrCreatedWithReadyForReview_EmitsFalsePrDraft_AndReadyForReviewSummary()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--repo", "owner/repo",
                "--issue", "515",
                "--pr", "999",
                "--outcome", "pr-created",
                "--pr-draft", "false",
                "--format", "json"
            },
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.False(document.RootElement.GetProperty("pr_draft").GetBoolean());
        Assert.False(document.RootElement.GetProperty("prDraft").GetBoolean());
        var warnings = document.RootElement.GetProperty("warnings")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.DoesNotContain(warnings, w => w!.Contains("draft PR", StringComparison.Ordinal));
        Assert.Contains("(ready for review)", document.RootElement.GetProperty("summary").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenPrCreatedAsDraft_EmitsTruePrDraft_DraftWarning_AndDraftSummary()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--repo", "owner/repo",
                "--issue", "515",
                "--pr", "999",
                "--outcome", "pr-created",
                "--pr-draft", "true",
                "--format", "json"
            },
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("pr_draft").GetBoolean());
        var warnings = document.RootElement.GetProperty("warnings")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(warnings, w => w!.Contains("draft PR", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w!.Contains("ready-for-review", StringComparison.Ordinal));
        Assert.Contains("(draft)", document.RootElement.GetProperty("summary").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenRepairPushedAsDraft_EmitsDraftWarning()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "pr-comment-fix",
                "--repo", "owner/repo",
                "--pr", "523",
                "--outcome", "repair-pushed",
                "--pr-draft", "true",
                "--format", "json"
            },
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("pr_draft").GetBoolean());
        var warnings = document.RootElement.GetProperty("warnings")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(warnings, w => w!.Contains("draft PR", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_RejectsInvalidPrDraftValue()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--repo", "owner/repo",
                "--issue", "515",
                "--pr", "999",
                "--outcome", "pr-created",
                "--pr-draft", "yes"
            },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--pr-draft must be 'true' or 'false'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_TextFormatWithDraft_RendersPrDraftLine()
    {
        using var workspace = new WorkerResultSummaryWorkspace();
        using var writer = new StringWriter();

        WorkerResultSummaryCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--repo", "owner/repo",
                "--issue", "515",
                "--pr", "999",
                "--outcome", "pr-created",
                "--pr-draft", "true"
            },
            writer);

        var output = writer.ToString();
        Assert.Contains("- pr_draft: true", output, StringComparison.Ordinal);
        Assert.Contains("host merge will be blocked", output, StringComparison.Ordinal);
    }

    private sealed class NoEvidenceIssueLookup : IGitHubIssueLookup
    {
        public GitHubIssueLookupResult Lookup(string repo, int issueNumber) => new()
        {
            Number = issueNumber,
            State = "OPEN",
            Title = "existing result-summary fixture",
            Body = "## Acceptance Criteria\n\n- Existing criterion without a G785 phrase.\n",
        };
    }

    private sealed class WorkerResultSummaryWorkspace : IDisposable
    {
        public WorkerResultSummaryWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("worker-result-summary-tests-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
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

        public IReadOnlyDictionary<string, string> SnapshotWorkspace()
        {
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories))
            {
                var bytes = File.ReadAllBytes(path);
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                snapshot[path] = hash;
            }
            return snapshot;
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
