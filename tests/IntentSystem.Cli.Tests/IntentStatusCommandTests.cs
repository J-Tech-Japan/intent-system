using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IntentStatusCommandTests
{
    [Fact]
    public void Execute_GivenNoQueueState_EmitsEmptySectionsAndNote()
    {
        using var workspace = new IntentStatusWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentStatusCommand.Execute(workspace.Context, [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Intent status — intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("queue-state present: no", output, StringComparison.Ordinal);
        Assert.Contains("## Latest completed", output, StringComparison.Ordinal);
        Assert.Contains("## WIP (in-flight)", output, StringComparison.Ordinal);
        Assert.Contains("## Queued / preloaded packets", output, StringComparison.Ordinal);
        Assert.Contains("## Open clarifications", output, StringComparison.Ordinal);
        Assert.Contains("no queue-state file at", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMixedQueueItems_BucketsCompletedWipAndQueued()
    {
        using var workspace = new IntentStatusWorkspace();
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G237",
                  "title": "old completed",
                  "state": "completed",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "G238",
                  "title": "newer completed",
                  "state": "completed",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "G239",
                  "title": "in flight",
                  "state": "active",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "G240",
                  "title": "preloaded",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentStatusCommand.Execute(workspace.Context, [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("queue-state present: yes", output, StringComparison.Ordinal);
        Assert.Contains("- G238 — newer completed", output, StringComparison.Ordinal);
        Assert.Contains("- G237 — old completed", output, StringComparison.Ordinal);
        Assert.Contains("- G239 (active) — in flight", output, StringComparison.Ordinal);
        Assert.Contains("- G240 (queued) — preloaded", output, StringComparison.Ordinal);
        // Latest completed should be ordered newest first (reverse order in queue array).
        var newerIndex = output.IndexOf("G238 — newer completed", StringComparison.Ordinal);
        var olderIndex = output.IndexOf("G237 — old completed", StringComparison.Ordinal);
        Assert.True(newerIndex >= 0 && olderIndex >= 0 && newerIndex < olderIndex);
    }

    [Fact]
    public void Execute_GivenJsonFormat_EmitsStructuredSnakeCase()
    {
        using var workspace = new IntentStatusWorkspace();
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G241",
                  "title": "queued slice",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentStatusCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        Assert.True(root.GetProperty("queue_state_present").GetBoolean());
        Assert.Equal(0, root.GetProperty("latest_completed").GetArrayLength());
        Assert.Equal(0, root.GetProperty("wip").GetArrayLength());
        Assert.Equal(1, root.GetProperty("queued").GetArrayLength());
        var queuedFirst = root.GetProperty("queued")[0];
        Assert.Equal("G241", queuedFirst.GetProperty("execution_unit").GetString());
        Assert.Equal("queued slice", queuedFirst.GetProperty("title").GetString());
        Assert.Equal("queued", queuedFirst.GetProperty("state").GetString());
        Assert.False(root.GetProperty("clarification_open").GetBoolean());
    }

    [Fact]
    public void Execute_GivenOpenClarification_FlagsOpenBlocker()
    {
        using var workspace = new IntentStatusWorkspace();
        workspace.WriteClarificationOpen(
            """
            # Open clarifications

            ## Current Open Blockers
            - Need answer on storage strategy
            """);

        using var writer = new StringWriter();
        var exitCode = IntentStatusCommand.Execute(workspace.Context, [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("file present: yes", output, StringComparison.Ordinal);
        Assert.Contains("has open blocker: yes", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenDomainOverride_ResolvesClarificationPathPerDomain()
    {
        using var workspace = new IntentStatusWorkspace();
        workspace.WriteClarificationOpen(
            """
            ## Current Open Blockers
            - none
            """,
            "other-domain");

        using var writer = new StringWriter();
        var exitCode = IntentStatusCommand.Execute(
            workspace.Context,
            ["--domain", "other-domain"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Intent status — other-domain", output, StringComparison.Ordinal);
        Assert.Contains("intents/other-domain/clarifications/open.md", output, StringComparison.Ordinal);
        Assert.Contains("file present: yes", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenInvalidQueueStateJson_RecordsParseNote()
    {
        using var workspace = new IntentStatusWorkspace();
        workspace.WriteQueueState("{ this is not json");

        using var writer = new StringWriter();
        var exitCode = IntentStatusCommand.Execute(workspace.Context, [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("queue-state JSON could not be parsed", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnsupportedFormat_ReturnsUsageError()
    {
        using var workspace = new IntentStatusWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentStatusCommand.Execute(
            workspace.Context,
            ["--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsUsageError()
    {
        using var workspace = new IntentStatusWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentStatusCommand.Execute(
            workspace.Context,
            ["--unknown"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument '--unknown'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingDomainValue_ReturnsUsageError()
    {
        using var workspace = new IntentStatusWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentStatusCommand.Execute(
            workspace.Context,
            ["--domain"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--domain requires a value", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenHelpFlag_PrintsUsageAndExitsZero()
    {
        using var workspace = new IntentStatusWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentStatusCommand.Execute(
            workspace.Context,
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("intent status", output, StringComparison.Ordinal);
        Assert.Contains("--domain", output, StringComparison.Ordinal);
        Assert.Contains("--format markdown|json", output, StringComparison.Ordinal);
    }

    private sealed class IntentStatusWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("intent-status-tests-")
            .FullName;

        public IntentStatusWorkspace()
        {
            Directory.CreateDirectory(Path.Combine(rootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = rootPath,
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

        public CliContext Context { get; }

        public void WriteQueueState(string content)
        {
            File.WriteAllText(Context.GetQueueStatePath(), content);
        }

        public void WriteClarificationOpen(string content, string domain = "intent-cli")
        {
            var path = Path.Combine(rootPath, "intents", domain, "clarifications");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "open.md"), content);
        }

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
