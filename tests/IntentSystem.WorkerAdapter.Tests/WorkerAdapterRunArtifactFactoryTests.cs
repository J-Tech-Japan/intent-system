using IntentSystem.WorkerAdapter.Models;
using IntentSystem.Workflow;
using IntentSystem.Workflow.Models;

namespace IntentSystem.WorkerAdapter.Tests;

public sealed class WorkerAdapterRunArtifactFactoryTests
{
    [Fact]
    public void CreateInitialResult_GivenRequestAndDefinition_ProducesCanonicalRunArtifactShape()
    {
        var definition = CreateDefinition();
        var request = WorkerAdapterRequestFactory.Create(
            "C2",
            ".intent-cli/workflows/C2.yaml",
            "/worktrees/intent-system");

        var result = WorkerAdapterRunArtifactFactory.CreateInitialResult(request, definition);

        Assert.Equal(WorkerAdapterRunStatus.Running, result.RunStatus);
        Assert.Equal(definition.Steps.Count, result.StepStatuses.Count);
        Assert.Equal(WorkflowStepKind.Implement, result.StepStatuses[0].Step);
        Assert.Equal(WorkerAdapterStepState.Running, result.StepStatuses[0].Status);
        Assert.All(result.StepStatuses.Skip(1), step => Assert.Equal(WorkerAdapterStepState.Pending, step.Status));
        Assert.Equal(WorkerReviewDisposition.Pending, result.ReviewResult.Disposition);
        Assert.Empty(result.ReviewCommentRefs);
        Assert.Empty(result.ClarificationRequests);
        Assert.Equal("Workflow run artifact initialized for C2.", result.ResultSummary);
        Assert.Equal([".intent-cli/workflows/C2.run.json"], result.RunLogRefs);
    }

    [Fact]
    public void CreateInitialResult_GivenMismatchedWorkflowDefinitionRef_ThrowsInvalidOperationException()
    {
        var definition = CreateDefinition();
        var request = WorkerAdapterRequestFactory.Create(
            "C2",
            ".intent-cli/workflows/C3.yaml",
            "/worktrees/intent-system");

        var exception = Assert.Throws<InvalidOperationException>(
            () => WorkerAdapterRunArtifactFactory.CreateInitialResult(request, definition));

        Assert.Contains("must match execution unit", exception.Message, StringComparison.Ordinal);
    }

    private static WorkflowDefinition CreateDefinition()
    {
        var roles = new WorkerRoles
        {
            Worker = "coder",
            Reviewer = "reviewer"
        };

        return new WorkflowDefinition
        {
            ExecutionUnit = "C2",
            PacketPaths = new WorkflowPacketPaths
            {
                Implementation = ".intent-cli/issues/C2/implementation.md",
                ReviewContext = ".intent-cli/issues/C2/review-context.md",
                Yaml = ".intent-cli/issues/C2/packet.yaml"
            },
            WorkerRoles = roles,
            DependencySnapshot = ["A1"],
            EntryConditions = ["A1 completed"],
            Steps = MvpWorkflowTemplate.CreateSteps(roles),
            SuccessSignal = "workflow render writes workflow artifact",
            ReviewMode = "deterministic-review",
            CompletionAction = "wait-for-deterministic-review"
        };
    }
}
