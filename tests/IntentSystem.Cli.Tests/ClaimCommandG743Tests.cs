using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class ClaimCommandG743Tests
{
    [Fact]
    public void RemoteCommitAfterPushProcessFailure_PreservesCommittedResultAndWarns_G743()
    {
        if (OperatingSystem.IsWindows()) return;

        using var repos = new ClaimRepositories();
        using var output = new StringWriter();
        using var warnings = new StringWriter();
        var deleteAttempts = 0;
        string? leftoverPath = null;

        try
        {
            using (new GitPushFailureShim())
            {
                var exitCode = ClaimCommand.ExecuteAcquire(
                    Context(repos.FirstClone),
                    [
                        "--scope", "execution-unit:G743",
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
                        throw new IOException("injected post-commit cleanup failure");
                    });

                Assert.True(exitCode == 0, $"expected committed success; exit_code={exitCode}; result={output}; warnings={warnings}");
            }

            using var emitted = JsonDocument.Parse(output.ToString());
            Assert.Equal("acquired", emitted.RootElement.GetProperty("status").GetString());
            Assert.True(emitted.RootElement.GetProperty("push_succeeded").GetBoolean());
            var commit = emitted.RootElement.GetProperty("commit").GetString();
            Assert.False(string.IsNullOrWhiteSpace(commit));
            Assert.Contains("remote branch", emitted.RootElement.GetProperty("detail").GetString()!, StringComparison.Ordinal);
            Assert.Equal(ClaimCommand.CleanupMaxAttempts, deleteAttempts);
            Assert.NotNull(leftoverPath);
            Assert.StartsWith(Path.GetTempPath(), leftoverPath!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(leftoverPath, warnings.ToString(), StringComparison.Ordinal);
            Assert.Contains("claim result and exit code are unchanged", warnings.ToString(), StringComparison.Ordinal);

            var remoteHead = Run(repos.Bare, "git", "rev-parse", "refs/heads/main").Trim();
            Assert.Equal(commit, remoteHead);
            var inspection = repos.CloneForInspection();
            Assert.True(File.Exists(Path.Combine(
                inspection, ClaimCommand.ClaimPath("execution-unit:G743"))));
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
    public void PreCommitFailure_IsNotMaskedByCleanupFailure_G743()
    {
        using var repos = new ClaimRepositories();
        var scope = "execution-unit:G743-precommit";
        var blockedClaimPath = Path.Combine(
            repos.FirstClone, ClaimCommand.ClaimPath(scope).Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(blockedClaimPath);
        File.WriteAllText(Path.Combine(blockedClaimPath, "blocker"), "tracked directory blocker\n");
        Run(repos.FirstClone, "git", "add", "--", ClaimCommand.ClaimPath(scope));
        Run(repos.FirstClone, "git", "commit", "--quiet", "-m", "create claim path blocker");
        Run(repos.FirstClone, "git", "push", "--quiet", "origin", "main");

        using var warnings = new StringWriter();
        string? leftoverPath = null;
        try
        {
            var exception = Assert.ThrowsAny<Exception>(() => ClaimCommand.RunTransaction(
                repos.FirstClone,
                new ClaimRequest(
                    ClaimOperation.Acquire,
                    scope,
                    "alice",
                    "implementation",
                    null,
                    null,
                    true,
                    "json",
                    ClaimCommand.DefaultMaxAttempts),
                warnings,
                path =>
                {
                    leftoverPath = path;
                    throw new IOException("injected pre-commit cleanup failure");
                }));

            Assert.DoesNotContain("before the claim state was committed", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("injected pre-commit cleanup failure", exception.Message, StringComparison.Ordinal);
            Assert.NotEmpty(exception.Message);
            Assert.NotNull(leftoverPath);
            Assert.Contains(leftoverPath, warnings.ToString(), StringComparison.Ordinal);
            Assert.Contains("original transaction failure is preserved", warnings.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            if (leftoverPath is not null && Directory.Exists(leftoverPath))
            {
                Directory.Delete(leftoverPath, recursive: true);
            }
        }
    }

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

    private sealed class GitPushFailureShim : IDisposable
    {
        private readonly string originalPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        private readonly string root = Directory.CreateTempSubdirectory("claim-git-shim-").FullName;

        public GitPushFailureShim()
        {
            var wrapper = Path.Combine(root, "git");
            File.WriteAllText(
                wrapper,
                "#!/bin/sh\n"
                + "if [ \"${1:-}\" = \"push\" ]; then\n"
                + "  /usr/bin/git \"$@\"\n"
                + "  status=$?\n"
                + "  if [ \"$status\" -eq 0 ]; then exit 77; fi\n"
                + "  exit \"$status\"\n"
                + "fi\n"
                + "exec /usr/bin/git \"$@\"\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    wrapper,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            Environment.SetEnvironmentVariable(
                "PATH", root + Path.PathSeparator + originalPath);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ClaimRepositories : IDisposable
    {
        private readonly TempDirectory temp = new("claim-repos-g743-");

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

        public string CloneForInspection()
        {
            var path = Path.Combine(temp.Path, $"inspect-{Guid.NewGuid():N}");
            Run(temp.Path, "git", "clone", "--quiet", Bare, path);
            return path;
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
