using IntentSystem.Clarify;
using IntentSystem.Clarify.Models;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Clarify.Tests;

public sealed class ClarifySupervisorLinkingTests
{
    [Fact]
    public void FindLinkedClarifications_GivenClarifyBlockedQueueItem_UsesExecutionUnitAsLinkKey()
    {
        var queueItem = CreateQueueItem("A2", QueueItemState.ClarifyBlocked);
        IReadOnlyList<ClarificationItem> clarifications =
        [
            CreateClarification("clar-1", "A2", ClarificationState.Open),
            CreateClarification("clar-2", "A2", ClarificationState.Answered),
            CreateClarification("clar-3", "B1", ClarificationState.Open)
        ];

        var linked = ClarificationInbox.FindLinkedClarifications(clarifications, queueItem.ExecutionUnit);

        Assert.Equal(2, linked.Count);
        Assert.All(linked, item => Assert.Equal(queueItem.ExecutionUnit, item.ExecutionUnit));
    }

    [Fact]
    public void HasPendingClarifications_GivenClarifyBlockedQueueItemAndOpenArtifact_ReturnsTrue()
    {
        var queueItem = CreateQueueItem("A2", QueueItemState.ClarifyBlocked);
        IReadOnlyList<ClarificationItem> clarifications =
        [
            CreateClarification("clar-1", "A2", ClarificationState.Open),
            CreateClarification("clar-2", "A2", ClarificationState.Applied)
        ];

        var hasPending = ClarificationInbox.HasPendingClarifications(clarifications, queueItem.ExecutionUnit);

        Assert.True(hasPending);
    }

    [Fact]
    public void HasPendingClarifications_GivenClarifyBlockedQueueItemAndNoOpenArtifact_ReturnsFalse()
    {
        var queueItem = CreateQueueItem("A2", QueueItemState.ClarifyBlocked);
        IReadOnlyList<ClarificationItem> clarifications =
        [
            CreateClarification("clar-1", "A2", ClarificationState.Answered),
            CreateClarification("clar-2", "A2", ClarificationState.Cancelled)
        ];

        var hasPending = ClarificationInbox.HasPendingClarifications(clarifications, queueItem.ExecutionUnit);

        Assert.False(hasPending);
    }

    private static ClarificationItem CreateClarification(string id, string executionUnit, ClarificationState state)
    {
        return new ClarificationItem
        {
            Id = id,
            ExecutionUnit = executionUnit,
            State = state,
            Question = $"Question for {executionUnit}",
            Context = "Clarification needed to unblock review.",
            CreatedAt = DateTimeOffset.Parse("2026-04-02T10:00:00Z")
        };
    }

    private static QueueItem CreateQueueItem(string executionUnit, QueueItemState state)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = "Clarify inbox boundary",
            State = state,
            Dependencies = [],
            BlockedBy = [],
            ClarificationReturnPath = ".takt/runs/20260402-191315-issue-13-d1-clarify-inbox-goal/context/task/order.md",
            PacketPaths = new PacketPaths
            {
                Implementation = ".intent-cli/issues/a2/implementation.md",
                ReviewContext = ".intent-cli/issues/a2/review-context.md",
                Yaml = ".intent-cli/issues/a2/packet.yaml"
            },
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "high"
        };
    }
}
