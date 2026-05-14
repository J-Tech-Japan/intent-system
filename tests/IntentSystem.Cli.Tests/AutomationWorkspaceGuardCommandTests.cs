using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class AutomationWorkspaceGuardCommandTests : IDisposable
{
    public AutomationWorkspaceGuardCommandTests()
    {
        AutomationWorkspaceGuardCommand.GitRunnerFactory = null;
        AutomationWorkspaceGuardCommand.UtcNowFactory = null;
    }

    public void Dispose()
    {
        AutomationWorkspaceGuardCommand.GitRunnerFactory = null;
        AutomationWorkspaceGuardCommand.UtcNowFactory = null;
    }

    [Fact]
    public void Plan_UnrelatedDirty_ProducesSafeStashPlan_WithoutMutation()
    {
        using var workspace = new GuardWorkspace();
        AutomationWorkspaceGuardCommand.GitRunnerFactory = _ => new FakeGitRunner(porcelain:
            " m submodules/other\n M scratch.txt\n");
        AutomationWorkspaceGuardCommand.UtcNowFactory = () => new DateTimeOffset(2026, 5, 9, 0, 0, 0, TimeSpan.Zero);

        using var writer = new StringWriter();
        var exitCode = AutomationWorkspaceGuardCommand.Execute(
            workspace.Context,
            ["--mode", "plan", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.True(root.GetProperty("proceed_allowed").GetBoolean());
        Assert.Equal(2, root.GetProperty("safe_stash_paths").GetArrayLength());
        Assert.Equal(0, root.GetProperty("dirty_durable_state_paths").GetArrayLength());
        Assert.Contains("intent-cli/G306 workspace-guard 2026-05-09T00:00:00Z",
            root.GetProperty("stash_message").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_DirtyDurableState_RefusesAndExitsOne()
    {
        using var workspace = new GuardWorkspace();
        AutomationWorkspaceGuardCommand.GitRunnerFactory = _ => new FakeGitRunner(porcelain:
            " M .intent-cli/queue-state.json\n");

        using var writer = new StringWriter();
        var exitCode = AutomationWorkspaceGuardCommand.Execute(
            workspace.Context,
            ["--mode", "plan", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("proceed_allowed").GetBoolean());
        Assert.Equal(1, doc.RootElement.GetProperty("dirty_durable_state_paths").GetArrayLength());
        Assert.Contains("safe-stash refused", doc.RootElement.GetProperty("summary").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Begin_Write_RunsGitStashPush_AndWritesStateFile()
    {
        using var workspace = new GuardWorkspace();
        var fake = new FakeGitRunner(porcelain: " m submodules/other\n");
        fake.QueueResponse("stash list --format=%gd %s",
            stdout: "stash@{0} On main: intent-cli/G306 workspace-guard 2026-05-09T00:00:00Z\n");
        AutomationWorkspaceGuardCommand.GitRunnerFactory = _ => fake;
        AutomationWorkspaceGuardCommand.UtcNowFactory = () => new DateTimeOffset(2026, 5, 9, 0, 0, 0, TimeSpan.Zero);

        using var writer = new StringWriter();
        var exitCode = AutomationWorkspaceGuardCommand.Execute(
            workspace.Context,
            ["--mode", "begin", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("stash@{0}", doc.RootElement.GetProperty("stash_ref").GetString());

        var statePath = Path.Combine(workspace.RepoRoot, ".intent-cli", "workspace-guard.json");
        Assert.True(File.Exists(statePath));
        using var stateDoc = JsonDocument.Parse(File.ReadAllText(statePath));
        Assert.Equal("stash@{0}", stateDoc.RootElement.GetProperty("stash_ref").GetString());
        Assert.Single(stateDoc.RootElement.GetProperty("stashed_paths").EnumerateArray());

        // Verify a stash push command was executed with the dirty path.
        Assert.Contains(fake.Invocations,
            args => args.Contains("stash") && args.Contains("push") && args.Contains("submodules/other"));
    }

    [Fact]
    public void Begin_DryRun_DoesNotMutateState()
    {
        using var workspace = new GuardWorkspace();
        AutomationWorkspaceGuardCommand.GitRunnerFactory = _ => new FakeGitRunner(porcelain: " m submodules/other\n");

        using var writer = new StringWriter();
        var exitCode = AutomationWorkspaceGuardCommand.Execute(
            workspace.Context,
            ["--mode", "begin", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.True(doc.RootElement.GetProperty("proceed_allowed").GetBoolean());
        Assert.Contains("dry-run", doc.RootElement.GetProperty("summary").GetString()!, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(workspace.RepoRoot, ".intent-cli", "workspace-guard.json")));
    }

    [Fact]
    public void Begin_Write_Refuses_WhenCreatedStashRefCannotBeIdentified()
    {
        using var workspace = new GuardWorkspace();
        var fake = new FakeGitRunner(porcelain: " m submodules/other\n");
        fake.QueueResponse("stash list --format=%gd %s",
            stdout: "stash@{0} On main: unrelated operator stash\n");
        AutomationWorkspaceGuardCommand.GitRunnerFactory = _ => fake;
        AutomationWorkspaceGuardCommand.UtcNowFactory = () => new DateTimeOffset(2026, 5, 9, 0, 0, 0, TimeSpan.Zero);

        using var writer = new StringWriter();
        var exitCode = AutomationWorkspaceGuardCommand.Execute(
            workspace.Context,
            ["--mode", "begin", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("proceed_allowed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("stash_ref").ValueKind);
        Assert.Contains("could not identify a stash entry", doc.RootElement.GetProperty("summary").GetString()!, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(workspace.RepoRoot, ".intent-cli", "workspace-guard.json")));
    }

    [Fact]
    public void Begin_Refuses_WhenDurableStateIsDirty_EvenWithWriteFlag()
    {
        using var workspace = new GuardWorkspace();
        AutomationWorkspaceGuardCommand.GitRunnerFactory = _ => new FakeGitRunner(porcelain:
            " M .intent-cli/queue-state.json\n M submodules/other\n");

        using var writer = new StringWriter();
        var exitCode = AutomationWorkspaceGuardCommand.Execute(
            workspace.Context,
            ["--mode", "begin", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("proceed_allowed").GetBoolean());
        Assert.False(File.Exists(Path.Combine(workspace.RepoRoot, ".intent-cli", "workspace-guard.json")));
    }

    [Fact]
    public void End_Write_PopsStashAndRemovesStateFile()
    {
        using var workspace = new GuardWorkspace();
        var stateFile = Path.Combine(workspace.RepoRoot, ".intent-cli", "workspace-guard.json");
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
        File.WriteAllText(stateFile, """
        {
          "stash_ref": "stash@{0}",
          "stash_message": "intent-cli/G306 workspace-guard 2026-05-09T00:00:00Z",
          "created_at": "2026-05-09T00:00:00+00:00",
          "stashed_paths": ["submodules/other"]
        }
        """);

        var fake = new FakeGitRunner(porcelain: string.Empty);
        fake.QueueResponse("stash pop stash@{0}", stdout: "Restored\n");
        AutomationWorkspaceGuardCommand.GitRunnerFactory = _ => fake;

        using var writer = new StringWriter();
        var exitCode = AutomationWorkspaceGuardCommand.Execute(
            workspace.Context,
            ["--mode", "end", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(stateFile));
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Contains("restored 1 path(s)", doc.RootElement.GetProperty("summary").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void End_StashPopConflict_ReportsStructuredRecovery_AndKeepsStateFile()
    {
        using var workspace = new GuardWorkspace();
        var stateFile = Path.Combine(workspace.RepoRoot, ".intent-cli", "workspace-guard.json");
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
        File.WriteAllText(stateFile, """
        {
          "stash_ref": "stash@{0}",
          "stash_message": "intent-cli/G306 workspace-guard 2026-05-09T00:00:00Z",
          "created_at": "2026-05-09T00:00:00+00:00",
          "stashed_paths": ["submodules/other"]
        }
        """);

        var fake = new FakeGitRunner(porcelain: string.Empty);
        fake.QueueResponse("stash pop stash@{0}",
            stdout: "CONFLICT (modify/modify): Merge conflict in submodules/other\n",
            exitCode: 1);
        AutomationWorkspaceGuardCommand.GitRunnerFactory = _ => fake;

        using var writer = new StringWriter();
        var exitCode = AutomationWorkspaceGuardCommand.Execute(
            workspace.Context,
            ["--mode", "end", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("proceed_allowed").GetBoolean());
        Assert.Contains("CONFLICT", doc.RootElement.GetProperty("summary").GetString()!, StringComparison.Ordinal);
        Assert.True(doc.RootElement.GetProperty("conflict_paths").GetArrayLength() >= 1);
        // State file is preserved so the operator can still recover the stash.
        Assert.True(File.Exists(stateFile));
    }

    [Fact]
    public void End_NoStateFile_IsNoop()
    {
        using var workspace = new GuardWorkspace();
        AutomationWorkspaceGuardCommand.GitRunnerFactory = _ => new FakeGitRunner(porcelain: string.Empty);

        using var writer = new StringWriter();
        var exitCode = AutomationWorkspaceGuardCommand.Execute(
            workspace.Context,
            ["--mode", "end", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Contains("no state file", doc.RootElement.GetProperty("summary").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RejectsUnknownMode()
    {
        using var workspace = new GuardWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationWorkspaceGuardCommand.Execute(
            workspace.Context,
            ["--mode", "bogus"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must be 'plan', 'begin', or 'end'", writer.ToString(), StringComparison.Ordinal);
    }

    // ======================================================================
    // G352 — gitlink-only submodule safe lane
    // ======================================================================

    [Fact]
    public void Plan_G352_GitlinkOnly_ExposesGitlinkPaths_NotSafeStashPaths()
    {
        // A submodule with a gitlink-only dirty state (no internal changes)
        // should appear in gitlink_paths, not safe_stash_paths.
        using var workspace = new GuardWorkspace();
        var fake = new FakeGitRunner(porcelain: " m submodules/SekibanAsAService\n");
        // ls-files returns mode 160000 → gitlink
        fake.QueueResponse("ls-files --stage -- submodules/SekibanAsAService",
            stdout: "160000 abc1234567890abcdef 0\tsubmodules/SekibanAsAService\n");
        // Internal status → empty (clean)
        fake.QueueResponse("-C submodules/SekibanAsAService status --short", stdout: "");
        AutomationWorkspaceGuardCommand.GitRunnerFactory = _ => fake;

        using var writer = new StringWriter();
        var exitCode = AutomationWorkspaceGuardCommand.Execute(
            workspace.Context,
            ["--mode", "plan", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.True(root.GetProperty("proceed_allowed").GetBoolean());
        Assert.Equal(0, root.GetProperty("safe_stash_paths").GetArrayLength());
        Assert.Equal(1, root.GetProperty("gitlink_paths").GetArrayLength());
        Assert.Equal("submodules/SekibanAsAService",
            root.GetProperty("gitlink_paths")[0].GetProperty("path").GetString());
        Assert.Contains("checkout lane", root.GetProperty("summary").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_G352_SubmoduleInternalDirty_RefusesAndExitsOne()
    {
        // A submodule that has internal uncommitted changes must be refused.
        using var workspace = new GuardWorkspace();
        var fake = new FakeGitRunner(porcelain: " m submodules/SekibanAsAService\n");
        fake.QueueResponse("ls-files --stage -- submodules/SekibanAsAService",
            stdout: "160000 abc1234567890abcdef 0\tsubmodules/SekibanAsAService\n");
        // Internal status → non-empty (dirty inside submodule)
        fake.QueueResponse("-C submodules/SekibanAsAService status --short",
            stdout: " M src/SomeFile.cs\n");
        AutomationWorkspaceGuardCommand.GitRunnerFactory = _ => fake;

        using var writer = new StringWriter();
        var exitCode = AutomationWorkspaceGuardCommand.Execute(
            workspace.Context,
            ["--mode", "plan", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("proceed_allowed").GetBoolean());
        Assert.Contains("internal uncommitted changes", doc.RootElement.GetProperty("summary").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Begin_G352_GitlinkOnly_Write_RecordsOriginalHeadAndCheckoutsParentCommit()
    {
        // begin --write for a gitlink-only dirty submodule should:
        // 1. Read the original submodule HEAD.
        // 2. Checkout the parent-recorded commit in the submodule.
        // 3. Write a state file with gitlink_restore_paths (no stash).
        using var workspace = new GuardWorkspace();
        AutomationWorkspaceGuardCommand.UtcNowFactory = () => new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero);

        var fake = new FakeGitRunner(porcelain: " m submodules/SekibanAsAService\n");
        // Classification: ls-files → 160000 (gitlink), internal status → clean
        fake.QueueResponse("ls-files --stage -- submodules/SekibanAsAService",
            stdout: "160000 parentcommit1234 0\tsubmodules/SekibanAsAService\n");
        fake.QueueResponse("-C submodules/SekibanAsAService status --short", stdout: "");
        // Begin write: get current submodule HEAD
        fake.QueueResponse("-C submodules/SekibanAsAService rev-parse HEAD",
            stdout: "originalhead5678\n");
        // Checkout to parent commit
        fake.QueueResponse("-C submodules/SekibanAsAService checkout parentcommit1234", stdout: "");
        AutomationWorkspaceGuardCommand.GitRunnerFactory = _ => fake;

        using var writer = new StringWriter();
        var exitCode = AutomationWorkspaceGuardCommand.Execute(
            workspace.Context,
            ["--mode", "begin", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.True(root.GetProperty("proceed_allowed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("stash_ref").ValueKind);
        Assert.Equal(1, root.GetProperty("gitlink_paths").GetArrayLength());
        Assert.Contains("checkout lane", root.GetProperty("summary").GetString()!, StringComparison.Ordinal);

        // State file should have gitlink_restore_paths with original HEAD.
        var statePath = Path.Combine(workspace.RepoRoot, ".intent-cli", "workspace-guard.json");
        Assert.True(File.Exists(statePath));
        using var stateDoc = JsonDocument.Parse(File.ReadAllText(statePath));
        var restorePaths = stateDoc.RootElement.GetProperty("gitlink_restore_paths");
        Assert.Equal(1, restorePaths.GetArrayLength());
        Assert.Equal("submodules/SekibanAsAService", restorePaths[0].GetProperty("path").GetString());
        Assert.Equal("originalhead5678", restorePaths[0].GetProperty("original_head").GetString());
        // No stash ref in state file.
        Assert.True(
            stateDoc.RootElement.GetProperty("stash_ref").ValueKind == JsonValueKind.Null ||
            !stateDoc.RootElement.TryGetProperty("stash_ref", out _) ||
            string.IsNullOrEmpty(stateDoc.RootElement.GetProperty("stash_ref").GetString()));

        // Verify checkout command was invoked.
        Assert.Contains(fake.Invocations, args =>
            args.Contains("-C") &&
            args.Contains("submodules/SekibanAsAService") &&
            args.Contains("checkout") &&
            args.Contains("parentcommit1234"));
    }

    [Fact]
    public void Begin_G352_SubmoduleInternalDirty_RefusesProceedAllowed()
    {
        // A submodule with internal uncommitted changes must refuse begin.
        using var workspace = new GuardWorkspace();
        var fake = new FakeGitRunner(porcelain: " m submodules/SekibanAsAService\n");
        fake.QueueResponse("ls-files --stage -- submodules/SekibanAsAService",
            stdout: "160000 abc1234567890 0\tsubmodules/SekibanAsAService\n");
        fake.QueueResponse("-C submodules/SekibanAsAService status --short",
            stdout: " M src/SomeFile.cs\n");
        AutomationWorkspaceGuardCommand.GitRunnerFactory = _ => fake;

        using var writer = new StringWriter();
        var exitCode = AutomationWorkspaceGuardCommand.Execute(
            workspace.Context,
            ["--mode", "begin", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("proceed_allowed").GetBoolean());
        Assert.Contains("internal uncommitted changes", doc.RootElement.GetProperty("summary").GetString()!, StringComparison.Ordinal);
        Assert.Contains("manually", doc.RootElement.GetProperty("summary").GetString()!, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(workspace.RepoRoot, ".intent-cli", "workspace-guard.json")));
    }

    [Fact]
    public void End_G352_GitlinkOnly_RestoresOriginalSubmoduleHead()
    {
        // end --write for a gitlink-only state should restore the submodule HEAD.
        using var workspace = new GuardWorkspace();
        var stateFile = Path.Combine(workspace.RepoRoot, ".intent-cli", "workspace-guard.json");
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
        File.WriteAllText(stateFile, """
        {
          "stash_ref": null,
          "stash_message": null,
          "created_at": "2026-05-13T00:00:00+00:00",
          "stashed_paths": [],
          "gitlink_restore_paths": [
            { "path": "submodules/SekibanAsAService", "original_head": "originalhead5678" }
          ]
        }
        """);

        var fake = new FakeGitRunner(porcelain: string.Empty);
        // Restore: checkout the original HEAD inside the submodule
        fake.QueueResponse("-C submodules/SekibanAsAService checkout originalhead5678", stdout: "HEAD is now at originalhead5678\n");
        AutomationWorkspaceGuardCommand.GitRunnerFactory = _ => fake;

        using var writer = new StringWriter();
        var exitCode = AutomationWorkspaceGuardCommand.Execute(
            workspace.Context,
            ["--mode", "end", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(stateFile));
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.True(doc.RootElement.GetProperty("proceed_allowed").GetBoolean());
        Assert.Contains("restored 1 gitlink", doc.RootElement.GetProperty("summary").GetString()!, StringComparison.Ordinal);
        Assert.Equal(1, doc.RootElement.GetProperty("gitlink_paths").GetArrayLength());

        // Verify the checkout restoration call was made.
        Assert.Contains(fake.Invocations, args =>
            args.Contains("-C") &&
            args.Contains("submodules/SekibanAsAService") &&
            args.Contains("checkout") &&
            args.Contains("originalhead5678"));
    }

    [Fact]
    public void End_G352_GitlinkRestoreFailure_PreservesStateFile_AndReportsFail()
    {
        using var workspace = new GuardWorkspace();
        var stateFile = Path.Combine(workspace.RepoRoot, ".intent-cli", "workspace-guard.json");
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
        File.WriteAllText(stateFile, """
        {
          "stash_ref": null,
          "stash_message": null,
          "created_at": "2026-05-13T00:00:00+00:00",
          "stashed_paths": [],
          "gitlink_restore_paths": [
            { "path": "submodules/SekibanAsAService", "original_head": "originalhead5678" }
          ]
        }
        """);

        var fake = new FakeGitRunner(porcelain: string.Empty);
        fake.QueueResponse("-C submodules/SekibanAsAService checkout originalhead5678",
            stdout: "error: something bad\n",
            exitCode: 1);
        AutomationWorkspaceGuardCommand.GitRunnerFactory = _ => fake;

        using var writer = new StringWriter();
        var exitCode = AutomationWorkspaceGuardCommand.Execute(
            workspace.Context,
            ["--mode", "end", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("proceed_allowed").GetBoolean());
        Assert.Contains("CONFLICT", doc.RootElement.GetProperty("summary").GetString()!, StringComparison.Ordinal);
        // State file preserved for operator recovery.
        Assert.True(File.Exists(stateFile));
    }

    [Fact]
    public void Begin_G352_MixedGitlinkAndRegular_HandlesBothLanes()
    {
        // When both a gitlink-only path and a regular dirty file are present,
        // begin --write should handle them independently: checkout for gitlink,
        // stash for regular files.
        using var workspace = new GuardWorkspace();
        AutomationWorkspaceGuardCommand.UtcNowFactory = () => new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero);

        var fake = new FakeGitRunner(porcelain: " m submodules/SekibanAsAService\n M scratch.txt\n");
        // For submodules/SekibanAsAService: ls-files → gitlink, internal → clean
        fake.QueueResponse("ls-files --stage -- submodules/SekibanAsAService",
            stdout: "160000 parentcommit1234 0\tsubmodules/SekibanAsAService\n");
        fake.QueueResponse("-C submodules/SekibanAsAService status --short", stdout: "");
        // For scratch.txt: ls-files → regular file (not 160000)
        fake.QueueResponse("ls-files --stage -- scratch.txt",
            stdout: "100644 abc123 0\tscratch.txt\n");
        // Gitlink begin: rev-parse HEAD and checkout
        fake.QueueResponse("-C submodules/SekibanAsAService rev-parse HEAD",
            stdout: "originalhead5678\n");
        fake.QueueResponse("-C submodules/SekibanAsAService checkout parentcommit1234", stdout: "");
        // Stash lane for scratch.txt
        fake.QueueResponse("stash list --format=%gd %s",
            stdout: "stash@{0} On main: intent-cli/G306 workspace-guard 2026-05-13T00:00:00Z\n");
        AutomationWorkspaceGuardCommand.GitRunnerFactory = _ => fake;

        using var writer = new StringWriter();
        var exitCode = AutomationWorkspaceGuardCommand.Execute(
            workspace.Context,
            ["--mode", "begin", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.True(doc.RootElement.GetProperty("proceed_allowed").GetBoolean());
        Assert.Equal(1, doc.RootElement.GetProperty("safe_stash_paths").GetArrayLength());
        Assert.Equal(1, doc.RootElement.GetProperty("gitlink_paths").GetArrayLength());
        Assert.Equal("stash@{0}", doc.RootElement.GetProperty("stash_ref").GetString());

        // State file should have both stashed_paths and gitlink_restore_paths.
        var statePath = Path.Combine(workspace.RepoRoot, ".intent-cli", "workspace-guard.json");
        Assert.True(File.Exists(statePath));
        using var stateDoc = JsonDocument.Parse(File.ReadAllText(statePath));
        Assert.Equal(1, stateDoc.RootElement.GetProperty("stashed_paths").GetArrayLength());
        Assert.Equal(1, stateDoc.RootElement.GetProperty("gitlink_restore_paths").GetArrayLength());
    }

    // ======================================================================
    // G352 pr-comment-fix — transactional begin for mixed gitlink+regular lanes
    // ======================================================================

    [Fact]
    public void Begin_G352_MixedLanes_StashPushFails_StateFilePreservesGitlinkRestoreEntries()
    {
        // Regression test: when stash push fails AFTER gitlinks are checked out,
        // the preliminary state file must have been written so the operator can
        // run `workspace-guard end --write` to restore the gitlinks.
        using var workspace = new GuardWorkspace();
        AutomationWorkspaceGuardCommand.UtcNowFactory = () => new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);

        var fake = new FakeGitRunner(porcelain: " m submodules/SekibanAsAService\n M scratch.txt\n");
        // Classification: gitlink + internal clean
        fake.QueueResponse("ls-files --stage -- submodules/SekibanAsAService",
            stdout: "160000 parentcommit1234 0\tsubmodules/SekibanAsAService\n");
        fake.QueueResponse("-C submodules/SekibanAsAService status --short", stdout: "");
        // scratch.txt is regular
        fake.QueueResponse("ls-files --stage -- scratch.txt",
            stdout: "100644 abc456 0\tscratch.txt\n");
        // Collect original HEAD (read-only, before any mutation)
        fake.QueueResponse("-C submodules/SekibanAsAService rev-parse HEAD",
            stdout: "originalhead5678\n");
        // Gitlink checkout succeeds
        fake.QueueResponse("-C submodules/SekibanAsAService checkout parentcommit1234", stdout: "");
        // Stash push FAILS (simulates transient git error)
        fake.QueueResponse("stash push", stdout: "", stderr: "error: cannot stash\n", exitCode: 1);
        AutomationWorkspaceGuardCommand.GitRunnerFactory = _ => fake;

        using var writer = new StringWriter();
        var exitCode = AutomationWorkspaceGuardCommand.Execute(
            workspace.Context,
            ["--mode", "begin", "--write", "--format", "json"],
            writer);

        // Begin must fail (stash push failed)
        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("proceed_allowed").GetBoolean());

        // State file MUST exist with the gitlink restore entry so `end` can restore it.
        var statePath = Path.Combine(workspace.RepoRoot, ".intent-cli", "workspace-guard.json");
        Assert.True(File.Exists(statePath),
            "Preliminary state file must be written before any checkout mutation.");
        using var stateDoc = JsonDocument.Parse(File.ReadAllText(statePath));
        var restorePaths = stateDoc.RootElement.GetProperty("gitlink_restore_paths");
        Assert.Equal(1, restorePaths.GetArrayLength());
        Assert.Equal("submodules/SekibanAsAService", restorePaths[0].GetProperty("path").GetString());
        Assert.Equal("originalhead5678", restorePaths[0].GetProperty("original_head").GetString());

        // Summary must reference the state file so operator knows recovery path.
        var summary = doc.RootElement.GetProperty("summary").GetString()!;
        Assert.Contains(statePath, summary, StringComparison.Ordinal);
        Assert.Contains("end --write", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Begin_G352_MixedLanes_StashListFails_StateFilePreservesGitlinkRestoreEntries()
    {
        // Regression test: stash push succeeded but stash list cannot find the ref.
        // Gitlinks are already checked out; preliminary state file must survive.
        using var workspace = new GuardWorkspace();
        AutomationWorkspaceGuardCommand.UtcNowFactory = () => new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);

        var fake = new FakeGitRunner(porcelain: " m submodules/SekibanAsAService\n M scratch.txt\n");
        fake.QueueResponse("ls-files --stage -- submodules/SekibanAsAService",
            stdout: "160000 parentcommit1234 0\tsubmodules/SekibanAsAService\n");
        fake.QueueResponse("-C submodules/SekibanAsAService status --short", stdout: "");
        fake.QueueResponse("ls-files --stage -- scratch.txt",
            stdout: "100644 abc456 0\tscratch.txt\n");
        fake.QueueResponse("-C submodules/SekibanAsAService rev-parse HEAD",
            stdout: "originalhead5678\n");
        fake.QueueResponse("-C submodules/SekibanAsAService checkout parentcommit1234", stdout: "");
        // Stash push succeeds, but stash list returns unrelated entry (ref not found)
        fake.QueueResponse("stash list --format=%gd %s",
            stdout: "stash@{0} On main: operator-personal-stash\n");
        AutomationWorkspaceGuardCommand.GitRunnerFactory = _ => fake;

        using var writer = new StringWriter();
        var exitCode = AutomationWorkspaceGuardCommand.Execute(
            workspace.Context,
            ["--mode", "begin", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("proceed_allowed").GetBoolean());

        // Preliminary state file must persist for gitlink recovery.
        var statePath = Path.Combine(workspace.RepoRoot, ".intent-cli", "workspace-guard.json");
        Assert.True(File.Exists(statePath));
        using var stateDoc = JsonDocument.Parse(File.ReadAllText(statePath));
        Assert.Equal(1, stateDoc.RootElement.GetProperty("gitlink_restore_paths").GetArrayLength());
        Assert.Equal("originalhead5678",
            stateDoc.RootElement.GetProperty("gitlink_restore_paths")[0].GetProperty("original_head").GetString());
    }

    private sealed class FakeGitRunner : IGitRunner
    {
        private readonly string defaultStatusOutput;
        private readonly Queue<(string match, GitRunResult result)> queued = new();
        public List<IReadOnlyList<string>> Invocations { get; } = new();

        public FakeGitRunner(string porcelain)
        {
            defaultStatusOutput = porcelain;
        }

        public void QueueResponse(string matchPrefix, string stdout = "", string stderr = "", int exitCode = 0)
        {
            queued.Enqueue((matchPrefix, new GitRunResult { ExitCode = exitCode, StandardOutput = stdout, StandardError = stderr }));
        }

        public GitRunResult Run(IReadOnlyList<string> args)
        {
            Invocations.Add(args);
            var joined = string.Join(' ', args);
            // Pop a matching queued response if any.
            if (queued.Count > 0 && joined.StartsWith(queued.Peek().match, StringComparison.Ordinal))
            {
                return queued.Dequeue().result;
            }
            // Default: status --porcelain returns the seeded status; everything else returns success.
            if (joined.StartsWith("status --porcelain", StringComparison.Ordinal))
            {
                return new GitRunResult { ExitCode = 0, StandardOutput = defaultStatusOutput, StandardError = string.Empty };
            }
            return new GitRunResult { ExitCode = 0, StandardOutput = string.Empty, StandardError = string.Empty };
        }
    }

    private sealed class GuardWorkspace : IDisposable
    {
        public GuardWorkspace()
        {
            RepoRoot = Directory.CreateTempSubdirectory("workspace-guard-tests-").FullName;
            Directory.CreateDirectory(Path.Combine(RepoRoot, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RepoRoot,
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

        public string RepoRoot { get; }
        public CliContext Context { get; }

        public void Dispose()
        {
            if (Directory.Exists(RepoRoot)) Directory.Delete(RepoRoot, recursive: true);
        }
    }
}
