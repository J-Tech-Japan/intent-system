using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G700: lock contention is exercised against real Git index writes. These
/// fixtures intentionally remain on disk so the test suite never performs
/// cleanup that could remove a /tmp path or an index.lock artifact.
/// </summary>
public sealed class HostStateGitRetryG700Tests
{
    [Fact]
    public void DefaultPolicy_IsDeclaredAndGuideTextNamesTheSafetyBoundary_G700()
    {
        var policy = HostStateGitRetryPolicy.Default;

        Assert.Equal(4, policy.MaxAttempts);
        Assert.Equal(2000, policy.WindowMilliseconds);
        Assert.Equal(25, policy.InitialDelayMilliseconds);
        Assert.Equal(250, policy.MaxDelayMilliseconds);
        Assert.Equal(25, policy.JitterMilliseconds);
        Assert.Contains("max_attempts=4", policy.GuideDescription(), StringComparison.Ordinal);
        Assert.Contains("window=2000ms", policy.GuideDescription(), StringComparison.Ordinal);
        Assert.Contains("manual remediation", policy.GuideDescription(), StringComparison.Ordinal);
        Assert.Contains("Non-lock Git failures are not retried", policy.GuideDescription(), StringComparison.Ordinal);
    }

    [Fact]
    public void BriefCompetingGitIndexLock_SucceedsAfterRetryAndReportsAttempts_G700()
    {
        var repo = CreateRepositoryWithSlowCleanFilter();
        var lockPath = Path.Combine(repo, ".git", "index.lock");
        using var competing = StartGit(repo, "add", "--", "a.txt");
        WaitForPath(lockPath);

        ClaimProcessResult result;
        try
        {
            result = HostStateGitRetryRunner.Run(
                repo,
                ["add", "--", "b.txt"],
                () => RunGit(repo, "add", "--", "b.txt"),
                new HostStateGitRetryPolicy(10, 2000, 20, 100, 0),
                sleep: Thread.Sleep,
                jitterSample: static () => 0);
        }
        finally
        {
            competing.WaitForExit();
        }

        Assert.True(result.ExitCode == 0, result.StandardError);
        Assert.NotNull(result.RetryEvidence);
        var evidence = result.RetryEvidence!;
        Assert.Equal("succeeded", evidence.Outcome);
        Assert.True(evidence.Attempts >= 2, $"expected contention, got {evidence.Attempts} attempt(s)");
        Assert.Equal(QuotedPath(evidence.OriginalGitError), evidence.LockPath);
        Assert.Contains("index.lock", evidence.OriginalGitError, StringComparison.Ordinal);
        Assert.True(evidence.ElapsedMilliseconds >= 0);
    }

