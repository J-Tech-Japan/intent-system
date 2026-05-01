using System.Security.Cryptography;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G211: Tests for <c>intent-cli worker complete</c>. Cover the
/// issue-side and PR-side completion transitions for every supported
/// outcome, the stale-state refusals, the "intent-pr-created stays on
/// the issue" defensive guard, the dry-run vs write split, and the
/// no-mutation invariants.
/// </summary>
public sealed class WorkerCompleteCommandTests : IDisposable
{
    public WorkerCompleteCommandTests()
    {
        WorkerCompleteCommand.MutatorFactory = null;
        WorkerCompleteCommand.NestedProviderLauncher = null;
    }

    public void Dispose()
    {
        WorkerCompleteCommand.MutatorFactory = null;
        WorkerCompleteCommand.NestedProviderLauncher = null;
    }

    [Fact]
    public void Execute_IssuePrCreatedOutcome_DryRunPlansSwapInProgressForPrCreated()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.False(result.Applied);
        Assert.Contains("intent-pr-created", result.AddLabels);
        Assert.Contains("intent-issue-in-progress", result.RemoveLabels);
        Assert.Empty(mutator.AppliedTransitions);

        Assert.Contains(result.Warnings, w =>
            w.Contains("intent-pr-created", StringComparison.Ordinal)
            && w.Contains("ISSUE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_IssuePrCreatedOutcome_WriteModeAppliesSwap()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Applied);
        Assert.Single(mutator.AppliedTransitions);
        var applied = mutator.AppliedTransitions[0];
        Assert.Contains("intent-pr-created", applied.AddLabels);
        Assert.Contains("intent-issue-in-progress", applied.RemoveLabels);
    }

    [Fact]
    public void Execute_IssueNotClaimed_RefusesCompleteNotClaimed()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target" }, // no in-progress
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.False(result.Proceed);
        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.CompleteNotClaimed, StringComparison.Ordinal));
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_IssueAlreadyPrCreated_RefusesAlreadyCompleted()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress", "intent-pr-created" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--format", "json",
            },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.CompleteAlreadyCompleted, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_IssueClarificationOutcome_RemovesInProgressOnly()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.ClarificationRequired,
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Contains("intent-issue-in-progress", result.RemoveLabels);
        Assert.DoesNotContain("intent-pr-created", result.AddLabels);
    }

    [Fact]
    public void Execute_PrRepairPushedOutcome_DryRunPlansSwapUpdateInProgressForRereviewReady()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-request-update", "intent-pr-update-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "pr",
                "--number", "514",
                "--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed,
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.False(result.Applied);
        Assert.Contains("intent-pr-rereview-ready", result.AddLabels);
        Assert.Contains("intent-pr-update-in-progress", result.RemoveLabels);
        Assert.Contains("intent-pr-request-update", result.RemoveLabels);
        Assert.Empty(mutator.AppliedTransitions);

        // intent-pr-created MUST NOT be added to a PR.
        Assert.DoesNotContain("intent-pr-created", result.AddLabels);
    }

    [Fact]
    public void Execute_PrAlreadyRereviewReady_RefusesAlreadyCompletedOnRepairPushed()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-update-in-progress", "intent-pr-rereview-ready" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "pr",
                "--number", "514",
                "--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed,
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.CompleteAlreadyCompleted, StringComparison.Ordinal));
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_PrRepairPushedOutcome_WriteRemovesRequestUpdateWhenAddingRereviewReady()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-request-update", "intent-pr-update-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "pr",
                "--number", "514",
                "--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed,
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.True(result.Applied);
        Assert.Contains("intent-pr-rereview-ready", result.AddLabels);
        Assert.Contains("intent-pr-update-in-progress", result.RemoveLabels);
        Assert.Contains("intent-pr-request-update", result.RemoveLabels);

        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Contains("intent-pr-rereview-ready", transition.AddLabels);
        Assert.Contains("intent-pr-update-in-progress", transition.RemoveLabels);
        Assert.Contains("intent-pr-request-update", transition.RemoveLabels);
    }

    [Fact]
    public void Execute_PrFailedOutcome_RemovesUpdateInProgressOnly()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-update-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "pr",
                "--number", "514",
                "--outcome", WorkerResultSummaryConstants.Outcomes.Failed,
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Contains("intent-pr-update-in-progress", result.RemoveLabels);
        Assert.Empty(result.AddLabels);
    }

    [Fact]
    public void Execute_PrUnsupportedOutcomeForKind_RefusesUnsupportedKindOutcome()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-update-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        // pr-created is an issue-to-pr outcome, not a pr-comment-fix outcome.
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "pr",
                "--number", "514",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--format", "json",
            },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.UnsupportedKindOutcome, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_IssueUnsupportedOutcomeForKind_RefusesUnsupportedKindOutcome()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        // repair-pushed is a pr-comment-fix outcome, not an issue-to-pr outcome.
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed,
                "--format", "json",
            },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.UnsupportedKindOutcome, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_IntentPrCreatedNeverEndsUpInPrAddList()
    {
        // This test sweeps every supported pr-comment-fix outcome and
        // asserts that intent-pr-created is never proposed as an add on
        // a PR. Together with the mutator's defensive guard it locks the
        // "intent-pr-created is issue-only" invariant end-to-end.
        var prOutcomes = new[]
        {
            WorkerResultSummaryConstants.Outcomes.RepairPushed,
            WorkerResultSummaryConstants.Outcomes.NoActionableComments,
            WorkerResultSummaryConstants.Outcomes.AlreadyResolved,
            WorkerResultSummaryConstants.Outcomes.ClarificationRequired,
            WorkerResultSummaryConstants.Outcomes.Failed,
            WorkerResultSummaryConstants.Outcomes.LabelCleanupRequired,
        };

        foreach (var outcome in prOutcomes)
        {
            using var workspace = new WorkerCompleteWorkspace();
            // Different setups for "stale" outcomes — but the test is
            // structural: regardless of proceed=true/false, the proposed
            // AddLabels must not contain intent-pr-created.
            var mutator = new FakeMutator
            {
                Labels = new[] { "intent-target", "intent-pr-update-in-progress" },
            };
            WorkerCompleteCommand.MutatorFactory = () => mutator;

            using var writer = new StringWriter();
            WorkerCompleteCommand.Execute(
                workspace.Context,
                new[]
                {
                    "--repo", "J-Tech-Japan/intent-system",
                    "--kind", "pr",
                    "--number", "514",
                    "--outcome", outcome,
                    "--format", "json",
                },
                writer);

            var raw = writer.ToString();
            // Trim warning text that mentions "intent-pr-created" in
            // the misplaced-label policy phrasing — we only care about
            // the add_labels[] array contents here.
            using var doc = JsonDocument.Parse(raw);
            var addLabels = doc.RootElement.GetProperty("add_labels");
            for (var i = 0; i < addLabels.GetArrayLength(); i++)
            {
                var label = addLabels[i].GetString();
                Assert.NotEqual("intent-pr-created", label);
            }
        }
    }

    [Fact]
    public void Execute_MissingRequiredArguments_ReturnsNonZero()
    {
        using var workspace = new WorkerCompleteWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context, Array.Empty<string>(), writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--repo is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingOutcome_ReturnsNonZero()
    {
        using var workspace = new WorkerCompleteWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--kind", "issue", "--number", "525" },
            writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--outcome is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void JsonOutput_IncludesCamelCaseAliasesForLabelArrays()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var raw = writer.ToString();
        Assert.Contains("\"add_labels\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"addLabels\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"remove_labels\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"removeLabels\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"current_labels\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"currentLabels\"", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverInvokesNestedProviderLauncher()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var launcherInvoked = false;
        WorkerCompleteCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };

        WorkerCompleteCommand.MutatorFactory = () => new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };

        using var writer = new StringWriter();
        Assert.Equal(0, WorkerCompleteCommand.Execute(workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--write",
                "--format", "json",
            },
            writer));

        Assert.False(launcherInvoked,
            "WorkerCompleteCommand must never invoke NestedProviderLauncher.");
    }

    [Fact]
    public void Execute_DryRun_LeavesIntentCliWorkspaceByteEquivalent()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var before = workspace.SnapshotWorkspace();

        WorkerCompleteCommand.MutatorFactory = () => new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };

        using (var writer = new StringWriter())
        {
            Assert.Equal(0, WorkerCompleteCommand.Execute(workspace.Context,
                new[]
                {
                    "--repo", "J-Tech-Japan/intent-system",
                    "--kind", "issue",
                    "--number", "525",
                    "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                    "--format", "json",
                },
                writer));
        }

        var after = workspace.SnapshotWorkspace();
        Assert.Equal(before.Count, after.Count);
        foreach (var (path, hash) in before)
        {
            Assert.True(after.TryGetValue(path, out var afterHash));
            Assert.Equal(hash, afterHash);
        }
    }

    [Fact]
    public void SourceScan_AnalyzerAndCommand_ContainNoProcessStartOrGhMutationLiterals()
    {
        var analyzer = StripCsharpComments(File.ReadAllText(LocateSourceFile("WorkerCompleteAnalyzer.cs")));
        var command = StripCsharpComments(File.ReadAllText(LocateSourceFile("WorkerCompleteCommand.cs")));
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

    private static string StripCsharpComments(string source)
    {
        var noBlockComments = System.Text.RegularExpressions.Regex.Replace(
            source, @"/\*[\s\S]*?\*/", string.Empty);
        var noLineComments = System.Text.RegularExpressions.Regex.Replace(
            noBlockComments, @"//.*?$", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Multiline);
        return noLineComments;
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

    internal sealed class FakeMutator : IGitHubLabelMutator
    {
        public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();

        public List<AppliedTransition> AppliedTransitions { get; } = new();

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number)
        {
            return Labels.Select(n => new GitHubAutomationLabel { Name = n }).ToArray();
        }

        public void ApplyLabelTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels)
        {
            if (string.Equals(kind, GhCliGitHubLabelMutator.Kinds.Pr, StringComparison.Ordinal)
                && (addLabels.Contains("intent-pr-created", StringComparer.Ordinal)
                    || removeLabels.Contains("intent-pr-created", StringComparer.Ordinal)))
            {
                throw new InvalidOperationException(
                    "label policy violation: 'intent-pr-created' is issue-only.");
            }

            AppliedTransitions.Add(new AppliedTransition
            {
                Repo = repo,
                Kind = kind,
                Number = number,
                AddLabels = addLabels.ToArray(),
                RemoveLabels = removeLabels.ToArray(),
            });
        }
    }

    internal sealed record AppliedTransition
    {
        public required string Repo { get; init; }
        public required string Kind { get; init; }
        public required int Number { get; init; }
        public required IReadOnlyList<string> AddLabels { get; init; }
        public required IReadOnlyList<string> RemoveLabels { get; init; }
    }

    private sealed class WorkerCompleteWorkspace : IDisposable
    {
        public WorkerCompleteWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("worker-complete-tests-").FullName;
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
