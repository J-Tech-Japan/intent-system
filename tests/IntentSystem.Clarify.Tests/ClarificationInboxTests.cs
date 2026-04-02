using IntentSystem.Clarify;
using IntentSystem.Clarify.Models;

namespace IntentSystem.Clarify.Tests;

public sealed class ClarificationInboxTests
{
    [Fact]
    public void GetPendingForUnit_GivenOpenAndResolvedClarifications_ReturnsOnlyOpenItemsForUnit()
    {
        IReadOnlyList<ClarificationItem> items =
        [
            CreateItem("clar-1", ["A2"], ClarificationState.Open),
            CreateItem("clar-2", ["A2"], ClarificationState.Answered),
            CreateItem("clar-3", ["B1"], ClarificationState.Open)
        ];

        var pending = ClarificationInbox.GetPendingForUnit(items, "A2");

        var item = Assert.Single(pending);
        Assert.Equal("clar-1", item.QuestionId);
    }

    [Fact]
    public void HasPendingClarifications_GivenOpenClarificationForUnit_ReturnsTrue()
    {
        IReadOnlyList<ClarificationItem> items =
        [
            CreateItem("clar-1", ["A2"], ClarificationState.Open),
            CreateItem("clar-2", ["A2"], ClarificationState.Answered)
        ];

        var result = ClarificationInbox.HasPendingClarifications(items, "A2");

        Assert.True(result);
    }

    [Fact]
    public void HasPendingClarifications_GivenOnlyResolvedClarificationsForUnit_ReturnsFalse()
    {
        IReadOnlyList<ClarificationItem> items =
        [
            CreateItem("clar-1", ["A2"], ClarificationState.Answered),
            CreateItem("clar-2", ["A2"], ClarificationState.Applied),
            CreateItem("clar-3", ["A2"], ClarificationState.Cancelled)
        ];

        var result = ClarificationInbox.HasPendingClarifications(items, "A2");

        Assert.False(result);
    }

    [Fact]
    public void FindLinkedClarifications_GivenExecutionUnit_ReturnsAllClarificationsForUnit()
    {
        IReadOnlyList<ClarificationItem> items =
        [
            CreateItem("clar-1", ["A2"], ClarificationState.Open),
            CreateItem("clar-2", ["A2"], ClarificationState.Answered),
            CreateItem("clar-3", ["B1"], ClarificationState.Open)
        ];

        var linked = ClarificationInbox.FindLinkedClarifications(items, "A2");

        Assert.Equal(2, linked.Count);
        Assert.All(linked, item => Assert.Contains("A2", item.AffectedExecutionUnits));
    }

    [Fact]
    public void FindLinkedClarifications_GivenItemAffectingMultipleUnits_ReturnsForEachAffectedUnit()
    {
        IReadOnlyList<ClarificationItem> items =
        [
            CreateItem("clar-1", ["A2", "B1"], ClarificationState.Open)
        ];

        var linkedA2 = ClarificationInbox.FindLinkedClarifications(items, "A2");
        var linkedB1 = ClarificationInbox.FindLinkedClarifications(items, "B1");

        Assert.Single(linkedA2);
        Assert.Single(linkedB1);
    }

    [Fact]
    public void GetPendingForUnit_GivenNullItems_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ClarificationInbox.GetPendingForUnit(null!, "A2"));
    }

    [Fact]
    public void FindLinkedClarifications_GivenNullOrWhitespaceExecutionUnit_ThrowsArgumentException()
    {
        IReadOnlyList<ClarificationItem> items = [CreateItem("clar-1", ["A2"], ClarificationState.Open)];

        Assert.ThrowsAny<ArgumentException>(() => ClarificationInbox.FindLinkedClarifications(items, null!));
        Assert.ThrowsAny<ArgumentException>(() => ClarificationInbox.FindLinkedClarifications(items, ""));
        Assert.ThrowsAny<ArgumentException>(() => ClarificationInbox.FindLinkedClarifications(items, "   "));
    }

    private static ClarificationItem CreateItem(
        string questionId, string[] affectedExecutionUnits, ClarificationState state)
    {
        return new ClarificationItem
        {
            ClarificationSource = "review",
            QuestionId = questionId,
            QuestionText = $"Question for {questionId}",
            Reason = "Clarification needed to continue execution.",
            AffectedIntents = [],
            AffectedExecutionUnits = affectedExecutionUnits,
            BlockingOrNonblocking = "blocking",
            ClarificationReturnPath = "intents/rules/issue-template-and-review-context.md",
            State = state,
            CreatedAt = DateTimeOffset.Parse("2026-04-02T10:00:00Z")
        };
    }
}
