using IntentSystem.ConceptIntake.Models;
using IntentSystem.DogfoodingBridge.Models;
using IntentSystem.DomainBinding.Models;
using IntentSystem.Workflow;
using IntentSystem.Workflow.Models;

namespace IntentSystem.DogfoodingBridge.Tests;

public sealed class DogfoodingBridgeMapperTests
{
    [Fact]
    public void Create_GivenBindingAndWorkflow_BuildsQueueAndWorkflowReadyInputs()
    {
        var binding = CreateBinding();
        var workflow = CreateWorkflowDefinition();
        var interviewItems = CreateInterviewItems();

        var bridge = DogfoodingBridgeMapper.Create(
            binding,
            workflow,
            "intents/rules/issue-template-and-review-context.md",
            interviewItems);

        Assert.Equal("F2", bridge.QueueInput.ExecutionUnit);
        Assert.Equal(".intent-cli/issues/F2/implementation.md", bridge.QueueInput.PacketPaths.Implementation);
        Assert.Equal(["C1", "D2", "E2", "F1"], bridge.QueueInput.Dependencies);
        Assert.Equal("coder", bridge.QueueInput.WorkerRole);
        Assert.Equal("reviewer", bridge.QueueInput.ReviewRole);

        Assert.Equal("F2", bridge.WorkflowInput.ExecutionUnit);
        Assert.Equal(["dependency snapshot captured"], bridge.WorkflowInput.EntryConditions);
        Assert.Equal("manual-review", bridge.WorkflowInput.ReviewMode);
        Assert.Equal("open-pr", bridge.WorkflowInput.CompletionAction);
    }

    [Fact]
    public void Create_GivenClarifyAndInterviewRoutes_DoesNotMixTheirReturnTargets()
    {
        var bridge = DogfoodingBridgeMapper.Create(
            CreateBinding(),
            CreateWorkflowDefinition(),
            "intents/rules/issue-template-and-review-context.md",
            CreateInterviewItems());

        Assert.Equal(
            "intents/rules/issue-template-and-review-context.md",
            bridge.ReturnRoutes.ClarificationReturnPath);
        Assert.Equal(
            [
                "private-intent-ref:backend",
                "private-intent-ref:shared"
            ],
            bridge.ReturnRoutes.InterviewReturnToIntentPaths);
        Assert.DoesNotContain(
            bridge.ReturnRoutes.InterviewReturnToIntentPaths,
            value => value.Contains("intents/private-domain/", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_GivenExecutionUnitMismatch_ThrowsInvalidOperationException()
    {
        var binding = CreateBinding();
        var workflow = CreateWorkflowDefinition() with
        {
            ExecutionUnit = "X9"
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => DogfoodingBridgeMapper.Create(
                binding,
                workflow,
                "intents/rules/issue-template-and-review-context.md",
                []));

        Assert.Contains("must match workflow execution unit", ex.Message, StringComparison.Ordinal);
    }

    private static ProjectionReadySlice CreateBinding()
    {
        return new ProjectionReadySlice
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
            DogfoodingTrack = DogfoodingTrack.BackendFirst,
            EmbeddedCanonicalSummary = "Backend-first private domain summary embedded for child-repo tests."
        };
    }

    private static WorkflowDefinition CreateWorkflowDefinition()
    {
        var roles = new WorkerRoles
        {
            Worker = "coder",
            Reviewer = "reviewer"
        };

        return new WorkflowDefinition
        {
            ExecutionUnit = "F2",
            PacketPaths = new WorkflowPacketPaths
            {
                Implementation = ".intent-cli/issues/F2/implementation.md",
                ReviewContext = ".intent-cli/issues/F2/review-context.md",
                Yaml = ".intent-cli/issues/F2/packet.yaml"
            },
            WorkerRoles = roles,
            DependencySnapshot = ["C1", "D2", "E2", "F1"],
            EntryConditions = ["dependency snapshot captured"],
            Steps = MvpWorkflowTemplate.CreateSteps(roles),
            SuccessSignal = "dogfooding run can be reconstructed from bridge input",
            ReviewMode = "manual-review",
            CompletionAction = "open-pr"
        };
    }

    private static IReadOnlyList<InterviewQueueItem> CreateInterviewItems()
    {
        return
        [
            new InterviewQueueItem
            {
                DomainSlug = "private-domain",
                SourceConceptRef = "embedded-summary",
                QuestionId = "iq-1",
                QuestionText = "What backend boundary should be dogfooded first?",
                Reason = "Need first trial path.",
                Affects = ["backend"],
                BlockingOrNonblocking = "blocking",
                Status = InterviewQueueItemStatus.Open,
                ReturnToIntentPaths =
                [
                    "intents/private-domain/intent-tree/backend.md",
                    "intents/private-domain/intent-tree/shared.md"
                ],
                CreatedAt = DateTimeOffset.Parse("2026-04-03T12:00:00Z")
            }
        ];
    }
}
