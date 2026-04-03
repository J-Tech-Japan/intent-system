using System.Text.Json;
using IntentSystem.DogfoodingBridge.Models;
using IntentSystem.DogfoodingBridge.Serialization;

namespace IntentSystem.DogfoodingBridge.Tests;

public sealed class DogfoodingBridgeSerializerTests
{
    [Fact]
    public void Serialize_GivenCompleteContract_ContainsQueueAndWorkflowReadyFields()
    {
        var contract = CreateBridgeContract();

        var serialized = DogfoodingBridgeSerializer.Serialize(contract);
        using var document = JsonDocument.Parse(serialized);
        var root = document.RootElement;

        Assert.Equal("F2", root.GetProperty("queue_input").GetProperty("execution_unit").GetString());
        Assert.Equal("F2", root.GetProperty("workflow_input").GetProperty("execution_unit").GetString());
        Assert.Equal("manual-review", root.GetProperty("workflow_input").GetProperty("review_mode").GetString());
        Assert.Equal("open-pr", root.GetProperty("workflow_input").GetProperty("completion_action").GetString());
        Assert.Equal(
            "intents/rules/issue-template-and-review-context.md",
            root.GetProperty("return_routes").GetProperty("clarification_return_path").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("return_routes").GetProperty("interview_return_to_intent_paths").ValueKind);
    }

