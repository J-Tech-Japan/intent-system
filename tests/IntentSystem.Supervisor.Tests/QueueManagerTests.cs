using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Supervisor.Tests;

public sealed class QueueManagerTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 4, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Activate_GivenQueuedItem_TransitionsToActive()
    {
        var state = CreateState(QueueItemState.Queued);

        var result = QueueManager.Activate(state, "A1", "worker", BaseTime);

        Assert.Equal(QueueItemState.Active, FindItem(result.UpdatedState, "A1").State);
        Assert.Equal("activated", result.Event.Event);
        Assert.Equal("A1", result.Event.ExecutionUnit);
        Assert.Equal("worker", result.Event.By);
    }

    [Fact]
    public void Activate_GivenNonQueuedItem_ThrowsInvalidOperationException()
    {
        var state = CreateState(QueueItemState.Active);

        var ex = Assert.Throws<InvalidOperationException>(
            () => QueueManager.Activate(state, "A1", "worker", BaseTime));

        Assert.Contains("expected state", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SubmitForReview_GivenActiveItem_TransitionsToReview()
    {
        var state = CreateState(QueueItemState.Active);

        var result = QueueManager.SubmitForReview(state, "A1", "worker", BaseTime, linkedPr: "https://github.com/org/repo/pull/1");

        var updatedItem = FindItem(result.UpdatedState, "A1");
        Assert.Equal(QueueItemState.Review, updatedItem.State);
        Assert.Equal("https://github.com/org/repo/pull/1", updatedItem.LinkedPr);
        Assert.Equal("review", result.Event.Event);
        Assert.Equal("https://github.com/org/repo/pull/1", result.Event.LinkedPr);
    }

    [Fact]
    public void SubmitForReview_GivenWorktreeProgressAdoptionRequiredBlocked_TransitionsToReview()
    {
        var blockedItem = CreateItem("A1", QueueItemState.Blocked) with
        {
            BlockedBy =
            [
                "worktree-progress-adoption-required: Implement direct run for 'A1' exited with backend exit code 1 ..."
            ]
        };
        var state = CreateState([blockedItem]);

        var result = QueueManager.SubmitForReview(
            state,
            "A1",
            "intent-cli",
            BaseTime,
            linkedPr: "https://github.com/org/repo/pull/9");

        var updatedItem = FindItem(result.UpdatedState, "A1");
        Assert.Equal(QueueItemState.Review, updatedItem.State);
        Assert.Equal("https://github.com/org/repo/pull/9", updatedItem.LinkedPr);
        Assert.Equal("review", result.Event.Event);
        Assert.Equal("https://github.com/org/repo/pull/9", result.Event.LinkedPr);
    }

    [Fact]
    public void SubmitForReview_GivenOrdinaryBlocked_ThrowsInvalidOperationException()
    {
        var blockedItem = CreateItem("A1", QueueItemState.Blocked) with
        {
            BlockedBy = ["dependency-incomplete: B1"]
        };
        var state = CreateState([blockedItem]);

        var ex = Assert.Throws<InvalidOperationException>(
            () => QueueManager.SubmitForReview(
                state,
                "A1",
                "intent-cli",
                BaseTime));

        Assert.Contains("expected state 'Active'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("found 'Blocked'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SubmitForReview_GivenBlockedWithoutMarkerAndAllowOverride_TransitionsToReview()
    {
        var blockedItem = CreateItem("A1", QueueItemState.Blocked) with
        {
            BlockedBy =
            [
                "Implement direct run for 'A1' ended after current-session product source/test read activity but before a bounded repair outcome."
            ]
        };
        var state = CreateState([blockedItem]);

        var result = QueueManager.SubmitForReview(
            state,
            "A1",
            "intent-cli",
            BaseTime,
            linkedPr: "https://github.com/org/repo/pull/11",
            allowBlockedWorktreeAdoption: true);

        var updatedItem = FindItem(result.UpdatedState, "A1");
        Assert.Equal(QueueItemState.Review, updatedItem.State);
        Assert.Equal("https://github.com/org/repo/pull/11", updatedItem.LinkedPr);
        Assert.Equal("review", result.Event.Event);
        Assert.Equal("https://github.com/org/repo/pull/11", result.Event.LinkedPr);
    }

    [Fact]
    public void SubmitForReview_GivenActiveAndAllowOverride_TransitionsToReview()
    {
        var state = CreateState(QueueItemState.Active);

        var result = QueueManager.SubmitForReview(
            state,
            "A1",
            "intent-cli",
            BaseTime,
            linkedPr: "https://github.com/org/repo/pull/12",
            allowBlockedWorktreeAdoption: true);

        var updatedItem = FindItem(result.UpdatedState, "A1");
        Assert.Equal(QueueItemState.Review, updatedItem.State);
        Assert.Equal("https://github.com/org/repo/pull/12", updatedItem.LinkedPr);
    }

    [Fact]
    public void RequestFix_GivenReviewItem_TransitionsToFixing()
    {
        var state = CreateState(QueueItemState.Review);

        var result = QueueManager.RequestFix(state, "A1", "reviewer", BaseTime, reason: "contract mismatch");

        Assert.Equal(QueueItemState.Fixing, FindItem(result.UpdatedState, "A1").State);
        Assert.Equal("fix-requested", result.Event.Event);
        Assert.Equal("contract mismatch", result.Event.Reason);
    }

    [Fact]
    public void ResubmitForReview_GivenFixingItem_TransitionsBackToReview()
    {
        var state = CreateState(QueueItemState.Fixing);

        var result = QueueManager.ResubmitForReview(
            state,
            "A1",
            "worker",
            BaseTime,
            linkedPr: "https://github.com/org/repo/pull/2");

        var updatedItem = FindItem(result.UpdatedState, "A1");
        Assert.Equal(QueueItemState.Review, updatedItem.State);
        Assert.Equal("https://github.com/org/repo/pull/2", updatedItem.LinkedPr);
        Assert.Equal("review", result.Event.Event);
        Assert.Equal("https://github.com/org/repo/pull/2", result.Event.LinkedPr);
    }

    [Fact]
    public void RepairInPlace_GivenReviewFixingReviewCycle_MaintainsSameExecutionUnit()
    {
        var state = CreateState(QueueItemState.Review);

        var fixResult = QueueManager.RequestFix(state, "A1", "reviewer", BaseTime, reason: "path mismatch");
        Assert.Equal(QueueItemState.Fixing, FindItem(fixResult.UpdatedState, "A1").State);

        var resubmitResult = QueueManager.ResubmitForReview(fixResult.UpdatedState, "A1", "worker", BaseTime.AddMinutes(30));
        Assert.Equal(QueueItemState.Review, FindItem(resubmitResult.UpdatedState, "A1").State);

        Assert.Equal("A1", fixResult.Event.ExecutionUnit);
        Assert.Equal("A1", resubmitResult.Event.ExecutionUnit);
    }

    [Fact]
    public void RequestClarification_GivenReviewItem_TransitionsToClarifyBlocked()
    {
        var state = CreateState(QueueItemState.Review);

        var result = QueueManager.RequestClarification(state, "A1", "reviewer", BaseTime, reason: "missing context");

        Assert.Equal(QueueItemState.ClarifyBlocked, FindItem(result.UpdatedState, "A1").State);
        Assert.Equal("clarify-requested", result.Event.Event);
        Assert.Equal("missing context", result.Event.Reason);
    }

    [Fact]
    public void ResolveClarification_GivenClarifyBlockedItem_TransitionsToActive()
    {
        var state = CreateState(QueueItemState.ClarifyBlocked);

        var result = QueueManager.ResolveClarification(state, "A1", "author", BaseTime);

        Assert.Equal(QueueItemState.Active, FindItem(result.UpdatedState, "A1").State);
        Assert.Equal("clarify-resolved", result.Event.Event);
    }

    [Fact]
    public void Complete_GivenReviewItem_TransitionsToCompleted()
    {
        var state = CreateState(QueueItemState.Review);

        var result = QueueManager.Complete(state, "A1", "reviewer", BaseTime);

        Assert.Equal(QueueItemState.Completed, FindItem(result.UpdatedState, "A1").State);
        Assert.Equal("completed", result.Event.Event);
    }

    [Fact]
    public void Complete_GivenDependentBlockedItem_UnblocksDependents()
    {
        var state = CreateStateWithDependency();

        var result = QueueManager.Complete(state, "A1", "reviewer", BaseTime);

        Assert.Equal(QueueItemState.Completed, FindItem(result.UpdatedState, "A1").State);
        Assert.Equal(QueueItemState.Queued, FindItem(result.UpdatedState, "B1").State);
        Assert.Empty(FindItem(result.UpdatedState, "B1").BlockedBy);
    }

    [Fact]
    public void Complete_GivenPartialDependency_KeepsItemBlocked()
    {
        var a1 = CreateItem("A1", QueueItemState.Review);
        var a2 = CreateItem("A2", QueueItemState.Active);
        var b1 = CreateItem("B1", QueueItemState.Blocked) with
        {
            Dependencies = ["A1", "A2"],
            BlockedBy = ["A1", "A2"]
        };

        var state = CreateState([a1, a2, b1]);

        var result = QueueManager.Complete(state, "A1", "reviewer", BaseTime);

        Assert.Equal(QueueItemState.Blocked, FindItem(result.UpdatedState, "B1").State);
        Assert.Equal(["A2"], FindItem(result.UpdatedState, "B1").BlockedBy);
    }

    [Fact]
    public void AcceptReview_GivenReviewItem_TransitionsToCompletedWithoutUnblockingDependents()
    {
        var state = CreateStateWithDependency();

        var result = QueueManager.AcceptReview(state, "A1", "reviewer", BaseTime);

        Assert.Equal(QueueItemState.Completed, FindItem(result.UpdatedState, "A1").State);
        Assert.Equal(QueueItemState.Blocked, FindItem(result.UpdatedState, "B1").State);
        Assert.Equal(["A1"], FindItem(result.UpdatedState, "B1").BlockedBy);
        Assert.Equal("completed", result.Event.Event);
    }

    [Fact]
    public void LinkIssue_GivenQueuedItem_UpdatesOnlySelectedItemAndEmitsIssueCreatedEvent()
    {
        var selectedItem = CreateItem("A1", QueueItemState.Queued);
        var otherItem = CreateItem("B1", QueueItemState.Blocked);
        var state = CreateState([selectedItem, otherItem]);
        var linkedIssue = new LinkedIssue
        {
            Repo = "J-Tech-Japan/intent-system",
            Number = 53,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/53"
        };

        var result = QueueManager.LinkIssue(state, "A1", linkedIssue, "intent-cli", BaseTime);

        Assert.Equal(linkedIssue, FindItem(result.UpdatedState, "A1").LinkedIssue);
        Assert.Null(FindItem(result.UpdatedState, "B1").LinkedIssue);
        Assert.Equal(QueueItemState.Queued, FindItem(result.UpdatedState, "A1").State);
        Assert.Equal("issue-created", result.Event.Event);
        Assert.Equal(linkedIssue.Url, result.Event.LinkedIssue);
    }

    [Fact]
    public void Enqueue_GivenNewItem_AppendsQueuedEventAndPopulatesBlockedBy()
    {
        var completedDependency = CreateItem("A1", QueueItemState.Completed);
        var queuedDependency = CreateItem("A2", QueueItemState.Queued);
        var state = CreateState([completedDependency, queuedDependency]);
        var candidate = CreateItem("B1", QueueItemState.Active) with
        {
            Dependencies = ["A2", "A1", "A2"],
            BlockedBy = ["stale-value"]
        };

        var result = QueueManager.Enqueue(state, candidate, "intent-cli", BaseTime);

        Assert.True(result.WasEnqueued);
        Assert.NotNull(result.Event);
        Assert.Equal("queued", result.Event!.Event);
        Assert.Equal("B1", result.Event.ExecutionUnit);
        Assert.Equal("intent-cli", result.Event.By);

        var insertedItem = FindItem(result.UpdatedState, "B1");
        Assert.Equal(QueueItemState.Queued, insertedItem.State);
        Assert.Equal(["A1", "A2"], insertedItem.Dependencies);
        Assert.Equal(["A2"], insertedItem.BlockedBy);
    }

    [Fact]
    public void Enqueue_GivenLinkedIssue_PropagatesLinkedIssueToQueuedEvent()
    {
        var state = CreateState([]);
        var candidate = CreateItem("B1", QueueItemState.Active) with
        {
            LinkedIssue = new LinkedIssue
            {
                Repo = "J-Tech-Japan/MyIntentHost",
                Number = 53,
                Url = "https://github.com/J-Tech-Japan/MyIntentHost/issues/53"
            }
        };

        var result = QueueManager.Enqueue(state, candidate, "intent-cli", BaseTime);

        Assert.NotNull(result.Event);
        Assert.Equal("https://github.com/J-Tech-Japan/MyIntentHost/issues/53", result.Event!.LinkedIssue);
    }

    [Fact]
    public void Enqueue_GivenExistingExecutionUnit_SkipsWithoutMutation()
    {
        var existingItem = CreateItem("A1", QueueItemState.Queued);
        var state = CreateState([existingItem]);
        var candidate = CreateItem("A1", QueueItemState.Active) with
        {
            Title = "[A1] Replacement"
        };

        var result = QueueManager.Enqueue(state, candidate, "intent-cli", BaseTime);

        Assert.False(result.WasEnqueued);
        Assert.Null(result.Event);
        Assert.Same(state, result.UpdatedState);
        Assert.Equal(existingItem, result.QueueItem);
    }

    [Fact]
    public void RefreshDependencies_GivenAllDepsCompleted_UnblocksItem()
    {
        var a1 = CreateItem("A1", QueueItemState.Completed);
        var b1 = CreateItem("B1", QueueItemState.Blocked) with
        {
            Dependencies = ["A1"],
            BlockedBy = ["A1"]
        };

        var state = CreateState([a1, b1]);

        var refreshed = QueueManager.RefreshDependencies(state);

        Assert.Equal(QueueItemState.Queued, FindItem(refreshed, "B1").State);
        Assert.Empty(FindItem(refreshed, "B1").BlockedBy);
    }

    [Fact]
    public void RefreshDependencies_GivenUnresolvedDeps_UpdatesBlockedBy()
    {
        var a1 = CreateItem("A1", QueueItemState.Completed);
        var a2 = CreateItem("A2", QueueItemState.Active);
        var b1 = CreateItem("B1", QueueItemState.Blocked) with
        {
            Dependencies = ["A1", "A2"],
            BlockedBy = ["A1", "A2"]
        };

        var state = CreateState([a1, a2, b1]);

        var refreshed = QueueManager.RefreshDependencies(state);

        Assert.Equal(QueueItemState.Blocked, FindItem(refreshed, "B1").State);
        Assert.Equal(["A2"], FindItem(refreshed, "B1").BlockedBy);
    }

    [Fact]
    public void TransitionUpdatesTimestamp()
    {
        var state = CreateState(QueueItemState.Queued);
        var newTime = BaseTime.AddHours(1);

        var result = QueueManager.Activate(state, "A1", "worker", newTime);

        Assert.Equal(newTime, result.UpdatedState.UpdatedAt);
        Assert.Equal(newTime, result.Event.Ts);
    }

    [Fact]
    public void FindItem_GivenMissingExecutionUnit_ThrowsInvalidOperationException()
    {
        var state = CreateState(QueueItemState.Queued);

        var ex = Assert.Throws<InvalidOperationException>(
            () => QueueManager.Activate(state, "MISSING", "worker", BaseTime));

        Assert.Contains("not found", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotAndEventAreSeparate_EventDoesNotMutateSnapshot()
    {
        var state = CreateState(QueueItemState.Queued);

        var result = QueueManager.Activate(state, "A1", "worker", BaseTime);

        Assert.Equal(QueueItemState.Queued, FindItem(state, "A1").State);
        Assert.Equal(QueueItemState.Active, FindItem(result.UpdatedState, "A1").State);
        Assert.NotNull(result.Event);
    }

    [Fact]
    public void TransitionNonBlocking_GivenCompletedTarget_UpdatesSelectedItemOnly()
    {
        var selectedItem = CreateItem("A1", QueueItemState.Active);
        var otherItem = CreateItem("B1", QueueItemState.Blocked) with
        {
            Dependencies = ["A1"],
            BlockedBy = ["A1"]
        };
        var state = CreateState([selectedItem, otherItem]);

        var result = QueueManager.TransitionNonBlocking(
            state,
            "A1",
            QueueItemState.Completed,
            "intent-cli",
            BaseTime);

        Assert.Equal(QueueItemState.Completed, FindItem(result.UpdatedState, "A1").State);
        Assert.Equal(QueueItemState.Blocked, FindItem(result.UpdatedState, "B1").State);
        Assert.Equal(["A1"], FindItem(result.UpdatedState, "B1").BlockedBy);
        Assert.Equal("completed", result.Event.Event);
        Assert.Equal("intent-cli", result.Event.By);
    }

    [Fact]
    public void TransitionNonBlocking_GivenBlockedTarget_ThrowsInvalidOperationException()
    {
        var state = CreateState(QueueItemState.Active);

        var exception = Assert.Throws<InvalidOperationException>(
            () => QueueManager.TransitionNonBlocking(
                state,
                "A1",
                QueueItemState.Blocked,
                "intent-cli",
                BaseTime));

        Assert.Contains("Unsupported queue transition target state 'blocked'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TransitionBlocking_GivenBlockedTarget_UpdatesSelectedItemOnlyAndStoresReason()
    {
        var selectedItem = CreateItem("A1", QueueItemState.Active);
        var otherItem = CreateItem("B1", QueueItemState.Blocked) with
        {
            Dependencies = ["A1"],
            BlockedBy = ["A1"]
        };
        var state = CreateState([selectedItem, otherItem]);

        var result = QueueManager.TransitionBlocking(
            state,
            "A1",
            QueueItemState.Blocked,
            "waiting on infra approval",
            "intent-cli",
            BaseTime);

        Assert.Equal(QueueItemState.Blocked, FindItem(result.UpdatedState, "A1").State);
        Assert.Equal(["waiting on infra approval"], FindItem(result.UpdatedState, "A1").BlockedBy);
        Assert.Equal(QueueItemState.Blocked, FindItem(result.UpdatedState, "B1").State);
        Assert.Equal(["A1"], FindItem(result.UpdatedState, "B1").BlockedBy);
        Assert.Equal("blocked", result.Event.Event);
        Assert.Equal("waiting on infra approval", result.Event.Reason);
    }

    [Fact]
    public void TransitionBlocking_GivenClarifyBlockedTarget_UsesClarifyRequestedEvent()
    {
        var state = CreateState(QueueItemState.Review);

        var result = QueueManager.TransitionBlocking(
            state,
            "A1",
            QueueItemState.ClarifyBlocked,
            "need product clarification",
            "intent-cli",
            BaseTime);

        Assert.Equal(QueueItemState.ClarifyBlocked, FindItem(result.UpdatedState, "A1").State);
        Assert.Equal(["need product clarification"], FindItem(result.UpdatedState, "A1").BlockedBy);
        Assert.Equal("clarify-requested", result.Event.Event);
    }

    [Fact]
    public void TransitionBlocking_GivenNonBlockingTarget_ThrowsInvalidOperationException()
    {
        var state = CreateState(QueueItemState.Active);

        var exception = Assert.Throws<InvalidOperationException>(
            () => QueueManager.TransitionBlocking(
                state,
                "A1",
                QueueItemState.Completed,
                "reason",
                "intent-cli",
                BaseTime));

        Assert.Contains("Unsupported blocking queue transition target state 'completed'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TransitionBlocking_GivenEmptyReason_ThrowsArgumentException()
    {
        var state = CreateState(QueueItemState.Active);

        Assert.Throws<ArgumentException>(
            () => QueueManager.TransitionBlocking(
                state,
                "A1",
                QueueItemState.Blocked,
                "",
                "intent-cli",
                BaseTime));
    }

    // ─── G534 review repair: guarded/idempotent/terminal Retire ──────────────

    [Fact]
    public void Retire_GivenQueuedItem_TransitionsToRetiredAndEmitsRetiredEvent()
    {
        var state = CreateState(QueueItemState.Queued);

        var result = QueueManager.Retire(state, "A1", "intent-cli", BaseTime);

        Assert.True(result.WasRetired);
        Assert.Equal(QueueItemState.Retired, FindItem(result.UpdatedState, "A1").State);
        Assert.NotNull(result.Event);
        Assert.Equal("retired", result.Event!.Event);
        Assert.Equal("A1", result.Event.ExecutionUnit);
        Assert.Equal("intent-cli", result.Event.By);
    }

    [Fact]
    public void Retire_GivenActiveOrBlockedItem_TransitionsToRetired()
    {
        var activeState = CreateState(QueueItemState.Active);
        var activeResult = QueueManager.Retire(activeState, "A1", "intent-cli", BaseTime);
        Assert.True(activeResult.WasRetired);
        Assert.Equal(QueueItemState.Retired, FindItem(activeResult.UpdatedState, "A1").State);

        var blockedItem = CreateItem("A1", QueueItemState.Blocked) with
        {
            BlockedBy = ["dependency-incomplete: B1"]
        };
        var blockedState = CreateState([blockedItem]);
        var blockedResult = QueueManager.Retire(blockedState, "A1", "intent-cli", BaseTime);
        Assert.True(blockedResult.WasRetired);
        Assert.Equal(QueueItemState.Retired, FindItem(blockedResult.UpdatedState, "A1").State);
    }

    [Fact]
    public void Retire_GivenCompletedItem_ThrowsWithoutMutation()
    {
        var state = CreateState(QueueItemState.Completed);

        var exception = Assert.Throws<InvalidOperationException>(
            () => QueueManager.Retire(state, "A1", "intent-cli", BaseTime));

        Assert.Contains("completed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(QueueItemState.Completed, FindItem(state, "A1").State);
    }

    [Fact]
    public void Retire_GivenCompletedItemWithLinkedPr_ThrowsWithoutMutation()
    {
        // G534 review repair: a Completed item carrying merged/linked-PR
        // evidence must refuse retirement exactly like any other Completed
        // item — retirement never reclassifies finished work.
        var mergedItem = CreateItem("A1", QueueItemState.Completed) with
        {
            LinkedPr = "https://github.com/org/repo/pull/42"
        };
        var state = CreateState([mergedItem]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => QueueManager.Retire(state, "A1", "intent-cli", BaseTime));

        Assert.Contains("completed", exception.Message, StringComparison.OrdinalIgnoreCase);
        var unchangedItem = FindItem(state, "A1");
        Assert.Equal(QueueItemState.Completed, unchangedItem.State);
        Assert.Equal("https://github.com/org/repo/pull/42", unchangedItem.LinkedPr);
    }

    [Fact]
    public void Retire_GivenAlreadyRetiredItem_IsIdempotentWithNoMutationOrDuplicateEvent()
    {
        var retiredItem = CreateItem("A1", QueueItemState.Retired);
        var state = CreateState([retiredItem]);

        var result = QueueManager.Retire(state, "A1", "intent-cli", BaseTime);

        Assert.False(result.WasRetired);
        Assert.Null(result.Event);
        Assert.Same(state, result.UpdatedState);
        Assert.Equal(QueueItemState.Retired, FindItem(result.UpdatedState, "A1").State);

        // Calling it again produces the exact same no-op — no event ever
        // appears on a re-run, however many times it is retried.
        var secondResult = QueueManager.Retire(result.UpdatedState, "A1", "intent-cli", BaseTime);
        Assert.False(secondResult.WasRetired);
        Assert.Null(secondResult.Event);
    }

    [Fact]
    public void Retire_GivenUnknownExecutionUnit_ThrowsInvalidOperationException()
    {
        var state = CreateState(QueueItemState.Queued);

        Assert.Throws<InvalidOperationException>(
            () => QueueManager.Retire(state, "does-not-exist", "intent-cli", BaseTime));
    }

    [Fact]
    public void TransitionNonBlocking_GivenRetiredTargetState_ThrowsUnsupported()
    {
        // G534 review repair: `retired` is exclusively reached via the
        // dedicated, guarded Retire method now — the generic non-blocking
        // path must never accept it as a target, even for a non-retired
        // source item.
        var state = CreateState(QueueItemState.Queued);

        var exception = Assert.Throws<InvalidOperationException>(
            () => QueueManager.TransitionNonBlocking(
                state,
                "A1",
                QueueItemState.Retired,
                "intent-cli",
                BaseTime));

        Assert.Contains("Unsupported queue transition target state 'retired'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TransitionNonBlocking_GivenRetiredItem_RefusesReactivationToActive()
    {
        var retiredItem = CreateItem("A1", QueueItemState.Retired);
        var state = CreateState([retiredItem]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => QueueManager.TransitionNonBlocking(
                state,
                "A1",
                QueueItemState.Active,
                "intent-cli",
                BaseTime));

        Assert.Contains("retired", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("terminal", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(QueueItemState.Retired, FindItem(state, "A1").State);
    }

    [Fact]
    public void TransitionNonBlocking_GivenRetiredItem_RefusesReactivationToQueued()
    {
        var retiredItem = CreateItem("A1", QueueItemState.Retired);
        var state = CreateState([retiredItem]);

        Assert.Throws<InvalidOperationException>(
            () => QueueManager.TransitionNonBlocking(
                state,
                "A1",
                QueueItemState.Queued,
                "intent-cli",
                BaseTime));

        Assert.Equal(QueueItemState.Retired, FindItem(state, "A1").State);
    }

    [Fact]
    public void TransitionBlocking_GivenRetiredItem_RefusesReclassificationToBlocked()
    {
        var retiredItem = CreateItem("A1", QueueItemState.Retired);
        var state = CreateState([retiredItem]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => QueueManager.TransitionBlocking(
                state,
                "A1",
                QueueItemState.Blocked,
                "reason",
                "intent-cli",
                BaseTime));

        Assert.Contains("retired", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(QueueItemState.Retired, FindItem(state, "A1").State);
    }

    private static QueueState CreateState(QueueItemState itemState)
    {
        return CreateState([CreateItem("A1", itemState)]);
    }

    private static QueueState CreateStateWithDependency()
    {
        var a1 = CreateItem("A1", QueueItemState.Review);
        var b1 = CreateItem("B1", QueueItemState.Blocked) with
        {
            Dependencies = ["A1"],
            BlockedBy = ["A1"]
        };
        return CreateState([a1, b1]);
    }

    private static QueueState CreateState(QueueItem[] items)
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = BaseTime.AddHours(-1),
            Items = items
        };
    }

    private static QueueItem CreateItem(string executionUnit, QueueItemState state)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = $"[{executionUnit}] Test Item",
            State = state,
            Dependencies = [],
            BlockedBy = [],
            ClarificationReturnPath = "intents/rules/issue-template-and-review-context.md",
            PacketPaths = new PacketPaths
            {
                Implementation = $".intent-cli/issues/{executionUnit.ToLowerInvariant()}/implementation.md",
                ReviewContext = $".intent-cli/issues/{executionUnit.ToLowerInvariant()}/review-context.md",
                Yaml = $".intent-cli/issues/{executionUnit.ToLowerInvariant()}/packet.yaml"
            },
            WorkerRole = "claude",
            ReviewRole = "human",
            Priority = "normal"
        };
    }

    private static QueueItem FindItem(QueueState state, string executionUnit)
    {
        return state.Items.First(i =>
            string.Equals(i.ExecutionUnit, executionUnit, StringComparison.Ordinal));
    }
}
