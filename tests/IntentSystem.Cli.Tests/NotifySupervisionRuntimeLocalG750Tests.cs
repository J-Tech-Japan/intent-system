using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class NotifySupervisionRuntimeLocalG750Tests : IDisposable
{
    private const string Domain = "demo";
    private const string Team = "intent-cli-dev";
    private readonly string root = Directory.CreateTempSubdirectory("intent-g750-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FreshInitCreatesScopedIgnoreAndKeepsSharedSupervisionStateTrackable_G750()
    {
        InitializeGitRepository();
        using var output = new StringWriter();

        Assert.Equal(
            0,
            IntentInitCommand.Execute(
                CreateContext(),
                ["--domain", Domain, "--write", "--format", "json"],
                output));

        var artifactRoot = CreateSupervisionFiles();
        var localIgnorePath = NotifySupervisionStore.ResolveCycleHistoryIgnorePath(artifactRoot);
        Assert.Equal(
            string.Join(Environment.NewLine, NotifySupervisionStore.CycleHistoryIgnoreLines) + Environment.NewLine,
            File.ReadAllText(localIgnorePath));

        RequireSuccess(RunGit("add", "."));
        RequireSuccess(RunGit("commit", "--quiet", "-m", "fresh G750 scaffold"));

        var ignored = new[]
        {
            Path.Combine(".intent-cli", "supervision", Domain, Team, "cycles.jsonl"),
            Path.Combine(".intent-cli", "supervision", Domain, Team, "cycles-archive", "2026-08.jsonl"),
        };
        var shared = new[]
        {
            Path.Combine(".intent-cli", "supervision", Domain, Team, "stalls.jsonl"),
            Path.Combine(".intent-cli", "supervision", Domain, Team, "bound.json"),
            Path.Combine(".intent-cli", "supervision", Domain, Team, "emission-policy.json"),
            Path.Combine(".intent-cli", "supervision", Domain, Team, "evidence-definitions.json"),
            Path.Combine(".intent-cli", "supervision", Domain, Team, "pre-approval-policy.json"),
            Path.Combine(".intent-cli", "supervision", Domain, Team, "shrink-audit.jsonl"),
        };
        Assert.All(ignored, path => Assert.Equal(0, RunGit("check-ignore", "-q", "--", path).ExitCode));
        Assert.All(shared, path => Assert.NotEqual(0, RunGit("check-ignore", "-q", "--", path).ExitCode));

        var tracked = RunGit("ls-files", "--", Path.Combine(".intent-cli", "supervision", Domain, Team))
            .StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var inventory = $"ignored={string.Join(",", ignored.Select(Normalize))}; tracked={string.Join(",", tracked)}";
        Console.WriteLine($"G750 scaffold inventory: {inventory}");
        Assert.DoesNotContain(ignored.Select(Normalize), path => tracked.Contains(path, StringComparer.Ordinal));
        Assert.All(shared.Select(Normalize), path => Assert.Contains(path, tracked, StringComparer.Ordinal));
    }

    [Fact]
    public void CycleWriterMaintainsScopedIgnoreWhenWritingToGitHost_G750()
    {
        InitializeGitRepository();
        var artifactRoot = Path.Combine(root, ".intent-cli", "supervision");
        var cyclePath = NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team);
        var cycle = new NotifySupervisionCycle
        {
            CycleId = "g750-cycle",
            StartedAt = DateTimeOffset.Parse("2026-08-28T12:00:00Z"),
            CompletedAt = DateTimeOffset.Parse("2026-08-28T12:01:00Z"),
            IntervalSeconds = 300,
        };

        var result = NotifySupervisionStore.RecordCycle(cyclePath, cycle, write: true);

        Assert.True(result.Applied, result.Error);
        Assert.True(File.Exists(NotifySupervisionStore.ResolveCycleHistoryIgnorePath(artifactRoot)));
        Assert.Equal(0, RunGit("check-ignore", "-q", "--", Path.GetRelativePath(root, cyclePath)).ExitCode);
        Assert.Contains("g750-cycle", File.ReadAllText(cyclePath), StringComparison.Ordinal);
    }

    [Fact]
    public void RepairCommandUntracksExistingCycleHistoryWithoutDeletingFiles_G750()
    {
        InitializeGitRepository();
        var artifactRoot = Path.Combine(root, ".intent-cli", "supervision");
        var teamDirectory = NotifySupervisionStore.ResolveDirectory(artifactRoot, Domain, Team);
        Directory.CreateDirectory(teamDirectory);
        File.WriteAllText(
            Path.Combine(root, ".gitignore"),
            ".intent-cli/supervision/**/cycles.jsonl\n"
            + ".intent-cli/supervision/**/stalls.jsonl\n"
            + "keep-local.txt\n");
        var cyclePath = Path.Combine(teamDirectory, "cycles.jsonl");
        var archivePath = Path.Combine(teamDirectory, "cycles-archive", "2026-08.jsonl");
        var stallsPath = Path.Combine(teamDirectory, "stalls.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        File.WriteAllText(cyclePath, "cycle-content\n");
        File.WriteAllText(archivePath, "archive-content\n");
        File.WriteAllText(stallsPath, "shared-stall-content\n");
        File.WriteAllText(Path.Combine(teamDirectory, "bound.json"), "{}\n");
        File.WriteAllText(Path.Combine(teamDirectory, "emission-policy.json"), "{}\n");
        File.WriteAllText(Path.Combine(teamDirectory, "evidence-definitions.json"), "{}\n");
        File.WriteAllText(Path.Combine(teamDirectory, "pre-approval-policy.json"), "{}\n");
        File.WriteAllText(Path.Combine(teamDirectory, "shrink-audit.jsonl"), "audit\n");

        RequireSuccess(RunGit("add", ".gitignore", "--", Path.Combine(".intent-cli", "supervision", Domain, Team)));
        RequireSuccess(RunGit("add", "-f", "--", Path.GetRelativePath(root, cyclePath), Path.GetRelativePath(root, archivePath), Path.GetRelativePath(root, stallsPath)));
        RequireSuccess(RunGit("commit", "--quiet", "-m", "tracked supervision history"));

        using var output = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(),
            [
                NotifySuperviseRepairCycleHistoryCommand.Operation,
                "--domain", Domain,
                "--team", Team,
                "--write",
                "--format", "json",
            ],
            output);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var payload = document.RootElement;
        Assert.Equal("repair-cycle-history", payload.GetProperty("operation").GetString());
        Assert.True(payload.GetProperty("applied").GetBoolean());
        Assert.True(payload.GetProperty("preserved_files").GetBoolean());
        Assert.Equal(
            new[]
            {
                Normalize(Path.GetRelativePath(root, cyclePath)),
                Normalize(Path.GetRelativePath(root, archivePath)),
            }.Order(StringComparer.Ordinal).ToArray(),
            payload.GetProperty("removed_from_index").EnumerateArray().Select(item => item.GetString()!).Order(StringComparer.Ordinal).ToArray());
        Assert.Contains(".intent-cli/supervision/**/cycles.jsonl", output.ToString(), StringComparison.Ordinal);
        Assert.Contains(".intent-cli/supervision/**/stalls.jsonl", output.ToString(), StringComparison.Ordinal);

        Assert.Equal("cycle-content\n", File.ReadAllText(cyclePath));
        Assert.Equal("archive-content\n", File.ReadAllText(archivePath));
        Assert.Empty(RunGit("ls-files", "--", Path.GetRelativePath(root, cyclePath)).StdOut);
        Assert.Empty(RunGit("ls-files", "--", Path.GetRelativePath(root, archivePath)).StdOut);
        Assert.Contains(
            Normalize(Path.GetRelativePath(root, stallsPath)),
            RunGit("ls-files", "--", Path.Combine(".intent-cli", "supervision", Domain, Team)).StdOut.Replace('\\', '/'),
            StringComparison.Ordinal);
        Assert.Equal(0, RunGit("check-ignore", "-q", "--", Path.GetRelativePath(root, cyclePath)).ExitCode);
        Assert.NotEqual(0, RunGit("check-ignore", "-q", "--", Path.GetRelativePath(root, stallsPath)).ExitCode);
        var rootIgnore = File.ReadAllText(Path.Combine(root, ".gitignore"));
        Assert.DoesNotContain(".intent-cli/supervision/**/cycles.jsonl", rootIgnore, StringComparison.Ordinal);
        Assert.DoesNotContain(".intent-cli/supervision/**/stalls.jsonl", rootIgnore, StringComparison.Ordinal);
        Assert.Contains("keep-local.txt", rootIgnore, StringComparison.Ordinal);
        Console.WriteLine($"G750 repair inventory: before=cycles.jsonl, cycles-archive/2026-08.jsonl, stalls.jsonl; after-index=stalls.jsonl; files-preserved=true; result={output}");
    }

    [Fact]
    public void RepairCommandDryRunReportsTrackedPathsWithoutMutation_G750()
    {
        InitializeGitRepository();
        var artifactRoot = Path.Combine(root, ".intent-cli", "supervision");
        var teamDirectory = NotifySupervisionStore.ResolveDirectory(artifactRoot, Domain, Team);
        Directory.CreateDirectory(teamDirectory);
        var cyclePath = Path.Combine(teamDirectory, "cycles.jsonl");
        File.WriteAllText(cyclePath, "cycle-content\n");
        RequireSuccess(RunGit("add", "."));
        RequireSuccess(RunGit("commit", "--quiet", "-m", "tracked cycle"));
        var before = File.ReadAllText(cyclePath);

        using var output = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(),
            [NotifySuperviseRepairCycleHistoryCommand.Operation, "--domain", Domain, "--team", Team, "--dry-run", "--format", "json"],
            output);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.True(document.RootElement.GetProperty("would_change").GetBoolean());
        Assert.False(document.RootElement.GetProperty("applied").GetBoolean());
        Assert.Equal(before, File.ReadAllText(cyclePath));
        Assert.Contains(Normalize(Path.GetRelativePath(root, cyclePath)), RunGit("ls-files", "--", Path.GetRelativePath(root, cyclePath)).StdOut, StringComparison.Ordinal);
        Assert.False(File.Exists(NotifySupervisionStore.ResolveCycleHistoryIgnorePath(artifactRoot)));
    }

    [Fact]
    public void DocumentationMirrorsDescribeNarrowRuntimeLocalOwnership_G750()
    {
        var repositoryRoot = RepoVersionPolicySource.RepoRoot();
        foreach (var language in new[] { "en", "ja" })
        {
            var content = File.ReadAllText(Path.Combine(repositoryRoot, "docs", language, "12-agent-message-orchestration.md"));
            Assert.Contains("G750", content, StringComparison.Ordinal);
            Assert.Contains(".intent-cli/supervision/.gitignore", content, StringComparison.Ordinal);
            Assert.Contains("**/cycles.jsonl", content, StringComparison.Ordinal);
            Assert.Contains("**/cycles-archive/", content, StringComparison.Ordinal);
            Assert.Contains("stalls.jsonl", content, StringComparison.Ordinal);
            Assert.Contains("pre-approval-policy.json", content, StringComparison.Ordinal);
            Assert.Contains("shrink-audit.jsonl", content, StringComparison.Ordinal);
            Assert.Contains("repair-cycle-history", content, StringComparison.Ordinal);
            Assert.Contains("G751", content, StringComparison.Ordinal);
        }
    }

    private void InitializeGitRepository()
    {
        RequireSuccess(RunGit("init", "--quiet", "--initial-branch=main"));
        RequireSuccess(RunGit("config", "user.name", "intent-cli-g750-tests"));
        RequireSuccess(RunGit("config", "user.email", "intent-cli-g750@example.invalid"));
    }

    private string CreateSupervisionFiles()
    {
        var artifactRoot = Path.Combine(root, ".intent-cli", "supervision");
        var teamDirectory = NotifySupervisionStore.ResolveDirectory(artifactRoot, Domain, Team);
        Directory.CreateDirectory(Path.Combine(teamDirectory, "cycles-archive"));
        File.WriteAllText(Path.Combine(teamDirectory, "cycles.jsonl"), "cycle\n");
        File.WriteAllText(Path.Combine(teamDirectory, "cycles-archive", "2026-08.jsonl"), "archive\n");
        File.WriteAllText(Path.Combine(teamDirectory, "stalls.jsonl"), "stall\n");
        File.WriteAllText(Path.Combine(teamDirectory, "bound.json"), "{}\n");
        File.WriteAllText(Path.Combine(teamDirectory, "emission-policy.json"), "{}\n");
        File.WriteAllText(Path.Combine(teamDirectory, "evidence-definitions.json"), "{}\n");
        File.WriteAllText(Path.Combine(teamDirectory, "pre-approval-policy.json"), "{}\n");
        File.WriteAllText(Path.Combine(teamDirectory, "shrink-audit.jsonl"), "audit\n");
        return artifactRoot;
    }

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = Domain,
                ArtifactRoot = ".intent-cli",
            },
            Supervision = new SupervisionConfig
            {
                ArtifactRoot = ".intent-cli/supervision",
            },
        },
    };

    private static string Normalize(string path) => path.Replace('\\', '/');

    private CommandResult RunGit(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("failed to start git");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new CommandResult(process.ExitCode, stdout, stderr);
    }

    private static void RequireSuccess(CommandResult result)
    {
        Assert.True(result.ExitCode == 0, $"git failed ({result.ExitCode}): {result.StdErr}\n{result.StdOut}");
    }

    private sealed record CommandResult(int ExitCode, string StdOut, string StdErr);
}
