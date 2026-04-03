using System.Text.Json;
using IntentSystem.Workflow.Models;
using IntentSystem.Workflow.Serialization;

namespace IntentSystem.Workflow.Tests;

public sealed class WorkflowDefinitionSerializerTests
{
    [Fact]
    public void Serialize_GivenCompleteDefinition_ContainsAllRequiredFields()
    {
        var definition = CreateDefinition();

        var serialized = WorkflowDefinitionSerializer.Serialize(definition);
        using var document = JsonDocument.Parse(serialized);
        var root = document.RootElement;

        Assert.Equal("A2", root.GetProperty("execution_unit").GetString());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("packet_paths").ValueKind);
        Assert.Equal(JsonValueKind.Object, root.GetProperty("worker_roles").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("dependency_snapshot").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("entry_conditions").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("steps").ValueKind);
        Assert.Equal("all tests pass", root.GetProperty("success_signal").GetString());
        Assert.Equal("manual-review", root.GetProperty("review_mode").GetString());
        Assert.Equal("open-pr", root.GetProperty("completion_action").GetString());
    }

    [Fact]
    public void Serialize_GivenCompleteDefinition_UsesSnakeCaseAndKebabCaseEnums()
    {
        var definition = CreateDefinition();

        var serialized = WorkflowDefinitionSerializer.Serialize(definition);

        Assert.DoesNotContain("\"executionUnit\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"packetPaths\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"workerRoles\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"implement\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"comment-findings\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_GivenMvpSteps_ContainsAllSevenStepKinds()
    {
        var definition = CreateDefinition();

        var serialized = WorkflowDefinitionSerializer.Serialize(definition);

        Assert.Contains("\"implement\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"review\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"comment-findings\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"fix\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"rereview\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"clarify\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"complete\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_GivenCompleteJson_RestoresAllFields()
    {
        var json = """
        {
          "execution_unit": "A2",
          "packet_paths": {
            "implementation": ".intent-cli/issues/a2/implementation.md",
            "review_context": ".intent-cli/issues/a2/review-context.md",
            "yaml": ".intent-cli/issues/a2/packet.yaml"
          },
          "worker_roles": {
            "worker": "claude",
            "reviewer": "human"
          },
          "dependency_snapshot": ["A1"],
          "entry_conditions": ["A1 completed"],
          "steps": [
            {
              "kind": "implement",
              "role": "claude",
              "on_success": ["review"],
              "on_failure": []
            }
          ],
          "success_signal": "all tests pass",
          "review_mode": "manual-review",
          "completion_action": "open-pr"
        }
        """;

        var definition = WorkflowDefinitionSerializer.Deserialize(json);

        Assert.Equal("A2", definition.ExecutionUnit);
        Assert.Equal(".intent-cli/issues/a2/implementation.md", definition.PacketPaths.Implementation);
        Assert.Equal("claude", definition.WorkerRoles.Worker);
        Assert.Equal(["A1"], definition.DependencySnapshot);
        Assert.Equal(["A1 completed"], definition.EntryConditions);
        Assert.Single(definition.Steps);
        Assert.Equal(WorkflowStepKind.Implement, definition.Steps[0].Kind);
        Assert.Equal("all tests pass", definition.SuccessSignal);
        Assert.Equal("manual-review", definition.ReviewMode);
        Assert.Equal("open-pr", definition.CompletionAction);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_ThrowsInvalidOperationException()
    {
        var json = """
        {
          "execution_unit": "A2",
          "packet_paths": {}
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(
            () => WorkflowDefinitionSerializer.Deserialize(json));

        Assert.Contains("required field", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeAndDeserialize_RoundTrips()
    {
        var definition = CreateDefinition();

        var serialized = WorkflowDefinitionSerializer.Serialize(definition);
        var deserialized = WorkflowDefinitionSerializer.Deserialize(serialized);

        Assert.Equal(definition.ExecutionUnit, deserialized.ExecutionUnit);
        Assert.Equal(definition.PacketPaths.Implementation, deserialized.PacketPaths.Implementation);
        Assert.Equal(definition.WorkerRoles.Worker, deserialized.WorkerRoles.Worker);
        Assert.Equal(definition.DependencySnapshot, deserialized.DependencySnapshot);
        Assert.Equal(definition.EntryConditions, deserialized.EntryConditions);
        Assert.Equal(definition.Steps.Count, deserialized.Steps.Count);
        Assert.Equal(definition.SuccessSignal, deserialized.SuccessSignal);
        Assert.Equal(definition.ReviewMode, deserialized.ReviewMode);
        Assert.Equal(definition.CompletionAction, deserialized.CompletionAction);
    }

    [Fact]
    public void Deserialize_GivenRepairInPlaceSteps_ParsesCommentFindingsAndRereview()
    {
        var json = """
        {
          "execution_unit": "A2",
          "packet_paths": {
            "implementation": ".intent-cli/issues/a2/implementation.md",
            "review_context": ".intent-cli/issues/a2/review-context.md",
            "yaml": ".intent-cli/issues/a2/packet.yaml"
          },
          "worker_roles": { "worker": "claude", "reviewer": "human" },
          "dependency_snapshot": [],
          "entry_conditions": [],
          "steps": [
            { "kind": "review", "role": "human", "on_success": ["complete"], "on_failure": ["comment-findings"] },
            { "kind": "comment-findings", "role": "human", "on_success": ["fix"], "on_failure": [] },
            { "kind": "fix", "role": "claude", "on_success": ["rereview"], "on_failure": [] },
            { "kind": "rereview", "role": "human", "on_success": ["complete"], "on_failure": ["comment-findings"] }
          ],
          "success_signal": "tests pass",
          "review_mode": "manual-review",
          "completion_action": "open-pr"
        }
        """;

        var definition = WorkflowDefinitionSerializer.Deserialize(json);

        Assert.Equal(4, definition.Steps.Count);
        Assert.Equal(WorkflowStepKind.Review, definition.Steps[0].Kind);
        Assert.Equal(WorkflowStepKind.CommentFindings, definition.Steps[1].Kind);
        Assert.Equal(WorkflowStepKind.Fix, definition.Steps[2].Kind);
        Assert.Equal(WorkflowStepKind.Rereview, definition.Steps[3].Kind);
        Assert.Equal([WorkflowStepKind.CommentFindings], definition.Steps[3].OnFailure);
    }

    private static WorkflowDefinition CreateDefinition()
    {
        var roles = new WorkerRoles { Worker = "claude", Reviewer = "human" };
        return new WorkflowDefinition
        {
            ExecutionUnit = "A2",
            PacketPaths = new WorkflowPacketPaths
            {
                Implementation = ".intent-cli/issues/a2/implementation.md",
                ReviewContext = ".intent-cli/issues/a2/review-context.md",
                Yaml = ".intent-cli/issues/a2/packet.yaml"
            },
            WorkerRoles = roles,
            DependencySnapshot = ["A1"],
            EntryConditions = ["A1 completed"],
            Steps = MvpWorkflowTemplate.CreateSteps(roles),
            SuccessSignal = "all tests pass",
            ReviewMode = "manual-review",
            CompletionAction = "open-pr"
        };
    }
}
