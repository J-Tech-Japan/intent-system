using IntentSystem.ConceptIntake.Models;
using IntentSystem.DogfoodingBridge.Models;
using IntentSystem.DomainBinding.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Workflow.Models;

namespace IntentSystem.DogfoodingBridge;

public static class DogfoodingBridgeMapper
{
    public static DogfoodingBridgeContract Create(
        ProjectionReadySlice binding,
        WorkflowDefinition workflowDefinition,
        string clarificationReturnPath,
        IReadOnlyList<InterviewQueueItem> interviewQueueItems)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(workflowDefinition);
        ArgumentException.ThrowIfNullOrWhiteSpace(clarificationReturnPath);
        ArgumentNullException.ThrowIfNull(interviewQueueItems);

        ValidateExecutionUnit(binding, workflowDefinition);

        var interviewReturnPaths = interviewQueueItems
            .SelectMany(item => item.ReturnToIntentPaths)
            .Select(RedactInterviewReturnPath)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new DogfoodingBridgeContract
        {
            Binding = binding,
            QueueInput = new QueueReadyDogfoodingInput
            {
                ExecutionUnit = binding.ExecutionUnit,
                PacketPaths = new PacketPaths
                {
                    Implementation = workflowDefinition.PacketPaths.Implementation,
                    ReviewContext = workflowDefinition.PacketPaths.ReviewContext,
                    Yaml = workflowDefinition.PacketPaths.Yaml
                },
                Dependencies = workflowDefinition.DependencySnapshot,
                ClarificationReturnPath = clarificationReturnPath,
                WorkerRole = workflowDefinition.WorkerRoles.Worker,
                ReviewRole = workflowDefinition.WorkerRoles.Reviewer
            },
            WorkflowInput = new WorkflowReadyDogfoodingInput
            {
                ExecutionUnit = workflowDefinition.ExecutionUnit,
                PacketPaths = workflowDefinition.PacketPaths,
                DependencySnapshot = workflowDefinition.DependencySnapshot,
                WorkerRoles = workflowDefinition.WorkerRoles,
                EntryConditions = workflowDefinition.EntryConditions,
                ReviewMode = workflowDefinition.ReviewMode,
                CompletionAction = workflowDefinition.CompletionAction
            },
            ReturnRoutes = new DogfoodingReturnRoutes
            {
                ClarificationReturnPath = clarificationReturnPath,
                InterviewReturnToIntentPaths = interviewReturnPaths
            }
        };
    }

    private static void ValidateExecutionUnit(
        ProjectionReadySlice binding,
        WorkflowDefinition workflowDefinition)
    {
        if (!string.Equals(binding.ExecutionUnit, workflowDefinition.ExecutionUnit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Binding execution unit '{binding.ExecutionUnit}' must match workflow execution unit '{workflowDefinition.ExecutionUnit}'.");
        }
    }

    private static string RedactInterviewReturnPath(string returnPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(returnPath);

        var token = Path.GetFileNameWithoutExtension(returnPath);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                $"Interview return path '{returnPath}' could not be converted to a public-safe token.");
        }

        return $"private-intent-ref:{token}";
    }
}
