using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G791 repair: production-path coverage for the host -> owning submodule ->
/// nested submodule boundary. These fixtures deliberately use real Git
/// repositories and leave the command factories unset, so parser and index vs
/// HEAD mistakes cannot be hidden by a synthetic probe.
/// </summary>
[Collection(AutomationSubmoduleSafetySharedStateCollection.Name)]
public sealed class G791NestedSubmoduleProductionFixtureTests : IDisposable
{
    public G791NestedSubmoduleProductionFixtureTests()
    {
        AutomationWorkspaceGuardCommand.GitRunnerFactory = null;
        AutomationWorkspaceGuardCommand.UtcNowFactory = null;
        AutomationHostSyncPreflightCommand.GitProbeFactory = null;
    }

    public void Dispose()
    {
        AutomationWorkspaceGuardCommand.GitRunnerFactory = null;
        AutomationWorkspaceGuardCommand.UtcNowFactory = null;
        AutomationHostSyncPreflightCommand.GitProbeFactory = null;
    }

    [Fact]
    public void Execute_G791_ProductionTopology_CleanNestedPointerDrift_ProceedsWithoutTouchingForeignPaths()
    {
        using var topology = RealNestedTopology.Create(TopologyState.CleanNestedPointerDrift);
        var before = topology.CaptureBoundary();

        var plan = ExecuteWorkspaceGuard(topology.Context, ["--mode", "plan", "--format", "json"]);
        Assert.True(plan.ExitCode == 0, plan.Output + Environment.NewLine + before);
        AssertWorkspaceGuardProceedingNestedPointerDrift(plan.Output);

        var begin = ExecuteWorkspaceGuard(topology.Context, ["--mode", "begin", "--write", "--format", "json"]);
        Assert.Equal(0, begin.ExitCode);
        AssertWorkspaceGuardProceedingNestedPointerDrift(begin.Output);
        Assert.False(File.Exists(Path.Combine(topology.HostRoot, ".intent-cli", "workspace-guard.json")));

        var preflight = ExecuteHostSyncPreflight(topology.Context);
        Assert.Equal(0, preflight.ExitCode);
        AssertHostSyncProceedingNestedPointerDrift(preflight.Output);

        Assert.Equal(before, topology.CaptureBoundary());
    }

    [Fact]
    public void Execute_G791_ProductionTopology_NestedUncommittedContent_HardStopsWithoutSafeStash()
    {
        using var topology = RealNestedTopology.Create(TopologyState.NestedUncommittedContent);
        var before = topology.CaptureBoundary();

        var plan = ExecuteWorkspaceGuard(topology.Context, ["--mode", "plan", "--format", "json"]);
        Assert.Equal(1, plan.ExitCode);
        AssertWorkspaceGuardHardStop(plan.Output);

        var begin = ExecuteWorkspaceGuard(topology.Context, ["--mode", "begin", "--write", "--format", "json"]);
        Assert.Equal(1, begin.ExitCode);
        AssertWorkspaceGuardHardStop(begin.Output);
        Assert.False(File.Exists(Path.Combine(topology.HostRoot, ".intent-cli", "workspace-guard.json")));

        var preflight = ExecuteHostSyncPreflight(topology.Context);
        Assert.Equal(1, preflight.ExitCode);
        AssertHostSyncHardStop(preflight.Output, topology.OwningSubmodulePath);

        Assert.Equal(before, topology.CaptureBoundary());
        Assert.True(File.Exists(Path.Combine(topology.NestedSubmoduleRoot, "foreign-content.txt")));
    }

    [Fact]
    public void Execute_G791_ProductionTopology_StagedParentGitlink_HardStopsWithoutDirectProceed()
    {
        using var topology = RealNestedTopology.Create(TopologyState.StagedParentGitlinkWithCleanNestedDrift);
        var before = topology.CaptureBoundary();
        Assert.Contains("MM owner", before.HostPorcelain, StringComparison.Ordinal);

        var plan = ExecuteWorkspaceGuard(topology.Context, ["--mode", "plan", "--format", "json"]);
        Assert.Equal(1, plan.ExitCode);
        AssertWorkspaceGuardHardStop(plan.Output);
        Assert.DoesNotContain("nested-pointer-drift", plan.Output, StringComparison.Ordinal);

        var begin = ExecuteWorkspaceGuard(topology.Context, ["--mode", "begin", "--write", "--format", "json"]);
        Assert.Equal(1, begin.ExitCode);
        AssertWorkspaceGuardHardStop(begin.Output);
        Assert.False(File.Exists(Path.Combine(topology.HostRoot, ".intent-cli", "workspace-guard.json")));

        var preflight = ExecuteHostSyncPreflight(topology.Context);
        Assert.Equal(1, preflight.ExitCode);
        AssertHostSyncHardStop(preflight.Output, topology.OwningSubmodulePath);
        Assert.DoesNotContain("nested-pointer-drift", preflight.Output, StringComparison.Ordinal);

        Assert.Equal(before, topology.CaptureBoundary());
    }

