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
    public void Execute_ReviewStart_DryRunSucceedsWhenOptionalRereviewLabelsAreAbsent()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target" },
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
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.False(result.Applied);
        Assert.Contains("intent-pr-reviewing", result.AddLabels);
        Assert.Contains("intent-pr-rereview-ready", result.RemoveLabels);
        Assert.Contains("rereview-ready", result.RemoveLabels);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_ReviewStart_WriteSkipsAbsentOptionalRereviewLabels()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target" },
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
        Assert.Contains("intent-pr-reviewing", result.AddLabels);
        Assert.Empty(result.RemoveLabels);

        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Contains("intent-pr-reviewing", transition.AddLabels);
        Assert.Empty(transition.RemoveLabels);
        Assert.DoesNotContain("intent-pr-created", transition.AddLabels);
        Assert.DoesNotContain("intent-pr-created", transition.RemoveLabels);
    }

    [Fact]
    public void Execute_ReviewStart_WriteRemovesOnlyPresentRereviewLabels()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-rereview-ready" },
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

        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Contains("intent-pr-reviewing", transition.AddLabels);
        Assert.Contains("intent-pr-rereview-ready", transition.RemoveLabels);
        Assert.DoesNotContain("rereview-ready", transition.RemoveLabels);
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
    public void Execute_RequestUpdate_DryRunPlansRepairRequestLabelsWithoutApplying()
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
                "--transition", "request-update",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.Equal("request-update", result.Transition);
        Assert.Equal("dry-run", result.Mode);
        Assert.False(result.Applied);
        Assert.Contains("intent-pr-request-update", result.AddLabels);
        Assert.Contains("intent-pr-reviewing", result.RemoveLabels);
        Assert.DoesNotContain("intent-pr-created", result.AddLabels);
        Assert.DoesNotContain("intent-pr-created", result.RemoveLabels);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_RequestUpdate_WriteAppliesExactRepairRequestLabels()
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
                "--transition", "request-update",
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
        Assert.Contains("intent-pr-request-update", transition.AddLabels);
        Assert.Contains("intent-pr-reviewing", transition.RemoveLabels);
        Assert.DoesNotContain("intent-pr-created", transition.AddLabels);
        Assert.DoesNotContain("intent-pr-created", transition.RemoveLabels);
    }

    // ─── G292 tests ────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_G292_ReviewRelease_RemovesReviewingWithoutAddingRequestUpdate()
    {
        // PR #682-shape: review-start was applied, then host metadata blocked
        // closeout. review-release must drop intent-pr-reviewing cleanly
        // without adding intent-pr-request-update so the implementer is not
        // told to repair host metadata.
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
                "--pr", "682",
                "--transition", "review-release",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.Equal("review-release", result.Transition);
        Assert.True(result.Applied);

        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Empty(transition.AddLabels);
        Assert.Contains("intent-pr-reviewing", transition.RemoveLabels);
        // Critically: review-release MUST NOT add request-update or any other
        // review-side label.
        Assert.DoesNotContain("intent-pr-request-update", transition.AddLabels);
        Assert.DoesNotContain("intent-pr-approved", transition.AddLabels);
        Assert.DoesNotContain("intent-pr-rereview-ready", transition.AddLabels);
    }

    [Fact]
    public void Execute_G292_ReviewRelease_DryRunReportsExactPlan()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        AutomationPrTransitionCommand.MutatorFactory = () => new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-reviewing" },
        };

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "682",
                "--transition", "review-release",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.Equal("dry-run", result.Mode);
        Assert.False(result.Applied);
        Assert.Empty(result.AddLabels);
        Assert.Contains("intent-pr-reviewing", result.RemoveLabels);
    }

    [Fact]
    public void Execute_G292_ReviewRelease_WriteIsIdempotentWhenReviewingAlreadyAbsent()
    {
        // Defensive: if a previous run already removed intent-pr-reviewing, a
        // re-run of review-release should not error and should not try to
        // remove a label that isn't there. Mirrors the review-start dry-run
        // hardening.
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "682",
                "--transition", "review-release",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Empty(transition.AddLabels);
        // The label wasn't present, so the write plan should not list it.
        Assert.DoesNotContain("intent-pr-reviewing", transition.RemoveLabels);
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
        Assert.Contains("--transition request-update", output, StringComparison.Ordinal);
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

            foreach (var removeLabel in removeLabels)
            {
                if (!Labels.Contains(removeLabel, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"cannot remove absent label '{removeLabel}' in fake mutator");
                }
            }

            AppliedTransitions.Add(new AppliedTransition(
                repo,
                kind,
                number,
                addLabels.ToArray(),
                removeLabels.ToArray()));
        }

        public void ApplyReconcileTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException("reconcile path not exercised by these tests");
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
