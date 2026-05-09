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
