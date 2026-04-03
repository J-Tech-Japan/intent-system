namespace IntentSystem.Workflow.Models;

/// <summary>
/// MVP workflow step kinds for queue item execution.
/// </summary>
public enum WorkflowStepKind
{
    Implement,
    Review,
    CommentFindings,
    Fix,
    Rereview,
    Clarify,
    Complete
}
