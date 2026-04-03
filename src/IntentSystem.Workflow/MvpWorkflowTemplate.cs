using IntentSystem.Workflow.Models;

namespace IntentSystem.Workflow;

/// <summary>
/// Provides the MVP workflow step template: implement → review → complete,
/// with repair-in-place cycle (comment-findings → fix → rereview) and
/// clarify branch for review-time blockers.
/// </summary>
public static class MvpWorkflowTemplate
{
    public static IReadOnlyList<WorkflowStep> CreateSteps(WorkerRoles roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        return
        [
            new WorkflowStep
            {
                Kind = WorkflowStepKind.Implement,
                Role = roles.Worker,
                OnSuccess = [WorkflowStepKind.Review],
                OnFailure = []
            },
            new WorkflowStep
            {
                Kind = WorkflowStepKind.Review,
                Role = roles.Reviewer,
                OnSuccess = [WorkflowStepKind.Complete],
                OnFailure = [WorkflowStepKind.CommentFindings]
            },
            new WorkflowStep
            {
                Kind = WorkflowStepKind.CommentFindings,
                Role = roles.Reviewer,
                OnSuccess = [WorkflowStepKind.Fix],
                OnFailure = []
            },
            new WorkflowStep
            {
                Kind = WorkflowStepKind.Fix,
                Role = roles.Worker,
                OnSuccess = [WorkflowStepKind.Rereview],
                OnFailure = []
            },
            new WorkflowStep
            {
                Kind = WorkflowStepKind.Rereview,
                Role = roles.Reviewer,
                OnSuccess = [WorkflowStepKind.Complete],
                OnFailure = [WorkflowStepKind.CommentFindings]
            },
            new WorkflowStep
            {
                Kind = WorkflowStepKind.Clarify,
                Role = roles.Reviewer,
                OnSuccess = [WorkflowStepKind.Review],
                OnFailure = []
            },
            new WorkflowStep
            {
                Kind = WorkflowStepKind.Complete,
                Role = roles.Worker,
                OnSuccess = [],
                OnFailure = []
            }
        ];
    }
}
