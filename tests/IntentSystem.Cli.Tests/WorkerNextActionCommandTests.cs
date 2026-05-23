using System.Security.Cryptography;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G206: Tests for <c>intent-cli worker next-action</c>. Cover the four
/// priority cases (PR repair > issue-to-PR > none), the in-progress and
/// pr-created exclusions, the misplaced-PR-label warning, and the
/// no-mutation invariants.
/// </summary>
public sealed class WorkerNextActionCommandTests : IDisposable
{
    public WorkerNextActionCommandTests()
    {
        WorkerNextActionCommand.CandidateListerFactory = null;
        WorkerNextActionCommand.NestedProviderLauncher = null;
        // G392: install hermetic defaults for the shared pr-comment-preflight
        // consult so every Execute-based pr-comment-fix test stays offline. The
        // defaults return one genuinely-actionable (non-host-metadata) review
        // comment plus a properly-labeled source issue, so preflight reports
        // actionable:true and the selector keeps its pr-comment-fix choice.
        // Downgrade tests override CommentsLookupFactory locally.
        WorkerNextActionCommand.CommentsLookupFactory = () => new FakeCommentsLookup();
        WorkerNextActionCommand.IssueLookupFactory = () => new FakeIssueLookup();
    }

    public void Dispose()
    {
        WorkerNextActionCommand.CandidateListerFactory = null;
        WorkerNextActionCommand.NestedProviderLauncher = null;
        WorkerNextActionCommand.CommentsLookupFactory = null;
        WorkerNextActionCommand.IssueLookupFactory = null;
    }

