using System.Text.Json;
using IntentSystem.Projection.Serialization;

namespace IntentSystem.Projection.Tests;

public sealed class ProjectionSourceSerializerTests
{
    [Fact]
    public void Serialize_GivenCompleteBundle_ContainsRequiredFieldsWithSnakeCaseContract()
    {
        var bundle = ProjectionTestData.CreateBundle();

        var serialized = ProjectionSourceSerializer.Serialize(bundle);
        using var document = JsonDocument.Parse(serialized);
        var row = document.RootElement.GetProperty("row");
        var context = document.RootElement.GetProperty("context");

        Assert.Equal("G2", row.GetProperty("source_execution_unit").GetString());
        Assert.Equal("cli projection command", row.GetProperty("target_part").GetString());
        Assert.Equal("feature", context.GetProperty("issue_kind").GetString());
        Assert.Equal(
            ".takt/runs/20260403-122452-issue-31-g2-projection-generat/context/task/order.md",
            context.GetProperty("clarification_return_path").GetString());
        Assert.DoesNotContain("\"SourceExecutionUnit\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"IssueKind\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_GivenCompleteJson_RestoresRowAndContext()
    {
        var json = """
        {
          "row": {
            "source_execution_unit": "G2",
            "goal": "Generate projection packet artifacts from current source data.",
            "target_repo": "J-Tech-Japan/intent-system",
            "target_path": ".",
            "target_part": "cli projection command",
            "depends_on_subslices": ["G1", "A2"],
            "depends_on": [],
            "related_intents": ["intents/intent-cli/intent-tree/00-map.md"],
            "source_concepts": ["intents/rules/issue-projection-format.md"],
            "success_signal": "projection command can regenerate packet artifacts deterministically",
            "review_mode": "manual-review",
            "completion_action": "open-pr",
            "landing_policy": "squash"
          },
          "context": {
            "issue_title": "[G2] Projection Generate Command",
            "issue_kind": "feature",
            "parent_intent_root": "intents/intent-cli/intent-tree/00-map.md",
            "clarification_return_path": ".takt/runs/20260403-122452-issue-31-g2-projection-generat/context/task/order.md",
            "acceptance_criteria": ["projection generate writes implementation.md"],
            "deterministic_review_checks": ["artifact path stays under .intent-cli/issues/<execution-unit>/"],
            "verification_evidence": ["tests-passing"],
            "technical_baseline": ["C# / .NET"],
            "project_local_guide": ["AGENTS.md"],
            "intent_baseline": ["A2 is complete"],
            "additional_in_scope": ["generator wiring"],
            "out_of_scope": ["queue mutation"]
          }
        }
        """;

        var bundle = ProjectionSourceSerializer.Deserialize(json);

        Assert.Equal("G2", bundle.Row.SourceExecutionUnit);
        Assert.Equal("cli projection command", bundle.Row.TargetPart);
        Assert.Equal(["G1", "A2"], bundle.Row.DependsOnSubslices);
        Assert.Equal("[G2] Projection Generate Command", bundle.Context.IssueTitle);
        Assert.Equal(Models.IssueKind.Feature, bundle.Context.IssueKind);
        Assert.Equal(
            ".takt/runs/20260403-122452-issue-31-g2-projection-generat/context/task/order.md",
            bundle.Context.ClarificationReturnPath);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_ThrowsInvalidOperationException()
    {
        var json = """
        {
          "row": {
            "goal": "Generate projection packet artifacts from current source data."
          },
          "context": {
            "issue_title": "[G2] Projection Generate Command",
            "issue_kind": "feature",
            "parent_intent_root": "intents/intent-cli/intent-tree/00-map.md",
            "clarification_return_path": ".takt/runs/20260403-122452-issue-31-g2-projection-generat/context/task/order.md"
          }
        }
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => ProjectionSourceSerializer.Deserialize(json));

        Assert.Contains("required field", exception.Message, StringComparison.Ordinal);
        Assert.Contains("source_execution_unit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeAndDeserialize_GivenSameBundle_RoundTrips()
    {
        var bundle = ProjectionTestData.CreateBundle();

        var serialized = ProjectionSourceSerializer.Serialize(bundle);
        var deserialized = ProjectionSourceSerializer.Deserialize(serialized);

        Assert.Equal(bundle.Row, deserialized.Row);
        Assert.Equal(bundle.Context, deserialized.Context);
    }
}