    [Fact]
    public void Serialize_GivenCompleteContract_DoesNotLeakPrivateSourceDetailsOrQueuePolicyFields()
    {
        var contract = CreateBridgeContract();

        var serialized = DogfoodingBridgeSerializer.Serialize(contract);

        Assert.DoesNotContain("\"source_url\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"source_path\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"state\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"blocked_by\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("intents/private-domain/", serialized, StringComparison.Ordinal);
        Assert.Contains("private-intent-ref:backend", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_GivenCompleteJson_RestoresAllRequiredFields()
    {
        var json = """
        {
          "binding": {
            "execution_unit": "F2",
            "goal": "Bridge a bound private execution unit into queue-ready and workflow-ready input.",
            "target_repo": "J-Tech-Japan/intent-system",
            "target_path": ".",
            "target_part": "dogfooding bridge",
            "dependencies": ["C1", "D2", "E2", "F1"],
            "success_signal": "dogfooding run can be reconstructed from bridge input",
            "review_mode": "manual-review",
            "completion_action": "open-pr",
            "landing_policy": "squash",
            "dogfooding_track": "backend-first",
            "embedded_canonical_summary": "Backend-first private domain summary embedded for child-repo tests."
          },
          "queue_input": {
            "execution_unit": "F2",
            "packet_paths": {
              "implementation": ".intent-cli/issues/F2/implementation.md",
              "review_context": ".intent-cli/issues/F2/review-context.md",
              "yaml": ".intent-cli/issues/F2/packet.yaml"
            },
            "dependencies": ["C1", "D2", "E2", "F1"],
            "clarification_return_path": "intents/rules/issue-template-and-review-context.md",
            "worker_role": "coder",
            "review_role": "reviewer"
          },
          "workflow_input": {
            "execution_unit": "F2",
            "packet_paths": {
              "implementation": ".intent-cli/issues/F2/implementation.md",
              "review_context": ".intent-cli/issues/F2/review-context.md",
              "yaml": ".intent-cli/issues/F2/packet.yaml"
            },
            "dependency_snapshot": ["C1", "D2", "E2", "F1"],
            "worker_roles": {
              "worker": "coder",
              "reviewer": "reviewer"
            },
            "entry_conditions": ["dependency snapshot captured"],
            "review_mode": "manual-review",
            "completion_action": "open-pr"
          },
          "return_routes": {
            "clarification_return_path": "intents/rules/issue-template-and-review-context.md",
            "interview_return_to_intent_paths": [
              "private-intent-ref:backend",
              "private-intent-ref:shared"
            ]
          }
        }
        """;

        var contract = DogfoodingBridgeSerializer.Deserialize(json);

        Assert.Equal("F2", contract.Binding.ExecutionUnit);
        Assert.Equal("F2", contract.QueueInput.ExecutionUnit);
        Assert.Equal(["C1", "D2", "E2", "F1"], contract.WorkflowInput.DependencySnapshot);
        Assert.Equal("coder", contract.WorkflowInput.WorkerRoles.Worker);
        Assert.Equal(
            [
                "private-intent-ref:backend",
                "private-intent-ref:shared"
            ],
            contract.ReturnRoutes.InterviewReturnToIntentPaths);
    }

    [Fact]
    public void Deserialize_GivenMissingWorkflowField_ThrowsInvalidOperationException()
    {
        var json = """
        {
          "binding": {},
          "queue_input": {
            "execution_unit": "F2",
            "packet_paths": {},
            "dependencies": [],
            "clarification_return_path": "path.md",
            "worker_role": "coder",
            "review_role": "reviewer"
          },
          "workflow_input": {
            "execution_unit": "F2"
          },
          "return_routes": {
            "clarification_return_path": "path.md",
            "interview_return_to_intent_paths": []
          }
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(
            () => DogfoodingBridgeSerializer.Deserialize(json));

        Assert.Contains("workflow_input", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeAndDeserialize_RoundTrips()
    {
        var contract = CreateBridgeContract();

        var serialized = DogfoodingBridgeSerializer.Serialize(contract);
        var deserialized = DogfoodingBridgeSerializer.Deserialize(serialized);

        Assert.Equal(contract.Binding.ExecutionUnit, deserialized.Binding.ExecutionUnit);
        Assert.Equal(contract.QueueInput.PacketPaths.ReviewContext, deserialized.QueueInput.PacketPaths.ReviewContext);
        Assert.Equal(contract.WorkflowInput.ReviewMode, deserialized.WorkflowInput.ReviewMode);
        Assert.Equal(contract.ReturnRoutes.ClarificationReturnPath, deserialized.ReturnRoutes.ClarificationReturnPath);
    }

    private static DogfoodingBridgeContract CreateBridgeContract()
    {
        return new DogfoodingBridgeContract
        {
            Binding = new IntentSystem.DomainBinding.Models.ProjectionReadySlice
            {
                ExecutionUnit = "F2",
                Goal = "Bridge a bound private execution unit into queue-ready and workflow-ready input.",
                TargetRepo = "J-Tech-Japan/intent-system",
                TargetPath = ".",
                TargetPart = "dogfooding bridge",
                Dependencies = ["C1", "D2", "E2", "F1"],
                SuccessSignal = "dogfooding run can be reconstructed from bridge input",
                ReviewMode = "manual-review",
                CompletionAction = "open-pr",
                LandingPolicy = "squash",
                DogfoodingTrack = IntentSystem.DomainBinding.Models.DogfoodingTrack.BackendFirst,
                EmbeddedCanonicalSummary = "Backend-first private domain summary embedded for child-repo tests."
            },
            QueueInput = new QueueReadyDogfoodingInput
            {
                ExecutionUnit = "F2",
                PacketPaths = new IntentSystem.Supervisor.Models.PacketPaths
                {
                    Implementation = ".intent-cli/issues/F2/implementation.md",
                    ReviewContext = ".intent-cli/issues/F2/review-context.md",
                    Yaml = ".intent-cli/issues/F2/packet.yaml"
                },
                Dependencies = ["C1", "D2", "E2", "F1"],
                ClarificationReturnPath = "intents/rules/issue-template-and-review-context.md",
                WorkerRole = "coder",
                ReviewRole = "reviewer"
            },
            WorkflowInput = new WorkflowReadyDogfoodingInput
            {
                ExecutionUnit = "F2",
                PacketPaths = new IntentSystem.Workflow.Models.WorkflowPacketPaths
                {
                    Implementation = ".intent-cli/issues/F2/implementation.md",
                    ReviewContext = ".intent-cli/issues/F2/review-context.md",
                    Yaml = ".intent-cli/issues/F2/packet.yaml"
                },
                DependencySnapshot = ["C1", "D2", "E2", "F1"],
                WorkerRoles = new IntentSystem.Workflow.Models.WorkerRoles
                {
                    Worker = "coder",
                    Reviewer = "reviewer"
                },
                EntryConditions = ["dependency snapshot captured"],
                ReviewMode = "manual-review",
                CompletionAction = "open-pr"
            },
            ReturnRoutes = new DogfoodingReturnRoutes
            {
                ClarificationReturnPath = "intents/rules/issue-template-and-review-context.md",
                InterviewReturnToIntentPaths =
                [
                    "private-intent-ref:backend",
                    "private-intent-ref:shared"
                ]
            }
        };
    }
}
