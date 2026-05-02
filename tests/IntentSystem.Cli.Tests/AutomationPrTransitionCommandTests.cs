using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class AutomationPrTransitionCommandTests : IDisposable
{
    public AutomationPrTransitionCommandTests()
    {
        AutomationPrTransitionCommand.MutatorFactory = null;
        AutomationPrTransitionCommand.NestedProviderLauncher = null;
    }

    public void Dispose()
    {
        AutomationPrTransitionCommand.MutatorFactory = null;
        AutomationPrTransitionCommand.NestedProviderLauncher = null;
    }

    [Fact]
    public void Execute_ReviewStart_DryRunPlansHostReviewLabelsWithoutApplying()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git");
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-pr-rereview-ready", "rereview-ready" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--pr", "542",
                "--transition", "review-start",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.Equal("J-Tech-Japan/intent-system", result.Repo);
        Assert.Equal(542, result.Pr);
        Assert.Equal("review-start", result.Transition);
        Assert.Equal("dry-run", result.Mode);
        Assert.False(result.Applied);
        Assert.Contains("intent-target", result.AddLabels);
        Assert.Contains("intent-pr-reviewing", result.AddLabels);
        Assert.Contains("intent-pr-rereview-ready", result.RemoveLabels);
        Assert.Contains("rereview-ready", result.RemoveLabels);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_ReviewStart_WriteAppliesExactHostReviewLabels()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-pr-rereview-ready", "rereview-ready" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "542",
                "--transition", "review-start",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.True(result.Applied);

        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Equal("pr", transition.Kind);
        Assert.Equal(542, transition.Number);
        Assert.Contains("intent-target", transition.AddLabels);
        Assert.Contains("intent-pr-reviewing", transition.AddLabels);
        Assert.Contains("intent-pr-rereview-ready", transition.RemoveLabels);
        Assert.Contains("rereview-ready", transition.RemoveLabels);
        Assert.DoesNotContain("intent-pr-created", transition.AddLabels);
        Assert.DoesNotContain("intent-pr-created", transition.RemoveLabels);
    }

    [Fact]
    public void Execute_Approved_WriteAppliesExactApprovedLabels()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-reviewing" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "542",
                "--transition", "approved",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.True(result.Applied);
        Assert.Contains("intent-pr-approved", result.AddLabels);
        Assert.Contains("intent-pr-reviewing", result.RemoveLabels);

        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Contains("intent-pr-approved", transition.AddLabels);
        Assert.Contains("intent-pr-reviewing", transition.RemoveLabels);
        Assert.DoesNotContain("intent-pr-created", transition.AddLabels);
        Assert.DoesNotContain("intent-pr-created", transition.RemoveLabels);
    }

    [Fact]
    public void CommandRouter_RegistersAutomationPrTransition()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-reviewing" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            [
                "automation",
                "pr-transition",
                "--repo",
                "J-Tech-Japan/intent-system",
                "--pr",
                "542",
                "--transition",
                "approved",
                "--format",
                "json",
            ],
            workspace.Context,
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.Equal("approved", result.Transition);
    }

    [Fact]
    public void CommandRouter_HelpSurfacesAutomationPrTransitionUsage()
    {
        using var workspace = new AutomationPrTransitionWorkspace();

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute([], workspace.Context, writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Automation commands:", output, StringComparison.Ordinal);
        Assert.Contains("automation pr-transition", output, StringComparison.Ordinal);
        Assert.Contains("--transition review-start", output, StringComparison.Ordinal);
        Assert.Contains("--transition approved", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverInvokesNestedProviderLauncher()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var launcherInvoked = false;
        AutomationPrTransitionCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };
        AutomationPrTransitionCommand.MutatorFactory = () => new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-reviewing" },
        };

        using var writer = new StringWriter();
        Assert.Equal(0, AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "542",
                "--transition", "approved",
                "--format", "json",
            },
            writer));

        Assert.False(launcherInvoked,
            "AutomationPrTransitionCommand must never invoke NestedProviderLauncher.");
    }

    private sealed class FakeMutator : IGitHubLabelMutator
    {
        public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();

        public List<AppliedTransition> AppliedTransitions { get; } = new();

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            Labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();

        public void ApplyLabelTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels)
        {
            if (string.Equals(kind, "pr", StringComparison.Ordinal)
                && (addLabels.Contains("intent-pr-created", StringComparer.Ordinal)
                    || removeLabels.Contains("intent-pr-created", StringComparer.Ordinal)))
            {
                throw new InvalidOperationException("'intent-pr-created' is issue-only.");
            }

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

    private sealed class AutomationPrTransitionWorkspace : IDisposable
    {
        public AutomationPrTransitionWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("automation-pr-transition-tests-").FullName;
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

        public void WriteOriginRemote(string remoteUrl)
        {
            var gitDirectory = Path.Combine(RootPath, ".git");
            Directory.CreateDirectory(gitDirectory);
            File.WriteAllText(
                Path.Combine(gitDirectory, "config"),
                $"""
                [remote "origin"]
                    url = {remoteUrl}
                """);
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
