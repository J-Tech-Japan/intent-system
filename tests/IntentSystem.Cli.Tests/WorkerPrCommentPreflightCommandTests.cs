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
    public void Execute_GivenPriorRepairRequestAndWorkerUpdateComments_DoesNotClassifyAsRepairRequired()
    {
        // G204 follow-up regression for #514 review:
        // After a worker update applied, the PR's history still contains the
        // host's prior deterministic review/rereview repair-request comments
        // and the worker's own "Update — fix applied" note. Once draft-state
        // gating no longer masks the comment pass, those already-addressed
        // comments must NOT classify the PR as `repair-required` and must
        // NOT appear in `ActionableComments` — otherwise the repair loop
        // chases its own history.
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 6041);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: Array.Empty<GitHubPrReviewThread>(),
            reviews: Array.Empty<GitHubPrReview>(),
            comments: new[]
            {
                // First-pass deterministic review (host automation/reviewer).
                BuildIssueComment(
                    id: "ic-prior-1",
                    author: "tomohisa",
                    body: "Deterministic review found one blocker to repair before closeout:\n\n- Adapter requested unsupported `reviewThreads` field on `gh pr view`.\n\nNarrow fix: split into supported call paths."),

                // Second-pass deterministic rereview (host automation/reviewer).
                BuildIssueComment(
                    id: "ic-prior-2",
                    author: "tomohisa",
                    body: "Deterministic rereview found one remaining blocker before closeout:\n\n- The reused PR lookup still requests `merged`.\n\nNarrow fix: derive merged from supported `state`."),

                // Worker update note posted by this loop after applying the fix.
                BuildIssueComment(
                    id: "ic-worker-update",
                    author: "claude-bot",
                    body: "Update — fix applied (commit `ccac873`).\n\nLive smoke verified, label swap to `intent-pr-rereview-ready`.")
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "6041", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;

        // Must NOT be classified as repair-required — these comments were
        // already addressed by the worker update.
        Assert.NotEqual(
            WorkerPrCommentPreflightConstants.Classifications.RepairRequired,
            result.Classification);

        // Specifically: classify as no-actionable-comments (PR is otherwise
        // healthy with intent-target only) and emit an empty actionable list.
        Assert.Equal(
            WorkerPrCommentPreflightConstants.Classifications.NoActionableComments,
            result.Classification);
        Assert.Empty(result.ActionableComments);

        // Belt-and-braces: none of the three filtered-out comment IDs may
        // leak through under any classification.
        Assert.DoesNotContain(result.ActionableComments, c => c.Id == "ic-prior-1");
        Assert.DoesNotContain(result.ActionableComments, c => c.Id == "ic-prior-2");
        Assert.DoesNotContain(result.ActionableComments, c => c.Id == "ic-worker-update");
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
    public void Execute_GivenRequestUpdateWithNoActionableComments_ClassifiesAsRequestUpdatePending()
    {
        // G392: a request-update PR with NO actionable comment is still the
        // genuine "reviewer set the label but there's no comment to act on yet"
        // wait — request-update-pending/actionable:false.
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 605, state: "OPEN", title: "request-update, no actionable comment",
            body: "Closes #100",
            labelNames: new[] { "intent-target", "intent-pr-request-update" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100, state: "OPEN", title: "Source", body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments());

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "605", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.RequestUpdatePending, result.Classification);
        Assert.False(result.Actionable);
        Assert.Equal(WorkerPrCommentPreflightConstants.RecommendedActions.WaitForWorkerUpdate, result.RecommendedAction);
    }

    [Fact]
    public void Execute_GivenRequestUpdateWithActionableComment_ClassifiesAsRepairRequired()
    {
        // G392: the contract change. A request-update PR that ALSO carries an
        // actionable review comment is genuinely repairable — preflight must no
        // longer short-circuit to a non-actionable request-update-pending wait,
        // but fall through to repair-required/actionable:true. This is what lets
        // `worker next-action` and `worker pr-comment-preflight` agree that such
        // a PR IS claimable child work.
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
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.RepairRequired, result.Classification);
        Assert.True(result.Actionable);
        Assert.Equal(WorkerPrCommentPreflightConstants.RecommendedActions.RepairPr, result.RecommendedAction);
        Assert.Single(result.ActionableComments);
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
    public void Execute_GivenClosedPrAndCommentsLookupThrows_StillClassifiesAsNonActionable()
    {
        // G370 regression: closed/draft PRs are state-level
        // non-actionable from the PR payload alone, so a transient
        // failure on `gh pr view --comments` (rate limit, permission
        // shortfall, network blip) MUST NOT flip the exit code. The
        // command must short-circuit before invoking the comments
        // lookup.
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 6151, state: "CLOSED", title: "closed",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            closed: true,
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () =>
            new ThrowingCommentsLookup("simulated comments-lookup failure: gh pr view --comments exited 1");

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "6151", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.NonActionable, result.Classification);
        Assert.Contains(result.Reasons, r => r.Contains("closed", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenFreshDraftPrAndCommentsLookupThrows_StillClassifiesAsNonActionable()
    {
        // G370 regression: a fresh draft (no `intent-pr-*` review-cycle
        // label) is state-level non-actionable. Comments lookup must
        // be skipped so a transport failure cannot flip exit 0 -> 1.
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 6152, state: "OPEN", title: "fresh draft",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            isDraft: true,
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () =>
            new ThrowingCommentsLookup("simulated comments-lookup failure: rate limit");

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "6152", "--format", "json" },
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

    // -----------------------------------------------------------------------
    // G353: host-artifact-repair-required classification
    // Comments that reference .intent-cli/** or intents/** must be escalated
    // to the host repair agent; the child worker must not attempt to fix them.
    // -----------------------------------------------------------------------

    [Fact]
    public void Execute_G353_AllCommentsTargetHostMetadata_ClassifiesAsHostArtifactRepairRequired()
    {
        // G353 regression: a comment asking to fix
        // `.intent-cli/issues/G347/github-body.md` (the host packet artifact)
        // must not be classified as `repair-required` for the child worker.
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 630);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: new[]
            {
                BuildThread(id: "t1", isResolved: false, comments: new[]
                {
                    BuildThreadComment(id: "c1", author: "tomohisa",
                        body: "The Related Links section is missing from `.intent-cli/issues/G347/github-body.md`. Please add it.")
                })
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "630", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.HostArtifactRepairRequired, result.Classification);
        Assert.False(result.Actionable, "host artifact repair must not be forwarded to child worker");
        Assert.Equal(WorkerPrCommentPreflightConstants.RecommendedActions.EscalateToHostRepair, result.RecommendedAction);
        // The comment appears in actionable_comments so the host agent knows what to repair.
        Assert.Single(result.ActionableComments);
        Assert.Contains(result.Reasons, r => r.Contains(".intent-cli/**", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_G353_CommentsTargetIntentsPath_ClassifiesAsHostArtifactRepairRequired()
    {
        // intents/** paths (clarifications, intent bodies) are also host metadata.
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 631);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            comments: new[]
            {
                BuildIssueComment(id: "ic1", author: "alice",
                    body: "Please update intents/intent-cli/clarifications/open.md with the resolution.")
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "631", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.HostArtifactRepairRequired, result.Classification);
        Assert.False(result.Actionable);
        Assert.Equal(WorkerPrCommentPreflightConstants.RecommendedActions.EscalateToHostRepair, result.RecommendedAction);
    }

    [Fact]
    public void Execute_G353_MixedComments_HostAndImpl_StillRepairRequired()
    {
        // When SOME comments target host metadata and SOME target implementation
        // code, the standard repair-required path applies — the child must still
        // address the implementation comments.
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 632);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: new[]
            {
                BuildThread(id: "t1", isResolved: false, comments: new[]
                {
                    BuildThreadComment(id: "c1", author: "alice",
                        body: "The test coverage for the adapter is missing. Please add a unit test.")
                }),
                BuildThread(id: "t2", isResolved: false, comments: new[]
                {
                    BuildThreadComment(id: "c2", author: "alice",
                        body: "Also update .intent-cli/issues/G347/github-body.md with the Related Links section.")
                })
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "632", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        // Mixed: some impl, some host → repair-required so child handles impl comments.
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.RepairRequired, result.Classification);
        Assert.True(result.Actionable);
        Assert.Equal(WorkerPrCommentPreflightConstants.RecommendedActions.RepairPr, result.RecommendedAction);
        Assert.Equal(2, result.ActionableComments.Count);
    }

    [Fact]
    public void Execute_G353_PureImplComment_NotAffectedByG353()
    {
        // A comment about implementation code must still route to repair-required.
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 633);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: new[]
            {
                BuildThread(id: "t1", isResolved: false, comments: new[]
                {
                    BuildThreadComment(id: "c1", author: "alice",
                        body: "The adapter factory is not disposed in the test. Please add a using statement.")
                })
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "633", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.RepairRequired, result.Classification);
        Assert.True(result.Actionable);
        Assert.NotEqual(
            WorkerPrCommentPreflightConstants.Classifications.HostArtifactRepairRequired,
            result.Classification);
    }

    [Fact]
    public void Execute_G353_HostArtifactRepairRequired_PrecedenceAfterStep8_BeforeStep9()
    {
        // G353 step is inserted between step 8 (source-issue-not-target) and
        // step 9 (repair-required). A PR that passes all guards 1–8 with ONLY
        // host metadata comments must land on host-artifact-repair-required,
        // not repair-required.
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 634);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            comments: new[]
            {
                BuildIssueComment(id: "ic1", author: "alice",
                    body: "Fix the publish artifact at .intent-cli/issues/G353/publish.yaml.")
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "634", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.HostArtifactRepairRequired, result.Classification);
        Assert.False(result.Actionable);
        // Step-8 guards (closed, approved, update-in-progress, missing-target, etc.)
        // must still dominate over step 8.5.
        Assert.NotEqual(
            WorkerPrCommentPreflightConstants.Classifications.NonActionable,
            result.Classification);
    }

    // -----------------------------------------------------------------------
    // G476: distinguish host-metadata evidence citations from edit targets.
    // A request-update comment that cites a packet path (.intent-cli/**) as
    // evidence but asks to edit implementation files must NOT be misclassified
    // as host-artifact-repair-required.
    // -----------------------------------------------------------------------

    [Fact]
    public void Execute_G476_PacketEvidenceCitationWithImplEditTarget_ClassifiesAsRepairRequired()
    {
        // The deadlock report: comment cites `.intent-cli/issues/E038/packet.yaml`
        // as evidence (G316 packet-aware review) but the requested change is in
        // implementation scripts. This must be repair-required/actionable so the
        // child loop can claim and fix it — not host-artifact-repair-required.
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 640);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: new[]
            {
                BuildThread(id: "t1", isResolved: false, comments: new[]
                {
                    BuildThreadComment(id: "c1", author: "tomohisa",
                        body: "According to `.intent-cli/issues/E038/packet.yaml`, update " +
                              "`scripts/reset-dev-db.ps1` and `scripts/start-dev.ps1` to honor the new flag.")
                })
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "640", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.RepairRequired, result.Classification);
        Assert.True(result.Actionable, "packet evidence citation must not deadlock an implementation repair");
        Assert.Equal(WorkerPrCommentPreflightConstants.RecommendedActions.RepairPr, result.RecommendedAction);

        // The result must explain the decision: the packet path is evidence, the
        // scripts are the requested edit targets.
        var comment = Assert.Single(result.ActionableComments);
        Assert.False(comment.TargetsHostMetadata);
        Assert.Contains("scripts/reset-dev-db.ps1", comment.RequestedEditPaths);
        Assert.Contains("scripts/start-dev.ps1", comment.RequestedEditPaths);
        Assert.Contains(".intent-cli/issues/E038/packet.yaml", comment.HostEvidencePaths);
        Assert.DoesNotContain(".intent-cli/issues/E038/packet.yaml", comment.RequestedEditPaths);
    }

    [Fact]
    public void Execute_G476_TrueHostArtifactEditRequest_StillHostArtifactRepairRequired()
    {
        // G353 must be preserved: a comment that genuinely asks to edit a host
        // artifact (no implementation edit target) stays host-artifact-repair-required.
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 641);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: new[]
            {
                BuildThread(id: "t1", isResolved: false, comments: new[]
                {
                    BuildThreadComment(id: "c1", author: "tomohisa",
                        body: "Please update `.intent-cli/issues/G347/github-body.md` with the Related Links section.")
                })
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "641", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.HostArtifactRepairRequired, result.Classification);
        Assert.False(result.Actionable);
        Assert.Equal(WorkerPrCommentPreflightConstants.RecommendedActions.EscalateToHostRepair, result.RecommendedAction);

        var comment = Assert.Single(result.ActionableComments);
        Assert.True(comment.TargetsHostMetadata);
        Assert.Contains(".intent-cli/issues/G347/github-body.md", comment.RequestedEditPaths);
        // The reason surfaces the host edit target for the host repair agent.
        Assert.Contains(result.Reasons, r => r.Contains(".intent-cli/issues/G347/github-body.md", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_G476_PacketEvidenceOnlyNoImplTarget_RemainsActionable()
    {
        // A comment that cites a packet path purely as evidence and uses repair
        // language but names no implementation path must not become
        // host-artifact-repair-required solely because of the `.intent-cli/`
        // text — it remains actionable (repair-required) since repair language
        // is present (G476 acceptance).
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        SetTargetedPrAndIssue(prNumber: 642);
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments(
            reviewThreads: new[]
            {
                BuildThread(id: "t1", isResolved: false, comments: new[]
                {
                    BuildThreadComment(id: "c1", author: "tomohisa",
                        body: "According to `.intent-cli/issues/E038/packet.yaml`, the reset flow is " +
                              "missing the new validation. Please add it before merge.")
                })
            }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "642", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrCommentPreflightConstants.Classifications.RepairRequired, result.Classification);
        Assert.True(result.Actionable);

        var comment = Assert.Single(result.ActionableComments);
        Assert.False(comment.TargetsHostMetadata);
        Assert.Empty(comment.RequestedEditPaths);
        Assert.Contains(".intent-cli/issues/E038/packet.yaml", comment.HostEvidencePaths);
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
    public void Execute_GivenParentHostMentionsOnlyInQuotedAndOutOfScopeContexts_DoesNotClassifyAsTargetMismatch()
    {
        // G204 follow-up regression for #514 third rereview:
        // PR #514's body mentions `parent-host` / `MyIntentHost` ONLY inside
        // backtick-quoted literals (enumerating the heuristic's own trigger
        // names) and inside an Out-Of-Scope / Confirmation / Test-plan
        // section. Those contexts must NOT trip `target-mismatch`; the PR
        // must classify by labels / source-issue / comments instead.
        const string realBodyShape = @"## Summary

- Adds `intent-cli worker pr-comment-preflight --repo <owner/repo> --pr <number>` under the existing `worker` group.
- First-match precedence rules:
  6. body/source-issue references different repo OR `submodules/...` OR `parent-host`/`MyIntentHost` literal → `target-mismatch`
  7. source-issue trace produces no candidate → `source-issue-missing`

## Out Of Scope

- This slice does not touch parent-host execution paths or MyIntentHost packets.

## Confirmation

- The command does NOT mutate parent-host state.
- The command does NOT post comments to MyIntentHost.

Closes #100
";
        using var workspace = new WorkerPrCommentPreflightWorkspace();
        WorkerPrCommentPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 6042,
            state: "OPEN",
            title: "G204 Add PR comment/thread preflight command",
            body: realBodyShape,
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrCommentPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100, state: "OPEN", title: "Source", body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));
        WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new FakeCommentsLookup(BuildComments());

        using var writer = new StringWriter();
        var exitCode = WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "6042", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrCommentPreflightResult>(writer.ToString())!;

        // Must NOT classify as target-mismatch — all parent-host/MyIntentHost
        // mentions are inside backtick-quoted literals or Out-Of-Scope /
        // Confirmation sections.
        Assert.NotEqual(
            WorkerPrCommentPreflightConstants.Classifications.TargetMismatch,
            result.Classification);

        // No mismatch reason should leak through.
        Assert.DoesNotContain(result.Reasons, r =>
            r.Contains("parent-host", StringComparison.OrdinalIgnoreCase)
            || r.Contains("MyIntentHost", StringComparison.Ordinal));

        // With no comments and the PR otherwise healthy (intent-target only,
        // source issue carries the expected labels), classification falls
        // through to no-actionable-comments.
        Assert.Equal(
            WorkerPrCommentPreflightConstants.Classifications.NoActionableComments,
            result.Classification);
    }

    [Fact]
    public void StripNonTargetingContexts_LeavesPlainProseParentHostMentionsIntact()
    {
        // Defensive: real targeting language (plain-prose execution-path
        // narration outside any quoted / Out-Of-Scope / Confirmation context)
        // must still survive the strip so `target-mismatch` continues to
        // fire for actually-misrouted PRs.
        const string realTargetingBody = @"## Summary

This PR runs in the parent-host repo and updates MyIntentHost packets.
";
        var stripped = WorkerPrReviewPreflightAnalyzer.StripNonTargetingContexts(realTargetingBody);

        Assert.Contains("parent-host", stripped, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MyIntentHost", stripped);
    }

    [Fact]
    public void StripNonTargetingContexts_RemovesBacktickQuotedAndOutOfScopeMentions()
    {
        // Locks the strip-helper contract at the helper level, independent
        // of the preflight pipeline. Mentions inside backtick spans, fenced
        // code blocks, and Out-Of-Scope / Confirmation sections must all
        // disappear; mentions in plain prose elsewhere must remain.
        const string mixedBody = @"## Summary

- `parent-host`/`MyIntentHost` literal → target-mismatch (quoted, must strip)

```
parent-host fenced code mention (must strip)
```

## Out Of Scope

- Parent-host execution paths (must strip)
- MyIntentHost packets (must strip)

## Confirmation

- Does NOT touch parent-host state (must strip)

## Architecture

This actually targets parent-host (must remain).
";
        var stripped = WorkerPrReviewPreflightAnalyzer.StripNonTargetingContexts(mixedBody);

        // Plain-prose mention under a non-stripped heading remains.
        Assert.Contains("This actually targets parent-host", stripped, StringComparison.Ordinal);

        // Stripped occurrences (Out Of Scope / Confirmation / fenced /
        // backtick) must not contribute the bare token in their original
        // contexts. We assert the surrounding strings are gone — if any
        // strip path regresses, this catches it.
        Assert.DoesNotContain("MyIntentHost packets", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("Does NOT touch parent-host state", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("parent-host fenced code mention", stripped, StringComparison.Ordinal);
    }

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

    [Fact]
    public void Adapter_ParsesRealShapePrViewCommentsJson_AuthorAsObjectAndCamelCaseKeys()
    {
        // G204 follow-up: the installed gh CLI emits `author` as an object
        // `{"login":"<user>"}` (not a bare string) and uses camelCase keys
        // `createdAt` / `submittedAt`. The adapter DTOs must deserialize that
        // real shape successfully — earlier in-memory-fake tests bypassed the
        // serializer so this regression is locked at the JSON-shape layer
        // without requiring live GitHub.
        const string realShape = """
        {
          "comments": [
            {
              "id": "IC_kwDO_real_id",
              "author": { "login": "tomohisa" },
              "authorAssociation": "MEMBER",
              "body": "Deterministic review note",
              "createdAt": "2026-04-30T01:19:48Z",
              "url": "https://github.com/example/repo/pull/1#issuecomment-1"
            }
          ],
          "reviews": [
            {
              "id": "PRR_kwDO_review_id",
              "author": { "login": "reviewer-bot" },
              "body": "approved",
              "state": "APPROVED",
              "submittedAt": "2026-04-30T01:18:00Z"
            }
          ]
        }
        """;

        var parsed = System.Text.Json.JsonSerializer.Deserialize<GitHubPrCommentsLookupResult>(realShape);

        Assert.NotNull(parsed);
        Assert.Single(parsed!.Comments);
        Assert.Equal("tomohisa", parsed.Comments[0].Author);
        Assert.Equal("Deterministic review note", parsed.Comments[0].Body);
        Assert.Equal("2026-04-30T01:19:48Z", parsed.Comments[0].CreatedAt);

        Assert.Single(parsed.Reviews);
        Assert.Equal("reviewer-bot", parsed.Reviews[0].Author);
        Assert.Equal("APPROVED", parsed.Reviews[0].State);
        Assert.Equal("2026-04-30T01:18:00Z", parsed.Reviews[0].SubmittedAt);
    }

    [Fact]
    public void Adapter_AuthorConverter_ToleratesNullAndBareStringAuthors()
    {
        // The author converter must accept the three live shapes:
        //   1. object       — {"login":"x"} (real gh output)
        //   2. bare string  — "x"            (legacy fixtures / tests)
        //   3. null         — author was deleted on GitHub
        // All three project to a flat string the analyzer can compare against.
        const string variants = """
        {
          "comments": [
            { "id": "a", "author": { "login": "obj-author" }, "body": "x", "createdAt": "t" },
            { "id": "b", "author": "string-author",            "body": "y", "createdAt": "t" },
            { "id": "c", "author": null,                        "body": "z", "createdAt": "t" }
          ]
        }
        """;

        var parsed = System.Text.Json.JsonSerializer.Deserialize<GitHubPrCommentsLookupResult>(variants);

        Assert.NotNull(parsed);
        Assert.Equal(3, parsed!.Comments.Count);
        Assert.Equal("obj-author", parsed.Comments[0].Author);
        Assert.Equal("string-author", parsed.Comments[1].Author);
        Assert.Equal(string.Empty, parsed.Comments[2].Author);
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
