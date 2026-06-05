using System.Security.Cryptography;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G215: Tests for <c>intent-cli automation clarification-stop</c>. The
/// helper renders a deterministic owner-facing stop summary and never
/// mutates GitHub, labels, files, branches, or launches a provider.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class AutomationClarificationStopCommandTests : IDisposable
{
    public AutomationClarificationStopCommandTests()
    {
        AutomationClarificationStopCommand.NestedProviderLauncher = null;
    }

    public void Dispose()
    {
        AutomationClarificationStopCommand.NestedProviderLauncher = null;
    }

    [Fact]
    public void Execute_IssueToPrContext_EmitsStableJsonWithOwnerAction()
    {
        using var workspace = new AutomationClarificationStopWorkspace();
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git");

        using var writer = new StringWriter();
        var exitCode = AutomationClarificationStopCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--issue", "535",
                "--reason", "Issue contract is missing acceptance criteria.",
                "--recommended-owner-action", "Repair the issue body into a standalone contract.",
                "--cooldown", "Do not retry until the owner updates the issue.",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationClarificationStopResult>(writer.ToString())!;
        Assert.Equal("issue-to-pr", result.Kind);
        Assert.Equal("clarification-required", result.Status);
        Assert.Equal(535, result.TargetNumber);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/535", result.TargetUrl);
        Assert.Contains("missing acceptance criteria", result.Reason, StringComparison.Ordinal);
        Assert.Contains("standalone contract", result.RecommendedOwnerAction, StringComparison.Ordinal);
        Assert.False(result.Mutated);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Execute_PrCommentFixContext_UsesPullUrl()
    {
        using var workspace = new AutomationClarificationStopWorkspace();

        using var writer = new StringWriter();
        var exitCode = AutomationClarificationStopCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "pr-comment-fix",
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "536",
                "--reason", "Review thread is ambiguous.",
                "--recommended-owner-action", "Clarify the requested repair in the PR thread.",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationClarificationStopResult>(writer.ToString())!;
        Assert.Equal("pr-comment-fix", result.Kind);
        Assert.Equal(536, result.TargetNumber);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/536", result.TargetUrl);
        Assert.False(result.Mutated);
    }

    [Fact]
    public void Execute_ExplicitUrlOverridesRepoInference()
    {
        using var workspace = new AutomationClarificationStopWorkspace();

        using var writer = new StringWriter();
        var exitCode = AutomationClarificationStopCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--number", "535",
                "--url", "https://github.example.local/custom/535",
                "--reason", "Ambiguous contract.",
                "--recommended-owner-action", "Update the issue.",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationClarificationStopResult>(writer.ToString())!;
        Assert.Equal("https://github.example.local/custom/535", result.TargetUrl);
    }

    [Fact]
    public void Execute_MissingReasonFailsDeterministically()
    {
        using var workspace = new AutomationClarificationStopWorkspace();

        using var writer = new StringWriter();
        var exitCode = AutomationClarificationStopCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--repo", "J-Tech-Japan/intent-system",
                "--issue", "535",
                "--recommended-owner-action", "Update the issue.",
                "--format", "json",
            },
            writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--reason is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CommandRouter_RegistersAutomationClarificationStop()
    {
        using var workspace = new AutomationClarificationStopWorkspace();

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            new[]
            {
                "automation",
                "clarification-stop",
                "--kind", "issue-to-pr",
                "--repo", "J-Tech-Japan/intent-system",
                "--issue", "535",
                "--reason", "Ambiguous issue.",
                "--recommended-owner-action", "Repair the issue.",
                "--format", "json",
            },
            workspace.Context,
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationClarificationStopResult>(writer.ToString())!;
        Assert.Equal("clarification-required", result.Status);
    }

    [Fact]
    public void ProgramMain_AutomationClarificationStopDoesNotRequireIntentCliDirectory()
    {
        using var workspace = new AutomationClarificationStopWorkspace();
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
                "clarification-stop",
                "--kind", "issue-to-pr",
                "--repo", "J-Tech-Japan/intent-system",
                "--issue", "535",
                "--reason", "Ambiguous issue.",
                "--recommended-owner-action", "Repair the issue.",
                "--format", "json",
            });

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());
            var result = JsonSerializer.Deserialize<AutomationClarificationStopResult>(stdout.ToString())!;
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/535", result.TargetUrl);
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
        using var workspace = new AutomationClarificationStopWorkspace();
        var launcherInvoked = false;
        AutomationClarificationStopCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };

        using var writer = new StringWriter();
        Assert.Equal(0, AutomationClarificationStopCommand.Execute(
            workspace.Context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--repo", "J-Tech-Japan/intent-system",
                "--issue", "535",
                "--reason", "Ambiguous issue.",
                "--recommended-owner-action", "Repair the issue.",
                "--format", "json",
            },
            writer));

        Assert.False(launcherInvoked);
    }

    [Fact]
    public void Execute_LeavesWorkspaceByteEquivalent()
    {
        using var workspace = new AutomationClarificationStopWorkspace();
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git");
        var before = workspace.SnapshotWorkspace();

        using (var writer = new StringWriter())
        {
            Assert.Equal(0, AutomationClarificationStopCommand.Execute(
                workspace.Context,
                new[]
                {
                    "--kind", "issue-to-pr",
                    "--issue", "535",
                    "--reason", "Ambiguous issue.",
                    "--recommended-owner-action", "Repair the issue.",
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

    private sealed class AutomationClarificationStopWorkspace : IDisposable
    {
        public AutomationClarificationStopWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("automation-clarification-stop-tests-").FullName;
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
