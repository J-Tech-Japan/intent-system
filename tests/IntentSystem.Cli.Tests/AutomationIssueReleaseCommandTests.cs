using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G462: tests for the safe issue RELEASE transition that removes
/// <c>intent-target</c> from a mistakenly-targeted issue without raw gh label
/// mutation.
/// </summary>
public sealed class AutomationIssueReleaseCommandTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 4, 19, 0, 0, TimeSpan.Zero);

    public AutomationIssueReleaseCommandTests()
    {
        AutomationIssueReleaseCommand.MutatorFactory = null;
        AutomationIssueReleaseCommand.UtcNowFactory = () => FixedNow;
    }

    public void Dispose()
    {
        AutomationIssueReleaseCommand.MutatorFactory = null;
        AutomationIssueReleaseCommand.UtcNowFactory = null;
    }

    [Fact]
    public void Execute_Write_RemovesIntentTargetFromIssue()
    {
        using var workspace = new Workspace();
        var mutator = new FakeMutator(new[] { "intent-target" });
        AutomationIssueReleaseCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueReleaseCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--issue", "1018",
             "--reason", "host-only packet (G458)", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueReleaseResult>(writer.ToString())!;
        Assert.True(result.Applied);
        Assert.True(result.HadTargetLabel);
        Assert.Contains("intent-target", result.RemoveLabels);
        Assert.Empty(result.AddLabels);
        Assert.Equal("host-only packet (G458)", result.Reason);

        var transition = Assert.Single(mutator.Transitions);
        Assert.Equal("issue", transition.Kind);
        Assert.Equal(1018, transition.Number);
        Assert.Contains("intent-target", transition.RemoveLabels);
        Assert.Empty(transition.AddLabels);
    }

    [Fact]
    public void Execute_DryRun_DoesNotMutate()
    {
        using var workspace = new Workspace();
        var mutator = new FakeMutator(new[] { "intent-target" });
        AutomationIssueReleaseCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueReleaseCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--issue", "1018", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueReleaseResult>(writer.ToString())!;
        Assert.False(result.Applied);
        Assert.True(result.HadTargetLabel);
        Assert.Empty(mutator.Transitions);
    }

    [Fact]
    public void Execute_IssueWithoutTarget_NothingToRelease()
    {
        using var workspace = new Workspace();
        var mutator = new FakeMutator(Array.Empty<string>());
        AutomationIssueReleaseCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueReleaseCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--issue", "1018", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueReleaseResult>(writer.ToString())!;
        Assert.False(result.HadTargetLabel);
        Assert.False(result.Applied);
        Assert.Empty(mutator.Transitions);
        Assert.Contains("nothing to release", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommandRouter_RegistersAutomationIssueRelease()
    {
        using var workspace = new Workspace();
        AutomationIssueReleaseCommand.MutatorFactory = () => new FakeMutator(new[] { "intent-target" });

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["automation", "issue-release", "--repo", "J-Tech-Japan/intent-system", "--issue", "1018", "--format", "json"],
            workspace.Context,
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueReleaseResult>(writer.ToString())!;
        Assert.Equal(1018, result.Issue);
    }

    private sealed class FakeMutator : IGitHubLabelMutator
    {
        private readonly IReadOnlyList<string> labels;
        public List<Transition> Transitions { get; } = new();
        public FakeMutator(IReadOnlyList<string> labels) => this.labels = labels;

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();

        public void ApplyLabelTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            Transitions.Add(new Transition(kind, number, addLabels.ToArray(), removeLabels.ToArray()));

        public void ApplyReconcileTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();
    }

    private sealed record Transition(string Kind, int Number, IReadOnlyList<string> AddLabels, IReadOnlyList<string> RemoveLabels);

    private sealed class Workspace : IDisposable
    {
        public Workspace()
        {
            RootPath = Directory.CreateTempSubdirectory("automation-issue-release-tests-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig { Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" } }
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
