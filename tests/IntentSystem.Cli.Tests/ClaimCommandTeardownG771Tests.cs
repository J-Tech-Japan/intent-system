using System.Diagnostics;
using System.Text;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class ClaimCommandTeardownG771Tests
{
    [Fact]
    public void CommittedCleanupFailure_WarnsAndPreservesSuccess_G771()
    {
        using var repos = new ClaimRepositories();
        using var warnings = new StringWriter();
        var deletedPaths = new List<string>();

        try
        {
            var result = ClaimCommand.RunTransaction(
                repos.FirstClone,
                Request(ClaimOperation.Acquire, "execution-unit:G771-committed", "alice", "implementation"),
                warnings,
                path =>
                {
                    deletedPaths.Add(path);
                    throw new IOException("injected committed cleanup failure");
                });

            Assert.Equal("acquired", result.Status);
            Assert.True(result.PushSucceeded);
            Assert.Equal(0, ExitCode(result));
            Assert.NotEmpty(deletedPaths);
            var transactionRoot = deletedPaths[^1];
            var expectedWarning =
                $"warning: claim transaction committed successfully, but best-effort cleanup could not remove temporary directory '{transactionRoot}' after 3 bounded attempt(s); the claim result and exit code are unchanged. The leftover path remains under the OS temp root.";
            Assert.Contains(expectedWarning, warnings.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            foreach (var path in deletedPaths.Distinct(StringComparer.Ordinal)) DeleteIfPresent(path);
        }
    }

    [Theory]
    [InlineData("acquired")]
    [InlineData("held-by-another")]
    [InlineData("not-held")]
    [InlineData("release-by-non-holder")]
    [InlineData("takeover-mismatch")]
    public void CleanupFailure_PreservesEveryClaimOutcomeAndExitCode_G771(string outcome)
    {
        var working = RunScenario(outcome, failCleanup: false);
        var failing = RunScenario(outcome, failCleanup: true);

        Assert.Null(working.ErrorMessage);
        Assert.Null(failing.ErrorMessage);
        Assert.Equal(working.Status, failing.Status);
        Assert.Equal(working.PushSucceeded, failing.PushSucceeded);
        Assert.Equal(working.Holder, failing.Holder);
        Assert.Equal(working.HolderTeam, failing.HolderTeam);
        Assert.Equal(working.DisplacedHolder, failing.DisplacedHolder);
        Assert.Equal(working.Detail, failing.Detail);
        Assert.Equal(working.HistoryPath, failing.HistoryPath);
        Assert.Equal(working.TargetRef, failing.TargetRef);
        Assert.Equal(working.ExitCode, failing.ExitCode);
        Assert.Equal(outcome == "acquired", failing.PushSucceeded);
        Assert.Equal(outcome == "acquired" ? 0 : 1, failing.ExitCode);
        Assert.NotNull(failing.LeftoverPath);
        Assert.Contains(failing.LeftoverPath!, failing.Warning, StringComparison.Ordinal);
        Assert.Contains("claim result and exit code are unchanged", failing.Warning, StringComparison.Ordinal);
        Assert.Contains("OS temp root", failing.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void PreCommitCloneFailure_PreservesOriginalCauseWhenCleanupAlsoFails_G771()
    {
        using var repos = new ClaimRepositories();
        using var warnings = new StringWriter();
        string? leftoverPath = null;

        try
        {
            using (new GitCloneFailureShim())
            {
                var exception = Assert.Throws<InvalidOperationException>(() =>
                    ClaimCommand.RunTransaction(
                        repos.FirstClone,
                        Request(ClaimOperation.Acquire, "execution-unit:G771-clone", "alice", "implementation"),
                        warnings,
                        path =>
                        {
                            leftoverPath = path;
                            throw new IOException("injected cleanup failure");
                        }));

                Assert.Contains("clone claim transaction workspace", exception.Message, StringComparison.Ordinal);
                Assert.Contains("injected clone failure", exception.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("injected cleanup failure", exception.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("could not remove temporary directory", exception.Message, StringComparison.Ordinal);
            }

            Assert.NotNull(leftoverPath);
            Assert.Contains(leftoverPath!, warnings.ToString(), StringComparison.Ordinal);
            Assert.Contains("original transaction failure is preserved", warnings.ToString(), StringComparison.Ordinal);
            Assert.Contains("cleanup also failed", warnings.ToString(), StringComparison.Ordinal);
            Assert.Equal(ClaimCommand.CleanupMaxAttempts, CountCleanupAttempts(warnings.ToString()));
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
    public void CommittedWarningRemainsByteIdenticalToDocumentation_G771()
    {
        const string documentedWarning =
            "warning: claim transaction committed successfully, but best-effort cleanup could not remove temporary directory '<path>' after 3 bounded attempt(s); the claim result and exit code are unchanged. The leftover path remains under the OS temp root.";

        var english = File.ReadAllText(
            Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", "en", "09-developer-reference.md"));
        var japanese = File.ReadAllText(
            Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", "ja", "09-developer-reference.md"));

        Assert.Contains(documentedWarning, english, StringComparison.Ordinal);
        Assert.Contains(documentedWarning, japanese, StringComparison.Ordinal);
    }

    [Fact]
    public void StaleSweepRemovesOldRootButProtectsLiveTransactionRoot_G771()
    {
        using var repos = new ClaimRepositories();
        var staleRoot = CreateOldTransactionRoot("intent-cli-claim-g771-stale-");
        var liveRoot = CreateOldTransactionRoot("intent-cli-claim-g771-live-");
        var liveLeasePath = liveRoot + ".lease";
        using var liveLease = new FileStream(
            liveLeasePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None);
        liveLease.WriteByte(1);
        liveLease.Flush(flushToDisk: true);

        try
        {
            using var warnings = new StringWriter();
            var result = ClaimCommand.RunTransaction(
                repos.FirstClone,
                Request(ClaimOperation.Acquire, "execution-unit:G771-sweep", "alice", "implementation"),
                warnings,
                path => Directory.Delete(path, recursive: true));

            Assert.Equal("acquired", result.Status);
            Assert.False(Directory.Exists(staleRoot));
            Assert.True(Directory.Exists(liveRoot));
            Assert.DoesNotContain(staleRoot, warnings.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            liveLease.Dispose();
            DeleteIfPresent(liveRoot);
            DeleteIfPresent(liveLeasePath);
            DeleteIfPresent(staleRoot);
        }
    }

    [Fact]
    public void StaleSweepFailureDoesNotChangeClaimResult_G771()
    {
        using var repos = new ClaimRepositories();
        var staleRoot = CreateOldTransactionRoot("intent-cli-claim-g771-failing-sweep-");

        try
        {
            using var warnings = new StringWriter();
            var result = ClaimCommand.RunTransaction(
                repos.FirstClone,
                Request(ClaimOperation.Acquire, "execution-unit:G771-sweep-failure", "alice", "implementation"),
                warnings,
                path =>
                {
                    if (string.Equals(path, staleRoot, StringComparison.Ordinal))
                    {
                        throw new IOException("injected stale sweep failure");
                    }

                    Directory.Delete(path, recursive: true);
                });

            Assert.Equal("acquired", result.Status);
            Assert.True(result.PushSucceeded);
            Assert.Contains(staleRoot, warnings.ToString(), StringComparison.Ordinal);
            Assert.Contains("could not be swept", warnings.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteIfPresent(staleRoot);
        }
    }

    [Fact]
    public void CleanupFailureOnHeldAcquireNeverReportsOwnershipOrPushSuccess_G771()
    {
        using var repos = new ClaimRepositories();
        const string scope = "execution-unit:G771-no-false-success";
        var existing = ClaimCommand.RunTransaction(
            repos.FirstClone,
            Request(ClaimOperation.Acquire, scope, "alice", "implementation"));
        Assert.Equal("acquired", existing.Status);

        using var warnings = new StringWriter();
        string? leftoverPath = null;
        try
        {
            var result = ClaimCommand.RunTransaction(
                repos.FirstClone,
                Request(ClaimOperation.Acquire, scope, "bob", "review"),
                warnings,
                path =>
                {
                    leftoverPath = path;
                    throw new IOException("injected held cleanup failure");
                });

            Assert.Equal("held", result.Status);
            Assert.False(result.PushSucceeded);
            Assert.Null(result.Commit);
            Assert.Equal("alice", result.Holder);
            Assert.Equal("implementation", result.HolderTeam);
            Assert.NotNull(leftoverPath);
            Assert.Contains(leftoverPath!, warnings.ToString(), StringComparison.Ordinal);
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
    public void DocumentationDescribesAllOutcomeCleanupAndStaleSweep_G771()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(root, "docs", "en", "09-developer-reference.md"));
        var japanese = File.ReadAllText(Path.Combine(root, "docs", "ja", "09-developer-reference.md"));

        Assert.Contains("Cleanup is best-effort on every claim outcome.", english, StringComparison.Ordinal);
        Assert.Contains("stale transaction roots are swept", english, StringComparison.Ordinal);
        Assert.Contains("すべての claim outcome", japanese, StringComparison.Ordinal);
        Assert.Contains("stale transaction root", japanese, StringComparison.Ordinal);
    }

    private static ScenarioObservation RunScenario(string outcome, bool failCleanup)
    {
        using var repos = new ClaimRepositories();
        var scope = $"execution-unit:G771-{outcome}";
        if (outcome is "held-by-another" or "release-by-non-holder" or "takeover-mismatch")
        {
            var seed = ClaimCommand.RunTransaction(
                repos.FirstClone,
                Request(ClaimOperation.Acquire, scope, "alice", "implementation"));
            Assert.Equal("acquired", seed.Status);
        }

        var request = outcome switch
        {
            "acquired" => Request(ClaimOperation.Acquire, scope, "alice", "implementation"),
            "held-by-another" => Request(ClaimOperation.Acquire, scope, "bob", "review"),
            "not-held" => Request(ClaimOperation.Release, scope, "alice", "implementation"),
            "release-by-non-holder" => Request(ClaimOperation.Release, scope, "bob", "review"),
            "takeover-mismatch" => new ClaimRequest(
                ClaimOperation.Takeover,
                scope,
                "bob",
                "review",
                "operator reassigned",
                "charlie",
                true,
                "json",
                ClaimCommand.DefaultMaxAttempts),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
        };

        using var warnings = new StringWriter();
        var leftoverPaths = new List<string>();
        ClaimTransactionResult? result = null;
        Exception? error = null;
        try
        {
            result = ClaimCommand.RunTransaction(
                repos.FirstClone,
                request,
                warnings,
                path =>
                {
                    leftoverPaths.Add(path);
                    if (failCleanup)
                    {
                        throw new IOException("injected cleanup failure");
                    }

                    Directory.Delete(path, recursive: true);
                });
        }
        catch (Exception exception)
        {
            error = exception;
        }

        try
        {
            foreach (var path in leftoverPaths.Distinct(StringComparer.Ordinal))
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Test-owned cleanup only.
        }

        return new ScenarioObservation(
            result?.Status,
            result?.PushSucceeded,
            result?.Holder,
            result?.HolderTeam,
            result?.DisplacedHolder,
            result?.Detail,
            result?.HistoryPath,
            result?.TargetRef,
            result is null ? 1 : ExitCode(result),
            error?.Message,
            leftoverPaths.LastOrDefault(),
            warnings.ToString());
    }

    private static int ExitCode(ClaimTransactionResult result) =>
        result.Status is "acquired" or "released" or "taken-over" or "planned" ? 0 : 1;

    private static int CountCleanupAttempts(string warning)
    {
        return warning.Contains("after 3 bounded attempt(s)", StringComparison.Ordinal) ? 3 : 0;
    }

    private static string CreateOldTransactionRoot(string prefix)
    {
        var root = Directory.CreateTempSubdirectory(prefix).FullName;
        File.WriteAllText(Path.Combine(root, "leftover.txt"), "stale transaction root\n");
        Directory.SetLastWriteTimeUtc(root, DateTime.UtcNow.AddHours(-1));
        return root;
    }

    private static void DeleteIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static ClaimRequest Request(
        ClaimOperation operation,
        string scope,
        string actor,
        string team) =>
        new(
            operation,
            scope,
            actor,
            team,
            operation == ClaimOperation.Acquire ? null : "test reason",
            operation == ClaimOperation.Takeover ? "charlie" : null,
            true,
            "json",
            ClaimCommand.DefaultMaxAttempts);

    private static int Run(string workdir, string fileName, params string[] arguments)
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
        return process.ExitCode;
    }

    private sealed record ScenarioObservation(
        string? Status,
        bool? PushSucceeded,
        string? Holder,
        string? HolderTeam,
        string? DisplacedHolder,
        string? Detail,
        string? HistoryPath,
        string? TargetRef,
        int ExitCode,
        string? ErrorMessage,
        string? LeftoverPath,
        string Warning);

    private sealed class GitCloneFailureShim : IDisposable
    {
        private readonly string originalPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        private readonly string root = Directory.CreateTempSubdirectory("claim-g771-git-shim-").FullName;

        public GitCloneFailureShim()
        {
            var wrapper = Path.Combine(root, "git");
            File.WriteAllText(
                wrapper,
                "#!/bin/sh\n"
                + "if [ \"${1:-}\" = \"clone\" ]; then\n"
                + "  target=\"\"\n"
                + "  for arg in \"$@\"; do target=\"$arg\"; done\n"
                + "  mkdir -p \"$target\"\n"
                + "  echo \"injected clone failure\" >&2\n"
                + "  exit 77\n"
                + "fi\n"
                + "exec /usr/bin/git \"$@\"\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    wrapper,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Environment.SetEnvironmentVariable("PATH", root + Path.PathSeparator + originalPath);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ClaimRepositories : IDisposable
    {
        private readonly TempDirectory temp = new("claim-repos-g771-");

        public ClaimRepositories()
        {
            Bare = Path.Combine(temp.Path, "origin.git");
            var seed = Path.Combine(temp.Path, "seed");
            FirstClone = Path.Combine(temp.Path, "first");
            Directory.CreateDirectory(Bare);
            Run(Bare, "git", "init", "--bare", "--quiet");
            Directory.CreateDirectory(seed);
            Run(seed, "git", "init", "--quiet", "--initial-branch=main");
            Run(seed, "git", "config", "user.name", "g771-fixture");
            Run(seed, "git", "config", "user.email", "g771-fixture@example.invalid");
            File.WriteAllText(Path.Combine(seed, "README.md"), "g771 fixture\n");
            Run(seed, "git", "add", "README.md");
            Run(seed, "git", "commit", "--quiet", "-m", "seed");
            Run(seed, "git", "remote", "add", "origin", Bare);
            Run(seed, "git", "push", "--quiet", "-u", "origin", "main");
            Run(Bare, "git", "symbolic-ref", "HEAD", "refs/heads/main");
            Run(temp.Path, "git", "clone", "--quiet", Bare, FirstClone);
        }

        public string Bare { get; }
        public string FirstClone { get; }

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
