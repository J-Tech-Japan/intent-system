using System.Security.Cryptography;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G211: Tests for <c>intent-cli worker complete</c>. Cover the
/// issue-side and PR-side completion transitions for every supported
/// outcome, the stale-state refusals, the "intent-pr-created stays on
/// the issue" defensive guard, the dry-run vs write split, and the
/// no-mutation invariants.
/// </summary>
public sealed class WorkerCompleteCommandTests : IDisposable
{
    public WorkerCompleteCommandTests()
    {
        WorkerCompleteCommand.MutatorFactory = null;
        WorkerCompleteCommand.NestedProviderLauncher = null;
        // G311: permissive default — pre-existing tests assume the
        // closing-reference gate is satisfied. Returns a fake PR lookup
        // whose `closingIssuesReferences` cover the issue numbers used
        // throughout the suite (514, 525). The fake intentionally
        // returns `Repository = null` so `IsSameRepo` accepts. New
        // G311-focused tests override this factory to exercise the
        // gate's failure paths.
        WorkerCompleteCommand.PrLookupFactory = () => new PermissivePrLookup();
    }

    public void Dispose()
    {
        WorkerCompleteCommand.MutatorFactory = null;
        WorkerCompleteCommand.NestedProviderLauncher = null;
        WorkerCompleteCommand.PrLookupFactory = null;
    }

    [Fact]
    public void Execute_IssuePrCreatedOutcome_DryRunPlansSwapInProgressForPrCreated()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.False(result.Applied);
        Assert.Contains("intent-pr-created", result.AddLabels);
        Assert.Contains("intent-issue-in-progress", result.RemoveLabels);
        Assert.Empty(mutator.AppliedTransitions);

