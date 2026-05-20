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
    }

    public void Dispose()
    {
        WorkerNextActionCommand.CandidateListerFactory = null;
        WorkerNextActionCommand.NestedProviderLauncher = null;
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
        int number, string title, string url, string createdAt, string[] labels)
    {
        return new GitHubAutomationPrCandidate
        {
            Number = number,
            Title = title,
            Url = url,
            CreatedAt = createdAt,
            Labels = labels.Select(n => new GitHubAutomationLabel { Name = n }).ToArray(),
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
