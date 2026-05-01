using System.Security.Cryptography;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G214: Tests for <c>intent-cli automation complete</c>. The command
/// combines result-summary normalization with the supported worker-complete
/// transition, defaults to dry-run, and only writes labels when explicitly
/// requested.
/// </summary>
public sealed class AutomationCompleteCommandTests : IDisposable
{
    public AutomationCompleteCommandTests()
    {
        AutomationCompleteCommand.MutatorFactory = null;
        AutomationCompleteCommand.NestedProviderLauncher = null;
        WorkerCompleteCommand.MutatorFactory = null;
        WorkerCompleteCommand.NestedProviderLauncher = null;
    }

    public void Dispose()
    {
        AutomationCompleteCommand.MutatorFactory = null;
        AutomationCompleteCommand.NestedProviderLauncher = null;
        WorkerCompleteCommand.MutatorFactory = null;
        WorkerCompleteCommand.NestedProviderLauncher = null;
    }

    [Fact]
    public void Execute_IssueToPrPrCreated_DryRunPlansCompletionWithoutApplying()
    {
        using var workspace = new AutomationCompleteWorkspace();
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git");
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
            LabelsByTarget =
            {
                ["pr:534"] = Array.Empty<string>(),
            },
        };
        AutomationCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--issue", "533",
                "--pr", "534",
                "--outcome", "pr-created",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationCompleteResult>(writer.ToString())!;
        Assert.Equal("J-Tech-Japan/intent-system", result.Repo);
        Assert.Equal("issue", result.TargetKind);
        Assert.Equal(533, result.TargetNumber);
        Assert.Equal("dry-run", result.Mode);
        Assert.True(result.Proceed);
        Assert.False(result.Applied);
        Assert.Empty(result.AppliedLabelActions);
        Assert.Contains(result.PlannedLabelActions, a =>
            a.Action == "remove" && a.Target == "issue" && a.Label == "intent-issue-in-progress");
        Assert.Contains(result.PlannedLabelActions, a =>
            a.Action == "add" && a.Target == "issue" && a.Label == "intent-pr-created");
        Assert.Contains(result.PlannedLabelActions, a =>
            a.Action == "add" && a.Target == "pr" && a.Label == "intent-target");
        Assert.Contains(result.SourceIssueLabelActions, a => a.Target == "issue");
        Assert.Contains(result.CreatedPrLabelActions, a =>
            a.Target == "pr" && a.Label == "intent-target");
        Assert.Empty(mutator.AppliedTransitions);
        Assert.Contains(result.Warnings, w => w.Contains("intent-pr-created", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_IssueToPrPrCreated_WriteAppliesSupportedTransition()
    {
        using var workspace = new AutomationCompleteWorkspace();
        workspace.WriteOriginRemote("git@github.com:J-Tech-Japan/intent-system.git");
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
            LabelsByTarget =
            {
                ["pr:534"] = Array.Empty<string>(),
            },
        };
        AutomationCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--issue", "533",
                "--pr", "534",
                "--outcome", "pr-created",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationCompleteResult>(writer.ToString())!;
        Assert.True(result.Applied);
        Assert.Equal(result.PlannedLabelActions.Count, result.AppliedLabelActions.Count);
        Assert.Equal(2, mutator.AppliedTransitions.Count);
        var issueTransition = mutator.AppliedTransitions.Single(t => t.Kind == "issue");
        Assert.Contains("intent-pr-created", issueTransition.AddLabels);
        Assert.Contains("intent-issue-in-progress", issueTransition.RemoveLabels);
        var prTransition = mutator.AppliedTransitions.Single(t => t.Kind == "pr");
        Assert.Contains("intent-target", prTransition.AddLabels);
        Assert.Empty(prTransition.RemoveLabels);
        Assert.DoesNotContain("intent-pr-created", prTransition.AddLabels);
    }

    [Fact]
    public void Execute_IssueToPrPrCreated_MissingPrFailsDeterministically()
    {
        using var workspace = new AutomationCompleteWorkspace();
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git");
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        AutomationCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--issue", "533",
                "--outcome", "pr-created",
                "--format", "json",
            },
            writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--pr is required", writer.ToString(), StringComparison.Ordinal);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_IssueToPrPrCreated_PrWithMisplacedPrCreatedRefuses()
    {
        using var workspace = new AutomationCompleteWorkspace();
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git");
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
            LabelsByTarget =
            {
                ["pr:534"] = new[] { "intent-pr-created" },
            },
        };
        AutomationCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--issue", "533",
                "--pr", "534",
                "--outcome", "pr-created",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<AutomationCompleteResult>(writer.ToString())!;
        Assert.False(result.Proceed);
        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("intent-pr-created", StringComparison.Ordinal));
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_PrCommentFixRepairPushed_WriteAppliesPrRereviewTransition()
    {
        using var workspace = new AutomationCompleteWorkspace();
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git");
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-request-update", "intent-pr-update-in-progress" },
        };
        AutomationCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "pr-comment-fix",
                "--pr", "535",
                "--outcome", "repair-pushed",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationCompleteResult>(writer.ToString())!;
        Assert.Equal("pr", result.TargetKind);
        Assert.Equal(535, result.TargetNumber);
        Assert.DoesNotContain(result.PlannedLabelActions, a => a.Label == "intent-pr-created");
        Assert.Contains(result.PlannedLabelActions, a =>
            a.Action == "remove" && a.Target == "pr" && a.Label == "intent-pr-update-in-progress");
        Assert.Contains(result.PlannedLabelActions, a =>
            a.Action == "add" && a.Target == "pr" && a.Label == "intent-pr-rereview-ready");
        Assert.Single(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_UnsupportedKindOutcomeFailsDeterministically()
    {
        using var workspace = new AutomationCompleteWorkspace();
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git");
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        AutomationCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--issue", "533",
                "--outcome", "repair-pushed",
                "--format", "json",
            },
            writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("not supported", writer.ToString(), StringComparison.Ordinal);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_RepoOverrideSkipsInference()
    {
        using var workspace = new AutomationCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        AutomationCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--repo", "Override/Repo",
                "--issue", "533",
                "--outcome", "failed",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        Assert.Equal("Override/Repo", mutator.SeenRepo);
        var result = JsonSerializer.Deserialize<AutomationCompleteResult>(writer.ToString())!;
        Assert.Equal("Override/Repo", result.Repo);
    }

    [Fact]
    public void Execute_WorkdirChangesRepoInferenceRoot()
    {
        using var workspace = new AutomationCompleteWorkspace();
        var child = workspace.CreateChildWorkdir("child");
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/wrong.git");
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git", child);
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        AutomationCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--workdir", child,
                "--issue", "533",
                "--outcome", "failed",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        Assert.Equal("J-Tech-Japan/intent-system", mutator.SeenRepo);
    }

    [Fact]
    public void CommandRouter_RegistersAutomationComplete()
    {
        using var workspace = new AutomationCompleteWorkspace();
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git");
        AutomationCompleteCommand.MutatorFactory = () => new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            new[]
            {
                "automation",
                "complete",
                "--kind", "issue-to-pr",
                "--issue", "533",
                "--outcome", "failed",
                "--format", "json",
            },
            workspace.Context,
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationCompleteResult>(writer.ToString())!;
        Assert.Equal("failed", result.Outcome);
    }

    [Fact]
    public void ProgramMain_AutomationCompleteDoesNotRequireIntentCliDirectory()
    {
        using var workspace = new AutomationCompleteWorkspace();
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git");
        AutomationCompleteCommand.MutatorFactory = () => new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        var originalDirectory = Directory.GetCurrentDirectory();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        try
        {
            Directory.SetCurrentDirectory(workspace.RootPath);
            Console.SetOut(stdout);
            Console.SetError(stderr);

            var exitCode = Program.Main(new[]
            {
                "automation",
                "complete",
                "--kind", "issue-to-pr",
                "--issue", "533",
                "--outcome", "failed",
                "--format", "json",
            });

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());
            var result = JsonSerializer.Deserialize<AutomationCompleteResult>(stdout.ToString())!;
            Assert.Equal("J-Tech-Japan/intent-system", result.Repo);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    [Fact]
    public void Execute_NeverInvokesNestedProviderLauncher()
    {
        using var workspace = new AutomationCompleteWorkspace();
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git");
        var launcherInvoked = false;
        AutomationCompleteCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };
        AutomationCompleteCommand.MutatorFactory = () => new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };

        using var writer = new StringWriter();
        Assert.Equal(0, AutomationCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--issue", "533",
                "--outcome", "failed",
                "--format", "json",
            },
            writer));

        Assert.False(launcherInvoked,
            "AutomationCompleteCommand must never invoke NestedProviderLauncher.");
    }

    [Fact]
    public void Execute_DryRunLeavesWorkspaceByteEquivalent()
    {
        using var workspace = new AutomationCompleteWorkspace();
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git");
        var before = workspace.SnapshotWorkspace();
        AutomationCompleteCommand.MutatorFactory = () => new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };

        using (var writer = new StringWriter())
        {
            Assert.Equal(0, AutomationCompleteCommand.Execute(
                workspace.Context,
                new[]
                {
                    "--kind", "issue-to-pr",
                    "--issue", "533",
                    "--outcome", "failed",
                    "--format", "json",
                },
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

    private sealed class FakeMutator : IGitHubLabelMutator
    {
        public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();

        public Dictionary<string, IReadOnlyList<string>> LabelsByTarget { get; } = new(StringComparer.Ordinal);

        public string? SeenRepo { get; private set; }

        public List<AppliedTransition> AppliedTransitions { get; } = new();

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number)
        {
            SeenRepo = repo;
            var key = $"{kind}:{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            var labels = LabelsByTarget.TryGetValue(key, out var targetLabels)
                ? targetLabels
                : Labels;
            return labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();
        }

        public void ApplyLabelTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels)
        {
            SeenRepo = repo;
            AppliedTransitions.Add(new AppliedTransition(
                repo,
                kind,
                number,
                addLabels.ToArray(),
                removeLabels.ToArray()));
        }
    }

    private sealed record AppliedTransition(
        string Repo,
        string Kind,
        int Number,
        IReadOnlyList<string> AddLabels,
        IReadOnlyList<string> RemoveLabels);

    private sealed class AutomationCompleteWorkspace : IDisposable
    {
        public AutomationCompleteWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("automation-complete-tests-").FullName;
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

        public string CreateChildWorkdir(string name)
        {
            var path = Path.Combine(RootPath, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void WriteOriginRemote(string remoteUrl, string? workdir = null)
        {
            var root = workdir ?? RootPath;
            var gitDirectory = Path.Combine(root, ".git");
            Directory.CreateDirectory(gitDirectory);
            File.WriteAllText(
                Path.Combine(gitDirectory, "config"),
                $"""
                [remote "origin"]
                    url = {remoteUrl}
                """);
        }

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