    private static (int ExitCode, string Output) ExecuteWorkspaceGuard(CliContext context, string[] args)
    {
        using var writer = new StringWriter();
        var exitCode = AutomationWorkspaceGuardCommand.Execute(context, args, writer);
        return (exitCode, writer.ToString());
    }

    private static (int ExitCode, string Output) ExecuteHostSyncPreflight(CliContext context)
    {
        using var writer = new StringWriter();
        var exitCode = AutomationHostSyncPreflightCommand.Execute(context, ["--format", "json"], writer);
        return (exitCode, writer.ToString());
    }

    private static void AssertWorkspaceGuardProceedingNestedPointerDrift(string output)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.True(root.GetProperty("proceed_allowed").GetBoolean());
        Assert.Empty(root.GetProperty("safe_stash_paths").EnumerateArray());
        Assert.Single(root.GetProperty("nested_pointer_drift_submodules").EnumerateArray());
    }

    private static void AssertHostSyncProceedingNestedPointerDrift(string output)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.Equal("nested-pointer-drift", root.GetProperty("classification").GetString());
        Assert.True(root.GetProperty("proceed_allowed").GetBoolean());
        Assert.False(root.GetProperty("safe_stash_required").GetBoolean());
        Assert.Empty(root.GetProperty("safe_stash_paths").EnumerateArray());
        Assert.Single(root.GetProperty("nested_pointer_drift_submodules").EnumerateArray());
    }

    private static void AssertWorkspaceGuardHardStop(string output)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.False(root.GetProperty("proceed_allowed").GetBoolean());
        Assert.Empty(root.GetProperty("safe_stash_paths").EnumerateArray());
        Assert.Empty(root.GetProperty("nested_pointer_drift_submodules").EnumerateArray());
        Assert.Contains("internal uncommitted changes", root.GetProperty("summary").GetString()!, StringComparison.Ordinal);
    }

    private static void AssertHostSyncHardStop(string output, string owningPath)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.Equal("submodule-internal-dirty", root.GetProperty("classification").GetString());
        Assert.False(root.GetProperty("proceed_allowed").GetBoolean());
        Assert.False(root.GetProperty("safe_stash_required").GetBoolean());
        Assert.Empty(root.GetProperty("safe_stash_paths").EnumerateArray());
        Assert.Equal(owningPath, Assert.Single(root.GetProperty("submodule_internal_dirty_paths").EnumerateArray()).GetString());
    }

    private enum TopologyState
    {
        CleanNestedPointerDrift,
        NestedUncommittedContent,
        StagedParentGitlinkWithCleanNestedDrift,
    }

    private sealed class RealNestedTopology : IDisposable
    {
        private readonly string root;

        private RealNestedTopology(string root, string hostRoot, string ownerCommitAtHostHead, string ownerSecondCommit, string nestedPointerCommit)
        {
            this.root = root;
            HostRoot = hostRoot;
            OwnerCommitAtHostHead = ownerCommitAtHostHead;
            OwnerSecondCommit = ownerSecondCommit;
            NestedPointerCommit = nestedPointerCommit;
            OwningSubmodulePath = "owner";
            OwningSubmoduleRoot = Path.Combine(HostRoot, OwningSubmodulePath);
            NestedSubmoduleRoot = Path.Combine(OwningSubmoduleRoot, "nested");
            Context = new CliContext
            {
                RepoRoot = HostRoot,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    }
                }
            };
        }

        public string HostRoot { get; }

        public string OwningSubmodulePath { get; }

        public string OwningSubmoduleRoot { get; }

        public string NestedSubmoduleRoot { get; }

        public string OwnerCommitAtHostHead { get; }

        public string OwnerSecondCommit { get; }

        public string NestedPointerCommit { get; }

        public CliContext Context { get; }

        public static RealNestedTopology Create(TopologyState state)
        {
            var root = Directory.CreateTempSubdirectory("g791-real-nested-topology-").FullName;
            var nestedSeed = Path.Combine(root, "nested-seed");
            var nestedRemote = Path.Combine(root, "nested-remote.git");
            var ownerSeed = Path.Combine(root, "owner-seed");
            var ownerRemote = Path.Combine(root, "owner-remote.git");
            var hostRoot = Path.Combine(root, "host");
            var hostRemote = Path.Combine(root, "host-remote.git");

            InitializeRepository(nestedSeed);
            File.WriteAllText(Path.Combine(nestedSeed, "nested.txt"), "nested A\n");
            CommitAll(nestedSeed, "nested A");
            var nestedAtA = RunGit(nestedSeed, "rev-parse", "HEAD").Trim();
            InitializeBareRepository(nestedRemote);
            RunGit(nestedSeed, "remote", "add", "origin", nestedRemote);
            RunGit(nestedSeed, "push", "-u", "origin", "main");

            InitializeRepository(ownerSeed);
            File.WriteAllText(Path.Combine(ownerSeed, "owner.txt"), "owner A\n");
            RunGit(ownerSeed, "-c", "protocol.file.allow=always", "submodule", "add", nestedRemote, "nested");
            CommitAll(ownerSeed, "owner with nested A");
            var ownerAtA = RunGit(ownerSeed, "rev-parse", "HEAD").Trim();
            File.WriteAllText(Path.Combine(ownerSeed, "owner.txt"), "owner C\n");
            CommitAll(ownerSeed, "owner second commit");
            var ownerAtC = RunGit(ownerSeed, "rev-parse", "HEAD").Trim();
            InitializeBareRepository(ownerRemote);
            RunGit(ownerSeed, "remote", "add", "origin", ownerRemote);
            RunGit(ownerSeed, "push", "-u", "origin", "main");

            File.WriteAllText(Path.Combine(nestedSeed, "nested.txt"), "nested B\n");
            CommitAll(nestedSeed, "nested B");
            var nestedAtB = RunGit(nestedSeed, "rev-parse", "HEAD").Trim();
            RunGit(nestedSeed, "push", "origin", "main");

            InitializeRepository(hostRoot);
            RunGit(hostRoot, "-c", "protocol.file.allow=always", "submodule", "add", ownerRemote, "owner");
            var ownerRoot = Path.Combine(hostRoot, "owner");
            RunGit(ownerRoot, "checkout", ownerAtA);
            RunGit(ownerRoot, "-c", "protocol.file.allow=always", "submodule", "update", "--init", "nested");
            RunGit(hostRoot, "add", "owner", ".gitmodules");
            CommitAll(hostRoot, "host records owner A");
            InitializeBareRepository(hostRemote);
            RunGit(hostRoot, "remote", "add", "origin", hostRemote);
            RunGit(hostRoot, "push", "-u", "origin", "main");

            var topology = new RealNestedTopology(root, hostRoot, ownerAtA, ownerAtC, nestedAtB);
            RunGit(topology.NestedSubmoduleRoot, "fetch", "origin");
            RunGit(topology.NestedSubmoduleRoot, "checkout", nestedAtB);

            switch (state)
            {
                case TopologyState.CleanNestedPointerDrift:
                    break;
                case TopologyState.NestedUncommittedContent:
                    File.WriteAllText(Path.Combine(topology.NestedSubmoduleRoot, "foreign-content.txt"), "owned elsewhere\n");
                    break;
                case TopologyState.StagedParentGitlinkWithCleanNestedDrift:
                    RunGit(topology.OwningSubmoduleRoot, "fetch", "origin");
                    RunGit(topology.OwningSubmoduleRoot, "checkout", ownerAtC);
                    RunGit(hostRoot, "add", topology.OwningSubmodulePath);
                    RunGit(topology.NestedSubmoduleRoot, "checkout", nestedAtB);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }

            Assert.Equal(ownerAtA, RunGit(hostRoot, "rev-parse", "HEAD:owner").Trim());
            Assert.Equal(nestedAtB, RunGit(topology.NestedSubmoduleRoot, "rev-parse", "HEAD").Trim());
            return topology;
        }

        public TopologyBoundary CaptureBoundary() => new(
            RunGit(HostRoot, "status", "--porcelain"),
            RunGit(HostRoot, "stash", "list"),
            RunGit(OwningSubmoduleRoot, "rev-parse", "HEAD").Trim(),
            RunGit(NestedSubmoduleRoot, "rev-parse", "HEAD").Trim(),
            RunGit(HostRoot, "submodule", "status"),
            RunGit(OwningSubmoduleRoot, "status", "--porcelain"),
            RunGit(OwningSubmoduleRoot, "submodule", "status"),
            RunGit(NestedSubmoduleRoot, "status", "--porcelain"),
            RunGit(HostRoot, "diff", "--cached", "--raw", "--", OwningSubmodulePath),
            RunGit(HostRoot, "ls-tree", "HEAD", "--", OwningSubmodulePath));

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static void InitializeRepository(string path)
        {
            Directory.CreateDirectory(path);
            RunGit(path, "init", "--initial-branch", "main");
            RunGit(path, "config", "user.email", "g791-fixture@example.test");
            RunGit(path, "config", "user.name", "G791 fixture");
        }

        private static void InitializeBareRepository(string path)
        {
            Directory.CreateDirectory(path);
            RunGit(path, "init", "--bare", "--initial-branch", "main");
        }

        private static void CommitAll(string path, string message)
        {
            RunGit(path, "add", "--all");
            RunGit(path, "commit", "-m", message);
        }

        private static string RunGit(string workingDirectory, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0,
                $"git {string.Join(' ', arguments)} failed in '{workingDirectory}' with exit {process.ExitCode}: {error}");
            return output;
        }
    }

    private sealed record TopologyBoundary(
        string HostPorcelain,
        string HostStashList,
        string OwnerHead,
        string NestedHead,
        string HostSubmoduleStatus,
        string OwnerPorcelain,
        string OwnerSubmoduleStatus,
        string NestedPorcelain,
        string StagedRaw,
        string HeadGitlink);
}
