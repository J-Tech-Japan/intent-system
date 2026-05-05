using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class CloseoutPrCommandTests : IDisposable
{
    public CloseoutPrCommandTests()
    {
        CloseoutPrCommand.UtcNowFactory = () => new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero);
    }

    public void Dispose()
    {
        CloseoutPrCommand.UtcNowFactory = null;
    }

    [Fact]
    public void Execute_GivenWriteWithReviewItem_TransitionsToCompletedAndAppendsRunsEvents()
    {
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueState("G246", "review", linkedPr: "594"));

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "594", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("write", root.GetProperty("mode").GetString());
        Assert.Equal("G246", root.GetProperty("execution_unit").GetString());
        Assert.Equal("review", root.GetProperty("queue_state_before").GetString());
        Assert.Equal("completed", root.GetProperty("queue_state_after").GetString());
        Assert.Equal(2, root.GetProperty("runs_events").GetArrayLength());

        var queueOnDisk = File.ReadAllText(workspace.Context.GetQueueStatePath());
        Assert.Contains("\"state\": \"completed\"", queueOnDisk, StringComparison.Ordinal);

        var runsLines = File.ReadAllLines(workspace.Context.GetRunLogPath());
        Assert.Equal(2, runsLines.Length);
        Assert.Contains("pr-merged", runsLines[0], StringComparison.Ordinal);
        Assert.Contains("closeout-recorded", runsLines[1], StringComparison.Ordinal);
        Assert.Contains("\"pr\":594", runsLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenDryRun_DoesNotMutateAnyFile()
    {
        using var workspace = new CloseoutPrWorkspace();
        var queueBefore = BuildQueueState("G246", "review", linkedPr: "594");
        workspace.WriteQueueState(queueBefore);

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "594", "--dry-run", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("dry-run", document.RootElement.GetProperty("mode").GetString());

        Assert.Equal(queueBefore, File.ReadAllText(workspace.Context.GetQueueStatePath()));
        Assert.False(File.Exists(workspace.Context.GetRunLogPath()));
    }

    [Fact]
    public void Execute_GivenAlreadyCompletedItem_ReportsAlreadyCompletedAndDoesNotAppend()
    {
        using var workspace = new CloseoutPrWorkspace();
        var queue = BuildQueueState("G246", "completed", linkedPr: "594");
        workspace.WriteQueueState(queue);

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "594", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("queue_already_completed").GetBoolean());

        Assert.Equal(queue, File.ReadAllText(workspace.Context.GetQueueStatePath()));
        Assert.False(File.Exists(workspace.Context.GetRunLogPath()));
    }

    [Fact]
    public void Execute_GivenQueuedItem_FailsWithUnsupportedTransition()
    {
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueState("G246", "queued", linkedPr: "594"));

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "594", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Contains("active/review/fixing → completed only", document.RootElement.GetProperty("error").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenNoMatchingLinkedPr_FailsWithLinkedPrError()
    {
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueState("G246", "review", linkedPr: "999"));

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "594", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Contains("no queue item found with linked_pr matching #594", document.RootElement.GetProperty("error").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenAnotherQueuedSlice_RecommendsNextSliceReady()
    {
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueWithTwoItems(
            completing: ("G246", "review", "594"),
            waiting: ("G247", "queued", null)));

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "594", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("next-slice-ready", document.RootElement.GetProperty("continuation_hint").GetString());
    }

    [Fact]
    public void Execute_GivenNoOtherSlice_RecommendsNoActionableItem()
    {
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueState("G246", "review", linkedPr: "594"));

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "594", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("no-actionable-item", document.RootElement.GetProperty("continuation_hint").GetString());
    }

    [Fact]
    public void Execute_GivenClarifyBlockedSibling_RecommendsClarificationRequired()
    {
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueWithTwoItems(
            completing: ("G246", "review", "594"),
            waiting: ("G247", "clarify-blocked", null)));

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "594", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("clarification-required", document.RootElement.GetProperty("continuation_hint").GetString());
    }

    [Fact]
    public void Execute_MissingPr_ReturnsUsageError()
    {
        using var workspace = new CloseoutPrWorkspace();
        using var writer = new StringWriter();

        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--pr is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingRepo_ReturnsUsageError()
    {
        using var workspace = new CloseoutPrWorkspace();
        using var writer = new StringWriter();

        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--pr", "594"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NonPositivePr_ReturnsUsageError()
    {
        using var workspace = new CloseoutPrWorkspace();
        using var writer = new StringWriter();

        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "0"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--pr must be a positive integer", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_BothWriteAndDryRun_ReturnsUsageError()
    {
        using var workspace = new CloseoutPrWorkspace();
        using var writer = new StringWriter();

        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "594", "--write", "--dry-run"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--write and --dry-run are mutually exclusive", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var workspace = new CloseoutPrWorkspace();
        using var writer = new StringWriter();

        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("closeout pr", writer.ToString(), StringComparison.Ordinal);
    }

    // ── focused-closeout-pr-skill-replacement-tests ───────────

    [Fact]
    public void Execute_GivenLinkedIssueWithNoLinkedPr_ResolvesViaIssueFlagDryRun()
    {
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueStateWithLinkedIssue("G268", "review", linkedIssueNumber: 639));

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "640", "--issue", "639", "--dry-run", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("G268", root.GetProperty("execution_unit").GetString());
        Assert.Equal("dry-run", root.GetProperty("mode").GetString());
        Assert.Equal("review", root.GetProperty("queue_state_before").GetString());
        Assert.Equal("completed", root.GetProperty("queue_state_after").GetString());
    }

    [Fact]
    public void Execute_GivenLinkedIssueWithNoLinkedPr_ResolvesViaIssueFlagWrite()
    {
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueStateWithLinkedIssue("G268", "review", linkedIssueNumber: 639));

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "640", "--issue", "639", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("write", root.GetProperty("mode").GetString());
        Assert.Equal("completed", root.GetProperty("queue_state_after").GetString());

        var queueOnDisk = File.ReadAllText(workspace.Context.GetQueueStatePath());
        Assert.Contains("\"state\": \"completed\"", queueOnDisk, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenNoLinkedPrAndNoIssueFlagProvided_HintsRetryWithIssueFlag()
    {
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueStateWithLinkedIssue("G268", "review", linkedIssueNumber: 639));

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "640", "--dry-run", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("retry with --issue", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenLinkedIssueWithNoLinkedPr_NextStepsContainsIssueCloseCommand()
    {
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueStateWithLinkedIssue("G268", "review", linkedIssueNumber: 639));

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "640", "--issue", "639", "--dry-run", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var nextSteps = document.RootElement.GetProperty("next_steps")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(nextSteps, s => s!.Contains("gh issue close 639", StringComparison.Ordinal));
        Assert.Contains(nextSteps, s => s!.Contains("Closed by PR #640", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenIssueFlagWithBothLinkedPrAndIssuePresent_PrefersLinkedPrMatch()
    {
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueState("G246", "review", linkedPr: "594"));

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "594", "--issue", "100", "--dry-run", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("G246", document.RootElement.GetProperty("execution_unit").GetString());
    }

    private static string BuildQueueStateWithLinkedIssue(string executionUnit, string state, int linkedIssueNumber)
    {
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
                  "linked_issue": {"repo": "J-Tech-Japan/intent-system", "number": {{linkedIssueNumber}}},
                  "linked_pr": null,
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """;
    }

    private static string BuildQueueState(string executionUnit, string state, string? linkedPr)
    {
        var linked = linkedPr is null ? "null" : $"\"{linkedPr}\"";
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
                  "linked_pr": {{linked}},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """;
    }

    private static string BuildQueueWithTwoItems(
        (string ExecutionUnit, string State, string? LinkedPr) completing,
        (string ExecutionUnit, string State, string? LinkedPr) waiting)
    {
        var completingLinked = completing.LinkedPr is null ? "null" : $"\"{completing.LinkedPr}\"";
        var waitingLinked = waiting.LinkedPr is null ? "null" : $"\"{waiting.LinkedPr}\"";
        return $$"""
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "{{completing.ExecutionUnit}}",
                  "title": "completing",
                  "state": "{{completing.State}}",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": {{completingLinked}},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "{{waiting.ExecutionUnit}}",
                  "title": "waiting",
                  "state": "{{waiting.State}}",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": {{waitingLinked}},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """;
    }

    private sealed class CloseoutPrWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("closeout-pr-tests-")
            .FullName;

        public CloseoutPrWorkspace()
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

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
