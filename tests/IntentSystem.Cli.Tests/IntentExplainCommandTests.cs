using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IntentExplainCommandTests
{
    [Fact]
    public void Execute_GivenPacketAndQueueItem_EmitsCombinedSummary()
    {
        using var workspace = new IntentExplainWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G241/packet.yaml",
            "execution_unit: G241\ntitle: intent status\n");
        workspace.WriteFile(
            ".intent-cli/issues/G241/github-body.md",
            """
            ## Goal
            Add read-only intent status command.

            ## Why
            Discoverable status replaces manual file inspection.
            """);
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G241",
                  "title": "intent status command",
                  "state": "completed",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal",
                  "linked_pr": "586"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentExplainCommand.Execute(workspace.Context, ["G241"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Intent explain — G241", output, StringComparison.Ordinal);
        Assert.Contains("found: yes", output, StringComparison.Ordinal);
        Assert.Contains("title: intent status command", output, StringComparison.Ordinal);
        Assert.Contains("state: completed", output, StringComparison.Ordinal);
        Assert.Contains("linked PR: 586", output, StringComparison.Ordinal);
        Assert.Contains("- packet.yaml", output, StringComparison.Ordinal);
        Assert.Contains("- github-body.md", output, StringComparison.Ordinal);
        Assert.Contains("github-body.md head", output, StringComparison.Ordinal);
        Assert.Contains("Add read-only intent status command.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenJsonFormat_EmitsStructuredResult()
    {
        using var workspace = new IntentExplainWorkspace();
        workspace.WriteFile(".intent-cli/issues/G242/packet.yaml", "x");

        using var writer = new StringWriter();
        var exitCode = IntentExplainCommand.Execute(
            workspace.Context,
            ["G242", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("G242", root.GetProperty("execution_unit").GetString());
        Assert.True(root.GetProperty("found").GetBoolean());
        var packetFiles = root.GetProperty("packet_files");
        Assert.Equal(1, packetFiles.GetArrayLength());
        Assert.Equal("packet.yaml", packetFiles[0].GetString());
    }

    [Fact]
    public void Execute_GivenUnknownExecutionUnit_ReturnsNotFound()
    {
        using var workspace = new IntentExplainWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentExplainCommand.Execute(
            workspace.Context,
            ["G999"],
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("found: no", output, StringComparison.Ordinal);
        Assert.Contains("No packet directory or queue-state record found", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingExecutionUnitArg_ReturnsUsageError()
    {
        using var workspace = new IntentExplainWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentExplainCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("execution-unit id is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MultipleExecutionUnitArgs_ReturnsUsageError()
    {
        using var workspace = new IntentExplainWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentExplainCommand.Execute(
            workspace.Context,
            ["G241", "G242"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Only one execution-unit id is allowed", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_InvalidExecutionUnitId_ReturnsUsageError()
    {
        using var workspace = new IntentExplainWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentExplainCommand.Execute(
            workspace.Context,
            ["bad/id"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Invalid execution-unit id", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var workspace = new IntentExplainWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentExplainCommand.Execute(
            workspace.Context,
            ["G241", "--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var workspace = new IntentExplainWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentExplainCommand.Execute(
            workspace.Context,
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("intent explain", writer.ToString(), StringComparison.Ordinal);
    }

    private sealed class IntentExplainWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("intent-explain-tests-")
            .FullName;

        public IntentExplainWorkspace()
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

        public void WriteFile(string relativePath, string content)
        {
            var full = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        public void WriteQueueState(string content)
        {
            File.WriteAllText(Context.GetQueueStatePath(), content);
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