    [Fact]
    public void PersistentHeldLock_ExhaustsBoundWithOriginalErrorAndLeavesBytesUntouched_G700()
    {
        var repo = CreateRepository();
        var lockPath = Path.Combine(repo, ".git", "index.lock");
        var before = new byte[] { 0x47, 0x37, 0x30, 0x30, 0x0a, 0xff };
        File.WriteAllBytes(lockPath, before);

        var result = HostStateGitRetryRunner.Run(
            repo,
            ["add", "--", "b.txt"],
            () => RunGit(repo, "add", "--", "b.txt"),
            new HostStateGitRetryPolicy(3, 1000, 0, 0, 0),
            sleep: static _ => { },
            jitterSample: static () => 0);

        Assert.NotEqual(0, result.ExitCode);
        Assert.NotNull(result.RetryEvidence);
        var evidence = result.RetryEvidence!;
        Assert.Equal("exhausted", evidence.Outcome);
        Assert.Equal(3, evidence.Attempts);
        Assert.True(evidence.ElapsedMilliseconds >= 0);
        Assert.Equal(QuotedPath(evidence.OriginalGitError), evidence.LockPath);
        Assert.Contains("Unable to create", evidence.OriginalGitError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("index.lock", evidence.OriginalGitError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("manual", evidence.ManualRemediation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllBytes(lockPath));
        Assert.Contains("Original git error:", HostStateGitRetryRunner.TerminalDetail(evidence), StringComparison.Ordinal);
        Assert.Contains("Exact lock path:", HostStateGitRetryRunner.TerminalDetail(evidence), StringComparison.Ordinal);
        Assert.Contains("attempt(s)", HostStateGitRetryRunner.TerminalDetail(evidence), StringComparison.Ordinal);
    }

    [Fact]
    public void StaleLock_IsByteCompatibleAndRemediationNamesExactPath_G700()
    {
        var repo = CreateRepository();
        var lockPath = Path.Combine(repo, ".git", "index.lock");
        var before = System.Text.Encoding.UTF8.GetBytes("operator-owned stale lock bytes\n");
        File.WriteAllBytes(lockPath, before);

        var result = HostStateGitRetryRunner.Run(
            repo,
            ["add", "--", "b.txt"],
            () => RunGit(repo, "add", "--", "b.txt"),
            new HostStateGitRetryPolicy(2, 1000, 0, 0, 0),
            sleep: static _ => { },
            jitterSample: static () => 0);

        Assert.NotNull(result.RetryEvidence);
        var evidence = result.RetryEvidence!;
        Assert.Equal(QuotedPath(evidence.OriginalGitError), evidence.LockPath);
        Assert.Contains(evidence.LockPath, evidence.ManualRemediation, StringComparison.Ordinal);
        Assert.Contains("never removes", evidence.ManualRemediation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllBytes(lockPath));
    }

    [Fact]
    public void NonLockGitError_IsReturnedWithoutRetry_G700()
    {
        var repo = CreateRepository();
        var runCount = 0;
        var sleepCount = 0;

        var result = HostStateGitRetryRunner.Run(
            repo,
            ["add", "--", "does-not-exist.txt"],
            () =>
            {
                runCount++;
                return RunGit(repo, "add", "--", "does-not-exist.txt");
            },
            HostStateGitRetryPolicy.Default,
            sleep: _ => sleepCount++,
            jitterSample: static () => 0);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Null(result.RetryEvidence);
        Assert.Equal(1, runCount);
        Assert.Equal(0, sleepCount);
        Assert.DoesNotContain("index.lock", result.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("next", "orchestration")]
    [InlineData("orchestrator-thread", "orchestration")]
    public void ExistingRoleGuideRoutes_AreExecutableFromBareMetadataFreeDirectory_G700(
        string route,
        string role)
    {
        var bareDirectory = Directory.CreateTempSubdirectory("intent-cli-g700-guide-").FullName;
        var cliAssembly = typeof(CliContext).Assembly.Location;
        var arguments = route == "next"
            ? new[]
            {
                cliAssembly, "guide", "next", "--domain", "intent-cli", "--team", "intent-cli-dev",
                "--target-repo", "J-Tech-Japan/intent-system", "--role", role, "--format", "json",
            }
            : new[]
            {
                cliAssembly, "guide", "orchestrator-thread", "--domain", "intent-cli", "--team", "intent-cli-dev",
                "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--format", "json",
            };

        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = bareDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(0, process.ExitCode);
        Assert.True(string.IsNullOrEmpty(error), error);
        Assert.DoesNotContain("Could not find file", output, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(output);
        var json = document.RootElement;
        Assert.Contains("index.lock", json.ToString(), StringComparison.Ordinal);
        Assert.Contains("max_attempts=4", json.ToString(), StringComparison.Ordinal);
        Assert.Contains("manual remediation", json.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(bareDirectory, ".intent-cli", "config.toml")));
    }

    private static string CreateRepositoryWithSlowCleanFilter()
    {
        var repo = CreateRepository();
        var filterDirectory = Directory.CreateDirectory(Path.Combine(repo, "filter")).FullName;
        var cleanPath = Path.Combine(filterDirectory, "clean");
        File.WriteAllText(cleanPath, "#!/bin/sh\nsleep 0.250\ncat\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                cleanPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        RunGit(repo, "config", "filter.g700.clean", cleanPath);
        RunGit(repo, "config", "filter.g700.required", "true");
        File.WriteAllText(Path.Combine(repo, ".gitattributes"), "a.txt filter=g700\n");
        RequireSuccess(RunGit(repo, "add", ".gitattributes"));
        RequireSuccess(RunGit(repo, "commit", "--quiet", "-m", "configure slow filter"));
        File.WriteAllText(Path.Combine(repo, "a.txt"), "changed\n");
        File.WriteAllText(Path.Combine(repo, "b.txt"), "second\n");
        return repo;
    }

    private static string CreateRepository()
    {
        var repo = Directory.CreateTempSubdirectory("intent-cli-g700-git-").FullName;
        RequireSuccess(RunGit(repo, "init", "--quiet", "--initial-branch=main"));
        RequireSuccess(RunGit(repo, "config", "user.name", "intent-cli-g700"));
        RequireSuccess(RunGit(repo, "config", "user.email", "intent-cli-g700@example.invalid"));
        File.WriteAllText(Path.Combine(repo, "a.txt"), "seed\n");
        File.WriteAllText(Path.Combine(repo, "b.txt"), "second\n");
        RequireSuccess(RunGit(repo, "add", "a.txt", "b.txt"));
        RequireSuccess(RunGit(repo, "commit", "--quiet", "-m", "seed"));
        return repo;
    }

    private static Process StartGit(string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info)!;
    }

    private static void WaitForPath(string path)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * 2.0);
        while (!File.Exists(path) && Stopwatch.GetTimestamp() < deadline)
        {
            Thread.Sleep(5);
        }

        Assert.True(File.Exists(path), $"competing Git process did not expose {path}");
    }

    private static ClaimProcessResult RunGit(string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ClaimProcessResult(process.ExitCode, output, error);
    }

    private static void RequireSuccess(ClaimProcessResult result) =>
        Assert.True(result.ExitCode == 0, result.StandardError);

    private static string QuotedPath(string gitError)
    {
        var firstQuote = gitError.IndexOf('\'');
        Assert.True(firstQuote >= 0, gitError);
        var secondQuote = gitError.IndexOf('\'', firstQuote + 1);
        Assert.True(secondQuote > firstQuote, gitError);
        return gitError[(firstQuote + 1)..secondQuote];
    }
}
