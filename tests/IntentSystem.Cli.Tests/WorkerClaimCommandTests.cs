using System.Security.Cryptography;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G211: Tests for <c>intent-cli worker claim</c>. Cover the issue and
/// PR claim paths, dry-run vs write split, stale-state refusals,
/// missing-target/missing-repair-requested refusals, the misplaced
/// <c>intent-pr-created</c> warning, and the no-mutation invariants
/// (no Process.Start in command/analyzer; no GitHub mutation in
/// dry-run; whole-workspace byte-snapshot stability).
/// </summary>
public sealed class WorkerClaimCommandTests : IDisposable
{
    public WorkerClaimCommandTests()
    {
        WorkerClaimCommand.MutatorFactory = null;
        WorkerClaimCommand.NestedProviderLauncher = null;
    }

    public void Dispose()
    {
        WorkerClaimCommand.MutatorFactory = null;
        WorkerClaimCommand.NestedProviderLauncher = null;
    }

    [Fact]
    public void Execute_IssueWithIntentTarget_DryRunPlansAddIntentIssueInProgress()
    {
        using var workspace = new WorkerClaimWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target" },
        };
        WorkerClaimCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerClaimCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--kind", "issue", "--number", "525", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerClaimResult>(writer.ToString())!;
        Assert.Equal("issue", result.Kind);
        Assert.Equal(525, result.Number);
        Assert.Equal(WorkerClaimCompleteConstants.Modes.DryRun, result.Mode);
        Assert.True(result.Proceed);
        Assert.False(result.Applied);
        Assert.Contains("intent-issue-in-progress", result.AddLabels);
        Assert.Empty(result.RemoveLabels);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_IssueWithIntentTarget_WriteModeAppliesTransition()
    {
        using var workspace = new WorkerClaimWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target" },
        };
        WorkerClaimCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerClaimCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--kind", "issue", "--number", "525", "--write", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerClaimResult>(writer.ToString())!;
        Assert.Equal(WorkerClaimCompleteConstants.Modes.Write, result.Mode);
        Assert.True(result.Proceed);
        Assert.True(result.Applied);
        Assert.Single(mutator.AppliedTransitions);
        var applied = mutator.AppliedTransitions[0];
        Assert.Equal(525, applied.Number);
        Assert.Equal("issue", applied.Kind);
        Assert.Contains("intent-issue-in-progress", applied.AddLabels);
        Assert.Empty(applied.RemoveLabels);
    }

    [Fact]
    public void Execute_IssueAlreadyInProgress_RefusesAndDoesNotMutateEvenInWriteMode()
    {
        using var workspace = new WorkerClaimWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerClaimCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerClaimCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--kind", "issue", "--number", "525", "--write", "--format", "json" },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerClaimResult>(writer.ToString())!;
        Assert.False(result.Proceed);
        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.AlreadyInProgress, StringComparison.Ordinal));
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_IssueAlreadyPrCreated_RefusesAlreadyCompleted()
    {
        using var workspace = new WorkerClaimWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-created" },
        };
        WorkerClaimCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerClaimCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--kind", "issue", "--number", "525", "--format", "json" },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerClaimResult>(writer.ToString())!;
        Assert.False(result.Proceed);
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.AlreadyCompleted, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_IssueWithoutIntentTarget_RefusesMissingTarget()
    {
        using var workspace = new WorkerClaimWorkspace();
        var mutator = new FakeMutator
        {
            Labels = Array.Empty<string>(),
        };
        WorkerClaimCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerClaimCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--kind", "issue", "--number", "525", "--format", "json" },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerClaimResult>(writer.ToString())!;
        Assert.False(result.Proceed);
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.MissingTarget, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_PrWithRequestUpdate_DryRunPlansAddIntentPrUpdateInProgress()
    {
        using var workspace = new WorkerClaimWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-request-update" },
        };
        WorkerClaimCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerClaimCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--kind", "pr", "--number", "514", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerClaimResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.False(result.Applied);
        Assert.Contains("intent-pr-update-in-progress", result.AddLabels);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_PrWithoutRequestUpdate_RefusesMissingRepairRequested()
    {
        using var workspace = new WorkerClaimWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target" },
        };
        WorkerClaimCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerClaimCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--kind", "pr", "--number", "514", "--format", "json" },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerClaimResult>(writer.ToString())!;
        Assert.False(result.Proceed);
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.MissingRepairRequested, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_PrAlreadyUpdateInProgress_RefusesAlreadyInProgress()
    {
        using var workspace = new WorkerClaimWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-request-update", "intent-pr-update-in-progress" },
        };
        WorkerClaimCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerClaimCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--kind", "pr", "--number", "514", "--write", "--format", "json" },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerClaimResult>(writer.ToString())!;
        Assert.False(result.Proceed);
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.AlreadyInProgress, StringComparison.Ordinal));
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_PrAlreadyRereviewReady_RefusesAlreadyRereviewReady()
    {
        using var workspace = new WorkerClaimWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-request-update", "intent-pr-rereview-ready" },
        };
        WorkerClaimCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerClaimCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--kind", "pr", "--number", "514", "--format", "json" },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerClaimResult>(writer.ToString())!;
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.AlreadyRereviewReady, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_PrWithMisplacedIntentPrCreated_EmitsWarningButStillEvaluates()
    {
        using var workspace = new WorkerClaimWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-request-update", "intent-pr-created" },
        };
        WorkerClaimCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerClaimCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--kind", "pr", "--number", "514", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerClaimResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.Contains(result.Warnings, w =>
            w.Contains("misplaced", StringComparison.Ordinal)
            && w.Contains("intent-pr-created", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_MissingRequiredArguments_ReturnsNonZero()
    {
        using var workspace = new WorkerClaimWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerClaimCommand.Execute(
            workspace.Context, Array.Empty<string>(), writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--repo is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_BadKind_ReturnsNonZero()
    {
        using var workspace = new WorkerClaimWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerClaimCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--kind", "bug", "--number", "1" },
            writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--kind must be 'issue' or 'pr'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void JsonOutput_IncludesCamelCaseAliasesForLabelArrays()
    {
        using var workspace = new WorkerClaimWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target" },
        };
        WorkerClaimCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerClaimCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--kind", "issue", "--number", "525", "--format", "json" },
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
        using var workspace = new WorkerClaimWorkspace();
        var launcherInvoked = false;
        WorkerClaimCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };
        WorkerClaimCommand.MutatorFactory = () => new FakeMutator
        {
            Labels = new[] { "intent-target" },
        };

        using var writer = new StringWriter();
        Assert.Equal(0, WorkerClaimCommand.Execute(workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--kind", "issue", "--number", "525", "--write", "--format", "json" },
            writer));

        // Also walk a refusal path.
        WorkerClaimCommand.MutatorFactory = () => new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        writer.GetStringBuilder().Clear();
        Assert.Equal(2, WorkerClaimCommand.Execute(workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--kind", "issue", "--number", "525", "--write", "--format", "json" },
            writer));

        Assert.False(launcherInvoked,
            "WorkerClaimCommand must never invoke NestedProviderLauncher.");
    }

    [Fact]
    public void Execute_DryRun_LeavesIntentCliWorkspaceByteEquivalent()
    {
        using var workspace = new WorkerClaimWorkspace();
        var before = workspace.SnapshotWorkspace();

        WorkerClaimCommand.MutatorFactory = () => new FakeMutator
        {
            Labels = new[] { "intent-target" },
        };

        using (var writer = new StringWriter())
        {
            Assert.Equal(0, WorkerClaimCommand.Execute(workspace.Context,
                new[] { "--repo", "J-Tech-Japan/intent-system", "--kind", "issue", "--number", "525", "--format", "json" },
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
    public void Execute_WriteMode_LeavesIntentCliWorkspaceByteEquivalent()
    {
        using var workspace = new WorkerClaimWorkspace();
        var before = workspace.SnapshotWorkspace();

        WorkerClaimCommand.MutatorFactory = () => new FakeMutator
        {
            Labels = new[] { "intent-target" },
        };

        using (var writer = new StringWriter())
        {
            Assert.Equal(0, WorkerClaimCommand.Execute(workspace.Context,
                new[] { "--repo", "J-Tech-Japan/intent-system", "--kind", "issue", "--number", "525", "--write", "--format", "json" },
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
        var analyzer = StripCsharpComments(File.ReadAllText(LocateSourceFile("WorkerClaimAnalyzer.cs")));
        var command = StripCsharpComments(File.ReadAllText(LocateSourceFile("WorkerClaimCommand.cs")));
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

    [Fact]
    public void Adapter_ViewArguments_RequestSupportedSubset()
    {
        var args = GhCliGitHubLabelMutator.BuildViewArguments(
            "J-Tech-Japan/intent-system", "issue", 525);

        Assert.Contains("issue", args);
        Assert.Contains("view", args);
        Assert.Contains("525", args);
        Assert.Contains("--repo", args);
        Assert.Contains("J-Tech-Japan/intent-system", args);
        Assert.Contains("--json", args);
        Assert.Contains(GhCliGitHubLabelMutator.ViewJsonFields, args);
    }

    [Fact]
    public void Adapter_EditArguments_IncludeAddAndRemoveFlagsPerLabel()
    {
        var args = GhCliGitHubLabelMutator.BuildEditArguments(
            "J-Tech-Japan/intent-system", "issue", 525,
            new[] { "intent-issue-in-progress" },
            new[] { "intent-target-old" });

        Assert.Contains("issue", args);
        Assert.Contains("edit", args);
        Assert.Contains("525", args);
        Assert.Contains("--repo", args);
        Assert.Contains("J-Tech-Japan/intent-system", args);
        Assert.Contains("--add-label", args);
        Assert.Contains("intent-issue-in-progress", args);
        Assert.Contains("--remove-label", args);
        Assert.Contains("intent-target-old", args);
    }

    [Fact]
    public void Adapter_ApplyLabelTransitions_RejectsIntentPrCreatedOnPr()
    {
        var mutator = new GhCliGitHubLabelMutator();
        Assert.Throws<InvalidOperationException>(() =>
            mutator.ApplyLabelTransitions(
                "J-Tech-Japan/intent-system", "pr", 514,
                new[] { "intent-pr-created" },
                Array.Empty<string>()));
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
            // Defensive policy guard mirrored from the production mutator.
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

    private sealed class WorkerClaimWorkspace : IDisposable
    {
        public WorkerClaimWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("worker-claim-tests-").FullName;
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
