using System.Diagnostics;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G726: the release gate must compare the exact commit being released with
/// the repository default branch, and the tag survey must remain read-only.
/// These tests use temporary repositories so no real tag or release is made.
/// </summary>
public sealed class ReleaseReachabilityGateTests
{
    [Fact]
    public void ReachableCommit_UsesDefaultBranchAndStaysNonInteractive()
    {
        var repo = CreateRepository(out var mainCommit, out _);

        var result = RunGate(repo, "--commit", mainCommit, "--default-branch", "main");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("release-reachability: reachable", result.Output, StringComparison.Ordinal);
        Assert.Contains($"commit={mainCommit}", result.Output, StringComparison.Ordinal);
        Assert.Contains("default_branch=main", result.Output, StringComparison.Ordinal);
        Assert.Contains("ordinary_path=non-interactive", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("REFUSED", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void UnreachableCommit_IsRefusedWithConcreteRepositoryConsequence()
    {
        var repo = CreateRepository(out var mainCommit, out var sideCommit);
        var mainBefore = RunGit(repo, "rev-parse", "refs/heads/main").StandardOutput.Trim();

        var result = RunGate(repo, "--commit", sideCommit, "--default-branch", "main");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("release-reachability: REFUSED", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "the repository default branch will not contain the released source until this commit lands",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains("no release build or publish may proceed", result.Output, StringComparison.Ordinal);
        Assert.Contains("land the commit on the repository default branch", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("are you sure", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(mainBefore, RunGit(repo, "rev-parse", "refs/heads/main").StandardOutput.Trim());
    }

    [Fact]
    public void Survey_ReportsReachabilityForEveryVersionTagWithoutRewritingRefs()
    {
        var repo = CreateRepository(out var mainCommit, out var sideCommit);
        RequireSuccess(RunGit(repo, "tag", "v1.0.0", mainCommit));
        RequireSuccess(RunGit(repo, "tag", "v1.1.0", sideCommit));
        var mainBefore = RunGit(repo, "rev-parse", "refs/heads/main").StandardOutput.Trim();
        var tagsBefore = RunGit(repo, "tag", "--list", "v*", "--sort=version:refname").StandardOutput.Trim();

        var result = RunGate(repo, "--survey", "--default-branch", "main");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"tag=v1.0.0 commit={mainCommit} result=reachable", result.Output, StringComparison.Ordinal);
        Assert.Contains($"tag=v1.1.0 commit={sideCommit} result=unreachable", result.Output, StringComparison.Ordinal);
        Assert.Contains("total=2 reachable=1 unreachable=1 unresolved=0", result.Output, StringComparison.Ordinal);
        Assert.Equal(mainBefore, RunGit(repo, "rev-parse", "refs/heads/main").StandardOutput.Trim());
        Assert.Equal(tagsBefore, RunGit(repo, "tag", "--list", "v*", "--sort=version:refname").StandardOutput.Trim());
    }

    private static string CreateRepository(out string mainCommit, out string sideCommit)
    {
        var repo = Directory.CreateTempSubdirectory("intent-cli-g726-reachability-").FullName;
        RequireSuccess(RunGit(repo, "init", "--quiet", "--initial-branch=main"));
        RequireSuccess(RunGit(repo, "config", "user.name", "intent-cli-g726"));
        RequireSuccess(RunGit(repo, "config", "user.email", "intent-cli-g726@example.invalid"));

        File.WriteAllText(Path.Combine(repo, "README.md"), "main\n");
        RequireSuccess(RunGit(repo, "add", "--", "README.md"));
        RequireSuccess(RunGit(repo, "commit", "--quiet", "-m", "seed main"));
        mainCommit = RunGit(repo, "rev-parse", "HEAD").StandardOutput.Trim();

        RequireSuccess(RunGit(repo, "switch", "--quiet", "-c", "release-candidate"));
        File.WriteAllText(Path.Combine(repo, "release.txt"), "candidate\n");
        RequireSuccess(RunGit(repo, "add", "--", "release.txt"));
        RequireSuccess(RunGit(repo, "commit", "--quiet", "-m", "candidate"));
        sideCommit = RunGit(repo, "rev-parse", "HEAD").StandardOutput.Trim();
        RequireSuccess(RunGit(repo, "switch", "--quiet", "main"));
        return repo;
    }

    private static CommandResult RunGate(string repo, params string[] arguments)
    {
        var script = LocateReachabilityScript();
        var allArguments = new[] { script, "--repo-root", repo }.Concat(arguments).ToArray();
        return RunProcess("bash", repo, allArguments);
    }

    private static CommandResult RunGit(string repo, params string[] arguments) => RunProcess("git", repo, arguments);

    private static CommandResult RunProcess(string fileName, string workingDirectory, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new CommandResult(process.ExitCode, standardOutput.GetAwaiter().GetResult(), standardError.GetAwaiter().GetResult());
    }

    private static string LocateReachabilityScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "eng", "release-reachability.sh");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate eng/release-reachability.sh");
    }

    private static void RequireSuccess(CommandResult result)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Command failed ({result.ExitCode}): {result.Output}");
        }
    }

    private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string Output => StandardOutput + StandardError;
    }
}
