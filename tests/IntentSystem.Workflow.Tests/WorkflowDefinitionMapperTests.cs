using IntentSystem.Projection.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Workflow.Models;

namespace IntentSystem.Workflow.Tests;

public sealed class WorkflowDefinitionMapperTests
{
    [Fact]
    public void Map_GivenQueueItemAndPacketContract_ProducesDefinitionUsingCurrentBaselineShape()
    {
        var definition = WorkflowDefinitionMapper.Map(CreateQueueItem(), CreatePacketContract("C2"));

        Assert.Equal("C2", definition.ExecutionUnit);
        Assert.Equal(".intent-cli/issues/C2/implementation.md", definition.PacketPaths.Implementation);
        Assert.Equal("coder", definition.WorkerRoles.Worker);
        Assert.Equal("reviewer", definition.WorkerRoles.Reviewer);
        Assert.Equal(["A1", "B1"], definition.DependencySnapshot);
        Assert.Equal(["A1 completed", "B1 completed"], definition.EntryConditions);
        Assert.Equal(7, definition.Steps.Count);
        Assert.Equal("workflow render writes workflow artifact", definition.SuccessSignal);
        Assert.Equal("deterministic-review", definition.ReviewMode);
        Assert.Equal("wait-for-deterministic-review", definition.CompletionAction);
    }

    [Fact]
    public void Map_GivenMismatchedExecutionUnit_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => WorkflowDefinitionMapper.Map(CreateQueueItem(), CreatePacketContract("C3")));

        Assert.Contains("must match packet execution unit", exception.Message, StringComparison.Ordinal);
    }

    private static QueueItem CreateQueueItem()
    {
        return new QueueItem
        {
            ExecutionUnit = "C2",
            Title = "Workflow render command",
            State = QueueItemState.Queued,
            Dependencies = ["A1", "B1"],
            BlockedBy = [],
            ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
            PacketPaths = new PacketPaths
            {
                Implementation = ".intent-cli/issues/C2/implementation.md",
                ReviewContext = ".intent-cli/issues/C2/review-context.md",
                Yaml = ".intent-cli/issues/C2/packet.yaml"
            },
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "high"
        };
    }

    private static ProjectionPacketContract CreatePacketContract(string executionUnit)
    {
        return new ProjectionPacketContract
        {
            ImplementationIssuePacket = new ImplementationIssuePacket
            {
                IssueTitle = $"[{executionUnit}] Workflow Render Command",
                IssueKind = IssueKind.Feature,
                SourceExecutionUnit = executionUnit,
                Goal = "Render workflow definition artifact from queue and packet sources.",
                InScope = ["cli workflow render command"],
                OutOfScope = ["workflow execution"],
                TargetRepo = "J-Tech-Japan/intent-system",
                TargetPath = ".",
                TargetPart = "cli workflow render command",
                Dependencies = ["G1", "B2", "C1", "C2"],
                TechnicalBaseline = ["C# / .NET"],
                ProjectLocalGuide = ["AGENTS.md"],
                IntentBaseline = ["C1 and C2 are fixed baselines"],
                IntentReferences = ["ICL.E.SLICES"],
                RulesAndSpecs = ["intents/intent-cli/specs/07-workflow-definition-and-takt-adapter.md"],
                AcceptanceCriteria =
                [
                    "workflow render writes workflow artifact",
                    "generated workflow artifact follows current definition contract"
                ],
                VerificationEvidence = ["contract-reviewed", "tests-passing", "acceptance-criteria-checked"],
                ReviewMode = "deterministic-review",
                CompletionAction = "wait-for-deterministic-review",
                LandingPolicy = "merge-after-review"
            },
            ReviewContextPacket = new ReviewContextPacket
            {
                SourceExecutionUnit = executionUnit,
                ParentIntentRoot = "intents/intent-cli/intent-tree/00-map.md",
                IntentReferences = ["ICL.E.SLICES"],
                RulesAndSpecs = ["intents/intent-cli/specs/07-workflow-definition-and-takt-adapter.md"],
                AcceptanceCriteria = ["workflow render writes workflow artifact"],
                DeterministicReviewChecks = ["definition shape stays canonical"],
                ClarificationReturnPath = "intents/intent-cli/clarifications/open.md"
            }
        };
    }
}
