using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IntentNextSliceCommandTests
{
    [Fact]
    public void Execute_GivenClarificationOpen_RecommendsClarificationRequired()
    {
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteClarificationOpen(
            """
            ## Current Open Blockers
            - Need decision on storage strategy
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("clarification-required", root.GetProperty("recommended_outcome").GetString());
        Assert.True(root.GetProperty("clarification_open").GetBoolean());
        Assert.True(root.GetProperty("dry_run").GetBoolean());
        Assert.Equal("J-Tech-Japan/intent-system", root.GetProperty("target_repo").GetString());
    }

    [Fact]
    public void Execute_GivenActiveQueueItem_RecommendsSkipDueToWip()
    {
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G241",
                  "title": "intent status",
                  "state": "active",
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
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("skip-next-slice-due-to-wip", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal(1, root.GetProperty("wip").GetArrayLength());
        Assert.Equal("G241", root.GetProperty("wip")[0].GetString());
    }

    [Fact]
    public void Execute_GivenNoCandidate_RecommendsNoActionableItem()
    {
        using var workspace = new IntentNextSliceWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("no-actionable-item", root.GetProperty("recommended_outcome").GetString());
        Assert.False(root.TryGetProperty("candidate", out var _));
    }

    [Fact]
    public void Execute_GivenCandidateWithMissingSections_RecommendsClarificationAndListsMissing()
    {
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G244/github-body.md",
            """
            ## Goal
            Add something.

            ## In Scope
            Foo.
            """);
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G244",
                  "title": "next slice",
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
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("clarification-required", root.GetProperty("recommended_outcome").GetString());
        var candidate = root.GetProperty("candidate");
        Assert.Equal("G244", candidate.GetProperty("execution_unit").GetString());
        Assert.True(candidate.GetProperty("github_body_present").GetBoolean());
        var missing = candidate.GetProperty("missing_contract_sections");
        Assert.True(missing.GetArrayLength() > 0);
        var missingNames = missing.EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("Verification", missingNames);
        Assert.Contains("Acceptance Criteria", missingNames);
    }

    [Fact]
    public void Execute_GivenCompleteCandidate_RecommendsIssueCutReady()
    {
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G244/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G244",
                  "title": "next slice",
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
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        var candidate = root.GetProperty("candidate");
        Assert.Equal("G244", candidate.GetProperty("execution_unit").GetString());
        Assert.Equal(0, candidate.GetProperty("missing_contract_sections").GetArrayLength());
    }

    [Fact]
    public void Execute_GivenMarkdownFormat_EmitsHumanReadableOutput()
    {
        using var workspace = new IntentNextSliceWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Intent next-slice dry-run — intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("recommended outcome: no-actionable-item", output, StringComparison.Ordinal);
        Assert.Contains("## WIP (in-flight)", output, StringComparison.Ordinal);
        Assert.Contains("## Open clarifications", output, StringComparison.Ordinal);
        Assert.Contains("## Candidate", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingDryRun_ReturnsUsageError()
    {
        using var workspace = new IntentNextSliceWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--dry-run is required.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var workspace = new IntentNextSliceWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'json' or 'markdown'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var workspace = new IntentNextSliceWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("intent next-slice", output, StringComparison.Ordinal);
        Assert.Contains("--dry-run", output, StringComparison.Ordinal);
    }

    private static string BuildCompleteContractBody()
    {
        return """
            ## Goal
            x

            ## Why This Slice Exists Now
            x

            ## Current Observed State
            x

            ## Accepted Baseline You May Assume
            x

            ## Target Repo / Path / Part
            x

            ## In Scope
            x

            ## Out Of Scope
            x

            ## Acceptance Criteria
            x

            ## Verification
            x

            ## Related Links
            - x
            """;
    }

    private sealed class IntentNextSliceWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("intent-next-slice-tests-")
            .FullName;

        public IntentNextSliceWorkspace()
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

        public void WriteFile(string relativePath, string content)
        {
            var full = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
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
