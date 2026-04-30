using System.Security.Cryptography;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class WorkerPrCommentPreflightCommandTests : IDisposable
{
    public WorkerPrCommentPreflightCommandTests()
    {
        WorkerPrCommentPreflightCommand.PrLookupFactory = null;
        WorkerPrCommentPreflightCommand.IssueLookupFactory = null;
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = null;
        WorkerPrCommentPreflightCommand.NestedProviderLauncher = null;
    }

    public void Dispose()
    {
        WorkerPrCommentPreflightCommand.PrLookupFactory = null;
        WorkerPrCommentPreflightCommand.IssueLookupFactory = null;
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = null;
        WorkerPrCommentPreflightCommand.NestedProviderLauncher = null;
    }

    [Fact]
    public void Execute_GivenPrWithUnresolvedActionableThread_ClassifiesAsRepairRequired()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 600);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: new[]
            {
                BuildThread(id: "t1", isResolved: false, comments: new[]
                {
                    BuildThreadComment(id: "c1", author: "alice",
                        body: "Please fix the empty-Related-Links case.")
                })
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "600", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.True(result.Actionable);
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.RepairRequired, result.Classification);
        Assert.Equal(WorkerPrCommentPreflightConstants.RecommendedActions.RepairPr, result.RecommendedAction);
        Assert.Single(result.ActionableComments);
        Assert.Equal(WorkerPrCommentPreflightConstants.CommentKinds.ReviewThread, result.ActionableComments[0].Kind);
        Assert.Equal("alice", result.ActionableComments[0].Author);
    }

    [Fact]
    public void Execute_GivenPrWithOnlyResolvedThreads_ClassifiesAsNoActionableComments()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 601);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: new[]
            {
                BuildThread(id: "t1", isResolved: true, comments: new[]
                {
                    BuildThreadComment(id: "c1", author: "alice", body: "fixme")
                }),
                BuildThread(id: "t2", isResolved: true, comments: new[]
                {
                    BuildThreadComment(id: "c2", author: "bob", body: "another")
                })
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "601", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.False(result.Actionable);
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.NoActionableComments, result.Classification);
        Assert.Equal(WorkerPrCommentPreflightConstants.RecommendedActions.NoAction, result.RecommendedAction);
        Assert.Empty(result.ActionableComments);
    }

    [Fact]
    public void Execute_GivenPrWithOnlyBotComments_ClassifiesAsNoActionableComments()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 602);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: new[]
            {
                BuildThread(id: "t1", isResolved: false, comments: new[]
                {
                    BuildThreadComment(id: "c1", author: "github-actions", body: "ci passed")
                })
            },
            comments: new[]
            {
                BuildIssueComment(id: "ic1", author: "dependabot", body: "bumping deps"),
                BuildIssueComment(id: "ic2", author: "Claude", body: "auto note")
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "602", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.NoActionableComments, result.Classification);
        Assert.Empty(result.ActionableComments);
    }

    [Fact]
    public void Execute_GivenPrWithOnlyAutoGeneratedSummaryMarkers_ClassifiesAsNoActionableComments()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 603);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: new[]
            {
                BuildThread(id: "t1", isResolved: false, comments: new[]
                {
                    BuildThreadComment(id: "c1", author: "alice",
                        body: "## Update — fix applied\n\ndid a thing")
                })
            },
            comments: new[]
            {
                BuildIssueComment(id: "ic1", author: "bob",
                    body: "🤖 Generated with [Claude Code](https://claude.com)"),
                BuildIssueComment(id: "ic2", author: "carol",
                    body: "## Summary\n\n- ships feature")
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "603", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.NoActionableComments, result.Classification);
        Assert.Empty(result.ActionableComments);
    }

    [Fact]
    public void Execute_GivenPrWithChangesRequestedReview_ClassifiesAsRepairRequired()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 604);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: Array.Empty<GitHubPrReviewThread>(),
            comments: Array.Empty<GitHubPrIssueComment>(),
            reviews: new[]
            {
                BuildReview(id: "r1", author: "alice", body: "Please address comments.",
                    state: "CHANGES_REQUESTED")
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "604", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.RepairRequired, result.Classification);
        Assert.Single(result.ActionableComments);
        Assert.Equal(WorkerPrCommentPreflightConstants.CommentKinds.Review, result.ActionableComments[0].Kind);
    }

    [Fact]
    public void Execute_GivenPrWithIntentPrRequestUpdate_ClassifiesAsRequestUpdatePending()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 605, state: "OPEN", title: "request-update + actionable",
            body: "Closes #100",
            labelNames: new[] { "intent-target", "intent-pr-request-update" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100, state: "OPEN", title: "Source", body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: new[]
            {
                BuildThread(id: "t1", isResolved: false, comments: new[]
                {
                    BuildThreadComment(id: "c1", author: "alice", body: "Please fix.")
                })
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "605", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.RequestUpdatePending, result.Classification);
        Assert.Equal(WorkerPrCommentPreflightConstants.RecommendedActions.WaitForWorkerUpdate, result.RecommendedAction);
    }

    [Fact]
    public void Execute_GivenPrWithIntentPrUpdateInProgress_ClassifiesAsUpdateInProgress()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 606, state: "OPEN", title: "update-in-progress",
            body: "Closes #100",
            labelNames: new[] { "intent-target", "intent-pr-update-in-progress" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100, state: "OPEN", title: "Source", body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments());

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "606", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.UpdateInProgress, result.Classification);
        Assert.Equal(WorkerPrCommentPreflightConstants.RecommendedActions.WaitForWorkerUpdate, result.RecommendedAction);
    }

    [Fact]
    public void Execute_GivenMergedPr_ClassifiesAsApprovedOrMerged()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 607, state: "MERGED", title: "merged",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            merged: true,
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100, state: "OPEN", title: "Source", body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments());

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "607", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.ApprovedOrMerged, result.Classification);
        Assert.Equal("merged", result.PrState);
    }

    [Fact]
    public void Execute_GivenPrWithIntentPrApproved_ClassifiesAsApprovedOrMerged()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 608, state: "OPEN", title: "approved",
            body: "Closes #100",
            labelNames: new[] { "intent-target", "intent-pr-approved" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100, state: "OPEN", title: "Source", body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments());

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "608", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.ApprovedOrMerged, result.Classification);
        Assert.Equal(WorkerPrCommentPreflightConstants.RecommendedActions.NoAction, result.RecommendedAction);
    }

    [Fact]
    public void Execute_GivenPrWithoutIntentTarget_ClassifiesAsMissingTargetLabel()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 609, state: "OPEN", title: "untargeted",
            body: "Closes #100",
            labelNames: Array.Empty<string>(),
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100, state: "OPEN", title: "Source", body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: new[]
            {
                BuildThread(id: "t1", isResolved: false, comments: new[]
                {
                    BuildThreadComment(id: "c1", author: "alice", body: "Please fix.")
                })
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "609", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.MissingTargetLabel, result.Classification);
        Assert.Equal(WorkerPrCommentPreflightConstants.RecommendedActions.DeclineWithSummary, result.RecommendedAction);
    }

    [Fact]
    public void Execute_GivenPrWithNoLinkedIssue_ClassifiesAsSourceIssueMissing()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 610, state: "OPEN", title: "no link",
            body: "no issue refs",
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: Array.Empty<int>()));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments());

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "610", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.SourceIssueMissing, result.Classification);
        Assert.Null(result.SourceIssue);
    }

    [Fact]
    public void Execute_GivenPrSourceIssueMissingIntentTarget_ClassifiesAsSourceIssueNotTarget()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 611, state: "OPEN", title: "source issue not target",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100, state: "OPEN", title: "Source", body: string.Empty,
            labelNames: new[] { "intent-pr-created" }));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments());

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "611", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.SourceIssueNotTarget, result.Classification);
        Assert.Equal(WorkerPrCommentPreflightConstants.RecommendedActions.LabelCleanupRequired, result.RecommendedAction);
    }

    [Fact]
    public void Execute_GivenPrCarriesIntentPrCreatedItself_ClassifiesAsSourceIssueNotTarget_LabelPolicyViolation()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 612, state: "OPEN", title: "PR carries intent-pr-created",
            body: "Closes #100",
            labelNames: new[] { "intent-target", "intent-pr-created" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100, state: "OPEN", title: "Source", body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments());

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "612", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.SourceIssueNotTarget, result.Classification);
        Assert.Contains(result.Reasons, r =>
            r.Contains("label-policy violation", StringComparison.Ordinal)
            && r.Contains("intent-pr-created", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenPrBodyReferencingDifferentRepo_ClassifiesAsTargetMismatch()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 613, state: "OPEN", title: "cross-repo",
            body: "Repository: SomeOther/Repo\n\nCloses #100",
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100, state: "OPEN", title: "Source", body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments());

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "613", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.TargetMismatch, result.Classification);
        Assert.Equal(WorkerPrCommentPreflightConstants.RecommendedActions.SwitchRepo, result.RecommendedAction);
    }

    [Fact]
    public void Execute_GivenClosedNotMergedPr_ClassifiesAsNonActionable()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 614, state: "CLOSED", title: "closed",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            closed: true,
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments());

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "614", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.NonActionable, result.Classification);
        Assert.Contains(result.Reasons, r => r.Contains("closed", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenFreshDraftPrNoReviewLabel_ClassifiesAsNonActionable()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 615, state: "OPEN", title: "fresh draft",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            isDraft: true,
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments());

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "615", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.NonActionable, result.Classification);
        Assert.Contains(result.Reasons, r => r.Contains("draft", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenJsonFormat_RoundTripsStably()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 616);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: new[]
            {
                BuildThread(id: "t1", isResolved: false, comments: new[]
                {
                    BuildThreadComment(id: "c1", author: "alice", body: "Please fix.")
                })
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "616", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var first = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        var serialized = JsonSerializer.Serialize(first);
        var second = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(serialized)!;

        Assert.Equal(first.Actionable, second.Actionable);
        Assert.Equal(first.Classification, second.Classification);
        Assert.Equal(first.Pr, second.Pr);
        Assert.Equal(first.Repo, second.Repo);
        Assert.Equal(first.Title, second.Title);
        Assert.Equal(first.PrState, second.PrState);
        Assert.Equal(first.IsDraft, second.IsDraft);
        Assert.Equal(first.Labels, second.Labels);
        Assert.Equal(first.SourceIssue, second.SourceIssue);
        Assert.Equal(first.SourceIssueLabels, second.SourceIssueLabels);
        Assert.Equal(first.ActionableComments.Count, second.ActionableComments.Count);
        Assert.Equal(first.Reasons, second.Reasons);
        Assert.Equal(first.RecommendedAction, second.RecommendedAction);
        Assert.Equal(first.SummaryLine, second.SummaryLine);
    }

    [Fact]
    public void Execute_GivenJsonFormat_FieldsMatchIssueAcceptanceCriteria()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 617);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments());

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "617", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        // Eight required fields named in the issue's minimum-JSON example.
        Assert.True(root.TryGetProperty("actionable", out _));
        Assert.True(root.TryGetProperty("classification", out _));
        Assert.True(root.TryGetProperty("pr", out _));
        Assert.True(root.TryGetProperty("repo", out _));
        Assert.True(root.TryGetProperty("sourceIssue", out var camelSource));
        Assert.True(root.TryGetProperty("actionableComments", out var camelComments));
        Assert.True(root.TryGetProperty("reasons", out _));
        Assert.True(root.TryGetProperty("recommendedAction", out var camelAction));

        // Both camelCase and snake_case keys present for the three aliased fields.
        Assert.True(root.TryGetProperty("source_issue", out var snakeSource));
        Assert.True(root.TryGetProperty("actionable_comments", out var snakeComments));
        Assert.True(root.TryGetProperty("recommended_action", out var snakeAction));

        Assert.Equal(snakeAction.GetString(), camelAction.GetString());
        Assert.Equal(snakeComments.GetArrayLength(), camelComments.GetArrayLength());
        if (snakeSource.ValueKind == JsonValueKind.Number)
        {
            Assert.Equal(snakeSource.GetInt32(), camelSource.GetInt32());
        }
    }

    [Fact]
    public void Execute_GivenJsonFormat_CamelCaseAliasesMatchIssueContract()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 618);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: new[]
            {
                BuildThread(id: "t1", isResolved: false, comments: new[]
                {
                    BuildThreadComment(id: "c1", author: "alice", body: "Please fix the thing.")
                })
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "618", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var stdout = writer.ToString();
        Assert.Contains("\"sourceIssue\"", stdout, StringComparison.Ordinal);
        Assert.Contains("\"actionableComments\"", stdout, StringComparison.Ordinal);
        Assert.Contains("\"recommendedAction\"", stdout, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("recommendedAction", out var camelAction));
        Assert.True(root.TryGetProperty("recommended_action", out var snakeAction));
        Assert.Equal(snakeAction.GetString(), camelAction.GetString());
        Assert.Equal(WorkerPrCommentPreflightConstants.RecommendedActions.RepairPr, camelAction.GetString());

        Assert.True(root.TryGetProperty("sourceIssue", out var camelSource));
        Assert.True(root.TryGetProperty("source_issue", out var snakeSource));
        Assert.Equal(snakeSource.GetInt32(), camelSource.GetInt32());

        Assert.True(root.TryGetProperty("actionableComments", out var camelComments));
        Assert.True(root.TryGetProperty("actionable_comments", out var snakeComments));
        Assert.Equal(snakeComments.GetArrayLength(), camelComments.GetArrayLength());
    }

    [Fact]
    public void Execute_GivenActionableThread_ExcerptIsTruncatedToFirst120Chars()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 619);
        var longBody = new string('x', 200);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: new[]
            {
                BuildThread(id: "t1", isResolved: false, comments: new[]
                {
                    BuildThreadComment(id: "c1", author: "alice", body: longBody)
                })
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "619", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Single(result.ActionableComments);
        Assert.Equal(120, result.ActionableComments[0].Excerpt.Length);
        Assert.Equal(new string('x', 120), result.ActionableComments[0].Excerpt);
    }

    [Fact]
    public void Execute_PrecedenceClosedBeatsActionableComments()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 620, state: "CLOSED", title: "closed with comments",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            closed: true,
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: new[]
            {
                BuildThread(id: "t1", isResolved: false, comments: new[]
                {
                    BuildThreadComment(id: "c1", author: "alice", body: "Please fix.")
                })
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "620", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.NonActionable, result.Classification);
    }

    [Fact]
    public void Execute_PrecedenceApprovedBeatsActionableComments()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 621, state: "OPEN", title: "approved with comments",
            body: "Closes #100",
            labelNames: new[] { "intent-target", "intent-pr-approved" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100, state: "OPEN", title: "Source", body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: new[]
            {
                BuildThread(id: "t1", isResolved: false, comments: new[]
                {
                    BuildThreadComment(id: "c1", author: "alice", body: "Please fix.")
                })
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "621", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.ApprovedOrMerged, result.Classification);
    }

    [Fact]
    public void Execute_PrecedenceUpdateInProgressBeatsActionableComments()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 622, state: "OPEN", title: "update + comments",
            body: "Closes #100",
            labelNames: new[] { "intent-target", "intent-pr-update-in-progress" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100, state: "OPEN", title: "Source", body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: new[]
            {
                BuildThread(id: "t1", isResolved: false, comments: new[]
                {
                    BuildThreadComment(id: "c1", author: "alice", body: "Please fix.")
                })
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "622", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.UpdateInProgress, result.Classification);
    }

    [Fact]
    public void Execute_PrecedenceMissingTargetLabelBeatsActionableComments()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 623, state: "OPEN", title: "no target + comments",
            body: "Closes #100",
            labelNames: Array.Empty<string>(),
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100, state: "OPEN", title: "Source", body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: new[]
            {
                BuildThread(id: "t1", isResolved: false, comments: new[]
                {
                    BuildThreadComment(id: "c1", author: "alice", body: "Please fix.")
                })
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "623", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.MissingTargetLabel, result.Classification);
    }

    [Fact]
    public void Execute_GivenCommentsLookupTransportFailure_ReturnsNonZero_ActionableErrorMessage()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 624, state: "OPEN", title: "comments throw",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () =>
            new ThrowingCommentsLookup("simulated comments lookup failure: gh exit 1");

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "624", "--format", "json" },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("simulated comments lookup failure", output, StringComparison.Ordinal);
        Assert.Contains("624", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenPrLookupTransportFailure_ReturnsNonZero_ActionableErrorMessage()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () =>
            new ThrowingPrLookup("simulated pr-lookup failure: gh pr view exited 1");

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "513", "--format", "json" },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("simulated pr-lookup failure", output, StringComparison.Ordinal);
        Assert.Contains("513", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenIssueLookupTransportFailure_ReturnsNonZero_ActionableErrorMessage()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 625, state: "OPEN", title: "issue throws",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments());
        WorkerPrCommentPreflightCommand.IssueLookupFactory = () =>
            new ThrowingIssueLookup("simulated issue-lookup failure: gh issue view exited 1");

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "625", "--format", "json" },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("simulated issue-lookup failure", output, StringComparison.Ordinal);
        Assert.Contains("100", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingRepoFlag_ReturnsNonZero()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--pr", "1" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingPrFlag_ReturnsNonZero()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--pr", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenNonNumericPr_ReturnsNonZero()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "xyz" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--pr", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsNonZero()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "1", "--bogus" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bogus", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverWritesToQueueStateOrRunsLog()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 626);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments());

        var queueStatePath = Path.Combine(workspace.RootPath, ".intent-cli", "queue-state.json");
        var runsLogPath = Path.Combine(workspace.RootPath, ".intent-cli", "runs.jsonl");
        File.WriteAllText(queueStatePath, "{\"queue\":[]}");
        File.WriteAllText(runsLogPath, "{\"event\":\"baseline\"}\n");

        var queueBefore = File.ReadAllBytes(queueStatePath);
        var runsBefore = File.ReadAllBytes(runsLogPath);

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "626", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var queueAfter = File.ReadAllBytes(queueStatePath);
        var runsAfter = File.ReadAllBytes(runsLogPath);
        Assert.Equal(queueBefore, queueAfter);
        Assert.Equal(runsBefore, runsAfter);
    }

    [Fact]
    public void Execute_NeverWritesAnyArtifactFile()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 627);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments());

        File.WriteAllText(Path.Combine(workspace.RootPath, "baseline.txt"), "baseline");
        var before = workspace.SnapshotWorkspace();

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "627", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var after = workspace.SnapshotWorkspace();
        Assert.Equal(before.Count, after.Count);
        foreach (var (path, hash) in before)
        {
            Assert.True(after.TryGetValue(path, out var afterHash),
                $"file disappeared during command execution: {path}");
            Assert.Equal(hash, afterHash);
        }
    }

    [Fact]
    public void Execute_NeverInvokesProvider_NoProcessStartInNewSurface()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        var invoked = false;
        WorkerPrCommentPreflightCommand.NestedProviderLauncher = () =>
        {
            invoked = true;
            return false;
        };
        SetTargetedPrAndIssue(prNumber: 628);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments());

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "628" },
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(invoked, "NestedProviderLauncher must never be invoked by the preflight command");

        var commandSource = File.ReadAllText(LocateSourceFile("WorkerPrCommentPreflightCommand.cs"));
        var analyzerSource = File.ReadAllText(LocateSourceFile("WorkerPrCommentPreflightAnalyzer.cs"));
        var resultSource = File.ReadAllText(LocateSourceFile("WorkerPrCommentPreflightResult.cs"));

        Assert.DoesNotContain("Process.Start(", commandSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start(", analyzerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start(", resultSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverMutatesAnyLabelOrPrOrThread_NoGhEditMergeCommentReviewCloseInAnalyzer()
    {
        var commandSource = File.ReadAllText(LocateSourceFile("WorkerPrCommentPreflightCommand.cs"));
        var analyzerSource = File.ReadAllText(LocateSourceFile("WorkerPrCommentPreflightAnalyzer.cs"));

        foreach (var literal in new[]
        {
            "gh issue edit",
            "gh pr edit",
            "gh pr merge",
            "gh pr close",
            "gh pr reopen",
            "gh pr comment",
            "gh pr review",
            "resolveReviewThread"
        })
        {
            Assert.DoesNotContain(literal, commandSource, StringComparison.Ordinal);
            Assert.DoesNotContain(literal, analyzerSource, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_GivenTextFormat_PrintsStableSummaryLine()
    {
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 629);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments());

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "629" },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Worker pr-comment-preflight for J-Tech-Japan/intent-system#629", output, StringComparison.Ordinal);
        Assert.Contains("classification=no-actionable-comments", output, StringComparison.Ordinal);
        Assert.Contains("actionable=false", output, StringComparison.Ordinal);
    }

    private static void SetTargetedPrAndIssue(int prNumber)
    {
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: prNumber,
            state: "OPEN",
            title: "Targeted PR",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100, state: "OPEN", title: "Source", body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));
    }

    private static string LocateSourceFile(string fileName)
    {
        var directory = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(directory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "src", "IntentSystem.Cli", "Commands", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate source file {fileName} from {directory}");
    }

    private static GitHubPrLookupResult BuildPr(
        int number,
        string state,
        string title,
        string body,
        string[] labelNames,
        bool isDraft = false,
        bool closed = false,
        bool merged = false,
        int[]? closingIssueNumbers = null)
    {
        var refs = (closingIssueNumbers ?? Array.Empty<int>())
            .Select(n => new GitHubPrClosingIssueReference { Number = n })
            .ToArray();

        return new GitHubPrLookupResult
        {
            Number = number,
            State = state,
            Title = title,
            Body = body,
            IsDraft = isDraft,
            Closed = closed,
            Merged = merged,
            MergedAt = merged ? "2025-01-01T00:00:00Z" : null,
            ClosedAt = (closed || merged) ? "2025-01-01T00:00:00Z" : null,
            Labels = labelNames.Select(n => new GitHubPrLabel { Name = n }).ToArray(),
            ClosingIssuesReferences = refs
        };
    }

    private static GitHubIssueLookupResult BuildIssue(
        int number,
        string state,
        string title,
        string body,
        string[] labelNames,
        bool closed = false)
    {
        return new GitHubIssueLookupResult
        {
            Number = number,
            State = state,
            Title = title,
            Body = body,
            Closed = closed,
            ClosedAt = closed ? "2025-01-01T00:00:00Z" : null,
            Labels = labelNames.Select(n => new GitHubIssueLabel { Name = n }).ToArray()
        };
    }

    private static GitHubPrCommentsLookupResult BuildComments(
        IReadOnlyList<GitHubPrReviewThread>? reviewThreads = null,
        IReadOnlyList<GitHubPrIssueComment>? comments = null,
        IReadOnlyList<GitHubPrReview>? reviews = null)
    {
        return new GitHubPrCommentsLookupResult
        {
            ReviewThreads = reviewThreads ?? Array.Empty<GitHubPrReviewThread>(),
            Comments = comments ?? Array.Empty<GitHubPrIssueComment>(),
            Reviews = reviews ?? Array.Empty<GitHubPrReview>()
        };
    }

    private static GitHubPrReviewThread BuildThread(
        string id,
        bool isResolved,
        IReadOnlyList<GitHubPrReviewThreadComment> comments)
    {
        return new GitHubPrReviewThread
        {
            Id = id,
            IsResolved = isResolved,
            Comments = comments
        };
    }

    private static GitHubPrReviewThreadComment BuildThreadComment(
        string id,
        string author,
        string body)
    {
        return new GitHubPrReviewThreadComment
        {
            Id = id,
            Author = author,
            Body = body
        };
    }

    private static GitHubPrIssueComment BuildIssueComment(
        string id,
        string author,
        string body)
    {
        return new GitHubPrIssueComment
        {
            Id = id,
            Author = author,
            Body = body,
            CreatedAt = "2025-01-01T00:00:00Z"
        };
    }

    private static GitHubPrReview BuildReview(
        string id,
        string author,
        string body,
        string state)
    {
        return new GitHubPrReview
        {
            Id = id,
            Author = author,
            Body = body,
            State = state,
            SubmittedAt = "2025-01-01T00:00:00Z"
        };
    }

    // -----------------------------------------------------------------------
    // PR #514 review fix (G204): the production `GhCliGitHubPrCommentsLookup`
    // adapter previously called `gh pr view --json reviewThreads`, which the
    // installed gh CLI rejects with `Unknown JSON field: "reviewThreads"`.
    // The adapter now splits the lookup into TWO read-only calls:
    //   - `gh pr view --json reviews,comments`         (supported subset)
    //   - `gh api graphql -f query=... reviewThreads`  (GraphQL chain)
    // The next four regressions lock both call shapes against accidental
    // re-introduction of the unsupported field. They exercise the adapter's
    // BuildXxxArguments builders directly so they catch the bug-protection
    // shape WITHOUT round-tripping a real gh call.
    // -----------------------------------------------------------------------

    [Fact]
    public void Adapter_PrViewArguments_DoNotRequestUnsupportedReviewThreadsField()
    {
        var arguments = GhCliGitHubPrCommentsLookup.BuildPrViewArguments("J-Tech-Japan/intent-system", 514);

        // The argument list MUST still pass --json with the supported subset
        // and MUST NOT contain the literal "reviewThreads" field anywhere.
        Assert.Contains("pr", arguments);
        Assert.Contains("view", arguments);
        Assert.Contains("--json", arguments);
        Assert.Contains("reviews,comments", arguments);
        Assert.DoesNotContain(arguments, a =>
            a.Contains("reviewThreads", StringComparison.Ordinal));
    }

    [Fact]
    public void Adapter_PrViewJsonFieldsConstant_DoesNotIncludeUnsupportedReviewThreads()
    {
        // The constant naming the supported gh-CLI subset must remain free of
        // the unsupported `reviewThreads` field. Locks the bug-protection at
        // the constant level.
        Assert.Equal("reviews,comments", GhCliGitHubPrCommentsLookup.PrViewJsonFields);
        Assert.DoesNotContain(
            "reviewThreads",
            GhCliGitHubPrCommentsLookup.PrViewJsonFields,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Adapter_GraphqlArguments_UseGhApiGraphqlAndIncludeReviewThreadsField()
    {
        var arguments = GhCliGitHubPrCommentsLookup.BuildGraphqlArguments("J-Tech-Japan/intent-system", 514);

        // The fallthrough call MUST be `gh api graphql` and MUST embed the
        // GraphQL `reviewThreads(` field name in the query body so the
        // documented connection on PullRequest is what we actually request.
        Assert.Equal("api", arguments[0]);
        Assert.Equal("graphql", arguments[1]);
        Assert.Contains(arguments, a => a.StartsWith("query=", StringComparison.Ordinal));
        Assert.Contains(arguments, a =>
            a.Contains("reviewThreads(", StringComparison.Ordinal));

        // And the variables MUST resolve to the input owner/repo/pr.
        Assert.Contains("owner=J-Tech-Japan", arguments);
        Assert.Contains("repo=intent-system", arguments);
        Assert.Contains("pr=514", arguments);
    }

    [Fact]
    public void Adapter_GraphqlQueryConstant_RetrievesIsResolvedAndAuthorLogin()
    {
        // The GraphQL query body must select the fields the analyzer relies
        // on: per-thread `isResolved`, per-comment `body`, and the comment
        // author `login`. Locks the GraphQL shape against accidental drift.
        var query = GhCliGitHubPrCommentsLookup.ReviewThreadsGraphqlQuery;

        Assert.Contains("reviewThreads(", query, StringComparison.Ordinal);
        Assert.Contains("isResolved", query, StringComparison.Ordinal);
        Assert.Contains("body", query, StringComparison.Ordinal);
        Assert.Contains("author{login}", query, StringComparison.Ordinal);
    }

    private sealed class FakePrLookup : IGitHubPrLookup
    {
        private readonly GitHubPrLookupResult payload;

        public FakePrLookup(GitHubPrLookupResult payload) { this.payload = payload; }

        public GitHubPrLookupResult Lookup(string repo, int prNumber) => payload;
    }

    private sealed class ThrowingPrLookup : IGitHubPrLookup
    {
        private readonly string message;
        public ThrowingPrLookup(string message) { this.message = message; }
        public GitHubPrLookupResult Lookup(string repo, int prNumber)
            => throw new InvalidOperationException(message);
    }

    private sealed class FakeIssueLookup : IGitHubIssueLookup
    {
        private readonly GitHubIssueLookupResult payload;
        public FakeIssueLookup(GitHubIssueLookupResult payload) { this.payload = payload; }
        public GitHubIssueLookupResult Lookup(string repo, int issueNumber) => payload;
    }

    private sealed class ThrowingIssueLookup : IGitHubIssueLookup
    {
        private readonly string message;
        public ThrowingIssueLookup(string message) { this.message = message; }
        public GitHubIssueLookupResult Lookup(string repo, int issueNumber)
            => throw new InvalidOperationException(message);
    }

    private sealed class FakeCommentsLookup : IGitHubPrCommentsLookup
    {
        private readonly GitHubPrCommentsLookupResult payload;
        public FakeCommentsLookup(GitHubPrCommentsLookupResult payload) { this.payload = payload; }
        public GitHubPrCommentsLookupResult Lookup(string repo, int prNumber) => payload;
    }

    private sealed class ThrowingCommentsLookup : IGitHubPrCommentsLookup
    {
        private readonly string message;
        public ThrowingCommentsLookup(string message) { this.message = message; }
        public GitHubPrCommentsLookupResult Lookup(string repo, int prNumber)
            => throw new InvalidOperationException(message);
    }

    private sealed class WorkerPrCommentPreflightWorkspace : IDisposable
    {
        public WorkerPrCommentPreflightWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("worker-pr-comment-preflight-tests-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli"
                    }
                }
            };
        }

        public string RootPath { get; }

        public CliContext Context { get; }

        public IReadOnlyDictionary<string, string> SnapshotWorkspace()
        {
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories))
            {
                var bytes = File.ReadAllBytes(path);
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                snapshot[path] = hash;
            }
            return snapshot;
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
