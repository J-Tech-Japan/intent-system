using IntentSystem.ConceptIntake.Interview;
using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.ConceptIntake.Tests;

public sealed class InterviewQueueTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetBlockingPending_GivenMixedBlockingAndNonblocking_ReturnsOnlyOpenBlockingItems()
    {
        IReadOnlyList<InterviewQueueItem> items =
        [
            CreateItem("iq-1", "blocking", InterviewQueueItemStatus.Open),
            CreateItem("iq-2", "nonblocking", InterviewQueueItemStatus.Open),
            CreateItem("iq-3", "blocking", InterviewQueueItemStatus.Applied)
        ];

        var blocking = InterviewQueue.GetBlockingPending(items);

        var item = Assert.Single(blocking);
        Assert.Equal("iq-1", item.QuestionId);
    }

    [Fact]
    public void GetAllPending_GivenMixedStatuses_ReturnsAllOpenItems()
    {
        IReadOnlyList<InterviewQueueItem> items =
        [
            CreateItem("iq-1", "blocking", InterviewQueueItemStatus.Open),
            CreateItem("iq-2", "nonblocking", InterviewQueueItemStatus.Open),
            CreateItem("iq-3", "blocking", InterviewQueueItemStatus.Applied)
        ];

        var pending = InterviewQueue.GetAllPending(items);

        Assert.Equal(2, pending.Count);
    }

    [Fact]
    public void IsFlowBlocked_GivenOpenBlockingQuestion_ReturnsTrue()
    {
        IReadOnlyList<InterviewQueueItem> items =
        [
            CreateItem("iq-1", "blocking", InterviewQueueItemStatus.Open),
            CreateItem("iq-2", "nonblocking", InterviewQueueItemStatus.Open)
        ];

        Assert.True(InterviewQueue.IsFlowBlocked(items));
    }

    [Fact]
    public void IsFlowBlocked_GivenOnlyNonblockingOpenQuestions_ReturnsFalse()
    {
        IReadOnlyList<InterviewQueueItem> items =
        [
            CreateItem("iq-1", "nonblocking", InterviewQueueItemStatus.Open),
            CreateItem("iq-2", "nonblocking", InterviewQueueItemStatus.Open)
        ];

        Assert.False(InterviewQueue.IsFlowBlocked(items));
    }

    [Fact]
    public void IsFlowBlocked_GivenAllBlockingAnswered_ReturnsFalse()
    {
        IReadOnlyList<InterviewQueueItem> items =
        [
            CreateItem("iq-1", "blocking", InterviewQueueItemStatus.Applied),
            CreateItem("iq-2", "nonblocking", InterviewQueueItemStatus.Open)
        ];

        Assert.False(InterviewQueue.IsFlowBlocked(items));
    }

    [Fact]
    public void ApplyAnswer_GivenOpenItem_ReturnsAppliedResultWithRecommendedUpdatesAndReturnPaths()
    {
        var item = CreateItem("iq-1", "blocking", InterviewQueueItemStatus.Open);
        var updates = new[] { "Update auth strategy document" };

        var result = InterviewQueue.ApplyAnswer(item, "Use OAuth2 with PKCE.", updates, BaseTime);

        Assert.Equal(InterviewQueueItemStatus.Applied, result.AppliedItem.Status);
        Assert.Equal("Use OAuth2 with PKCE.", result.AppliedItem.Answer);
        Assert.Equal(BaseTime, result.AppliedItem.AnsweredAt);
        Assert.Equal(updates, result.RecommendedUpdates);
        Assert.Equal(item.ReturnToIntentPaths, result.ReturnToIntentPaths);
    }

    [Fact]
    public void ApplyAnswer_GivenNonOpenItem_ThrowsInvalidOperationException()
    {
        var item = CreateItem("iq-1", "blocking", InterviewQueueItemStatus.Applied) with
        {
            Answer = "Already answered.",
            AnsweredAt = BaseTime.AddHours(-1)
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => InterviewQueue.ApplyAnswer(item, "New answer.", [], BaseTime));

        Assert.Contains("Open", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyAnswer_DoesNotMutateOriginalItem()
    {
        var item = CreateItem("iq-1", "blocking", InterviewQueueItemStatus.Open);

        _ = InterviewQueue.ApplyAnswer(item, "Answer text.", [], BaseTime);

        Assert.Equal(InterviewQueueItemStatus.Open, item.Status);
        Assert.Null(item.Answer);
    }

    [Fact]
    public void GetForDomain_GivenDomainSlug_FiltersCorrectly()
    {
        IReadOnlyList<InterviewQueueItem> items =
        [
            CreateItem("iq-1", "blocking", InterviewQueueItemStatus.Open, domainSlug: "auth"),
            CreateItem("iq-2", "blocking", InterviewQueueItemStatus.Open, domainSlug: "billing"),
            CreateItem("iq-3", "nonblocking", InterviewQueueItemStatus.Open, domainSlug: "auth")
        ];

        var authItems = InterviewQueue.GetForDomain(items, "auth");

        Assert.Equal(2, authItems.Count);
        Assert.All(authItems, i => Assert.Equal("auth", i.DomainSlug));
    }

    private static InterviewQueueItem CreateItem(
        string questionId, string blockingOrNonblocking, InterviewQueueItemStatus status,
        string domainSlug = "auth")
    {
        return new InterviewQueueItem
        {
            DomainSlug = domainSlug,
            SourceConceptRef = "intents/intent-cli/concepts/auth-oauth2.md",
            QuestionId = questionId,
            QuestionText = $"Question for {questionId}",
            Reason = "Explore unknown area.",
            Affects = ["auth-oauth2"],
            BlockingOrNonblocking = blockingOrNonblocking,
            Status = status,
            ReturnToIntentPaths = ["intents/intent-cli/intent-tree/means/auth-oauth2.md"],
            CreatedAt = BaseTime.AddHours(-1)
        };
    }
}
