using System.Text.Json;
using IntentSystem.Clarify.Models;
using IntentSystem.Clarify.Serialization;

namespace IntentSystem.Clarify.Tests;

public sealed class ClarificationSerializerTests
{
    [Fact]
    public void Serialize_GivenOpenClarification_UsesClarificationArtifactContract()
    {
        var item = CreateOpenItem();

        var serialized = ClarificationSerializer.Serialize(item);
        using var document = JsonDocument.Parse(serialized);
        var root = document.RootElement;

        Assert.Equal("clarification", root.GetProperty("artifact_kind").GetString());
        Assert.Equal("clar-1", root.GetProperty("id").GetString());
        Assert.Equal("A2", root.GetProperty("execution_unit").GetString());
        Assert.Equal("open", root.GetProperty("state").GetString());
        Assert.DoesNotContain("\"artifactKind\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"executionUnit\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"answer\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"answered_at\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_GivenAnsweredClarificationJson_RestoresStateAndAnswerMetadata()
    {
        var json = """
        {
          "artifact_kind": "clarification",
          "id": "clar-1",
          "execution_unit": "A2",
          "state": "answered",
          "question": "Which queue field owns the return path?",
          "context": "The queue manager already stores clarification_return_path.",
          "answer": "Keep return path on queue item and link artifact by execution_unit.",
          "created_at": "2026-04-02T10:00:00Z",
          "answered_at": "2026-04-02T10:05:00Z"
        }
        """;

        var item = ClarificationSerializer.Deserialize(json);

        Assert.Equal("clarification", item.ArtifactKind);
        Assert.Equal("clar-1", item.Id);
        Assert.Equal("A2", item.ExecutionUnit);
        Assert.Equal(ClarificationState.Answered, item.State);
        Assert.Equal("Keep return path on queue item and link artifact by execution_unit.", item.Answer);
        Assert.Equal(DateTimeOffset.Parse("2026-04-02T10:05:00Z"), item.AnsweredAt);
    }

    [Fact]
    public void Serialize_GivenAppliedClarification_UsesKebabCaseEnumValue()
    {
        var item = CreateOpenItem() with
        {
            State = ClarificationState.Applied,
            Answer = "Use the clarification inbox boundary.",
            AnsweredAt = DateTimeOffset.Parse("2026-04-02T10:05:00Z")
        };

        var serialized = ClarificationSerializer.Serialize(item);

        Assert.Contains("\"state\": \"applied\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_GivenOpenClarificationWithAnswerMetadata_ThrowsInvalidOperationException()
    {
        var item = CreateOpenItem() with
        {
            Answer = "Use execution_unit as the link key.",
            AnsweredAt = DateTimeOffset.Parse("2026-04-02T10:05:00Z")
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => ClarificationSerializer.Serialize(item));

        Assert.Equal(
            "Open clarification items must not contain answer metadata.",
            ex.Message);
    }

    [Fact]
    public void Deserialize_GivenAnsweredClarificationWithoutAnswer_ThrowsInvalidOperationException()
    {
        var json = """
        {
          "artifact_kind": "clarification",
          "id": "clar-1",
          "execution_unit": "A2",
          "state": "answered",
          "question": "Which queue field owns the return path?",
          "context": "The queue manager already stores clarification_return_path.",
          "created_at": "2026-04-02T10:00:00Z",
          "answered_at": "2026-04-02T10:05:00Z"
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(
            () => ClarificationSerializer.Deserialize(json));

        Assert.Equal(
            "Clarification items in state 'Answered' must contain answer and answered_at.",
            ex.Message);
    }

    [Fact]
    public void DeserializeAll_GivenAppliedClarificationWithoutAnsweredAt_ThrowsInvalidOperationException()
    {
        var json = """
        [
          {
            "artifact_kind": "clarification",
            "id": "clar-1",
            "execution_unit": "A2",
            "state": "open",
            "question": "Which queue field owns the return path?",
            "context": "The queue manager already stores clarification_return_path.",
            "created_at": "2026-04-02T10:00:00Z"
          },
          {
            "artifact_kind": "clarification",
            "id": "clar-2",
            "execution_unit": "A2",
            "state": "applied",
            "question": "Which queue field owns the return path?",
            "context": "The queue manager already stores clarification_return_path.",
            "answer": "Use execution_unit as the link key.",
            "created_at": "2026-04-02T10:00:00Z"
          }
        ]
        """;

        var ex = Assert.Throws<InvalidOperationException>(
            () => ClarificationSerializer.DeserializeAll(json));

        Assert.Equal(
            "Clarification items in state 'Applied' must contain answer and answered_at.",
            ex.Message);
    }

    [Fact]
    public void SerializeAllAndDeserializeAll_GivenMixedClarifications_RoundTripsCollection()
    {
        IReadOnlyList<ClarificationItem> items =
        [
            CreateOpenItem(),
            CreateOpenItem() with
            {
                Id = "clar-2",
                State = ClarificationState.Cancelled
            }
        ];

        var serialized = ClarificationSerializer.SerializeAll(items);
        var deserialized = ClarificationSerializer.DeserializeAll(serialized);

        Assert.Equal(2, deserialized.Count);
        Assert.Equal("clar-1", deserialized[0].Id);
        Assert.Equal(ClarificationState.Open, deserialized[0].State);
        Assert.Equal("clar-2", deserialized[1].Id);
        Assert.Equal(ClarificationState.Cancelled, deserialized[1].State);
    }

    private static ClarificationItem CreateOpenItem()
    {
        return new ClarificationItem
        {
            Id = "clar-1",
            ExecutionUnit = "A2",
            State = ClarificationState.Open,
            Question = "Which queue field owns the return path?",
            Context = "The queue manager already stores clarification_return_path.",
            CreatedAt = DateTimeOffset.Parse("2026-04-02T10:00:00Z")
        };
    }
}
