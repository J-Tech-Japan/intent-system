using System.Text.Json;
using IntentSystem.Clarify.Models;
using IntentSystem.Clarify.Serialization;

namespace IntentSystem.Clarify.Tests;

public sealed class ClarificationSerializerTests
{
    [Fact]
    public void Serialize_GivenOpenClarification_ContainsAllRequiredContractFields()
    {
        var item = CreateOpenItem();

        var serialized = ClarificationSerializer.Serialize(item);
        using var document = JsonDocument.Parse(serialized);
        var root = document.RootElement;

        Assert.Equal("clarification", root.GetProperty("artifact_kind").GetString());
        Assert.Equal("clar-1", root.GetProperty("question_id").GetString());
        Assert.Equal("review", root.GetProperty("clarification_source").GetString());
        Assert.Equal("Which queue field owns the return path?", root.GetProperty("question_text").GetString());
        Assert.Equal("Queue manager stores clarification_return_path but it is unclear which field is canonical.", root.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("affected_intents").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("affected_execution_units").ValueKind);
        Assert.Equal("blocking", root.GetProperty("blocking_or_nonblocking").GetString());
        Assert.Equal("intents/rules/issue-template-and-review-context.md", root.GetProperty("clarification_return_path").GetString());
        Assert.Equal("open", root.GetProperty("state").GetString());
    }

    [Fact]
    public void Serialize_GivenOpenClarification_UsesSnakeCaseAndOmitsNullFields()
    {
        var item = CreateOpenItem();

        var serialized = ClarificationSerializer.Serialize(item);

        Assert.DoesNotContain("\"artifactKind\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"questionId\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"questionText\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"answer\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"answered_at\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_GivenAnsweredClarificationJson_RestoresStateAndAnswerMetadata()
    {
        var json = """
        {
          "artifact_kind": "clarification",
          "clarification_source": "review",
          "question_id": "clar-1",
          "question_text": "Which queue field owns the return path?",
          "reason": "Queue manager stores clarification_return_path but it is unclear which field is canonical.",
          "affected_intents": ["intents/intent-cli/intent-tree/00-map.md"],
          "affected_execution_units": ["A2"],
          "blocking_or_nonblocking": "blocking",
          "clarification_return_path": "intents/rules/issue-template-and-review-context.md",
          "state": "answered",
          "created_at": "2026-04-02T10:00:00Z",
          "answer": "Keep return path on queue item and link artifact by execution_unit.",
          "answered_at": "2026-04-02T10:05:00Z"
        }
        """;

        var item = ClarificationSerializer.Deserialize(json);

        Assert.Equal("clarification", item.ArtifactKind);
        Assert.Equal("clar-1", item.QuestionId);
        Assert.Equal("review", item.ClarificationSource);
        Assert.Equal(ClarificationState.Answered, item.State);
        Assert.Equal("Keep return path on queue item and link artifact by execution_unit.", item.Answer);
        Assert.Equal(DateTimeOffset.Parse("2026-04-02T10:05:00Z"), item.AnsweredAt);
        Assert.Equal(["A2"], item.AffectedExecutionUnits);
        Assert.Equal("blocking", item.BlockingOrNonblocking);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredContractField_ThrowsInvalidOperationException()
    {
        var json = """
        {
          "artifact_kind": "clarification",
          "question_id": "clar-1",
          "question_text": "Which queue field owns the return path?",
          "state": "open",
          "created_at": "2026-04-02T10:00:00Z"
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(
            () => ClarificationSerializer.Deserialize(json));

        Assert.Contains("required contract field", ex.Message, StringComparison.Ordinal);
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
          "clarification_source": "review",
          "question_id": "clar-1",
          "question_text": "Which queue field owns the return path?",
          "reason": "Unclear ownership.",
          "affected_intents": [],
          "affected_execution_units": ["A2"],
          "blocking_or_nonblocking": "blocking",
          "clarification_return_path": "intents/rules/issue-template-and-review-context.md",
          "state": "answered",
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
            "clarification_source": "review",
            "question_id": "clar-1",
            "question_text": "Which queue field owns the return path?",
            "reason": "Unclear ownership.",
            "affected_intents": [],
            "affected_execution_units": ["A2"],
            "blocking_or_nonblocking": "blocking",
            "clarification_return_path": "intents/rules/issue-template-and-review-context.md",
            "state": "open",
            "created_at": "2026-04-02T10:00:00Z"
          },
          {
            "artifact_kind": "clarification",
            "clarification_source": "review",
            "question_id": "clar-2",
            "question_text": "Which queue field owns the return path?",
            "reason": "Unclear ownership.",
            "affected_intents": [],
            "affected_execution_units": ["A2"],
            "blocking_or_nonblocking": "nonblocking",
            "clarification_return_path": "intents/rules/issue-template-and-review-context.md",
            "state": "applied",
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
                QuestionId = "clar-2",
                State = ClarificationState.Cancelled
            }
        ];

        var serialized = ClarificationSerializer.SerializeAll(items);
        var deserialized = ClarificationSerializer.DeserializeAll(serialized);

        Assert.Equal(2, deserialized.Count);
        Assert.Equal("clar-1", deserialized[0].QuestionId);
        Assert.Equal(ClarificationState.Open, deserialized[0].State);
        Assert.Equal("clar-2", deserialized[1].QuestionId);
        Assert.Equal(ClarificationState.Cancelled, deserialized[1].State);
    }

    private static ClarificationItem CreateOpenItem()
    {
        return new ClarificationItem
        {
            ClarificationSource = "review",
            QuestionId = "clar-1",
            QuestionText = "Which queue field owns the return path?",
            Reason = "Queue manager stores clarification_return_path but it is unclear which field is canonical.",
            AffectedIntents = ["intents/intent-cli/intent-tree/00-map.md"],
            AffectedExecutionUnits = ["A2"],
            BlockingOrNonblocking = "blocking",
            ClarificationReturnPath = "intents/rules/issue-template-and-review-context.md",
            State = ClarificationState.Open,
            CreatedAt = DateTimeOffset.Parse("2026-04-02T10:00:00Z")
        };
    }
}
