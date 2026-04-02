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

        Assert.Equal(QueueItemState.Review, FindItem(result.UpdatedState, "A1").State);
        Assert.Equal("review-started", result.Event.Event);
        Assert.Equal("https://github.com/org/repo/pull/1", result.Event.LinkedPr);
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

        var result = QueueManager.ResubmitForReview(state, "A1", "worker", BaseTime);

        Assert.Equal(QueueItemState.Review, FindItem(result.UpdatedState, "A1").State);
        Assert.Equal("review-started", result.Event.Event);
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
