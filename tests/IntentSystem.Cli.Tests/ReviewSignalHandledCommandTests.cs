using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G374: tests for <c>intent-cli review signal-handled</c> — the
/// dry-run / write split, the add-handled / remove-sent convergence, and
/// the already-handled / no-pending no-op.
/// </summary>
public sealed class ReviewSignalHandledCommandTests : IDisposable
{
    public ReviewSignalHandledCommandTests()
    {
        ReviewSignalHandledCommand.MutatorFactory = null;
    }

    public void Dispose()
    {
        ReviewSignalHandledCommand.MutatorFactory = null;
    }

    [Fact]
    public void Execute_DryRun_PlansConvergence_AppliesNothing()
    {
        using var workspace = new SignalWorkspace();
        var mutator = new FakeMutator { Labels = new[] { "intent-signal-sent" } };
        ReviewSignalHandledCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exit = ReviewSignalHandledCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "851", "--format", "json" },
            writer);

        Assert.Equal(0, exit);
        var result = JsonSerializer.Deserialize<ReviewSignalHandledResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.False(result.Applied);
        Assert.Contains("intent-signal-handled", result.AddLabels);
        Assert.Contains("intent-signal-sent", result.RemoveLabels);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_Write_AddsHandled_RemovesSent()
    {
        using var workspace = new SignalWorkspace();
        var mutator = new FakeMutator { Labels = new[] { "intent-signal-sent" } };
        ReviewSignalHandledCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exit = ReviewSignalHandledCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "860", "--write", "--format", "json" },
            writer);

        Assert.Equal(0, exit);
        var result = JsonSerializer.Deserialize<ReviewSignalHandledResult>(writer.ToString())!;
        Assert.True(result.Applied);
        var applied = Assert.Single(mutator.AppliedTransitions);
        Assert.Equal("pr", applied.Kind);
        Assert.Contains("intent-signal-handled", applied.AddLabels);
        Assert.Contains("intent-signal-sent", applied.RemoveLabels);
    }

    [Fact]
    public void Execute_AlreadyHandled_NoOp()
    {
        using var workspace = new SignalWorkspace();
        var mutator = new FakeMutator { Labels = new[] { "intent-signal-handled" } };
        ReviewSignalHandledCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exit = ReviewSignalHandledCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "851", "--write", "--format", "json" },
            writer);

        Assert.Equal(0, exit);
        var result = JsonSerializer.Deserialize<ReviewSignalHandledResult>(writer.ToString())!;
        Assert.False(result.Proceed);
        Assert.False(result.Applied);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_NoPendingSignal_WarnsButStillNoOp()
    {
        using var workspace = new SignalWorkspace();
        var mutator = new FakeMutator { Labels = new[] { "intent-target" } };
        ReviewSignalHandledCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exit = ReviewSignalHandledCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "851", "--write", "--format", "json" },
            writer);

        Assert.Equal(0, exit);
        var result = JsonSerializer.Deserialize<ReviewSignalHandledResult>(writer.ToString())!;
        // No intent-signal-sent present, but intent-signal-handled is absent,
        // so the add-handled half still makes this a real (idempotent) change.
        Assert.True(result.Proceed);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Execute_RequiresExactlyOneTarget()
    {
        using var workspace = new SignalWorkspace();
        using var writer = new StringWriter();
        var exit = ReviewSignalHandledCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system" },
            writer);
        Assert.Equal(1, exit);
        Assert.Contains("--issue", writer.ToString(), StringComparison.Ordinal);
    }

    internal sealed class FakeMutator : IGitHubLabelMutator
    {
        public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();
        public List<AppliedTransition> AppliedTransitions { get; } = new();

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            Labels.Select(n => new GitHubAutomationLabel { Name = n }).ToArray();

        public void ApplyLabelTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels)
        {
            AppliedTransitions.Add(new AppliedTransition
            {
                Repo = repo,
                Kind = kind,
                Number = number,
                AddLabels = addLabels.ToArray(),
                RemoveLabels = removeLabels.ToArray(),
            });
        }

        public void ApplyReconcileTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();
    }

    internal sealed record AppliedTransition
    {
        public required string Repo { get; init; }
        public required string Kind { get; init; }
        public required int Number { get; init; }
        public required IReadOnlyList<string> AddLabels { get; init; }
        public required IReadOnlyList<string> RemoveLabels { get; init; }
    }

    private sealed class SignalWorkspace : IDisposable
    {
        public SignalWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("signal-handled-tests-").FullName;
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" },
                },
            };
        }

        public string RootPath { get; }
        public CliContext Context { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
