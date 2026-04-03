using IntentSystem.Supervisor.Models;

namespace IntentSystem.Supervisor.Tests;

public sealed class QueueSelectionTests
{
    [Fact]
    public void SelectNext_GivenEligibleQueuedItem_ReturnsFirstEligibleSnapshotItem()
    {
        var state = CreateState(
        [
            CreateItem("A1", QueueItemState.Completed),
            CreateItem("B1", QueueItemState.Queued) with { Dependencies = ["A1"] },
            CreateItem("C1", QueueItemState.Queued)
        ]);

        var nextItem = QueueSelection.SelectNext(state);

        Assert.NotNull(nextItem);
        Assert.Equal("B1", nextItem!.ExecutionUnit);
    }

    [Fact]
    public void SelectNext_GivenQueuedItemWithBlockedBy_SkipsBlockedCandidate()
    {
        var state = CreateState(
        [
            CreateItem("A1", QueueItemState.Completed),
            CreateItem("B1", QueueItemState.Queued) with
            {
                Dependencies = ["A1"],
                BlockedBy = ["manual-hold"]
            },
            CreateItem("C1", QueueItemState.Queued)
        ]);

        var nextItem = QueueSelection.SelectNext(state);

        Assert.NotNull(nextItem);
        Assert.Equal("C1", nextItem!.ExecutionUnit);
    }

    [Fact]
    public void SelectNext_GivenQueuedItemWithUnresolvedDependency_SkipsIneligibleCandidate()
    {
        var state = CreateState(
        [
            CreateItem("A1", QueueItemState.Active),
            CreateItem("B1", QueueItemState.Queued) with { Dependencies = ["A1"] },
            CreateItem("C1", QueueItemState.Queued)
        ]);

        var nextItem = QueueSelection.SelectNext(state);

        Assert.NotNull(nextItem);
        Assert.Equal("C1", nextItem!.ExecutionUnit);
    }

    [Fact]
    public void SelectNext_GivenNoEligibleQueuedItems_ReturnsNull()
    {
        var state = CreateState(
        [
            CreateItem("A1", QueueItemState.Active),
            CreateItem("B1", QueueItemState.Blocked) with
            {
                Dependencies = ["A1"],
                BlockedBy = ["A1"]
            },
            CreateItem("C1", QueueItemState.Review)
        ]);

        var nextItem = QueueSelection.SelectNext(state);

        Assert.Null(nextItem);
    }

    private static QueueState CreateState(QueueItem[] items)
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items = items
        };
    }

    private static QueueItem CreateItem(string executionUnit, QueueItemState state)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = $"[{executionUnit}] Queue Item",
            State = state,
            Dependencies = [],
            BlockedBy = [],
            ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
            PacketPaths = new PacketPaths
            {
                Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
                Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml"
            },
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "normal"
        };
    }
}
