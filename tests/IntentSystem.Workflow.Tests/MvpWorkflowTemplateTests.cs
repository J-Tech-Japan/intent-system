using IntentSystem.Workflow.Models;

namespace IntentSystem.Workflow.Tests;

public sealed class MvpWorkflowTemplateTests
{
    [Fact]
    public void CreateSteps_ReturnsAllSevenMvpStepKinds()
    {
        var roles = new WorkerRoles { Worker = "claude", Reviewer = "human" };

        var steps = MvpWorkflowTemplate.CreateSteps(roles);

        var kinds = steps.Select(s => s.Kind).ToArray();
        Assert.Contains(WorkflowStepKind.Implement, kinds);
        Assert.Contains(WorkflowStepKind.Review, kinds);
        Assert.Contains(WorkflowStepKind.CommentFindings, kinds);
        Assert.Contains(WorkflowStepKind.Fix, kinds);
        Assert.Contains(WorkflowStepKind.Rereview, kinds);
        Assert.Contains(WorkflowStepKind.Clarify, kinds);
        Assert.Contains(WorkflowStepKind.Complete, kinds);
    }

    [Fact]
    public void CreateSteps_ImplementOnSuccess_LeadsToReview()
    {
        var roles = new WorkerRoles { Worker = "claude", Reviewer = "human" };

        var steps = MvpWorkflowTemplate.CreateSteps(roles);
        var implement = steps.First(s => s.Kind == WorkflowStepKind.Implement);

        Assert.Equal([WorkflowStepKind.Review], implement.OnSuccess);
    }

    [Fact]
    public void CreateSteps_ReviewOnFailure_LeadsToCommentFindings()
    {
        var roles = new WorkerRoles { Worker = "claude", Reviewer = "human" };

        var steps = MvpWorkflowTemplate.CreateSteps(roles);
        var review = steps.First(s => s.Kind == WorkflowStepKind.Review);

        Assert.Equal([WorkflowStepKind.Complete], review.OnSuccess);
        Assert.Equal([WorkflowStepKind.CommentFindings], review.OnFailure);
    }

    [Fact]
    public void CreateSteps_RepairInPlaceCycle_CommentFindingsFixRereview()
    {
        var roles = new WorkerRoles { Worker = "claude", Reviewer = "human" };

        var steps = MvpWorkflowTemplate.CreateSteps(roles);
        var commentFindings = steps.First(s => s.Kind == WorkflowStepKind.CommentFindings);
        var fix = steps.First(s => s.Kind == WorkflowStepKind.Fix);
        var rereview = steps.First(s => s.Kind == WorkflowStepKind.Rereview);

        Assert.Equal([WorkflowStepKind.Fix], commentFindings.OnSuccess);
        Assert.Equal([WorkflowStepKind.Rereview], fix.OnSuccess);
        Assert.Equal([WorkflowStepKind.Complete], rereview.OnSuccess);
        Assert.Equal([WorkflowStepKind.CommentFindings], rereview.OnFailure);
    }

    [Fact]
    public void CreateSteps_ClarifyOnSuccess_ReturnsToReview()
    {
        var roles = new WorkerRoles { Worker = "claude", Reviewer = "human" };

        var steps = MvpWorkflowTemplate.CreateSteps(roles);
        var clarify = steps.First(s => s.Kind == WorkflowStepKind.Clarify);

        Assert.Equal([WorkflowStepKind.Review], clarify.OnSuccess);
    }

    [Fact]
    public void CreateSteps_CompleteHasNoTransitions()
    {
        var roles = new WorkerRoles { Worker = "claude", Reviewer = "human" };

        var steps = MvpWorkflowTemplate.CreateSteps(roles);
        var complete = steps.First(s => s.Kind == WorkflowStepKind.Complete);

        Assert.Empty(complete.OnSuccess);
        Assert.Empty(complete.OnFailure);
    }

    [Fact]
    public void CreateSteps_AssignsCorrectRoles()
    {
        var roles = new WorkerRoles { Worker = "claude", Reviewer = "human" };

        var steps = MvpWorkflowTemplate.CreateSteps(roles);

        Assert.Equal("claude", steps.First(s => s.Kind == WorkflowStepKind.Implement).Role);
        Assert.Equal("human", steps.First(s => s.Kind == WorkflowStepKind.Review).Role);
        Assert.Equal("human", steps.First(s => s.Kind == WorkflowStepKind.CommentFindings).Role);
        Assert.Equal("claude", steps.First(s => s.Kind == WorkflowStepKind.Fix).Role);
        Assert.Equal("human", steps.First(s => s.Kind == WorkflowStepKind.Rereview).Role);
    }
}