        Assert.Contains(result.Warnings, w =>
            w.Contains("intent-pr-created", StringComparison.Ordinal)
            && w.Contains("ISSUE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_IssuePrCreatedOutcome_WriteModeAppliesSwapAndPublishesPrIntentTarget()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Applied);
        Assert.Equal(999, result.PrNumber);
        Assert.True(result.PrTargetApplied);

        // G283: two writer calls — one for the source issue swap, one for the PR intent-target publish.
        Assert.Equal(2, mutator.AppliedTransitions.Count);
        var issueWrite = mutator.AppliedTransitions[0];
        Assert.Equal("issue", issueWrite.Kind);
        Assert.Equal(525, issueWrite.Number);
        Assert.Contains("intent-pr-created", issueWrite.AddLabels);
        Assert.Contains("intent-issue-in-progress", issueWrite.RemoveLabels);

        var prWrite = mutator.AppliedTransitions[1];
        Assert.Equal("pr", prWrite.Kind);
        Assert.Equal(999, prWrite.Number);
        Assert.Contains("intent-target", prWrite.AddLabels);
        Assert.DoesNotContain("intent-pr-created", prWrite.AddLabels);
    }

    // ── G283: PR review publication during issue-to-pr completion ─────

    [Fact]
    public void Execute_IssuePrCreatedOutcomeWithoutPr_ReturnsUsageError()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--pr is required", writer.ToString(), StringComparison.Ordinal);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_IssuePrCreatedOutcomeWithPr_AppliesIntentTargetToPrAndSyncsLinkedPr()
    {
        using var workspace = new WorkerCompleteWorkspace();
        workspace.SeedQueueStateWithLinkedIssue(
            executionUnit: "G283",
            title: "G283 source unit",
            sourceIssueNumber: 525);

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.True(result.Applied);
        Assert.Equal(999, result.PrNumber);
        Assert.True(result.PrTargetApplied);
        Assert.True(result.LinkedPrSynced);

        // Mutator received: (1) issue-side swap, (2) PR-side intent-target add.
        Assert.Equal(2, mutator.AppliedTransitions.Count);
        Assert.Contains(mutator.AppliedTransitions, t =>
            t.Kind == "pr" && t.Number == 999
            && t.AddLabels.Contains("intent-target")
            && !t.AddLabels.Contains("intent-pr-created"));

        // Queue-state linked_pr is synced for the matching unit.
        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        var item = Assert.Single(queueState.Items);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/999", item.LinkedPr);
    }

    [Fact]
    public void Execute_IssuePrCreatedOutcomeWithParentIntentRoot_SyncsParentQueueStateLinkedPr()
    {
        using var parentWorkspace = new WorkerCompleteWorkspace();
        parentWorkspace.SeedQueueStateWithLinkedIssue(
            executionUnit: "G283",
            title: "G283 source unit",
            sourceIssueNumber: 525);
        using var childWorkspace = new WorkerCompleteWorkspace(parentIntentRepoRoot: parentWorkspace.RootPath);
        Assert.False(File.Exists(childWorkspace.QueueStatePath));

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            childWorkspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.LinkedPrSynced);
        Assert.False(File.Exists(childWorkspace.QueueStatePath));

        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(parentWorkspace.QueueStatePath));
        var item = Assert.Single(queueState.Items);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/999", item.LinkedPr);
    }

    [Fact]
    public void Execute_PrTargetApplicationFailure_ReportsIncompleteWithReconcileGuidance()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FailingPrMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.False(result.Proceed);
        Assert.False(result.Applied);
        Assert.False(result.PrTargetApplied);
        Assert.Contains(result.Errors, e =>
            e.Contains("intent-target", StringComparison.Ordinal)
            && e.Contains("PR #999", StringComparison.Ordinal)
            && e.Contains("automation reconcile", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_IssuePrCreatedOutcome_LinkedPrSyncWarnsWhenQueueStateAbsent()
    {
        // G283/G330: when a parent intent repo root IS configured but
        // the queue-state file is missing, the writer surfaces the
        // pre-G330 "queue-state.json not found" warning — this is the
        // host-context drift signal that recommends running
        // `automation reconcile`. (Child-cwd mode without a parent
        // root is exercised in a separate G330 test, see below.)
        using var parentWorkspace = new WorkerCompleteWorkspace();
        // intentionally do NOT seed queue-state.json on the parent
        Assert.False(File.Exists(parentWorkspace.QueueStatePath));
        using var workspace = new WorkerCompleteWorkspace(parentIntentRepoRoot: parentWorkspace.RootPath);

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.True(result.PrTargetApplied);
        Assert.False(result.LinkedPrSynced);
        Assert.False(result.ChildCwd,
            "with a parent intent repo root configured the writer must NOT auto-enter child-cwd mode.");
        Assert.Contains(result.Warnings, w => w.Contains("queue-state.json not found", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_IssuePrCreatedRerunIdempotent_DoesNotDoubleSyncLinkedPr()
    {
        using var workspace = new WorkerCompleteWorkspace();
        workspace.SeedQueueStateWithLinkedIssue(
            executionUnit: "G283",
            title: "G283 unit",
            sourceIssueNumber: 525,
            existingLinkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/999");

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        var beforeBytes = File.ReadAllBytes(workspace.QueueStatePath);

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.LinkedPrSynced);
        // queue-state.json is unchanged when linked_pr already matches.
        var afterBytes = File.ReadAllBytes(workspace.QueueStatePath);
        Assert.Equal(beforeBytes, afterBytes);
    }

    [Fact]
    public void Execute_IssuePrCreatedAlreadyRecordedWithMissingLinkedPr_RepairsPrPublicationMetadata()
    {
        using var workspace = new WorkerCompleteWorkspace();
        workspace.SeedQueueStateWithLinkedIssue(
            executionUnit: "G283",
            title: "G283 unit",
            sourceIssueNumber: 525);

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-created" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.True(result.Applied);
        Assert.True(result.PrTargetApplied);
        Assert.True(result.LinkedPrSynced);
        Assert.Empty(result.Errors);

        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Equal("pr", transition.Kind);
        Assert.Equal(999, transition.Number);
        Assert.Contains("intent-target", transition.AddLabels);

        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        var item = Assert.Single(queueState.Items);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/999", item.LinkedPr);
    }

    [Fact]
    public void Execute_IssueNotClaimed_RefusesCompleteNotClaimed()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target" }, // no in-progress
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.False(result.Proceed);
        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.CompleteNotClaimed, StringComparison.Ordinal));
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_IssueAlreadyPrCreated_RefusesAlreadyCompleted()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress", "intent-pr-created" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--format", "json",
            },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.CompleteAlreadyCompleted, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_IssueClarificationOutcome_RemovesInProgressOnly()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.ClarificationRequired,
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Contains("intent-issue-in-progress", result.RemoveLabels);
        Assert.DoesNotContain("intent-pr-created", result.AddLabels);
    }

    [Fact]
    public void Execute_PrRepairPushedOutcome_DryRunPlansSwapUpdateInProgressForRereviewReady()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-request-update", "intent-pr-update-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "pr",
                "--number", "514",
                "--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed,
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.False(result.Applied);
        Assert.Contains("intent-pr-rereview-ready", result.AddLabels);
        Assert.Contains("intent-pr-update-in-progress", result.RemoveLabels);
        Assert.Contains("intent-pr-request-update", result.RemoveLabels);
        Assert.Empty(mutator.AppliedTransitions);

        // intent-pr-created MUST NOT be added to a PR.
        Assert.DoesNotContain("intent-pr-created", result.AddLabels);
    }

    [Fact]
    public void Execute_PrAlreadyRereviewReady_RefusesAlreadyCompletedOnRepairPushed()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-update-in-progress", "intent-pr-rereview-ready" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "pr",
                "--number", "514",
                "--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed,
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.CompleteAlreadyCompleted, StringComparison.Ordinal));
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_PrRepairPushedOutcome_WriteRemovesRequestUpdateWhenAddingRereviewReady()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-request-update", "intent-pr-update-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "pr",
                "--number", "514",
                "--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed,
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.True(result.Applied);
        Assert.Contains("intent-pr-rereview-ready", result.AddLabels);
        Assert.Contains("intent-pr-update-in-progress", result.RemoveLabels);
        Assert.Contains("intent-pr-request-update", result.RemoveLabels);

        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Contains("intent-pr-rereview-ready", transition.AddLabels);
        Assert.Contains("intent-pr-update-in-progress", transition.RemoveLabels);
        Assert.Contains("intent-pr-request-update", transition.RemoveLabels);

        var finalLabels = mutator.Labels
            .Except(transition.RemoveLabels, StringComparer.Ordinal)
            .Concat(transition.AddLabels)
            .ToArray();
        Assert.Contains("intent-pr-rereview-ready", finalLabels);
        Assert.DoesNotContain("intent-pr-request-update", finalLabels);
    }

    [Fact]
    public void Execute_PrFailedOutcome_RemovesUpdateInProgressOnly()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-update-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "pr",
                "--number", "514",
                "--outcome", WorkerResultSummaryConstants.Outcomes.Failed,
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Contains("intent-pr-update-in-progress", result.RemoveLabels);
        Assert.Empty(result.AddLabels);
    }

    [Fact]
    public void Execute_PrUnsupportedOutcomeForKind_RefusesUnsupportedKindOutcome()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-update-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        // pr-created is an issue-to-pr outcome, not a pr-comment-fix outcome.
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "pr",
                "--number", "514",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--format", "json",
            },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.UnsupportedKindOutcome, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_IssueUnsupportedOutcomeForKind_RefusesUnsupportedKindOutcome()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        // repair-pushed is a pr-comment-fix outcome, not an issue-to-pr outcome.
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed,
                "--format", "json",
            },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.UnsupportedKindOutcome, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_IntentPrCreatedNeverEndsUpInPrAddList()
    {
        // This test sweeps every supported pr-comment-fix outcome and
        // asserts that intent-pr-created is never proposed as an add on
        // a PR. Together with the mutator's defensive guard it locks the
        // "intent-pr-created is issue-only" invariant end-to-end.
        var prOutcomes = new[]
        {
            WorkerResultSummaryConstants.Outcomes.RepairPushed,
            WorkerResultSummaryConstants.Outcomes.NoActionableComments,
            WorkerResultSummaryConstants.Outcomes.AlreadyResolved,
            WorkerResultSummaryConstants.Outcomes.ClarificationRequired,
            WorkerResultSummaryConstants.Outcomes.Failed,
            WorkerResultSummaryConstants.Outcomes.LabelCleanupRequired,
        };

        foreach (var outcome in prOutcomes)
        {
            using var workspace = new WorkerCompleteWorkspace();
            // Different setups for "stale" outcomes — but the test is
            // structural: regardless of proceed=true/false, the proposed
            // AddLabels must not contain intent-pr-created.
            var mutator = new FakeMutator
            {
                Labels = new[] { "intent-target", "intent-pr-update-in-progress" },
            };
            WorkerCompleteCommand.MutatorFactory = () => mutator;

            using var writer = new StringWriter();
            WorkerCompleteCommand.Execute(
                workspace.Context,
                new[]
                {
                    "--repo", "J-Tech-Japan/intent-system",
                    "--kind", "pr",
                    "--number", "514",
                    "--outcome", outcome,
                    "--format", "json",
                },
                writer);

            var raw = writer.ToString();
            // Trim warning text that mentions "intent-pr-created" in
            // the misplaced-label policy phrasing — we only care about
            // the add_labels[] array contents here.
            using var doc = JsonDocument.Parse(raw);
            var addLabels = doc.RootElement.GetProperty("add_labels");
            for (var i = 0; i < addLabels.GetArrayLength(); i++)
            {
                var label = addLabels[i].GetString();
                Assert.NotEqual("intent-pr-created", label);
            }
        }
    }

    [Fact]
    public void Execute_MissingRequiredArguments_ReturnsNonZero()
    {
        using var workspace = new WorkerCompleteWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context, Array.Empty<string>(), writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--repo is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingOutcome_ReturnsNonZero()
    {
        using var workspace = new WorkerCompleteWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--kind", "issue", "--number", "525" },
            writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--outcome is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void JsonOutput_IncludesCamelCaseAliasesForLabelArrays()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var raw = writer.ToString();
        Assert.Contains("\"add_labels\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"addLabels\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"remove_labels\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"removeLabels\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"current_labels\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"currentLabels\"", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverInvokesNestedProviderLauncher()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var launcherInvoked = false;
        WorkerCompleteCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };

        WorkerCompleteCommand.MutatorFactory = () => new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };

        using var writer = new StringWriter();
        Assert.Equal(0, WorkerCompleteCommand.Execute(workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer));

        Assert.False(launcherInvoked,
            "WorkerCompleteCommand must never invoke NestedProviderLauncher.");
    }

    [Fact]
    public void Execute_DryRun_LeavesIntentCliWorkspaceByteEquivalent()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var before = workspace.SnapshotWorkspace();

        WorkerCompleteCommand.MutatorFactory = () => new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };

        using (var writer = new StringWriter())
        {
            Assert.Equal(0, WorkerCompleteCommand.Execute(workspace.Context,
                new[]
                {
                    "--repo", "J-Tech-Japan/intent-system",
                    "--kind", "issue",
                    "--number", "525",
                    "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                    "--pr", "999",
                    "--format", "json",
                },
                writer));
        }

        var after = workspace.SnapshotWorkspace();
        Assert.Equal(before.Count, after.Count);
        foreach (var (path, hash) in before)
        {
            Assert.True(after.TryGetValue(path, out var afterHash));
            Assert.Equal(hash, afterHash);
        }
    }

    [Fact]
    public void SourceScan_AnalyzerAndCommand_ContainNoProcessStartOrGhMutationLiterals()
    {
        var analyzer = StripCsharpComments(File.ReadAllText(LocateSourceFile("WorkerCompleteAnalyzer.cs")));
        var command = StripCsharpComments(File.ReadAllText(LocateSourceFile("WorkerCompleteCommand.cs")));
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

    // ----- G311: closing-reference gate on `--kind issue --outcome pr-created` -----

    [Fact]
    public void Execute_G311_RefusesPrCreatedWhenPrBodyHasNoClosingReference()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        // Override default permissive lookup with a body that lacks a closing reference.
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup
        {
            Body = "## Summary\n- Implement something. (no closing reference at all)",
            ClosingIssuesReferences = Array.Empty<GitHubPrClosingIssueReference>(),
        };

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "725",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "724",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("refused to complete issue #725", output, StringComparison.Ordinal);
        Assert.Contains("Closes #725", output, StringComparison.Ordinal);
        // The mutator must NOT have been invoked — gate fires before any write.
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_G311_RefusesPrCreatedWhenPrBodyClosesWrongIssue()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup
        {
            Body = "Closes #999", // wrong issue
            ClosingIssuesReferences = Array.Empty<GitHubPrClosingIssueReference>(),
        };

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "725",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "724",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("#999", output, StringComparison.Ordinal);
        Assert.Contains("#725", output, StringComparison.Ordinal);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_G311_RefusesPrCreatedWhenPrBodyHasMultipleDistinctClosingReferences()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup
        {
            // Body text closes two different issues; GitHub-resolved
            // closingIssuesReferences left empty so the body parser runs.
            Body = "Closes #725 and Fixes #800.",
            ClosingIssuesReferences = Array.Empty<GitHubPrClosingIssueReference>(),
        };

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "725",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "724",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("multiple", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_G311_AcceptsPrCreatedWhenGitHubClosingIssuesReferencesContainSourceIssue()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        // The PR body wording is irrelevant — GitHub-resolved
        // closingIssuesReferences is the authoritative signal.
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup
        {
            Body = "Body without explicit Closes wording — GitHub still resolved the link.",
            ClosingIssuesReferences = new[]
            {
                new GitHubPrClosingIssueReference { Number = 725, Repository = null },
            },
        };

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "725",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "724",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        // Mutator must have been called — the gate did NOT block the write.
        Assert.NotEmpty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_G311_DryRunAlsoEnforcesGate_NoMutation()
    {
        // Dry-run must report the same refusal so an automation operator
        // never thinks the completion plan is safe when it isn't.
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup
        {
            Body = "no closing reference here",
            ClosingIssuesReferences = Array.Empty<GitHubPrClosingIssueReference>(),
        };

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "725",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "724",
                "--format", "json",
            },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Empty(mutator.AppliedTransitions);
    }

    private static string StripCsharpComments(string source)
    {
        var noBlockComments = System.Text.RegularExpressions.Regex.Replace(
            source, @"/\*[\s\S]*?\*/", string.Empty);
        var noLineComments = System.Text.RegularExpressions.Regex.Replace(
            noBlockComments, @"//.*?$", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Multiline);
        return noLineComments;
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

    /// <summary>
    /// G311: default <see cref="IGitHubPrLookup"/> fake that satisfies the
    /// closing-reference gate for any issue number used by pre-existing
    /// tests. Returns a body that names both 514 and 525 in
    /// <c>closingIssuesReferences</c> with <c>Repository = null</c> so
    /// <c>IsSameRepo</c> accepts it. Tests that need to exercise the
    /// gate's failure paths inject a different fake via
    /// <see cref="WorkerCompleteCommand.PrLookupFactory"/>.
    /// </summary>
    internal sealed class PermissivePrLookup : IGitHubPrLookup
    {
        public GitHubPrLookupResult Lookup(string repo, int prNumber) => new()
        {
            Number = prNumber,
            State = "OPEN",
            Title = "permissive fake",
            Body = "Closes #525\nCloses #514",
            ClosingIssuesReferences = new[]
            {
                new GitHubPrClosingIssueReference { Number = 514, Repository = null },
                new GitHubPrClosingIssueReference { Number = 525, Repository = null },
            },
        };
    }

    /// <summary>
    /// G311: parametric <see cref="IGitHubPrLookup"/> fake. Tests inject
    /// this when they want to exercise the gate's failure paths or vary
    /// the body text and closing-issue references PR-by-PR.
    /// </summary>
    internal sealed class StubPrLookup : IGitHubPrLookup
    {
        public string Body { get; init; } = string.Empty;

        public IReadOnlyList<GitHubPrClosingIssueReference> ClosingIssuesReferences { get; init; }
            = Array.Empty<GitHubPrClosingIssueReference>();

        public GitHubPrLookupResult Lookup(string repo, int prNumber) => new()
        {
            Number = prNumber,
            State = "OPEN",
            Title = "stub",
            Body = Body,
            ClosingIssuesReferences = ClosingIssuesReferences,
        };
    }

    internal sealed class FakeMutator : IGitHubLabelMutator
    {
        public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();

        public List<AppliedTransition> AppliedTransitions { get; } = new();

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number)
        {
            return Labels.Select(n => new GitHubAutomationLabel { Name = n }).ToArray();
        }

        public void ApplyLabelTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels)
        {
            if (string.Equals(kind, GhCliGitHubLabelMutator.Kinds.Pr, StringComparison.Ordinal)
                && (addLabels.Contains("intent-pr-created", StringComparer.Ordinal)
                    || removeLabels.Contains("intent-pr-created", StringComparer.Ordinal)))
            {
                throw new InvalidOperationException(
                    "label policy violation: 'intent-pr-created' is issue-only.");
            }

            AppliedTransitions.Add(new AppliedTransition
            {
                Repo = repo,
                Kind = kind,
                Number = number,
                AddLabels = addLabels.ToArray(),
                RemoveLabels = removeLabels.ToArray(),
            });
        }

        public void ApplyReconcileTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException("reconcile path not exercised by these tests");
    }

    /// <summary>G283: variant that throws on PR-side intent-target writes so tests can
    /// assert the partial-failure path (issue swap applied, PR publish failed).</summary>
    internal sealed class FailingPrMutator : IGitHubLabelMutator
    {
        public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();
        public List<AppliedTransition> AppliedTransitions { get; } = new();

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            Labels.Select(n => new GitHubAutomationLabel { Name = n }).ToArray();

        public void ApplyLabelTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels)
        {
            if (string.Equals(kind, "pr", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("simulated PR-side mutator failure");
            }
            AppliedTransitions.Add(new AppliedTransition
            {
                Repo = repo,
                Kind = kind,
                Number = number,
                AddLabels = addLabels.ToArray(),
                RemoveLabels = removeLabels.ToArray(),
            });
        }

        public void ApplyReconcileTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException("reconcile path not exercised by these tests");
    }

    internal sealed record AppliedTransition
    {
        public required string Repo { get; init; }
        public required string Kind { get; init; }
        public required int Number { get; init; }
        public required IReadOnlyList<string> AddLabels { get; init; }
        public required IReadOnlyList<string> RemoveLabels { get; init; }
    }

    // --- G330: child worker flows are parent-host-state-free -----------------

    [Fact]
    public void Execute_G330_ChildCwdImplicit_IssueToPr_NeverTouchesParentState()
    {
        // G330 acceptance: a child worker completing an issue-to-pr
        // from a child worktree WITHOUT parent .intent-cli/ MUST:
        //  - apply GitHub label transitions (issue-side + PR-side)
        //  - report `child_cwd: true` (auto-detected — no parent root
        //    configured AND no local queue-state)
        //  - NOT touch any queue-state file
        //  - surface the G329 closeout-plan recovery command pointer
        //    so the operator/review-runtime can recover linkage host-side
        using var workspace = new WorkerCompleteWorkspace();
        // No parent intent repo root configured; no local queue-state.
        Assert.False(File.Exists(workspace.QueueStatePath));

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "764",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.True(result.PrTargetApplied);
        Assert.False(result.LinkedPrSynced);
        Assert.True(result.ChildCwd);
        Assert.Contains(result.Warnings,
            w => w.Contains("child-cwd mode", StringComparison.Ordinal));
        Assert.Contains(result.Warnings,
            w => w.Contains("closeout-plan", StringComparison.Ordinal));
        // The "queue-state.json not found" warning MUST NOT fire in
        // child-cwd mode — that signal is for hosts with a real
        // parent-host-root configuration.
        Assert.DoesNotContain(result.Warnings,
            w => w.Contains("queue-state.json not found", StringComparison.Ordinal));

        // G330 review fix: the child-cwd warning must contain the
        // ACTUAL pr number and repo, not the literal `{0}` / `{1}`
        // placeholders. A child worker pasting the warning text into
        // a host shell must get a directly runnable command.
        Assert.Contains(result.Warnings,
            w => w.Contains("--pr 764", StringComparison.Ordinal)
                && w.Contains("--repo J-Tech-Japan/intent-system", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Warnings,
            w => w.Contains("{0}", StringComparison.Ordinal) || w.Contains("{1}", StringComparison.Ordinal));

        // No queue-state file was ever created by the writer.
        Assert.False(File.Exists(workspace.QueueStatePath));
    }

    [Fact]
    public void Execute_G330_ChildCwdExplicit_SkipsQueueStateEvenWhenLocalFileExists()
    {
        // G330 invariant: --child-cwd is a hard refuse-to-write
        // assertion. Even if a stray local `.intent-cli/queue-state.json`
        // exists in the child worktree (which violates G300), the
        // command must NOT touch it.
        using var workspace = new WorkerCompleteWorkspace();
        // Seed a local queue-state on the child workspace — a G300
        // violation, but the test guards against accidentally writing
        // to it.
        workspace.SeedQueueStateWithLinkedIssue(
            executionUnit: "G330",
            title: "G330 unit",
            sourceIssueNumber: 525,
            existingLinkedPr: null);
        var queueBefore = File.ReadAllText(workspace.QueueStatePath);

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "764",
                "--child-cwd",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.True(result.ChildCwd);
        Assert.False(result.LinkedPrSynced);

        // Local queue-state must be byte-identical to before — even
        // though it existed on disk, --child-cwd refused to touch it.
        Assert.Equal(queueBefore, File.ReadAllText(workspace.QueueStatePath));
    }

    [Fact]
    public void Execute_G330_ChildCwd_PrCommentFix_MovesToRereviewReadyWithoutHostState()
    {
        // G330 acceptance: child PR-comment-fix completion (repair-pushed)
        // moves the PR to rereview-ready via GitHub labels only —
        // no parent durable state involved at all. linked_pr_synced is
        // null because that field is issue-to-pr-specific.
        using var workspace = new WorkerCompleteWorkspace();
        Assert.False(File.Exists(workspace.QueueStatePath));

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-update-in-progress", "intent-pr-request-update" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "pr",
                "--number", "762",
                "--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed,
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.True(result.Applied);
        Assert.True(result.ChildCwd,
            "pr-comment-fix completion from a child cwd must auto-detect child_cwd=true.");
        Assert.Contains("intent-pr-rereview-ready", result.AddLabels);
        Assert.DoesNotContain(result.Warnings,
            w => w.Contains("queue-state.json not found", StringComparison.Ordinal));
        // No queue-state file ever created.
        Assert.False(File.Exists(workspace.QueueStatePath));
    }

    [Fact]
    public void Execute_G330_HostContextWithParentRootSeeded_ChildCwdFalseAndQueueStateWritten()
    {
        // G330 invariant: hosts with a real parent root + seeded
        // queue-state must NOT auto-enter child-cwd mode. The writer
        // continues its pre-G330 host behavior: linked_pr is synced
        // and `child_cwd` is false.
        using var parent = new WorkerCompleteWorkspace();
        parent.SeedQueueStateWithLinkedIssue(
            executionUnit: "G283",
            title: "host packet",
            sourceIssueNumber: 525,
            existingLinkedPr: null);
        using var workspace = new WorkerCompleteWorkspace(parentIntentRepoRoot: parent.RootPath);

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.False(result.ChildCwd);
        Assert.True(result.LinkedPrSynced);
        // The parent queue-state actually got the linked_pr URL.
        var parentQueue = QueueStateSerializer.Deserialize(File.ReadAllText(parent.QueueStatePath));
        var matched = parentQueue.Items.Single(i => i.ExecutionUnit == "G283");
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/999", matched.LinkedPr);
    }

    // --- G333: --github-only strict child-loop assertion ---------------------

    [Fact]
    public void Execute_G333_GithubOnly_ImpliesChildCwdAndRefusesQueueStateMutation()
    {
        // G333 acceptance: `--github-only` is the strict assertion used
        // by the Claude/Codex child implementation loop. It implies
        // --child-cwd (so the writer never touches parent durable
        // state), and the result records `github_only: true` so the
        // host loop can audit the binding.
        //
        // Seed BOTH a host parent root AND a local queue-state to prove
        // --github-only refuses both: even when the writer could find
        // a parent file, it must not write.
        using var parent = new WorkerCompleteWorkspace();
        parent.SeedQueueStateWithLinkedIssue(
            executionUnit: "G333",
            title: "host packet",
            sourceIssueNumber: 525,
            existingLinkedPr: null);
        using var workspace = new WorkerCompleteWorkspace(parentIntentRepoRoot: parent.RootPath);
        workspace.SeedQueueStateWithLinkedIssue(
            executionUnit: "G333-LOCAL",
            title: "stray local file",
            sourceIssueNumber: 525,
            existingLinkedPr: null);
        var parentBefore = File.ReadAllText(parent.QueueStatePath);
        var localBefore = File.ReadAllText(workspace.QueueStatePath);

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exit = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--github-only",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exit);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.True(result.PrTargetApplied);
        Assert.False(result.LinkedPrSynced);
        // --github-only implies --child-cwd.
        Assert.True(result.ChildCwd);
        Assert.True(result.GithubOnly);

        // Both queue-state files are byte-identical.
        Assert.Equal(parentBefore, File.ReadAllText(parent.QueueStatePath));
        Assert.Equal(localBefore, File.ReadAllText(workspace.QueueStatePath));
    }

    [Fact]
    public void Execute_G333_WithoutGithubOnly_PreservesPriorBehavior_GithubOnlyFalseOnResult()
    {
        // G333 invariant: callers that don't pass --github-only keep
        // their pre-G333 result shape. The `github_only` field is
        // explicitly false on host/review-runtime invocations so the
        // host loop can tell the strict child-loop binding from the
        // host-context binding.
        using var parent = new WorkerCompleteWorkspace();
        parent.SeedQueueStateWithLinkedIssue(
            executionUnit: "G333",
            title: "host packet",
            sourceIssueNumber: 525,
            existingLinkedPr: null);
        using var workspace = new WorkerCompleteWorkspace(parentIntentRepoRoot: parent.RootPath);

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exit = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exit);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.False(result.GithubOnly);
        // The host-context write actually synced linked_pr (pre-G333
        // behavior preserved).
        Assert.True(result.LinkedPrSynced);
    }

    private sealed class WorkerCompleteWorkspace : IDisposable
    {
        public WorkerCompleteWorkspace(string parentIntentRepoRoot = "")
        {
            RootPath = Directory.CreateTempSubdirectory("worker-complete-tests-").FullName;
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
                        ParentIntentRepoRoot = parentIntentRepoRoot
                    }
                }
            };
        }

        public string RootPath { get; }
        public CliContext Context { get; }

        public string QueueStatePath => Path.Combine(RootPath, ".intent-cli", "queue-state.json");

        /// <summary>G283: seed queue-state.json with a single execution-unit row whose
        /// linked_issue.number matches the source issue, optionally pre-filling linked_pr
        /// (for the idempotent rerun test).</summary>
        public void SeedQueueStateWithLinkedIssue(
            string executionUnit,
            string title,
            int sourceIssueNumber,
            string? existingLinkedPr = null)
        {
            var state = new QueueState
            {
                SchemaVersion = "1",
                UpdatedAt = new DateTimeOffset(2026, 5, 7, 0, 0, 0, TimeSpan.Zero),
                Items =
                [
                    new QueueItem
                    {
                        ExecutionUnit = executionUnit,
                        Title = title,
                        State = QueueItemState.Queued,
                        Dependencies = Array.Empty<string>(),
                        BlockedBy = Array.Empty<string>(),
                        ClarificationReturnPath = string.Empty,
                        PacketPaths = new PacketPaths
                        {
                            Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                            ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
                            Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml",
                        },
                        LinkedIssue = new LinkedIssue
                        {
                            Repo = "J-Tech-Japan/intent-system",
                            Number = sourceIssueNumber,
                            Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{sourceIssueNumber}",
                        },
                        LinkedPr = existingLinkedPr,
                        WorkerRole = "child-impl",
                        ReviewRole = "host-review",
                        Priority = "normal",
                    }
                ]
            };
            File.WriteAllText(QueueStatePath, QueueStateSerializer.Serialize(state));
        }

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
