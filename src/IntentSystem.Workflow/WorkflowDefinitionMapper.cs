using IntentSystem.Projection.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Workflow.Models;

namespace IntentSystem.Workflow;

public static class WorkflowDefinitionMapper
{
    public static WorkflowDefinition Map(QueueItem queueItem, ProjectionPacketContract packetContract)
    {
        ArgumentNullException.ThrowIfNull(queueItem);
        ArgumentNullException.ThrowIfNull(packetContract);

        var implementationPacket = packetContract.ImplementationIssuePacket;
        var reviewContextPacket = packetContract.ReviewContextPacket;

        if (!string.Equals(queueItem.ExecutionUnit, implementationPacket.SourceExecutionUnit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Queue item execution unit '{queueItem.ExecutionUnit}' must match packet execution unit '{implementationPacket.SourceExecutionUnit}'.");
        }

        if (!string.Equals(
                implementationPacket.SourceExecutionUnit,
                reviewContextPacket.SourceExecutionUnit,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Implementation packet execution unit must match review context execution unit.");
        }

        var workerRoles = new WorkerRoles
        {
            Worker = queueItem.WorkerRole,
            Reviewer = queueItem.ReviewRole
        };

        return new WorkflowDefinition
        {
            ExecutionUnit = queueItem.ExecutionUnit,
            PacketPaths = new WorkflowPacketPaths
            {
                Implementation = queueItem.PacketPaths.Implementation,
                ReviewContext = queueItem.PacketPaths.ReviewContext,
                Yaml = queueItem.PacketPaths.Yaml
            },
            WorkerRoles = workerRoles,
            DependencySnapshot = queueItem.Dependencies.ToArray(),
            EntryConditions = queueItem.Dependencies
                .Select(dependency => $"{dependency} completed")
                .ToArray(),
            Steps = MvpWorkflowTemplate.CreateSteps(workerRoles),
            SuccessSignal = SelectSuccessSignal(implementationPacket),
            ReviewMode = implementationPacket.ReviewMode,
            CompletionAction = implementationPacket.CompletionAction
        };
    }

    private static string SelectSuccessSignal(ImplementationIssuePacket implementationPacket)
    {
        if (implementationPacket.AcceptanceCriteria.Count > 0)
        {
            return implementationPacket.AcceptanceCriteria[0];
        }

        return implementationPacket.Goal;
    }
}
