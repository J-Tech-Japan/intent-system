using System.Security.Cryptography;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G213: Tests for <c>intent-cli automation check</c>. The command infers a
/// GitHub repo from a local worktree and delegates to worker next-action
/// without mutating files, labels, issues, PRs, branches, or launching a
/// provider.
/// </summary>
public sealed class AutomationCheckCommandTests : IDisposable
{
    public AutomationCheckCommandTests()
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
    public void Execute_InfersRepoFromHttpsOriginRemote()
    {
        using var workspace = new AutomationCheckWorkspace();
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git");
        var lister = new CapturingLister
        {
            Issues = new[]
            {
                BuildIssue(531, "G213", "https://github.com/J-Tech-Japan/intent-system/issues/531",
                    "2026-05-01T00:00:00Z", new[] { "intent-target" }),
            },
        };
        WorkerNextActionCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationCheckCommand.Execute(
            workspace.Context,
            new[] { "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        Assert.Equal("J-Tech-Japan/intent-system", lister.SeenRepo);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal("J-Tech-Japan/intent-system", result.Repo);
        Assert.Equal(WorkerNextActionConstants.Actions.IssueToPr, result.Action);
        Assert.Equal(531, result.Number);
    }

    [Fact]
    public void Execute_InfersRepoFromSshOriginRemote()
    {
        using var workspace = new AutomationCheckWorkspace();
        workspace.WriteOriginRemote("git@github.com:J-Tech-Japan/intent-system.git");
        var lister = new CapturingLister();
        WorkerNextActionCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationCheckCommand.Execute(
            workspace.Context,
            new[] { "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        Assert.Equal("J-Tech-Japan/intent-system", lister.SeenRepo);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.None, result.Action);
    }

    [Fact]
    public void Execute_RepoOverrideSkipsWorktreeInference()
    {
        using var workspace = new AutomationCheckWorkspace();
        var lister = new CapturingLister();
        WorkerNextActionCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationCheckCommand.Execute(
            workspace.Context,
            new[] { "--repo", "Override/Repo", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        Assert.Equal("Override/Repo", lister.SeenRepo);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal("Override/Repo", result.Repo);
    }

    [Fact]
    public void Execute_WorkdirChangesRepoInferenceRoot()
    {
        using var workspace = new AutomationCheckWorkspace();
        var child = workspace.CreateChildWorkdir("child");
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/wrong.git");
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system", child);
        var lister = new CapturingLister();
        WorkerNextActionCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationCheckCommand.Execute(
            workspace.Context,
            new[] { "--workdir", child, "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        Assert.Equal("J-Tech-Japan/intent-system", lister.SeenRepo);
    }

    [Fact]
    public void Execute_InferenceFailureReturnsActionableError()
    {
        using var workspace = new AutomationCheckWorkspace();
        workspace.WriteOriginRemote("https://example.com/J-Tech-Japan/intent-system.git");

        using var writer = new StringWriter();
        var exitCode = AutomationCheckCommand.Execute(
            workspace.Context,
            new[] { "--format", "json" },
            writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Cannot infer GitHub repo", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Expected a github.com owner/repo remote", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CommandRouter_RegistersAutomationCheck()
    {
        using var workspace = new AutomationCheckWorkspace();
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git");
        WorkerNextActionCommand.CandidateListerFactory = () => new CapturingLister();

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            new[] { "automation", "check", "--format", "json" },
            workspace.Context,
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.None, result.Action);
    }

    [Fact]
    public void ProgramMain_AutomationCheckDoesNotRequireIntentCliDirectory()
    {
        using var workspace = new AutomationCheckWorkspace();
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git");
        WorkerNextActionCommand.CandidateListerFactory = () => new CapturingLister();
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

            var exitCode = Program.Main(new[] { "automation", "check", "--format", "json" });

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());
            var result = JsonSerializer.Deserialize<WorkerNextActionResult>(stdout.ToString())!;
            Assert.Equal("J-Tech-Japan/intent-system", result.Repo);
            Assert.Equal(WorkerNextActionConstants.Actions.None, result.Action);
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
        using var workspace = new AutomationCheckWorkspace();
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git");
        var launcherInvoked = false;
        WorkerNextActionCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };
        WorkerNextActionCommand.CandidateListerFactory = () => new CapturingLister();

        using var writer = new StringWriter();
        var exitCode = AutomationCheckCommand.Execute(
            workspace.Context,
            new[] { "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(launcherInvoked,
            "AutomationCheckCommand must never invoke NestedProviderLauncher.");
    }

    [Fact]
    public void Execute_LeavesWorkspaceByteEquivalent()
    {
        using var workspace = new AutomationCheckWorkspace();
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git");
        var before = workspace.SnapshotWorkspace();
        WorkerNextActionCommand.CandidateListerFactory = () => new CapturingLister();

        using (var writer = new StringWriter())
        {
            Assert.Equal(0, AutomationCheckCommand.Execute(
                workspace.Context,
                new[] { "--format", "json" },
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

    private static GitHubAutomationIssueCandidate BuildIssue(
        int number,
        string title,
        string url,
        string createdAt,
        string[] labels)
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

    private sealed class CapturingLister : IGitHubAutomationCandidateLister
    {
        public string? SeenRepo { get; private set; }

        public IReadOnlyList<GitHubAutomationPrCandidate> Prs { get; init; }
            = Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationIssueCandidate> Issues { get; init; }
            = Array.Empty<GitHubAutomationIssueCandidate>();

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels)
        {
            SeenRepo = repo;
            return Prs;
        }

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo,
            IReadOnlyCollection<string> requiredLabels)
        {
            SeenRepo = repo;
            return Issues;
        }
    }

    private sealed class AutomationCheckWorkspace : IDisposable
    {
        public AutomationCheckWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("automation-check-tests-").FullName;
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
