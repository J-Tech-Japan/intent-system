using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class ContextCollectCommandTests
{
    [Fact]
    public void Execute_GivenNormalWorkspace_EmitsMarkdownWithExpectedSections()
    {
        // Required scenario 1 (G180): normal context collection. Real queue-state with
        // a Review unit, a clarification with no open blockers, automation bindings,
        // and packet files for the focus unit. Output must include all packet sections
        // and surface the focus unit so the AI tasking thread can read context without
        // opening five files.
        using var workspace = new ContextCollectWorkspace();
        workspace.WriteQueueState(NormalQueueStateJson);
        workspace.WriteClarificationOpen(NoBlockerClarification);
        workspace.WriteAutomationBindings(NormalAutomationBindings);
        workspace.WritePacketFile("G180", "implementation.md", "# Implementation packet for G180\n");
        workspace.WritePacketFile("G180", "review-context.md", "# Review context for G180\n");
        workspace.WritePacketFile("G180", "packet.yaml", "execution_unit: G180\n");
        workspace.WriteRunLog(
            """
            {"ts":"2026-04-29T00:00:00Z","execution_unit":"G179","event":"completed","by":"reviewer"}
            {"ts":"2026-04-29T00:01:00Z","execution_unit":"G180","event":"queued","by":"system"}
            """);

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(workspace.Context, [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Context packet: intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("## Queue state", output, StringComparison.Ordinal);
        Assert.Contains("## Focus", output, StringComparison.Ordinal);
        Assert.Contains("## Clarification", output, StringComparison.Ordinal);
        Assert.Contains("## Automation bindings", output, StringComparison.Ordinal);
        Assert.Contains("## Recent events", output, StringComparison.Ordinal);
        Assert.Contains("Unit: G180", output, StringComparison.Ordinal);
        Assert.Contains("Open blocker: no", output, StringComparison.Ordinal);
        Assert.Contains("G179", output, StringComparison.Ordinal); // recent event mention
    }

    [Fact]
    public void Execute_GivenMissingOptionalArtifacts_RecordsDegradedNotesWithoutThrowing()
    {
        // Required scenario 2: missing optional artifacts. No queue-state, no
        // clarification, no automation bindings, no runs.jsonl. Must succeed with
        // explicit notes for each missing source.
        using var workspace = new ContextCollectWorkspace();

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(workspace.Context, [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("## Notes", output, StringComparison.Ordinal);
        Assert.Contains("no queue-state file", output, StringComparison.Ordinal);
        Assert.Contains("no clarification file", output, StringComparison.Ordinal);
        Assert.Contains("no automation bindings file", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMalformedQueueState_RecordsParseNoteAndKeepsCommandReadOnly()
    {
        // Required scenario 3: malformed queue or run state. Must not throw; must
        // record a deterministic degraded note and continue with the rest of the
        // packet so the AI thread can still see clarification / runs / bindings.
        using var workspace = new ContextCollectWorkspace();
        workspace.WriteQueueState("{ this is intentionally not valid JSON");
        workspace.WriteRunLog("not a real json line\n");

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("queue_state_present").GetBoolean());
        Assert.False(root.GetProperty("queue_state_readable").GetBoolean());
        var notes = root.GetProperty("notes").EnumerateArray().Select(n => n.GetString()).ToArray();
        Assert.Contains(notes, note => note is not null && note.Contains("queue-state", StringComparison.Ordinal));
        Assert.Contains(notes, note => note is not null && note.Contains("runs.jsonl", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenDomainOverride_UsesOverrideForResolvedPaths()
    {
        // Required scenario 4: domain override. The packet's domain field and
        // resolved clarification / automation paths must reflect the --domain value,
        // not the workspace default.
        using var workspace = new ContextCollectWorkspace();

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(
            workspace.Context,
            ["--domain", "alt-domain", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("alt-domain", root.GetProperty("domain").GetString());
        var clarificationPath = root.GetProperty("clarification_open_path").GetString();
        Assert.NotNull(clarificationPath);
        Assert.Contains(
            Path.Combine("intents", "alt-domain", "clarifications", "open.md"),
            clarificationPath!,
            StringComparison.Ordinal);
        var bindingsPath = root.GetProperty("automation_bindings_path").GetString();
        Assert.NotNull(bindingsPath);
        Assert.Contains(
            Path.Combine("intents", "alt-domain", "automation", "bindings.md"),
            bindingsPath!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenJsonFormat_EmitsParsableSnakeCaseFields()
    {
        using var workspace = new ContextCollectWorkspace();
        workspace.WriteQueueState(NormalQueueStateJson);
        workspace.WriteClarificationOpen(NoBlockerClarification);
        workspace.WriteAutomationBindings(NormalAutomationBindings);

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        Assert.True(root.GetProperty("queue_state_readable").GetBoolean());
        Assert.True(root.GetProperty("automation_bindings_present").GetBoolean());
        Assert.False(root.GetProperty("clarification_open").GetBoolean());
        Assert.Equal("G180", root.GetProperty("focus_unit").GetString());
        Assert.True(root.TryGetProperty("focus_packet", out var focusPacket));
        Assert.Equal(JsonValueKind.Object, focusPacket.ValueKind);
    }

    [Fact]
    public void Execute_GivenOpenBlockerInClarificationFile_ReportsClarificationOpenTrue()
    {
        // Aligns with G179 semantics: structured "## Current Open Blockers" with a
        // real entry must surface as clarification_open=true and provide the
        // excerpt to the AI thread.
        using var workspace = new ContextCollectWorkspace();
        workspace.WriteClarificationOpen(
            """
            # intent-cli clarifications

            ## Current Open Blockers

            - Should `context collect` include parent automation bindings by default?
            """);

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("clarification_open").GetBoolean());
        var excerpt = root.GetProperty("clarification_excerpt").GetString();
        Assert.NotNull(excerpt);
        Assert.Contains("Current Open Blockers", excerpt!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsErrorExitCode()
    {
        using var workspace = new ContextCollectWorkspace();
        using var writer = new StringWriter();

        var exitCode = ContextCollectCommand.Execute(
            workspace.Context,
            ["--bogus"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bogus", writer.ToString(), StringComparison.Ordinal);
    }

    private const string NormalQueueStateJson =
        """
        {
          "schema_version": "1",
          "updated_at": "2026-04-29T00:00:00Z",
          "items": [
            {
              "execution_unit": "G179",
              "title": "status brief",
              "state": "completed",
              "dependencies": [],
              "blocked_by": [],
              "clarification_return_path": "intents/intent-cli/clarifications/open.md",
              "packet_paths": {
                "implementation": ".intent-cli/issues/G179/implementation.md",
                "review_context": ".intent-cli/issues/G179/review-context.md",
                "yaml": ".intent-cli/issues/G179/packet.yaml"
              },
              "worker_role": "coder",
              "review_role": "reviewer",
              "priority": "high"
            },
            {
              "execution_unit": "G180",
              "title": "context collect",
              "state": "review",
              "dependencies": ["G179"],
              "blocked_by": [],
              "clarification_return_path": "intents/intent-cli/clarifications/open.md",
              "packet_paths": {
                "implementation": ".intent-cli/issues/G180/implementation.md",
                "review_context": ".intent-cli/issues/G180/review-context.md",
                "yaml": ".intent-cli/issues/G180/packet.yaml"
              },
              "worker_role": "coder",
              "review_role": "reviewer",
              "priority": "high"
            }
          ]
        }
        """;

    private const string NoBlockerClarification =
        """
        # intent-cli clarifications

        durable prose

        ## Current Open Blockers

        - 現時点で child issue cut を要する root blocker はない。
        """;

    private const string NormalAutomationBindings =
        """
        # intent-cli automation bindings

        - timer-implement-loop: every 10m
        - timer-review-loop: every 5m
        - status-brief-recommendation-mapping: review-closeout, clarification-required, ...
        """;

    private sealed class ContextCollectWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("context-collect-tests-")
            .FullName;

        public ContextCollectWorkspace()
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
                        ArtifactRoot = ".intent-cli"
                    }
                }
            };
        }

        public CliContext Context { get; }

        public void WriteQueueState(string content)
        {
            File.WriteAllText(Context.GetQueueStatePath(), content);
        }

        public void WriteRunLog(string content)
        {
            File.WriteAllText(Context.GetRunLogPath(), content);
        }

        public void WriteClarificationOpen(string content)
        {
            var path = Path.Combine(rootPath, "intents", Context.Config.Project.Domain, "clarifications");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "open.md"), content);
        }

        public void WriteAutomationBindings(string content)
        {
            var path = Path.Combine(rootPath, "intents", Context.Config.Project.Domain, "automation");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "bindings.md"), content);
        }

        public void WritePacketFile(string executionUnit, string fileName, string content)
        {
            var path = Path.Combine(rootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, fileName), content);
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
