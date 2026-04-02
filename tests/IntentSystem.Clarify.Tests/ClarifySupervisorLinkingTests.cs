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
            CreateClarification("clar-1", "A2", ClarificationStatus.Open),
            CreateClarification("clar-2", "A2", ClarificationStatus.Answered),
            CreateClarification("clar-3", "B1", ClarificationStatus.Open)
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
            CreateClarification("clar-1", "A2", ClarificationStatus.Open),
            CreateClarification("clar-2", "A2", ClarificationStatus.Applied)
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
            CreateClarification("clar-1", "A2", ClarificationStatus.Answered),
            CreateClarification("clar-2", "A2", ClarificationStatus.Cancelled)
        ];

        var hasPending = ClarificationInbox.HasPendingClarifications(clarifications, queueItem.ExecutionUnit);

        Assert.False(hasPending);
    }

    private static ClarificationItem CreateClarification(
        string questionId, string executionUnit, ClarificationStatus status)
    {
        return new ClarificationItem
        {
            ClarificationSource = "review",
            QuestionId = questionId,
            ExecutionUnit = executionUnit,
            QuestionText = $"Question for {questionId}",
            Reason = "Clarification needed to unblock review.",
            AffectedIntents = [],
            AffectedExecutionUnits = [executionUnit],
            BlockingOrNonblocking = "blocking",
            ClarificationReturnPath = "intents/rules/issue-template-and-review-context.md",
            Status = status,
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
            ClarificationReturnPath = "intents/rules/issue-template-and-review-context.md",
            PacketPaths = new PacketPaths
            {
                Implementation = $".intent-cli/issues/{executionUnit.ToLowerInvariant()}/implementation.md",
                ReviewContext = $".intent-cli/issues/{executionUnit.ToLowerInvariant()}/review-context.md",
                Yaml = $".intent-cli/issues/{executionUnit.ToLowerInvariant()}/packet.yaml"
            },
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "high"
        };
    }
}