    [Fact]
    public void Execute_GivenPrWithRequestUpdate_SelectsPrCommentFix()
    {
        using var workspace = new WorkerNextActionWorkspace();
        var lister = new FakeLister
        {
            Prs = new[]
            {
                BuildPr(514, "G204 PR", "https://github.com/J-Tech-Japan/intent-system/pull/514",
                    createdAt: "2026-04-30T00:00:00Z",
                    labels: new[] { "intent-target", "intent-pr-request-update" }),
            },
            Issues = new[]
            {
                BuildIssue(515, "G205 Issue", "https://github.com/J-Tech-Japan/intent-system/issues/515",
                    createdAt: "2026-04-30T01:00:00Z",
                    labels: new[] { "intent-target" }),
            },
        };
        WorkerNextActionCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.PrCommentFix, result.Action);
        Assert.Equal(514, result.Number);
        Assert.Equal(WorkerNextActionConstants.RecommendedWorkflows.PrCommentFix, result.RecommendedWorkflow);
        Assert.Equal(WorkerNextActionConstants.SourceClassifications.RepairRequired, result.SourceClassification);
        Assert.StartsWith("https://github.com/", result.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G392_RequestUpdatePrWithoutSourceIssue_IsNotSelectedAsPrCommentFix()
    {
        // AIC #3648 shape: intent-pr-request-update but no source issue (no
        // closing reference / no Closes ref). next-action must NOT return it as
        // claimable pr-comment-fix; instead a stable not-child-actionable wait.
        using var workspace = new WorkerNextActionWorkspace();
        WorkerNextActionCommand.CandidateListerFactory = () => new FakeLister
        {
            Prs = new[]
            {
                BuildPr(3648, "G5/G6 dependency PR", "https://github.com/J-Tech-Japan/intent-system/pull/3648",
                    createdAt: "2026-05-23T00:00:00Z",
                    labels: new[] { "intent-target", "intent-pr-request-update" },
                    hasSourceIssue: false),
            },
        };

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--github-only", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.NotEqual(WorkerNextActionConstants.Actions.PrCommentFix, result.Action);
        Assert.Equal(WorkerNextActionConstants.Actions.Wait, result.Action);
        Assert.Equal(3648, result.Number);
        Assert.Equal(
            WorkerNextActionConstants.SourceClassifications.RequestUpdateNotChildActionable,
            result.SourceClassification);
        Assert.Contains("no resolvable source issue", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G392_RequestUpdatePrWithoutSourceIssue_IsStableAcrossWakes()
    {
        // Repeated wakes for the #3648 shape must return the same stable
        // non-actionable classification (deterministic), never claimable work.
        var pr = BuildPr(3648, "dep PR", "https://github.com/J-Tech-Japan/intent-system/pull/3648",
            createdAt: "2026-05-23T00:00:00Z",
            labels: new[] { "intent-target", "intent-pr-request-update" },
            hasSourceIssue: false);

        var first = WorkerNextActionAnalyzer.Analyze(
            "J-Tech-Japan/intent-system", new[] { pr }, Array.Empty<GitHubAutomationIssueCandidate>());
        var second = WorkerNextActionAnalyzer.Analyze(
            "J-Tech-Japan/intent-system", new[] { pr }, Array.Empty<GitHubAutomationIssueCandidate>());

        Assert.Equal(WorkerNextActionConstants.Actions.Wait, first.Action);
        Assert.Equal(first.Action, second.Action);
        Assert.Equal(first.SourceClassification, second.SourceClassification);
        Assert.Equal(
            WorkerNextActionConstants.SourceClassifications.RequestUpdateNotChildActionable,
            first.SourceClassification);
    }

    [Fact]
    public void Execute_G392_RequestUpdatePrWithSourceIssue_StillSelectsPrCommentFix()
    {
        // Pure label/closing-ref selector layer: a request-update PR WITH a
        // Closes reference in the body is a candidate for narrow child repair,
        // so the analyzer selects pr-comment-fix. Whether that selection is
        // ACTUALLY claimable is reconciled at the command layer, which consults
        // `worker pr-comment-preflight` on the comments the selector cannot see
        // (covered by Execute_G392_PrCommentFixWith*_* below). This test pins
        // the analyzer's first-pass choice only.
        var pr = new GitHubAutomationPrCandidate
        {
            Number = 700,
            Title = "G300 narrow repair",
            Url = "https://github.com/J-Tech-Japan/intent-system/pull/700",
            CreatedAt = "2026-05-23T00:00:00Z",
            Body = "Addresses the review. Closes #699",
            Labels = new[] { "intent-target", "intent-pr-request-update" }
                .Select(n => new GitHubAutomationLabel { Name = n }).ToArray(),
        };

        var result = WorkerNextActionAnalyzer.Analyze(
            "J-Tech-Japan/intent-system", new[] { pr }, Array.Empty<GitHubAutomationIssueCandidate>());

        Assert.Equal(WorkerNextActionConstants.Actions.PrCommentFix, result.Action);
        Assert.Equal(700, result.Number);
    }

    [Fact]
    public void Execute_G392_IssueWorkStillWinsOverNotChildActionablePr()
    {
        // A non-child-actionable request-update PR must not starve genuine
        // issue-to-pr work — issue-to-pr keeps priority over the wait surfacing.
        var pr = BuildPr(3648, "dep PR", "https://github.com/J-Tech-Japan/intent-system/pull/3648",
            createdAt: "2026-05-23T00:00:00Z",
            labels: new[] { "intent-target", "intent-pr-request-update" },
            hasSourceIssue: false);
        var issue = BuildIssue(900, "Eligible", "https://github.com/J-Tech-Japan/intent-system/issues/900",
            createdAt: "2026-05-23T01:00:00Z",
            labels: new[] { "intent-target" });

        var result = WorkerNextActionAnalyzer.Analyze(
            "J-Tech-Japan/intent-system", new[] { pr }, new[] { issue });

        Assert.Equal(WorkerNextActionConstants.Actions.IssueToPr, result.Action);
        Assert.Equal(900, result.Number);
    }

    // ── G392: command-level shared pr-comment-preflight consult ─────────────
    // The label/closing-ref analyzer picks pr-comment-fix; the command then
    // consults the SAME classifier `worker pr-comment-preflight` uses (fetching
    // the comments + source issue the selector cannot see) and downgrades to a
    // stable `wait` whenever preflight reports actionable:false — so the two
    // surfaces never disagree on child-claimability.

    [Fact]
    public void Execute_G392_PrCommentFixWithActionableComment_StaysPrCommentFix()
    {
        // A source-issue-present request-update PR that ALSO has a genuine
        // (non-host-metadata) actionable review comment: preflight is
        // actionable:true, so the selector keeps pr-comment-fix and the two
        // surfaces agree.
        using var workspace = new WorkerNextActionWorkspace();
        WorkerNextActionCommand.CandidateListerFactory = () => new FakeLister
        {
            Prs = new[]
            {
                BuildPr(514, "Repair me", "https://github.com/J-Tech-Japan/intent-system/pull/514",
                    createdAt: "2026-05-23T00:00:00Z",
                    labels: new[] { "intent-target", "intent-pr-request-update" }),
            },
        };
        // default FakeCommentsLookup returns one actionable comment.

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--github-only", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.PrCommentFix, result.Action);
        Assert.Equal(514, result.Number);
        Assert.Equal(WorkerNextActionConstants.SourceClassifications.RepairRequired, result.SourceClassification);
    }

    [Fact]
    public void Execute_G392_PrCommentFixWithNoActionableComments_DowngradesToWait()
    {
        // Reviewer set intent-pr-request-update but there is no actionable
        // comment yet — preflight reports request-update-pending/actionable:false.
        // The selector must NOT hand the child loop a claimable pr-comment-fix;
        // it downgrades to a stable wait keyed to the preflight verdict.
        using var workspace = new WorkerNextActionWorkspace();
        WorkerNextActionCommand.CandidateListerFactory = () => new FakeLister
        {
            Prs = new[]
            {
                BuildPr(514, "Label set, no comment yet",
                    "https://github.com/J-Tech-Japan/intent-system/pull/514",
                    createdAt: "2026-05-23T00:00:00Z",
                    labels: new[] { "intent-target", "intent-pr-request-update" }),
            },
        };
        WorkerNextActionCommand.CommentsLookupFactory = () => new FakeCommentsLookup
        {
            Result = FakeCommentsLookup.NoActionableComments(),
        };

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--github-only", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.Wait, result.Action);
        Assert.Equal(514, result.Number);
        Assert.Equal(
            WorkerNextActionConstants.SourceClassifications.PrCommentPreflightNotActionable,
            result.SourceClassification);
        Assert.Contains("actionable=false", result.Reason, StringComparison.Ordinal);
        Assert.Contains(
            WorkerPrCommentPreflightConstants.Classifications.RequestUpdatePending,
            result.Reason,
            StringComparison.Ordinal);
        // The repair lane is still the recommended workflow for the held PR.
        Assert.Equal(WorkerNextActionConstants.RecommendedWorkflows.PrCommentFix, result.RecommendedWorkflow);
        // pr-comment-fix-only terminal-outcome fields are cleared for a wait.
        Assert.Null(result.MustCreatePr);
        Assert.Null(result.AllowedTerminalOutcomes);
        Assert.Null(result.ForbiddenTerminalOutcomes);
    }

    [Fact]
    public void Execute_G392_PrCommentFixWithHostMetadataOnlyComments_DowngradesToWait()
    {
        // Every actionable comment targets host metadata (intents/** or
        // .intent-cli/**): preflight reports host-artifact-repair-required/
        // actionable:false. The child loop must not claim it as pr-comment-fix.
        using var workspace = new WorkerNextActionWorkspace();
        WorkerNextActionCommand.CandidateListerFactory = () => new FakeLister
        {
            Prs = new[]
            {
                BuildPr(514, "Host-artifact feedback",
                    "https://github.com/J-Tech-Japan/intent-system/pull/514",
                    createdAt: "2026-05-23T00:00:00Z",
                    labels: new[] { "intent-target", "intent-pr-request-update" }),
            },
        };
        WorkerNextActionCommand.CommentsLookupFactory = () => new FakeCommentsLookup
        {
            Result = FakeCommentsLookup.HostMetadataOnly(),
        };

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--github-only", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.Wait, result.Action);
        Assert.Equal(
            WorkerNextActionConstants.SourceClassifications.PrCommentPreflightNotActionable,
            result.SourceClassification);
        Assert.Contains(
            WorkerPrCommentPreflightConstants.Classifications.HostArtifactRepairRequired,
            result.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G392_PreflightConsultLookupFailure_KeepsPrCommentFix()
    {
        // Fail-open: a transient comment-fetch failure must NOT abort the
        // selector nor flip its verdict. The analyzer's pr-comment-fix decision
        // (already source-issue-gated) stands; the worker still catches a
        // non-actionable PR post-claim via its terminal outcomes.
        using var workspace = new WorkerNextActionWorkspace();
        WorkerNextActionCommand.CandidateListerFactory = () => new FakeLister
        {
            Prs = new[]
            {
                BuildPr(514, "Repair me", "https://github.com/J-Tech-Japan/intent-system/pull/514",
                    createdAt: "2026-05-23T00:00:00Z",
                    labels: new[] { "intent-target", "intent-pr-request-update" }),
            },
        };
        WorkerNextActionCommand.CommentsLookupFactory = () => new ThrowingCommentsLookup();

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--github-only", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.PrCommentFix, result.Action);
        Assert.Equal(514, result.Number);
    }

    [Fact]
    public void Execute_GivenPrConvergedToRereviewReadyAfterAlreadyResolved_ReturnsNone()
    {
        // G372 selector regression: after a pr-comment-fix completes with
        // `already-resolved`, the PR carries intent-pr-rereview-ready and no
        // longer carries intent-pr-request-update / intent-pr-update-in-progress
        // (see WorkerCompleteAnalyzer). The selector MUST NOT re-pick such a
        // PR — this is the exact state PR #842 was stuck oscillating on
        // before the convergence fix. With no other candidates the action
        // must be `none`, not pr-comment-fix.
        using var workspace = new WorkerNextActionWorkspace();
        var lister = new FakeLister
        {
            Prs = new[]
            {
                BuildPr(842, "Converged PR (fix already present)",
                    "https://github.com/J-Tech-Japan/intent-system/pull/842",
                    createdAt: "2026-04-30T00:00:00Z",
                    labels: new[] { "intent-target", "intent-pr-rereview-ready" }),
            },
            Issues = Array.Empty<GitHubAutomationIssueCandidate>(),
        };
        WorkerNextActionCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--github-only", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.None, result.Action);
        Assert.Null(result.Number);
    }

    [Fact]
    public void Execute_GivenPrInUpdateInProgress_SurfacesWaitLease()
    {
        // G325 replaces the previous "fall through to issue-to-pr"
        // behavior with an explicit `wait` action so the active update
        // lease (intent-pr-update-in-progress) is visible to operators
        // and controllers instead of being masked as generic `none`
        // (or, as previously, leaking past the held PR to issue-to-pr,
        // which would let a second worker race the lease holder).
        using var workspace = new WorkerNextActionWorkspace();
        var lister = new FakeLister
        {
            Prs = new[]
            {
                BuildPr(514, "Already-claimed PR", "https://github.com/J-Tech-Japan/intent-system/pull/514",
                    createdAt: "2026-04-30T00:00:00Z",
                    labels: new[] { "intent-target", "intent-pr-request-update", "intent-pr-update-in-progress" }),
            },
            Issues = new[]
            {
                BuildIssue(515, "G205", "https://github.com/J-Tech-Japan/intent-system/issues/515",
                    createdAt: "2026-04-30T01:00:00Z",
                    labels: new[] { "intent-target" }),
            },
        };
        WorkerNextActionCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.Wait, result.Action);
        Assert.Equal(514, result.Number);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/514", result.Url);
        Assert.Equal(WorkerNextActionConstants.SourceClassifications.PrUpdateInProgress, result.SourceClassification);
        Assert.Equal(WorkerNextActionConstants.RecommendedWorkflows.PrCommentFix, result.RecommendedWorkflow);
        Assert.Contains("active PR update lease", result.Reason, StringComparison.Ordinal);
        // Issue-to-pr must NOT be surfaced while the lease is held.
        Assert.NotEqual(515, result.Number);
    }

    [Fact]
    public void Execute_GivenIssueWithIntentPrCreated_ExcludesItFromIssueToPrSelection()
    {
        using var workspace = new WorkerNextActionWorkspace();
        var lister = new FakeLister
        {
            Prs = Array.Empty<GitHubAutomationPrCandidate>(),
            Issues = new[]
            {
                // First (older) issue carries intent-pr-created — excluded.
                BuildIssue(513, "Already PR'd", "https://github.com/J-Tech-Japan/intent-system/issues/513",
                    createdAt: "2026-04-29T00:00:00Z",
                    labels: new[] { "intent-target", "intent-pr-created" }),
                // Eligible newer issue.
                BuildIssue(517, "Eligible", "https://github.com/J-Tech-Japan/intent-system/issues/517",
                    createdAt: "2026-04-30T00:00:00Z",
                    labels: new[] { "intent-target" }),
            },
        };
        WorkerNextActionCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.IssueToPr, result.Action);
        Assert.Equal(517, result.Number);
    }

    [Fact]
    public void Execute_GivenIssueInProgress_ExcludesItFromIssueToPrSelection()
    {
        using var workspace = new WorkerNextActionWorkspace();
        var lister = new FakeLister
        {
            Prs = Array.Empty<GitHubAutomationPrCandidate>(),
            Issues = new[]
            {
                BuildIssue(517, "In progress", "https://github.com/J-Tech-Japan/intent-system/issues/517",
                    createdAt: "2026-04-30T00:00:00Z",
                    labels: new[] { "intent-target", "intent-issue-in-progress" }),
            },
        };
        WorkerNextActionCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.None, result.Action);
        Assert.Null(result.Number);
    }

    [Fact]
    public void Execute_GivenNoCandidates_ReturnsNoneActionWithDeterministicReason()
    {
        using var workspace = new WorkerNextActionWorkspace();
        var lister = new FakeLister();
        WorkerNextActionCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.None, result.Action);
        Assert.Equal("no actionable coding automation target", result.Reason);
        Assert.Null(result.RecommendedWorkflow);
        Assert.Null(result.Number);
    }

    [Fact]
    public void Execute_GivenPrWithMisplacedIntentPrCreated_EmitsWarningAndExcludesIt()
    {
        using var workspace = new WorkerNextActionWorkspace();
        var lister = new FakeLister
        {
            Prs = new[]
            {
                // Misplaced label: intent-pr-created on the PR itself.
                BuildPr(600, "Misplaced", "https://github.com/J-Tech-Japan/intent-system/pull/600",
                    createdAt: "2026-04-30T00:00:00Z",
                    labels: new[] { "intent-target", "intent-pr-request-update", "intent-pr-created" }),
            },
            Issues = Array.Empty<GitHubAutomationIssueCandidate>(),
        };
        WorkerNextActionCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        // Misplaced PR is excluded → no eligible target → none.
        Assert.Equal(WorkerNextActionConstants.Actions.None, result.Action);
        Assert.Contains(result.Warnings, w =>
            w.Contains("PR #600", StringComparison.Ordinal)
            && w.Contains("intent-pr-created", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_PrRepairBeatsIssueToPrEvenWhenIssueIsOlder()
    {
        using var workspace = new WorkerNextActionWorkspace();
        var lister = new FakeLister
        {
            // Issue was created first (older) but priority is still PR repair.
            Issues = new[]
            {
                BuildIssue(100, "Old issue", "https://github.com/J-Tech-Japan/intent-system/issues/100",
                    createdAt: "2026-01-01T00:00:00Z",
                    labels: new[] { "intent-target" }),
            },
            Prs = new[]
            {
                BuildPr(900, "New PR repair", "https://github.com/J-Tech-Japan/intent-system/pull/900",
                    createdAt: "2026-04-30T00:00:00Z",
                    labels: new[] { "intent-target", "intent-pr-request-update" }),
            },
        };
        WorkerNextActionCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.PrCommentFix, result.Action);
        Assert.Equal(900, result.Number);
    }

    [Fact]
    public void Execute_PicksOldestPrWhenMultipleAreEligible()
    {
        using var workspace = new WorkerNextActionWorkspace();
        var lister = new FakeLister
        {
            Prs = new[]
            {
                BuildPr(901, "newer", "https://github.com/J-Tech-Japan/intent-system/pull/901",
                    createdAt: "2026-04-30T05:00:00Z",
                    labels: new[] { "intent-target", "intent-pr-request-update" }),
                BuildPr(900, "older", "https://github.com/J-Tech-Japan/intent-system/pull/900",
                    createdAt: "2026-04-29T05:00:00Z",
                    labels: new[] { "intent-target", "intent-pr-request-update" }),
            },
        };
        WorkerNextActionCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(900, result.Number);
    }

    [Fact]
    public void Execute_MissingRepo_ReturnsNonZero()
    {
        using var workspace = new WorkerNextActionWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            Array.Empty<string>(),
            writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--repo is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_TransportFailure_ReturnsNonZero()
    {
        using var workspace = new WorkerNextActionWorkspace();
        WorkerNextActionCommand.CandidateListerFactory =
            () => new ThrowingLister("simulated gh failure");

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--format", "json" },
            writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("simulated gh failure", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void JsonOutput_IncludesCamelCaseAliasesForRecommendedWorkflowAndSourceClassification()
    {
        using var workspace = new WorkerNextActionWorkspace();
        var lister = new FakeLister
        {
            Prs = new[]
            {
                BuildPr(514, "PR repair", "https://github.com/J-Tech-Japan/intent-system/pull/514",
                    createdAt: "2026-04-30T00:00:00Z",
                    labels: new[] { "intent-target", "intent-pr-request-update" }),
            },
        };
        WorkerNextActionCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var raw = writer.ToString();
        Assert.Contains("\"recommended_workflow\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"recommendedWorkflow\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"source_classification\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"sourceClassification\"", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Adapter_PrListArguments_RequestSupportedSubsetWithLabelFilter()
    {
        var args = GhCliGitHubAutomationCandidateLister.BuildPrListArguments(
            "J-Tech-Japan/intent-system",
            new[] { "intent-target" });

        Assert.Contains("pr", args);
        Assert.Contains("list", args);
        Assert.Contains("--repo", args);
        Assert.Contains("J-Tech-Japan/intent-system", args);
        Assert.Contains("--state", args);
        Assert.Contains("open", args);
        Assert.Contains("--json", args);
        Assert.Contains(GhCliGitHubAutomationCandidateLister.PrListJsonFields, args);
        Assert.Contains("--label", args);
        Assert.Contains("intent-target", args);
    }

    [Fact]
    public void Adapter_IssueListArguments_RequestSupportedSubsetWithLabelFilter()
    {
        var args = GhCliGitHubAutomationCandidateLister.BuildIssueListArguments(
            "J-Tech-Japan/intent-system",
            new[] { "intent-target" });

        Assert.Contains("issue", args);
        Assert.Contains("list", args);
        Assert.Contains("--repo", args);
        Assert.Contains("J-Tech-Japan/intent-system", args);
        Assert.Contains(GhCliGitHubAutomationCandidateLister.ListJsonFields, args);
        Assert.Contains("--label", args);
        Assert.Contains("intent-target", args);
    }

    [Fact]
    public void Execute_G389_IssueToPr_CarriesMustCreatePrAndTerminalOutcomes()
    {
        using var workspace = new WorkerNextActionWorkspace();
        WorkerNextActionCommand.CandidateListerFactory = () => new FakeLister
        {
            Issues = new[]
            {
                BuildIssue(517, "Eligible", "https://github.com/J-Tech-Japan/intent-system/issues/517",
                    createdAt: "2026-04-30T00:00:00Z",
                    labels: new[] { "intent-target" }),
            },
        };

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--github-only", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var root = JsonDocument.Parse(writer.ToString()).RootElement;
        Assert.Equal("issue-to-pr", root.GetProperty("action").GetString());
        Assert.True(root.GetProperty("must_create_pr").GetBoolean());
        var allowed = root.GetProperty("allowed_terminal_outcomes").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("pr-created", allowed);
        Assert.Contains("ambiguous-contract", allowed);
        var forbidden = root.GetProperty("forbidden_terminal_outcomes").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("local-commits-only", forbidden);
        Assert.Contains("lease-released-without-pr", forbidden);
    }

    [Fact]
    public void Execute_G389_PrCommentFix_MustCreatePrIsFalse_WithRepairTerminalOutcomes()
    {
        using var workspace = new WorkerNextActionWorkspace();
        WorkerNextActionCommand.CandidateListerFactory = () => new FakeLister
        {
            Prs = new[]
            {
                BuildPr(514, "Repair me", "https://github.com/J-Tech-Japan/intent-system/pull/514",
                    createdAt: "2026-04-30T00:00:00Z",
                    labels: new[] { "intent-target", "intent-pr-request-update" }),
            },
        };

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--github-only", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var root = JsonDocument.Parse(writer.ToString()).RootElement;
        Assert.Equal("pr-comment-fix", root.GetProperty("action").GetString());
        Assert.False(root.GetProperty("must_create_pr").GetBoolean());
        var allowed = root.GetProperty("allowed_terminal_outcomes").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("repair-pushed", allowed);
        Assert.Contains("host-artifact-repair-required", allowed);
    }

    [Fact]
    public void Adapter_DeserializeList_ValidStdout_MapsToTypedCandidates()
    {
        // G385: clean stdout still yields the same candidates after the
        // hardened boundary (no behavior change for legitimate output).
        const string stdout =
            "[{\"number\":78,\"title\":\"x\",\"url\":\"https://example.com/issue/78\","
            + "\"labels\":[{\"name\":\"intent-target\"}],\"state\":\"OPEN\"}]";

        var candidates = GhCliGitHubAutomationCandidateLister.DeserializeList<GitHubAutomationIssueCandidate>(
            stdout, "`gh issue list` for J-Tech-Japan/intent-system");

        var candidate = Assert.Single(candidates);
        Assert.Equal(78, candidate.Number);
    }

    [Fact]
    public void Adapter_DeserializeList_ContaminatedStdout_ThrowsClassifiedDiagnostic()
    {
        // G385: the JTJ_Estivo byte-366 shape — a valid array followed by a gh
        // update notice — must raise a classified, sanitized diagnostic, not a
        // raw JsonException.
        const string stdout =
            "[{\"number\":78}]A new release of gh is available: 2.40.0 → 2.50.0";

        var exception = Assert.Throws<InvalidOperationException>(
            () => GhCliGitHubAutomationCandidateLister.DeserializeList<GitHubAutomationIssueCandidate>(
                stdout, "`gh issue list` for J-Tech-Japan/JTJ_Estivo"));

        Assert.Contains(
            $"[{GitHubCliJsonBoundary.Classifications.GithubJsonInvalid}]",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains("J-Tech-Japan/JTJ_Estivo", exception.Message, StringComparison.Ordinal);
        Assert.Contains("worker next-action --github-only", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverInvokesNestedProviderLauncher()
    {
        using var workspace = new WorkerNextActionWorkspace();
        var launcherInvoked = false;
        WorkerNextActionCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };
        WorkerNextActionCommand.CandidateListerFactory = () => new FakeLister
        {
            Prs = new[]
            {
                BuildPr(514, "PR", "https://example.com/pr/514",
                    createdAt: "2026-04-30T00:00:00Z",
                    labels: new[] { "intent-target", "intent-pr-request-update" }),
            },
            Issues = new[]
            {
                BuildIssue(517, "Issue", "https://example.com/issue/517",
                    createdAt: "2026-04-30T01:00:00Z",
                    labels: new[] { "intent-target" }),
            },
        };

        using var writer = new StringWriter();
        // Walk both selection paths and the no-action path.
        Assert.Equal(0, WorkerNextActionCommand.Execute(workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--format", "json" },
            writer));

        WorkerNextActionCommand.CandidateListerFactory = () => new FakeLister();
        writer.GetStringBuilder().Clear();
        Assert.Equal(0, WorkerNextActionCommand.Execute(workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--format", "json" },
            writer));

        Assert.False(launcherInvoked,
            "WorkerNextActionCommand must never invoke NestedProviderLauncher.");
    }

    [Fact]
    public void Execute_LeavesIntentCliWorkspaceByteEquivalent()
    {
        using var workspace = new WorkerNextActionWorkspace();
        var before = workspace.SnapshotWorkspace();

        WorkerNextActionCommand.CandidateListerFactory = () => new FakeLister
        {
            Prs = new[]
            {
                BuildPr(514, "PR", "https://example.com/pr/514",
                    createdAt: "2026-04-30T00:00:00Z",
                    labels: new[] { "intent-target", "intent-pr-request-update" }),
            },
        };

        using (var writer = new StringWriter())
        {
            Assert.Equal(0, WorkerNextActionCommand.Execute(workspace.Context,
                new[] { "--repo", "J-Tech-Japan/intent-system", "--format", "json" },
                writer));
        }

        var after = workspace.SnapshotWorkspace();
        Assert.Equal(before.Count, after.Count);
        foreach (var (path, hash) in before)
        {
            Assert.True(after.TryGetValue(path, out var afterHash),
                $"file disappeared after run: {path}");
            Assert.Equal(hash, afterHash);
        }
    }

    [Fact]
    public void SourceScan_AnalyzerAndCommand_ContainNoProcessStartOrGhMutationLiterals()
    {
        // The analyzer and command files must remain pure: no Process.Start
        // and no gh CLI mutation literals in EXECUTABLE code. The lister
        // adapter is exempt (its job is to shell out to `gh pr list` /
        // `gh issue list` for read-only metadata). Doc-comment listings of
        // forbidden mutations are stripped before scanning so the
        // documentation can name what it forbids.
        var analyzer = StripCsharpComments(File.ReadAllText(LocateSourceFile("WorkerNextActionAnalyzer.cs")));
        var command = StripCsharpComments(File.ReadAllText(LocateSourceFile("WorkerNextActionCommand.cs")));
        var combined = analyzer + "\n" + command;

        Assert.DoesNotContain("Process.Start(", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh issue edit", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr edit", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr merge", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr close", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr reopen", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr comment", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr review", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("resolveReviewThread", combined, StringComparison.Ordinal);
    }

    // ── G281: --workdir is child worktree context only ──────────────────

    [Fact]
    public void Execute_GivenPrCommentFixWithWorkdirAndSourceIssuePrCreated_StillSelectsPrCommentFix()
    {
        using var workspace = new WorkerNextActionWorkspace();
        // Child worktree directory that does NOT contain .intent-cli — mimics
        // the operator's setup: parent host state lives in the cwd, the child
        // worktree is a separate git checkout.
        var childWorktree = Directory.CreateTempSubdirectory("g281-child-worktree-").FullName;
        Directory.CreateDirectory(Path.Combine(childWorktree, ".git"));
        Assert.False(Directory.Exists(Path.Combine(childWorktree, ".intent-cli")));

        try
        {
            var lister = new FakeLister
            {
                Prs = new[]
                {
                    BuildPr(660, "G278 PR with repair feedback",
                        "https://github.com/J-Tech-Japan/intent-system/pull/660",
                        createdAt: "2026-05-06T20:00:00Z",
                        labels: new[] { "intent-target", "intent-pr-request-update" }),
                },
                Issues = new[]
                {
                    // Source issue carries intent-pr-created and must NOT
                    // suppress PR comment-fix selection.
                    BuildIssue(659, "G278 source issue (already created)",
                        "https://github.com/J-Tech-Japan/intent-system/issues/659",
                        createdAt: "2026-05-06T19:00:00Z",
                        labels: new[] { "intent-target", "intent-pr-created" }),
                },
            };
            WorkerNextActionCommand.CandidateListerFactory = () => lister;

            using var writer = new StringWriter();
            var exitCode = WorkerNextActionCommand.Execute(
                workspace.Context,
                new[]
                {
                    "--repo", "J-Tech-Japan/intent-system",
                    "--workdir", childWorktree,
                    "--format", "json",
                },
                writer);

            Assert.Equal(0, exitCode);
            var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
            Assert.Equal(WorkerNextActionConstants.Actions.PrCommentFix, result.Action);
            Assert.Equal(660, result.Number);
            // No workdir-related warning when the workdir IS a git worktree.
            Assert.DoesNotContain(result.Warnings, w => w.Contains("workdir", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(childWorktree))
            {
                Directory.Delete(childWorktree, recursive: true);
            }
        }
    }

    [Fact]
    public void Execute_GivenWorkdirWithoutGitDirectory_EmitsWarningButStillSelectsPrCommentFix()
    {
        using var workspace = new WorkerNextActionWorkspace();
        var notAGitWorktree = Directory.CreateTempSubdirectory("g281-not-a-git-worktree-").FullName;

        try
        {
            var lister = new FakeLister
            {
                Prs = new[]
                {
                    BuildPr(660, "G278 PR with repair feedback",
                        "https://github.com/J-Tech-Japan/intent-system/pull/660",
                        createdAt: "2026-05-06T20:00:00Z",
                        labels: new[] { "intent-target", "intent-pr-request-update" }),
                },
            };
            WorkerNextActionCommand.CandidateListerFactory = () => lister;

            using var writer = new StringWriter();
            var exitCode = WorkerNextActionCommand.Execute(
                workspace.Context,
                new[]
                {
                    "--repo", "J-Tech-Japan/intent-system",
                    "--workdir", notAGitWorktree,
                    "--format", "json",
                },
                writer);

            Assert.Equal(0, exitCode);
            var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
            Assert.Equal(WorkerNextActionConstants.Actions.PrCommentFix, result.Action);
            Assert.Contains(result.Warnings, w =>
                w.Contains("not a git worktree", StringComparison.Ordinal)
                && w.Contains("selection used GitHub state from --repo only", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(notAGitWorktree))
            {
                Directory.Delete(notAGitWorktree, recursive: true);
            }
        }
    }

    [Fact]
    public void Execute_GivenWorkdirThatDoesNotExist_EmitsWarningButStillSelects()
    {
        using var workspace = new WorkerNextActionWorkspace();
        var missingWorktree = Path.Combine(Path.GetTempPath(), "g281-definitely-missing-" + Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(missingWorktree));

        var lister = new FakeLister
        {
            Issues = new[]
            {
                BuildIssue(700, "G300 ready", "https://github.com/J-Tech-Japan/intent-system/issues/700",
                    createdAt: "2026-05-07T00:00:00Z",
                    labels: new[] { "intent-target" }),
            },
        };
        WorkerNextActionCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--workdir", missingWorktree,
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.IssueToPr, result.Action);
        Assert.Contains(result.Warnings, w =>
            w.Contains("does not exist", StringComparison.Ordinal)
            && w.Contains("selection used GitHub state from --repo only", StringComparison.Ordinal));
    }

    [Fact]
    public void AutomationCheck_AndWorkerNextAction_AgreeOnPrCommentFixUnderChildWorkdirContext()
    {
        using var workspace = new WorkerNextActionWorkspace();
        var childWorktree = Directory.CreateTempSubdirectory("g281-aligned-workdir-").FullName;
        // give it both a .git and an origin-like config so automation check's
        // repo inference succeeds against the same workdir.
        Directory.CreateDirectory(Path.Combine(childWorktree, ".git"));
        File.WriteAllText(
            Path.Combine(childWorktree, ".git", "config"),
            "[remote \"origin\"]\n\turl = https://github.com/J-Tech-Japan/intent-system.git\n");

        try
        {
            var lister = new FakeLister
            {
                Prs = new[]
                {
                    BuildPr(660, "G278 PR with repair feedback",
                        "https://github.com/J-Tech-Japan/intent-system/pull/660",
                        createdAt: "2026-05-06T20:00:00Z",
                        labels: new[] { "intent-target", "intent-pr-request-update" }),
                },
            };
            WorkerNextActionCommand.CandidateListerFactory = () => lister;

            using var workerWriter = new StringWriter();
            Assert.Equal(0, WorkerNextActionCommand.Execute(
                workspace.Context,
                new[]
                {
                    "--repo", "J-Tech-Japan/intent-system",
                    "--workdir", childWorktree,
                    "--format", "json",
                },
                workerWriter));

            using var checkWriter = new StringWriter();
            Assert.Equal(0, AutomationCheckCommand.Execute(
                workspace.Context,
                new[] { "--workdir", childWorktree, "--format", "json" },
                checkWriter));

            var workerResult = JsonSerializer.Deserialize<WorkerNextActionResult>(workerWriter.ToString())!;
            var checkResult = JsonSerializer.Deserialize<WorkerNextActionResult>(checkWriter.ToString())!;
            Assert.Equal(workerResult.Action, checkResult.Action);
            Assert.Equal(WorkerNextActionConstants.Actions.PrCommentFix, checkResult.Action);
            Assert.Equal(workerResult.Number, checkResult.Number);
        }
        finally
        {
            if (Directory.Exists(childWorktree))
            {
                Directory.Delete(childWorktree, recursive: true);
            }
        }
    }

    // --- G333: --github-only strict child-loop assertion --------------------

    [Fact]
    public void Execute_G333_GithubOnly_RecordsBindingOnResultWithoutChangingSelection()
    {
        // G333 acceptance: `worker next-action --github-only --repo <r>`
        // returns issue-to-pr / pr-comment-fix / none from GitHub state
        // only. The selection algorithm is already label-based (no
        // queue-state read); the flag records the strict child-loop
        // binding so the host loop can audit.
        using var workspace = new WorkerNextActionWorkspace();
        var lister = new FakeLister
        {
            Prs = Array.Empty<GitHubAutomationPrCandidate>(),
            Issues = new[]
            {
                BuildIssue(701, "G333 Issue", "https://github.com/J-Tech-Japan/intent-system/issues/701",
                    createdAt: "2026-05-12T00:00:00Z",
                    labels: new[] { "intent-target" })
            },
        };
        WorkerNextActionCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--github-only", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.IssueToPr, result.Action);
        Assert.Equal(701, result.Number);
        Assert.True(result.GithubOnly);
    }

    [Fact]
    public void Execute_G333_WithoutGithubOnly_ResultDoesNotSurfaceField()
    {
        // G333 invariant: callers that don't assert the strict
        // contract see the pre-G333 result shape — `github_only` is
        // null/omitted.
        using var workspace = new WorkerNextActionWorkspace();
        var lister = new FakeLister
        {
            Prs = Array.Empty<GitHubAutomationPrCandidate>(),
            Issues = Array.Empty<GitHubAutomationIssueCandidate>(),
        };
        WorkerNextActionCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.None, result.Action);
        Assert.Null(result.GithubOnly);
    }

    private static string StripCsharpComments(string source)
    {
        // Remove block comments (/* … */) including XML doc blocks like
        // /** … */, and remove line comments (// … to end of line, plus
        // /// … to end of line). Order matters — strip block comments
        // first so a "//" inside a /* … */ block doesn't survive.
        var noBlockComments = System.Text.RegularExpressions.Regex.Replace(
            source, @"/\*[\s\S]*?\*/", string.Empty);
        var noLineComments = System.Text.RegularExpressions.Regex.Replace(
            noBlockComments, @"//.*?$", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Multiline);
        return noLineComments;
    }

    // --- G325: active PR update lease surfacing -------------------------

    [Fact]
    public void Execute_GivenPrWithUpdateInProgressOnly_SurfacesWaitLease()
    {
        // G325: a PR carrying only `intent-pr-update-in-progress`
        // (request-update label dropped or never present) is still an
        // active worker lease; the selector must surface `wait` and
        // identify the PR, not collapse to `none`.
        using var workspace = new WorkerNextActionWorkspace();
        var lister = new FakeLister
        {
            Prs = new[]
            {
                BuildPr(801, "Active lease (no request-update)",
                    "https://github.com/J-Tech-Japan/intent-system/pull/801",
                    createdAt: "2026-05-01T00:00:00Z",
                    labels: new[] { "intent-target", "intent-pr-update-in-progress" }),
            },
            Issues = Array.Empty<GitHubAutomationIssueCandidate>(),
        };
        WorkerNextActionCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        Assert.Equal(0, WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--format", "json" },
            writer));

        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.Wait, result.Action);
        Assert.Equal(801, result.Number);
        Assert.Equal(WorkerNextActionConstants.SourceClassifications.PrUpdateInProgress, result.SourceClassification);
    }

    [Fact]
    public void Execute_GivenRequestUpdateWithoutLease_StillReturnsPrCommentFix()
    {
        // G325 regression guard: the new wait lane must not regress the
        // canonical actionable-repair path. A PR with
        // `intent-pr-request-update` and NO `intent-pr-update-in-progress`
        // must still surface as `pr-comment-fix`.
        using var workspace = new WorkerNextActionWorkspace();
        var lister = new FakeLister
        {
            Prs = new[]
            {
                BuildPr(802, "Repair feedback waiting",
                    "https://github.com/J-Tech-Japan/intent-system/pull/802",
                    createdAt: "2026-05-01T00:00:00Z",
                    labels: new[] { "intent-target", "intent-pr-request-update" }),
            },
            Issues = Array.Empty<GitHubAutomationIssueCandidate>(),
        };
        WorkerNextActionCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        Assert.Equal(0, WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--format", "json" },
            writer));

        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.PrCommentFix, result.Action);
        Assert.Equal(802, result.Number);
        Assert.Equal(WorkerNextActionConstants.SourceClassifications.RepairRequired, result.SourceClassification);
    }

    [Fact]
    public void Execute_GivenNoPrOrIssue_StillReturnsTrueNone()
    {
        // G325 regression guard: when there is no PR at all, `wait`
        // does NOT fire — the selector still returns true `none` so
        // the host loop can declare idle.
        using var workspace = new WorkerNextActionWorkspace();
        var lister = new FakeLister
        {
            Prs = Array.Empty<GitHubAutomationPrCandidate>(),
            Issues = Array.Empty<GitHubAutomationIssueCandidate>(),
        };
        WorkerNextActionCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        Assert.Equal(0, WorkerNextActionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--format", "json" },
            writer));

        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.None, result.Action);
        Assert.Null(result.Number);
    }

    private static GitHubAutomationPrCandidate BuildPr(
        int number, string title, string url, string createdAt, string[] labels,
        bool hasSourceIssue = true)
    {
        return new GitHubAutomationPrCandidate
        {
            Number = number,
            Title = title,
            Url = url,
            CreatedAt = createdAt,
            Labels = labels.Select(n => new GitHubAutomationLabel { Name = n }).ToArray(),
            // G392: a real intent PR closes its source issue (G311 mandatory),
            // so fixtures default to one closing reference; the
            // not-child-actionable case passes hasSourceIssue: false.
            ClosingIssuesReferences = hasSourceIssue
                ? new[] { new GitHubPrClosingIssueReference { Number = number - 1 } }
                : Array.Empty<GitHubPrClosingIssueReference>(),
        };
    }

    private static GitHubAutomationIssueCandidate BuildIssue(
        int number, string title, string url, string createdAt, string[] labels)
    {
        return new GitHubAutomationIssueCandidate
        {
            Number = number,
            Title = title,
            Url = url,
            CreatedAt = createdAt,
            Labels = labels.Select(n => new GitHubAutomationLabel { Name = n }).ToArray(),
        };
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

    private sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> Prs { get; init; }
            = Array.Empty<GitHubAutomationPrCandidate>();
        public IReadOnlyList<GitHubAutomationIssueCandidate> Issues { get; init; }
            = Array.Empty<GitHubAutomationIssueCandidate>();

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels) => Prs;

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo,
            IReadOnlyCollection<string> requiredLabels) => Issues;
    }

    private sealed class ThrowingLister : IGitHubAutomationCandidateLister
    {
        private readonly string message;

        public ThrowingLister(string message)
        {
            this.message = message;
        }

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo, IReadOnlyCollection<string> requiredLabels) =>
            throw new InvalidOperationException(message);

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo, IReadOnlyCollection<string> requiredLabels) =>
            throw new InvalidOperationException(message);
    }

    // ── G392: fakes for the shared pr-comment-preflight consult ─────────────

    /// <summary>
    /// G392 fake comments lookup. Defaults to a single genuinely-actionable,
    /// non-host-metadata review-thread comment (so preflight reaches
    /// repair-required/actionable:true). Tests pass a different
    /// <see cref="GitHubPrCommentsLookupResult"/> via <see cref="Result"/> to
    /// exercise the downgrade paths.
    /// </summary>
    private sealed class FakeCommentsLookup : IGitHubPrCommentsLookup
    {
        public GitHubPrCommentsLookupResult Result { get; init; } = ActionableReviewThread();

        public GitHubPrCommentsLookupResult Lookup(string repo, int prNumber) => Result;

        public static GitHubPrCommentsLookupResult ActionableReviewThread() =>
            new()
            {
                ReviewThreads = new[]
                {
                    new GitHubPrReviewThread
                    {
                        Id = "RT_actionable",
                        IsResolved = false,
                        Comments = new[]
                        {
                            new GitHubPrReviewThreadComment
                            {
                                Id = "C_actionable",
                                Author = "human-reviewer",
                                Body = "Please add a null check before dereferencing config in Foo().",
                            },
                        },
                    },
                },
            };

        public static GitHubPrCommentsLookupResult NoActionableComments() =>
            new();

        public static GitHubPrCommentsLookupResult HostMetadataOnly() =>
            new()
            {
                ReviewThreads = new[]
                {
                    new GitHubPrReviewThread
                    {
                        Id = "RT_host",
                        IsResolved = false,
                        Comments = new[]
                        {
                            new GitHubPrReviewThreadComment
                            {
                                Id = "C_host",
                                Author = "human-reviewer",
                                Body = "Please update intents/G392.md to reflect the new contract.",
                            },
                        },
                    },
                },
            };
    }

    private sealed class ThrowingCommentsLookup : IGitHubPrCommentsLookup
    {
        public GitHubPrCommentsLookupResult Lookup(string repo, int prNumber) =>
            throw new InvalidOperationException("simulated gh pr view --comments transport failure");
    }

    /// <summary>
    /// G392 fake source-issue lookup. Returns an issue carrying the
    /// intent-target + intent-pr-created labels the preflight classifier expects
    /// on a properly-linked source issue, so step 8 does not flag the PR.
    /// </summary>
    private sealed class FakeIssueLookup : IGitHubIssueLookup
    {
        public GitHubIssueLookupResult Result { get; init; } = TargetCreatedIssue();

        public GitHubIssueLookupResult Lookup(string repo, int issueNumber) => Result;

        public static GitHubIssueLookupResult TargetCreatedIssue() =>
            new()
            {
                Number = 0,
                State = "OPEN",
                Title = "source issue",
                Labels = new[]
                {
                    new GitHubIssueLabel { Name = "intent-target" },
                    new GitHubIssueLabel { Name = "intent-pr-created" },
                },
            };
    }

    private sealed class WorkerNextActionWorkspace : IDisposable
    {
        public WorkerNextActionWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("worker-next-action-tests-").FullName;
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
