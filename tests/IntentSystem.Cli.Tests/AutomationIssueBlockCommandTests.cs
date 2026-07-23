using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G545: tests for the canonical issue-level blocked transition that applies
/// (or, with <c>--clear</c>, removes) <c>intent-issue-blocked</c> without raw
/// gh label mutation.
/// </summary>
public sealed class AutomationIssueBlockCommandTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    public AutomationIssueBlockCommandTests()
    {
        AutomationIssueBlockCommand.MutatorFactory = null;
        AutomationIssueBlockCommand.UtcNowFactory = () => FixedNow;
    }

    public void Dispose()
    {
        AutomationIssueBlockCommand.MutatorFactory = null;
        AutomationIssueBlockCommand.UtcNowFactory = null;
    }

    [Fact]
    public void Execute_Write_AppliesBlockedLabelWithReason()
    {
        using var workspace = new Workspace();
        var mutator = new FakeMutator(new[] { "intent-target", "intent-issue-in-progress" });
        AutomationIssueBlockCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueBlockCommand.Execute(
            workspace.Context,
            ["--repo", "sekiban-as-a-service/sekiban", "--issue", "818",
             "--reason", "SKS-G837", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueBlockResult>(writer.ToString())!;
        Assert.True(result.Applied);
        Assert.False(result.Clear);
        Assert.False(result.HadBlockedLabel);
        Assert.Contains("intent-issue-blocked", result.AddLabels);
        Assert.Empty(result.RemoveLabels);
        Assert.Equal("SKS-G837", result.Reason);

        var transition = Assert.Single(mutator.Transitions);
        Assert.Equal("issue", transition.Kind);
        Assert.Equal(818, transition.Number);
        Assert.Contains("intent-issue-blocked", transition.AddLabels);
        Assert.Empty(transition.RemoveLabels);
    }

    [Fact]
    public void Execute_Write_Clear_RemovesBlockedLabel()
    {
        using var workspace = new Workspace();
        var mutator = new FakeMutator(new[] { "intent-target", "intent-issue-in-progress", "intent-issue-blocked" });
        AutomationIssueBlockCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueBlockCommand.Execute(
            workspace.Context,
            ["--repo", "sekiban-as-a-service/sekiban", "--issue", "818", "--clear", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueBlockResult>(writer.ToString())!;
        Assert.True(result.Applied);
        Assert.True(result.Clear);
        Assert.True(result.HadBlockedLabel);
        Assert.Contains("intent-issue-blocked", result.RemoveLabels);
        Assert.Empty(result.AddLabels);

        var transition = Assert.Single(mutator.Transitions);
        Assert.Contains("intent-issue-blocked", transition.RemoveLabels);
        Assert.Empty(transition.AddLabels);
    }

    [Fact]
    public void Execute_DryRun_DoesNotMutate()
    {
        using var workspace = new Workspace();
        var mutator = new FakeMutator(new[] { "intent-target", "intent-issue-in-progress" });
        AutomationIssueBlockCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueBlockCommand.Execute(
            workspace.Context,
            ["--repo", "sekiban-as-a-service/sekiban", "--issue", "818", "--reason", "SKS-G837", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueBlockResult>(writer.ToString())!;
        Assert.False(result.Applied);
        Assert.False(result.HadBlockedLabel);
        Assert.Empty(mutator.Transitions);
    }

    [Fact]
    public void Execute_AlreadyBlocked_IsIdempotent_NothingToApply()
    {
        using var workspace = new Workspace();
        var mutator = new FakeMutator(new[] { "intent-target", "intent-issue-in-progress", "intent-issue-blocked" });
        AutomationIssueBlockCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueBlockCommand.Execute(
            workspace.Context,
            ["--repo", "sekiban-as-a-service/sekiban", "--issue", "818", "--reason", "SKS-G837", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueBlockResult>(writer.ToString())!;
        Assert.True(result.HadBlockedLabel);
        Assert.False(result.Applied);
        Assert.Empty(mutator.Transitions);
        Assert.Contains("nothing to apply", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_ClearWithoutBlockedLabel_IsIdempotent_NothingToClear()
    {
        using var workspace = new Workspace();
        var mutator = new FakeMutator(new[] { "intent-target", "intent-issue-in-progress" });
        AutomationIssueBlockCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueBlockCommand.Execute(
            workspace.Context,
            ["--repo", "sekiban-as-a-service/sekiban", "--issue", "818", "--clear", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueBlockResult>(writer.ToString())!;
        Assert.False(result.HadBlockedLabel);
        Assert.False(result.Applied);
        Assert.Empty(mutator.Transitions);
        Assert.Contains("nothing to clear", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_RefusesReasonWithClear()
    {
        using var workspace = new Workspace();
        using var writer = new StringWriter();
        var exitCode = AutomationIssueBlockCommand.Execute(
            workspace.Context,
            ["--repo", "sekiban-as-a-service/sekiban", "--issue", "818", "--clear", "--reason", "x", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--reason is only supported when applying", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RefusesMissingReasonWithoutClear()
    {
        using var workspace = new Workspace();
        using var writer = new StringWriter();
        var exitCode = AutomationIssueBlockCommand.Execute(
            workspace.Context,
            ["--repo", "sekiban-as-a-service/sekiban", "--issue", "818", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--reason is required unless --clear", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CommandRouter_RegistersAutomationIssueBlock()
    {
        using var workspace = new Workspace();
        AutomationIssueBlockCommand.MutatorFactory = () => new FakeMutator(new[] { "intent-target", "intent-issue-in-progress" });

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["automation", "issue-block", "--repo", "sekiban-as-a-service/sekiban", "--issue", "818", "--reason", "SKS-G837", "--format", "json"],
            workspace.Context,
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueBlockResult>(writer.ToString())!;
        Assert.Equal(818, result.Issue);
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
            RootPath = Directory.CreateTempSubdirectory("automation-issue-block-tests-").FullName;
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
