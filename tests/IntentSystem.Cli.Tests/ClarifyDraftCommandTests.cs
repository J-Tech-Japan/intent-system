using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class ClarifyDraftCommandTests
{
    [Fact]
    public void Execute_GivenQuestionAndDomain_EmitsMarkdownDraftPacketWithExpectedSections()
    {
        // Required scenario 1 (G181): normal draft generation. The packet must
        // include all structured sections so the AI tasking thread + owner can
        // review a consistent shape.
        using var workspace = new ClarifyDraftWorkspace();
        workspace.WriteQueueState(NormalQueueStateJson);

        using var writer = new StringWriter();
        var exitCode = ClarifyDraftCommand.Execute(
            workspace.Context,
            ["--question", "Should the next issue be G182 or G183?"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Clarification draft: intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("## Question", output, StringComparison.Ordinal);
        Assert.Contains("Should the next issue be G182 or G183?", output, StringComparison.Ordinal);
        Assert.Contains("## Background", output, StringComparison.Ordinal);
        Assert.Contains("## Options", output, StringComparison.Ordinal);
        Assert.Contains("### A.", output, StringComparison.Ordinal);
        Assert.Contains("### B.", output, StringComparison.Ordinal);
        Assert.Contains("Pros:", output, StringComparison.Ordinal);
        Assert.Contains("Cons:", output, StringComparison.Ordinal);
        Assert.Contains("## Recommendation", output, StringComparison.Ordinal);
        Assert.Contains("## Return path", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenJsonFormat_EmitsParsableSnakeCaseFields()
    {
        // Required scenario 2: JSON output. Stable snake_case field names so the
        // AI tasking thread can ingest the packet directly.
        using var workspace = new ClarifyDraftWorkspace();

        using var writer = new StringWriter();
        var exitCode = ClarifyDraftCommand.Execute(
            workspace.Context,
            ["--question", "Should we keep markdown default?", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        Assert.Equal("Should we keep markdown default?", root.GetProperty("question").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("background").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("options").ValueKind);
        Assert.Equal(2, root.GetProperty("options").GetArrayLength());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("notes").ValueKind);
        Assert.True(root.TryGetProperty("return_path", out _));
        var firstOption = root.GetProperty("options")[0];
        Assert.Equal("A", firstOption.GetProperty("label").GetString());
        Assert.Equal(JsonValueKind.Array, firstOption.GetProperty("pros").ValueKind);
        Assert.Equal(JsonValueKind.Array, firstOption.GetProperty("cons").ValueKind);
    }

    [Fact]
    public void Execute_GivenMissingOptionalContext_RecordsDegradedNotesWithoutThrowing()
    {
        // Required scenario 3: missing optional context (no queue-state, no
        // clarification file). Must succeed with explicit notes for each missing
        // source — no unhandled exceptions.
        using var workspace = new ClarifyDraftWorkspace();

        using var writer = new StringWriter();
        var exitCode = ClarifyDraftCommand.Execute(
            workspace.Context,
            ["--question", "Is it safe to start G182 now?"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("## Notes", output, StringComparison.Ordinal);
        Assert.Contains("no queue-state file", output, StringComparison.Ordinal);
        Assert.Contains("no clarification file", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingQuestion_ReturnsErrorExitCode()
    {
        // Required scenario 4 (a): invalid arguments — --question missing entirely.
        using var workspace = new ClarifyDraftWorkspace();

        using var writer = new StringWriter();
        var exitCode = ClarifyDraftCommand.Execute(workspace.Context, [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--question", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsErrorExitCode()
    {
        // Required scenario 4 (b): invalid arguments — unknown flag.
        using var workspace = new ClarifyDraftWorkspace();

        using var writer = new StringWriter();
        var exitCode = ClarifyDraftCommand.Execute(
            workspace.Context,
            ["--question", "Q", "--bogus"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bogus", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenInvalidFormatValue_ReturnsErrorExitCode()
    {
        using var workspace = new ClarifyDraftWorkspace();

        using var writer = new StringWriter();
        var exitCode = ClarifyDraftCommand.Execute(
            workspace.Context,
            ["--question", "Q", "--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenDomainOverride_ResolvesReturnPathUnderOverrideDomain()
    {
        using var workspace = new ClarifyDraftWorkspace();

        using var writer = new StringWriter();
        var exitCode = ClarifyDraftCommand.Execute(
            workspace.Context,
            ["--domain", "alt-domain", "--question", "Q", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("alt-domain", root.GetProperty("domain").GetString());
        var returnPath = root.GetProperty("return_path").GetString();
        Assert.NotNull(returnPath);
        Assert.Contains(
            Path.Combine("intents", "alt-domain", "clarifications", "open.md"),
            returnPath!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenOpenBlockerInClarificationFile_AnnotatesBackgroundAccordingly()
    {
        using var workspace = new ClarifyDraftWorkspace();
        workspace.WriteClarificationOpen(
            """
            # intent-cli clarifications

            ## Current Open Blockers

            - Should we ship clarify draft now?
            """);

        using var writer = new StringWriter();
        var exitCode = ClarifyDraftCommand.Execute(
            workspace.Context,
            ["--question", "Q", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var background = document.RootElement.GetProperty("background")
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();
        Assert.Contains(background, item => item.Contains("open blocker: yes", StringComparison.Ordinal));
    }

    private const string NormalQueueStateJson =
        """
        {
          "schema_version": "1",
          "updated_at": "2026-04-29T00:00:00Z",
          "items": [
            {
              "execution_unit": "G180",
              "title": "context collect",
              "state": "completed",
              "dependencies": [],
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
            },
            {
              "execution_unit": "G181",
              "title": "clarify draft",
              "state": "active",
              "dependencies": ["G180"],
              "blocked_by": [],
              "clarification_return_path": "intents/intent-cli/clarifications/open.md",
              "packet_paths": {
                "implementation": ".intent-cli/issues/G181/implementation.md",
                "review_context": ".intent-cli/issues/G181/review-context.md",
                "yaml": ".intent-cli/issues/G181/packet.yaml"
              },
              "worker_role": "coder",
              "review_role": "reviewer",
              "priority": "high"
            }
          ]
        }
        """;

    private sealed class ClarifyDraftWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("clarify-draft-tests-")
            .FullName;

        public ClarifyDraftWorkspace()
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

        public void WriteClarificationOpen(string content)
        {
            var path = Path.Combine(rootPath, "intents", Context.Config.Project.Domain, "clarifications");
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
