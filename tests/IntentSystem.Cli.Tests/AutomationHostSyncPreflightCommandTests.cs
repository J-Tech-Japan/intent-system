using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G400: command-level coverage that the host-sync preflight command threads the
/// ahead-count and diverged-commit details from the git probe into the analyzer
/// and surfaces the ff-blocked pre-mutation operator stop in its JSON + exit code.
/// </summary>
public sealed class AutomationHostSyncPreflightCommandTests : IDisposable
{
    public void Dispose() => AutomationHostSyncPreflightCommand.GitProbeFactory = null;

    [Fact]
    public void Execute_FfBlockedProbe_EmitsFfBlockedJson_AndNonZeroExit()
    {
        AutomationHostSyncPreflightCommand.GitProbeFactory = _ => new HostSyncGitProbe
        {
            Branch = "main",
            BehindOriginCommits = 2,
            AheadOriginCommits = 1,
            LocalOnlyCommits = new[] { new HostSyncCommitInfo { Sha = "abc1234", Title = "G399 local-only packet commit" } },
            OriginAheadCommits = new[]
            {
                new HostSyncCommitInfo { Sha = "def5678", Title = "Merge PR #900" },
                new HostSyncCommitInfo { Sha = "9990abc", Title = "Merge PR #901" }
            },
            WorkingTreeEntries = Array.Empty<HostSyncWorkingTreeEntry>()
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostSyncPreflightCommand.Execute(CreateContext(), ["--format", "json"], writer);

        // proceed_allowed=false → exit 1.
        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("ff-blocked", root.GetProperty("classification").GetString());
        Assert.False(root.GetProperty("proceed_allowed").GetBoolean());
        Assert.True(root.GetProperty("pre_mutation_abort").GetBoolean());
        Assert.False(root.GetProperty("durable_runs_summary_allowed").GetBoolean());
        Assert.Equal(1, root.GetProperty("ahead_origin_commits").GetInt32());
        Assert.Equal(2, root.GetProperty("behind_origin_commits").GetInt32());

        var localOnly = root.GetProperty("local_only_commits").EnumerateArray().ToArray();
        Assert.Single(localOnly);
        Assert.Equal("abc1234", localOnly[0].GetProperty("sha").GetString());
        var originAhead = root.GetProperty("origin_ahead_commits").EnumerateArray().ToArray();
        Assert.Equal(2, originAhead.Length);
    }

    [Fact]
    public void Execute_CleanProbe_EmitsCleanJson_AndZeroExit()
    {
        AutomationHostSyncPreflightCommand.GitProbeFactory = _ => new HostSyncGitProbe
        {
            Branch = "main",
            BehindOriginCommits = 0,
            AheadOriginCommits = 0,
            WorkingTreeEntries = Array.Empty<HostSyncWorkingTreeEntry>()
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostSyncPreflightCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("clean", root.GetProperty("classification").GetString());
        Assert.True(root.GetProperty("proceed_allowed").GetBoolean());
        Assert.False(root.GetProperty("pre_mutation_abort").GetBoolean());
        Assert.True(root.GetProperty("durable_runs_summary_allowed").GetBoolean());
    }

    [Fact]
    public void Execute_G791_CleanNestedPointerDrift_EmitsProceedWithoutSafeStash()
    {
        AutomationHostSyncPreflightCommand.GitProbeFactory = _ => new HostSyncGitProbe
        {
            Branch = "main",
            BehindOriginCommits = 0,
            WorkingTreeEntries = new[]
            {
                new HostSyncWorkingTreeEntry { Path = "submodules/SekibanAsAService", Status = " m" }
            },
            NestedPointerDriftSubmodules = new[]
            {
                new NestedPointerDriftSubmodule
                {
                    OwningSubmodulePath = "submodules/SekibanAsAService",
                    ParentRecordedCommit = "a111111111111111111111111111111111111111",
                    UntouchedNestedPaths = new[] { "submodules/SekibanAsAService/Sekiban" }
                }
            }
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostSyncPreflightCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("nested-pointer-drift", root.GetProperty("classification").GetString());
        Assert.True(root.GetProperty("proceed_allowed").GetBoolean());
        Assert.False(root.GetProperty("safe_stash_required").GetBoolean());
        Assert.Equal(0, root.GetProperty("safe_stash_paths").GetArrayLength());
        var drift = Assert.Single(root.GetProperty("nested_pointer_drift_submodules").EnumerateArray());
        Assert.Equal("submodules/SekibanAsAService", drift.GetProperty("owning_submodule_path").GetString());
        Assert.Contains("leave the owning and nested submodule paths untouched", root.GetProperty("summary").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G791_RealNestedContent_RefusesInsteadOfOfferingTheG306Lane()
    {
        AutomationHostSyncPreflightCommand.GitProbeFactory = _ => new HostSyncGitProbe
        {
            Branch = "main",
            BehindOriginCommits = 0,
            WorkingTreeEntries = new[]
            {
                new HostSyncWorkingTreeEntry { Path = "submodules/SekibanAsAService", Status = " M" }
            },
            SubmoduleInternalDirtyPaths = new[] { "submodules/SekibanAsAService" }
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostSyncPreflightCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("submodule-internal-dirty", root.GetProperty("classification").GetString());
        Assert.False(root.GetProperty("proceed_allowed").GetBoolean());
        Assert.False(root.GetProperty("safe_stash_required").GetBoolean());
        Assert.Empty(root.GetProperty("safe_stash_paths").EnumerateArray());
        Assert.Equal("submodules/SekibanAsAService", Assert.Single(root.GetProperty("submodule_internal_dirty_paths").EnumerateArray()).GetString());
        Assert.Contains("do not enter the G306 safe-stash lane", root.GetProperty("summary").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    private static CliContext CreateContext()
    {
        return new CliContext
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };
    }
}
