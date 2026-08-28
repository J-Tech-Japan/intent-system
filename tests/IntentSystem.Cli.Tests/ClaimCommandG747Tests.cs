using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class ClaimCommandG747Tests
{
    [Fact]
    public void IdenticallyHeldAcquire_CleanupFailureReportsNoOpCauseSeparately_G747()
    {
        using var repos = new ClaimRepositories();
        const string scope = "execution-unit:G747-no-op";
        var acquired = ClaimCommand.RunTransaction(
            repos.FirstClone, Request(scope, "alice", "implementation"));
        Assert.Equal("acquired", acquired.Status);
        var before = ReadBareRef(repos.Bare, "main");

        using var output = new StringWriter();
        using var warnings = new StringWriter();
        var deleteAttempts = 0;
        string? leftoverPath = null;
        try
        {
            var exitCode = ClaimCommand.ExecuteAcquire(
                Context(repos.FirstClone),
                [
                    "--scope", scope,
                    "--actor", "alice",
                    "--team", "implementation",
                    "--write",
                    "--format", "json",
                ],
                output,
                warnings,
                path =>
                {
                    leftoverPath = path;
                    deleteAttempts++;
                    throw new IOException("injected no-op cleanup failure");
                });

            Assert.Equal(1, exitCode);
            using var emitted = JsonDocument.Parse(output.ToString());
            var result = emitted.RootElement;
            Assert.Equal("held", result.GetProperty("status").GetString());
            Assert.Equal(scope, result.GetProperty("scope").GetString());
            Assert.False(result.GetProperty("push_succeeded").GetBoolean());
            Assert.False(result.TryGetProperty("commit", out _));
            Assert.Contains("already held", result.GetProperty("detail").GetString()!, StringComparison.Ordinal);
            Assert.Contains("nothing to commit", result.GetProperty("detail").GetString()!, StringComparison.Ordinal);
            Assert.Equal("refs/heads/main", result.GetProperty("target_ref").GetString());
            Assert.DoesNotContain("warning:", output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("before the claim state was committed", result.GetProperty("detail").GetString()!, StringComparison.Ordinal);
            Assert.Equal(ClaimCommand.CleanupMaxAttempts, deleteAttempts);
            Assert.NotNull(leftoverPath);
            Assert.Contains(leftoverPath!, warnings.ToString(), StringComparison.Ordinal);
            Assert.Contains("cleanup could not remove", warnings.ToString(), StringComparison.Ordinal);
            Assert.Contains("already-held claim result and exit code are unchanged", warnings.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("ArgumentException", output.ToString() + warnings.ToString(), StringComparison.Ordinal);
            Assert.Equal(before, ReadBareRef(repos.Bare, "main"));
        }
        finally
        {
            if (leftoverPath is not null && Directory.Exists(leftoverPath))
            {
                Directory.Delete(leftoverPath, recursive: true);
            }
        }
    }
    [Fact]
    public void NonDefaultCheckout_PushesOnlyResolvedRemoteDefaultRef_G747()
    {
        using var repos = new ClaimRepositories();
        repos.PrepareNonDefaultCheckout();
        var currentBranchBefore = Run(repos.FirstClone, "git", "branch", "--show-current").Trim();
        var mainBefore = ReadBareRef(repos.Bare, "main");
        var featureBefore = ReadBareRef(repos.Bare, repos.FeatureBranch);

        var result = ClaimCommand.RunTransaction(
            repos.FirstClone,
            Request("execution-unit:G747-default-branch", "alice", "implementation"));

        Assert.Equal("acquired", result.Status);
        Assert.True(result.PushSucceeded);
        Assert.Equal("refs/heads/main", result.TargetRef);
        Assert.Equal(result.Commit, ReadBareRef(repos.Bare, "main"));
        Assert.NotEqual(mainBefore, ReadBareRef(repos.Bare, "main"));
        Assert.Equal(featureBefore, ReadBareRef(repos.Bare, repos.FeatureBranch));
        Assert.Equal(currentBranchBefore, Run(repos.FirstClone, "git", "branch", "--show-current").Trim());
    }

    [Fact]
    public void JsonOutput_IsOneDocumentWhileCleanupWarningRemainsObservable_G747()
    {
        using var repos = new ClaimRepositories();
        using var output = new StringWriter();
        using var warnings = new StringWriter();
        string? leftoverPath = null;
        try
        {
            var exitCode = ClaimCommand.ExecuteAcquire(
                Context(repos.FirstClone),
                [
                    "--scope", "execution-unit:G747-json",
                    "--actor", "alice",
                    "--team", "implementation",
                    "--write",
                    "--format", "json",
                ],
                output,
                warnings,
                path =>
                {
                    leftoverPath = path;
                    throw new IOException("injected JSON cleanup failure");
                });

            Assert.Equal(0, exitCode);
            using var emitted = JsonDocument.Parse(output.ToString());
            Assert.Equal("acquired", emitted.RootElement.GetProperty("status").GetString());
            Assert.True(emitted.RootElement.GetProperty("push_succeeded").GetBoolean());
            Assert.Equal("refs/heads/main", emitted.RootElement.GetProperty("target_ref").GetString());
            Assert.DoesNotContain("warning:", output.ToString(), StringComparison.Ordinal);
            Assert.NotNull(leftoverPath);
            Assert.Contains(leftoverPath!, warnings.ToString(), StringComparison.Ordinal);
            Assert.Contains("claim result and exit code are unchanged", warnings.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            if (leftoverPath is not null && Directory.Exists(leftoverPath))
            {
                Directory.Delete(leftoverPath, recursive: true);
            }
        }
    }

    [Fact]
    public void RemoteDefaultBranchParser_RequiresOneSafeSymrefAndHead_G747()
    {
        const string sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        Assert.True(ClaimCommand.TryParseRemoteDefaultBranch(
            $"ref: refs/heads/release-line\tHEAD\n{sha}\tHEAD\n",
            out var branch));
        Assert.Equal("release-line", branch);
        Assert.False(ClaimCommand.TryParseRemoteDefaultBranch(
            $"{sha}\tHEAD\n", out _));
        Assert.False(ClaimCommand.TryParseRemoteDefaultBranch(
            $"ref: refs/heads/main\tHEAD\n{sha}\tHEAD\nref: refs/heads/other\tHEAD\n", out _));
        Assert.False(ClaimCommand.TryParseRemoteDefaultBranch(
            $"ref: refs/heads/../main\tHEAD\n{sha}\tHEAD\n", out _));
    }

    private static ClaimRequest Request(string scope, string actor, string team) =>
        new(ClaimOperation.Acquire, scope, actor, team, null, null, true, "json", ClaimCommand.DefaultMaxAttempts);

    private static CliContext Context(string root) => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = "intent-cli",
                ArtifactRoot = ".intent-cli",
                WorktreeRoot = ".intent-cli/worktrees",
            },
        },
    };

    private static string ReadBareRef(string bare, string branch) =>
        Run(bare, "git", "rev-parse", $"refs/heads/{branch}").Trim();

    private static string Run(string workdir, string fileName, params string[] arguments)
    {
        var info = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workdir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{fileName} {string.Join(' ', arguments)} failed: {error}");
        return output;
    }

    private sealed class ClaimRepositories : IDisposable
    {
        private readonly TempDirectory temp = new("claim-repos-g747-");

        public ClaimRepositories()
        {
            Bare = Path.Combine(temp.Path, "origin.git");
            var seed = Path.Combine(temp.Path, "seed");
            FirstClone = Path.Combine(temp.Path, "first");
            Directory.CreateDirectory(Bare);
            Run(Bare, "git", "init", "--bare", "--quiet");
            Directory.CreateDirectory(seed);
            Run(seed, "git", "init", "--quiet", "--initial-branch=main");
            Run(seed, "git", "config", "user.name", "seed");
            Run(seed, "git", "config", "user.email", "seed@example.invalid");
            File.WriteAllText(Path.Combine(seed, "README.md"), "seed\n");
            Run(seed, "git", "add", "README.md");
            Run(seed, "git", "commit", "--quiet", "-m", "seed");
            Run(seed, "git", "remote", "add", "origin", Bare);
            Run(seed, "git", "push", "--quiet", "-u", "origin", "main");
            Run(Bare, "git", "symbolic-ref", "HEAD", "refs/heads/main");
            Run(temp.Path, "git", "clone", "--quiet", Bare, FirstClone);
        }

        public string Bare { get; }
        public string FirstClone { get; }
        public string FeatureBranch { get; } = "design-thread";

        public void PrepareNonDefaultCheckout()
        {
            Run(FirstClone, "git", "switch", "--quiet", "-c", FeatureBranch);
            File.WriteAllText(Path.Combine(FirstClone, "feature.txt"), "feature\n");
            Run(FirstClone, "git", "add", "feature.txt");
            Run(FirstClone, "git", "-c", "user.name=feature", "-c", "user.email=feature@example.invalid",
                "commit", "--quiet", "-m", "feature");
            Run(FirstClone, "git", "push", "--quiet", "-u", "origin", FeatureBranch);
        }

        public void Dispose() => temp.Dispose();
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix) => Path = Directory.CreateTempSubdirectory(prefix).FullName;
        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
