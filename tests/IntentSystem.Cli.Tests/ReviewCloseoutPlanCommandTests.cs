using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class ReviewCloseoutPlanCommandTests
{
    [Fact]
    public void Execute_GivenCompletePacketAndQueueMatch_ReportsReadyTrue()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G247", "review", linkedPr: "596", linkedIssue: ("J-Tech-Japan/intent-system", 595, "https://github.com/J-Tech-Japan/intent-system/issues/595")));
        workspace.WriteFile(".intent-cli/issues/G247/github-body.md", BuildCompleteContractBody());
        workspace.WriteFile(".intent-cli/issues/G247/packet.yaml", "x");

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "596", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("ready").GetBoolean());
        Assert.Equal("G247", root.GetProperty("execution_unit").GetString());
        Assert.Equal("review", root.GetProperty("queue_item_state").GetString());
        Assert.Equal("submodules/intent-system", root.GetProperty("expected_submodule_path").GetString());
        Assert.Equal(0, root.GetProperty("missing_contract_sections").GetArrayLength());
        Assert.Equal(0, root.GetProperty("gaps").GetArrayLength());
        var linked = root.GetProperty("linked_issue");
        Assert.Equal("J-Tech-Japan/intent-system", linked.GetProperty("repo").GetString());
        Assert.Equal(595, linked.GetProperty("number").GetInt32());
        Assert.True(root.GetProperty("packet_files").GetArrayLength() >= 2);
        Assert.True(root.GetProperty("validation_steps").GetArrayLength() >= 2);
        Assert.True(root.GetProperty("closeout_steps").GetArrayLength() >= 2);
    }

    [Fact]
    public void Execute_GivenMissingContractSections_ReportsGapAndExitsNonZero()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G247", "review", linkedPr: "596", linkedIssue: ("J-Tech-Japan/intent-system", 595, null)));
        workspace.WriteFile(".intent-cli/issues/G247/github-body.md", "## Goal\nx\n");

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "596", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("ready").GetBoolean());
        Assert.True(root.GetProperty("gaps").GetArrayLength() > 0);
        var missingNames = root.GetProperty("missing_contract_sections").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("Verification", missingNames);
    }

    [Fact]
    public void Execute_GivenNoLinkedIssue_ReportsLinkedIssueGap()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G247", "review", linkedPr: "596", linkedIssue: null));
        workspace.WriteFile(".intent-cli/issues/G247/github-body.md", BuildCompleteContractBody());

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "596", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var gaps = document.RootElement.GetProperty("gaps").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(gaps, gap => gap!.Contains("linked_issue", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenMissingPacketDirectory_ReportsPacketGap()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G247", "review", linkedPr: "596", linkedIssue: ("J-Tech-Japan/intent-system", 595, null)));

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "596", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var gaps = document.RootElement.GetProperty("gaps").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(gaps, gap => gap!.Contains("packet directory not found", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenNoMatchingLinkedPr_ReportsQueueGap()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G247", "review", linkedPr: "999", linkedIssue: null));

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "596", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var gaps = document.RootElement.GetProperty("gaps").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(gaps, gap => gap!.Contains("no queue item found with linked_pr", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenSamePrNumberInDifferentRepo_SkipsOtherRepo()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState("""
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G192",
                  "title": "wrong repo",
                  "state": "completed",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_issue": {"repo": "J-Tech-Japan/intent-system", "number": 489, "url": "https://github.com/J-Tech-Japan/intent-system/issues/489"},
                  "linked_pr": {"repo": "J-Tech-Japan/intent-system", "number": 490, "url": "https://github.com/J-Tech-Japan/intent-system/pull/490"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "SKS-G185",
                  "title": "right repo",
                  "state": "review",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/sekiban-as-a-service/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_issue": {"repo": "J-Tech-Japan/SekibanAsAService", "number": 489, "url": "https://github.com/J-Tech-Japan/SekibanAsAService/issues/489"},
                  "linked_pr": {"repo": "J-Tech-Japan/SekibanAsAService", "number": 490, "url": "https://github.com/J-Tech-Japan/SekibanAsAService/pull/490"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);
        workspace.WriteFile(".intent-cli/issues/SKS-G185/github-body.md", BuildCompleteContractBody());
        workspace.WriteFile(".intent-cli/issues/SKS-G185/packet.yaml", "x");

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/SekibanAsAService", "--pr", "490", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("SKS-G185", document.RootElement.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_MissingPr_ReturnsUsageError()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        using var writer = new StringWriter();

        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--pr is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingRepo_ReturnsUsageError()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        using var writer = new StringWriter();

        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--pr", "596"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        using var writer = new StringWriter();

        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "596", "--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'json' or 'markdown'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MarkdownFormat_EmitsHumanReadableOutput()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G247", "review", linkedPr: "596", linkedIssue: ("J-Tech-Japan/intent-system", 595, null)));
        workspace.WriteFile(".intent-cli/issues/G247/github-body.md", BuildCompleteContractBody());

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "596"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Review closeout plan — J-Tech-Japan/intent-system#596", output, StringComparison.Ordinal);
        Assert.Contains("expected submodule path: submodules/intent-system", output, StringComparison.Ordinal);
        Assert.Contains("ready: yes", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        using var writer = new StringWriter();

        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("review closeout-plan", writer.ToString(), StringComparison.Ordinal);
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
            - x

            ## Out Of Scope
            - x

            ## Acceptance Criteria
            - x

            ## Verification
            x

            ## Related Links
            - x
            """;
    }

    private static string BuildQueueState(string executionUnit, string state, string? linkedPr, (string Repo, int Number, string? Url)? linkedIssue)
    {
        var linkedPrToken = linkedPr is null ? "null" : $"\"{linkedPr}\"";
        var linkedIssueBlock = linkedIssue is null
            ? ""
            : $@",
                  ""linked_issue"": {{
                    ""repo"": ""{linkedIssue.Value.Repo}"",
                    ""number"": {linkedIssue.Value.Number},
                    ""url"": {(linkedIssue.Value.Url is null ? "null" : $"\"{linkedIssue.Value.Url}\"")}
                  }}";
        return $$"""
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "{{executionUnit}}",
                  "title": "title",
                  "state": "{{state}}",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": {{linkedPrToken}}{{linkedIssueBlock}},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """;
    }

    private sealed class ReviewCloseoutPlanWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("review-closeout-plan-tests-")
            .FullName;

        public ReviewCloseoutPlanWorkspace()
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
