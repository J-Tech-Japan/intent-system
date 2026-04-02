using System.Text.Json;
using IntentSystem.ConceptIntake.Models;
using IntentSystem.ConceptIntake.Serialization;

namespace IntentSystem.ConceptIntake.Tests;

public sealed class InterviewQueueSerializerTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Serialize_GivenOpenItem_ContainsCanonicalInterviewArtifactFields()
    {
        var item = CreateOpenItem();

        var serialized = InterviewQueueSerializer.Serialize(item);
        using var document = JsonDocument.Parse(serialized);
        var root = document.RootElement;

        Assert.Equal("interview", root.GetProperty("artifact_kind").GetString());
        Assert.Equal("auth", root.GetProperty("domain_slug").GetString());
        Assert.Equal("intents/intent-cli/concepts/auth-oauth2.md",
            root.GetProperty("source_concept_ref").GetString());
        Assert.Equal("iq-1", root.GetProperty("question_id").GetString());
        Assert.Equal("Which OAuth providers?", root.GetProperty("question_text").GetString());
        Assert.Equal("Provider choice impacts architecture.", root.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("affects").ValueKind);
        Assert.Equal("blocking", root.GetProperty("blocking_or_nonblocking").GetString());
        Assert.Equal("open", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("return_to_intent_paths").ValueKind);
    }

    [Fact]
    public void Serialize_GivenOpenItem_OmitsNullAnswerFields()
    {
        var item = CreateOpenItem();

        var serialized = InterviewQueueSerializer.Serialize(item);

        Assert.DoesNotContain("\"answer\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"answered_at\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"recommended_updates\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_GivenCancelledItem_UsesCancelledStatusValue()
    {
        var item = CreateOpenItem() with { Status = InterviewQueueItemStatus.Cancelled };

        var serialized = InterviewQueueSerializer.Serialize(item);

        Assert.Contains("\"status\": \"cancelled\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("skipped", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_GivenAppliedItemJson_RestoresAllFields()
    {
        var json = """
        {
          "artifact_kind": "interview",
          "domain_slug": "auth",
          "source_concept_ref": "intents/intent-cli/concepts/auth-oauth2.md",
          "question_id": "iq-1",
          "question_text": "Which OAuth providers?",
          "reason": "Provider choice impacts architecture.",
          "affects": ["auth-oauth2"],
          "blocking_or_nonblocking": "blocking",
          "status": "applied",
          "return_to_intent_paths": ["intents/intent-cli/intent-tree/means/auth-oauth2.md"],
          "created_at": "2026-04-02T11:00:00Z",
          "answer": "Use Google and GitHub OAuth.",
          "answered_at": "2026-04-02T12:00:00Z",
          "recommended_updates": ["Update auth strategy"]
        }
        """;

        var item = InterviewQueueSerializer.Deserialize(json);

        Assert.Equal("interview", item.ArtifactKind);
        Assert.Equal("iq-1", item.QuestionId);
        Assert.Equal("intents/intent-cli/concepts/auth-oauth2.md", item.SourceConceptRef);
        Assert.Equal(InterviewQueueItemStatus.Applied, item.Status);
        Assert.Equal("Use Google and GitHub OAuth.", item.Answer);
        Assert.NotNull(item.AnsweredAt);
        Assert.Single(item.ReturnToIntentPaths);
        Assert.Single(item.RecommendedUpdates!);
    }

    [Fact]
    public void Deserialize_GivenMissingArtifactKind_ThrowsInvalidOperationException()
    {
        var json = """
        {
          "domain_slug": "auth",
          "source_concept_ref": "ref.md",
          "question_id": "iq-1",
          "question_text": "test",
          "reason": "test",
          "affects": [],
          "blocking_or_nonblocking": "blocking",
          "status": "open",
          "return_to_intent_paths": [],
          "created_at": "2026-04-02T11:00:00Z"
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(
            () => InterviewQueueSerializer.Deserialize(json));

        Assert.Contains("artifact_kind", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_GivenMissingSourceConceptRef_ThrowsInvalidOperationException()
    {
        var json = """
        {
          "artifact_kind": "interview",
          "domain_slug": "auth",
          "question_id": "iq-1",
          "question_text": "test",
          "reason": "test",
          "affects": [],
          "blocking_or_nonblocking": "blocking",
          "status": "open",
          "return_to_intent_paths": [],
          "created_at": "2026-04-02T11:00:00Z"
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(
            () => InterviewQueueSerializer.Deserialize(json));

        Assert.Contains("source_concept_ref", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_GivenMissingReturnToIntentPaths_ThrowsInvalidOperationException()
    {
        var json = """
        {
          "artifact_kind": "interview",
          "domain_slug": "auth",
          "source_concept_ref": "ref.md",
          "question_id": "iq-1",
          "question_text": "test",
          "reason": "test",
          "affects": [],
          "blocking_or_nonblocking": "blocking",
          "status": "open",
          "created_at": "2026-04-02T11:00:00Z"
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(
            () => InterviewQueueSerializer.Deserialize(json));

        Assert.Contains("return_to_intent_paths", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_GivenOpenItemWithAnswer_ThrowsInvalidOperationException()
    {
        var item = CreateOpenItem() with
        {
            Answer = "Should not be here.",
            AnsweredAt = BaseTime
        };

        Assert.Throws<InvalidOperationException>(
            () => InterviewQueueSerializer.Serialize(item));
    }

    [Fact]
    public void Deserialize_GivenAppliedItemWithoutAnswer_ThrowsInvalidOperationException()
    {
        var json = """
        {
          "artifact_kind": "interview",
          "domain_slug": "auth",
          "source_concept_ref": "ref.md",
          "question_id": "iq-1",
          "question_text": "Which OAuth providers?",
          "reason": "test",
          "affects": [],
          "blocking_or_nonblocking": "blocking",
          "status": "applied",
          "return_to_intent_paths": [],
          "created_at": "2026-04-02T11:00:00Z"
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(
            () => InterviewQueueSerializer.Deserialize(json));

        Assert.Contains("must contain answer", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeAllAndDeserializeAll_RoundTrips()
    {
        IReadOnlyList<InterviewQueueItem> items =
        [
            CreateOpenItem(),
            CreateOpenItem() with
            {
                QuestionId = "iq-2",
                Status = InterviewQueueItemStatus.Cancelled
            }
        ];

        var serialized = InterviewQueueSerializer.SerializeAll(items);
        var deserialized = InterviewQueueSerializer.DeserializeAll(serialized);

        Assert.Equal(2, deserialized.Count);
        Assert.Equal("iq-1", deserialized[0].QuestionId);
        Assert.Equal(InterviewQueueItemStatus.Open, deserialized[0].Status);
        Assert.Equal("iq-2", deserialized[1].QuestionId);
        Assert.Equal(InterviewQueueItemStatus.Cancelled, deserialized[1].Status);
    }

    private static InterviewQueueItem CreateOpenItem()
    {
        return new InterviewQueueItem
        {
            DomainSlug = "auth",
            SourceConceptRef = "intents/intent-cli/concepts/auth-oauth2.md",
            QuestionId = "iq-1",
            QuestionText = "Which OAuth providers?",
            Reason = "Provider choice impacts architecture.",
            Affects = ["auth-oauth2"],
            BlockingOrNonblocking = "blocking",
            Status = InterviewQueueItemStatus.Open,
            ReturnToIntentPaths = ["intents/intent-cli/intent-tree/means/auth-oauth2.md"],
            CreatedAt = BaseTime.AddHours(-1)
        };
    }
}
