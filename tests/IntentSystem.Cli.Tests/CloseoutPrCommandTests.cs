using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class CloseoutPrCommandTests : IDisposable
{
    public CloseoutPrCommandTests()
    {
        CloseoutPrCommand.UtcNowFactory = () => new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero);
        // G477: default the closing-issues fetcher to a deterministic empty
        // result so the missing-linked_pr auto-recovery path never shells out
        // to live GitHub during unit tests. Recovery tests override this.
        CloseoutPrCommand.PrClosingIssuesFetcherFactory = () => new FakeClosingIssuesFetcher(Array.Empty<int>());
    }

    public void Dispose()
    {
        CloseoutPrCommand.UtcNowFactory = null;
        CloseoutPrCommand.PrClosingIssuesFetcherFactory = null;
    }

    private sealed class FakeClosingIssuesFetcher : IPrClosingIssuesFetcher
    {
        private readonly IReadOnlyList<int> _closingIssues;

        public FakeClosingIssuesFetcher(IReadOnlyList<int> closingIssues)
        {
            _closingIssues = closingIssues;
        }

        public IReadOnlyList<int> Fetch(string repo, int prNumber) => _closingIssues;
    }

    // --- G324: durable writes use current RunEvent schema + auto-commit safe ---

    [Fact]
    public void Execute_GivenWrite_AppendsCurrentSchemaRunEventsThatDeserialize()
    {
        // G324 acceptance: closeout runs.jsonl lines must use the
        // canonical `ts` / `execution_unit` / `event` / `by` fields
        // (plus the new `repo` / `pr` correlation) so the supervisor
        // and the G312 durable-state preflight can deserialize them.
        // Legacy `timestamp` / `kind` are gone.
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueState("G324", "review", linkedPr: "595"));

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "595", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var lines = workspace.RunsLines();
        Assert.Equal(2, lines.Length);
        foreach (var line in lines)
        {
            // New schema fields must be present.
            Assert.Contains("\"ts\":", line, StringComparison.Ordinal);
            Assert.Contains("\"event\":", line, StringComparison.Ordinal);
            Assert.Contains("\"by\":", line, StringComparison.Ordinal);
            Assert.Contains("\"execution_unit\":\"G324\"", line, StringComparison.Ordinal);
            // Legacy schema fields must NOT appear in the new line.
            Assert.DoesNotContain("\"timestamp\":", line, StringComparison.Ordinal);
            Assert.DoesNotContain("\"kind\":", line, StringComparison.Ordinal);
            // Every emitted line must round-trip through the supervisor
            // deserializer with no exception (this is what `RunsJsonlAppendOnlyAnalyzer`
            // calls to validate appended lines).
            var deserialized = RunLogSerializer.DeserializeLine(line);
            Assert.Equal("G324", deserialized.ExecutionUnit);
            Assert.Equal("intent-cli closeout pr", deserialized.By);
            Assert.Equal("J-Tech-Japan/intent-system", deserialized.Repo);
            Assert.Equal(595, deserialized.Pr);
        }
        Assert.Equal("pr-merged", RunLogSerializer.DeserializeLine(lines[0]).Event);
        Assert.Equal("closeout-recorded", RunLogSerializer.DeserializeLine(lines[1]).Event);
    }

    [Fact]
    public void Execute_GivenWrite_QueueStateDeltaIsForwardOnlyAutoCommitSafe()
    {
        // G324 acceptance: durable-state-preflight (G312) must classify
        // the closeout-only state change as forward-only / verified so
        // the host loop's auto-commit lane proceeds without an operator
        // review stop on the next wake.
        using var workspace = new CloseoutPrWorkspace();
        var beforeQueueText = BuildQueueState("G324", "review", linkedPr: "596");
        workspace.WriteQueueState(beforeQueueText);

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "596", "--write", "--format", "json"],
            writer);
        Assert.Equal(0, exitCode);

        var afterQueueText = workspace.QueueStateOnDisk();
        var delta = QueueStateForwardDeltaAnalyzer.Analyze(beforeQueueText, afterQueueText);

        Assert.Equal(QueueStateForwardDeltaAnalyzer.ClassificationForwardOnly, delta.Classification);
        var change = Assert.Single(delta.Changes);
        Assert.Equal(QueueStateForwardChangeKind.ClosedOutToCompleted, change.Kind);
        Assert.Equal("G324", change.ExecutionUnit);
    }

    [Fact]
    public void Execute_GivenWrite_RunsLinesAreAppendOnlyAutoCommitSafe()
    {
        // G324 acceptance: combined with the queue-state forward delta,
        // the runs.jsonl append must classify as append-only so the
        // overall durable-state preflight reports verified-commit-ready.
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueState("G324", "review", linkedPr: "597"));
        // Seed an empty runs.jsonl as HEAD so the analyzer compares
        // empty HEAD vs the 2 appended lines.
        File.WriteAllText(workspace.Context.GetRunLogPath(), string.Empty);
        var headRuns = File.ReadAllText(workspace.Context.GetRunLogPath());

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "597", "--write", "--format", "json"],
            writer);
        Assert.Equal(0, exitCode);

        var workingRuns = File.ReadAllText(workspace.Context.GetRunLogPath());
        var runsDelta = RunsJsonlAppendOnlyAnalyzer.Analyze(headRuns, workingRuns);

        Assert.Equal(RunsJsonlAppendOnlyAnalyzer.ClassificationAppendOnly, runsDelta.Classification);
        Assert.Equal(2, runsDelta.AppendedEventCount);
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
    public void Execute_GivenQueuedItem_TransitionsToCompleted()
    {
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueState("G246", "queued", linkedPr: "594"));

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "594", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("queued", document.RootElement.GetProperty("queue_state_before").GetString());
        Assert.Equal("completed", document.RootElement.GetProperty("queue_state_after").GetString());
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
    public void Execute_GivenSamePrNumberInDifferentRepo_SkipsOtherRepo()
    {
        using var workspace = new CloseoutPrWorkspace();
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
                  "linked_pr": {"repo": "J-Tech-Japan/SekibanAsAService", "number": 490, "url": "https://github.com/J-Tech-Japan/SekibanAsAService/pull/490"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/SekibanAsAService", "--pr", "490", "--dry-run", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("SKS-G185", document.RootElement.GetProperty("execution_unit").GetString());
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
    public void Execute_GivenQueuedStateWithLinkedIssueAndNoLinkedPr_ResolvesViaIssueFlagDryRun()
    {
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueStateWithLinkedIssue("G268", "queued", linkedIssueNumber: 639));

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
        Assert.Equal("queued", root.GetProperty("queue_state_before").GetString());
        Assert.Equal("completed", root.GetProperty("queue_state_after").GetString());
    }

    [Fact]
    public void Execute_GivenQueuedStateWithLinkedIssueAndNoLinkedPr_ResolvesViaIssueFlagWrite()
    {
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueStateWithLinkedIssue("G268", "queued", linkedIssueNumber: 639));

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "640", "--issue", "639", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("write", root.GetProperty("mode").GetString());
        Assert.Equal("queued", root.GetProperty("queue_state_before").GetString());
        Assert.Equal("completed", root.GetProperty("queue_state_after").GetString());

        var queueOnDisk = File.ReadAllText(workspace.Context.GetQueueStatePath());
        Assert.Contains("\"state\": \"completed\"", queueOnDisk, StringComparison.Ordinal);
    }

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

    // ── G297 draft / merge-atomic ───────────────────────────────────────

    [Fact]
    public void Execute_PrMergedFalse_RefusesAndDoesNotMutate()
    {
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueState("G297", "review", linkedPr: "523"));
        using var writer = new StringWriter();

        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            new[]
            {
                "--pr", "523",
                "--repo", "owner/repo",
                "--pr-merged", "false",
                "--write",
                "--format", "json"
            },
            writer);

        Assert.Equal(1, exitCode);
        var json = writer.ToString();
        Assert.Contains("not merged", json, StringComparison.Ordinal);
        Assert.Contains("G297", json, StringComparison.Ordinal);

        // Mutation invariant: queue-state must not have been modified.
        Assert.Equal(BuildQueueState("G297", "review", linkedPr: "523"),
            File.ReadAllText(workspace.Context.GetQueueStatePath()));
        Assert.False(File.Exists(workspace.Context.GetRunLogPath()));
    }

    [Fact]
    public void Execute_PrMergedTrue_AllowsCloseout()
    {
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueState("G297", "review", linkedPr: "523"));
        using var writer = new StringWriter();

        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            new[]
            {
                "--pr", "523",
                "--repo", "owner/repo",
                "--pr-merged", "true",
                "--write",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("completed", document.RootElement.GetProperty("queue_state_after").GetString());
    }

    [Fact]
    public void Execute_PrMergedOmitted_BackwardsCompatible_AllowsCloseout()
    {
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueState("G297", "review", linkedPr: "523"));
        using var writer = new StringWriter();

        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            new[]
            {
                "--pr", "523",
                "--repo", "owner/repo",
                "--write",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Execute_RejectsInvalidPrMergedValue()
    {
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueState("G297", "review", linkedPr: "523"));
        using var writer = new StringWriter();

        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            new[]
            {
                "--pr", "523",
                "--repo", "owner/repo",
                "--pr-merged", "yes",
                "--write"
            },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--pr-merged must be 'true' or 'false'", writer.ToString(), StringComparison.Ordinal);
    }

    // --- G477: auto-fallback closeout when linked_pr missing but deterministic ---

    [Fact]
    public void Execute_G477_MissingLinkedPrButDeterministicClosingIssue_RecoversWithoutIssueFlag()
    {
        // Queue item G412 has linked_issue #820 and null linked_pr; GitHub says
        // merged PR #821 closes #820. `closeout pr --pr 821 --write` must succeed
        // without the operator passing --issue 820.
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueStateWithLinkedIssue("G412", "review", linkedIssueNumber: 820));
        CloseoutPrCommand.PrClosingIssuesFetcherFactory = () => new FakeClosingIssuesFetcher(new[] { 820 });

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "821", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutPrResult>(writer.ToString())!;
        Assert.Equal("G412", result.ExecutionUnit);
        Assert.Equal("completed", result.QueueStateAfterState);
        Assert.True(result.RecoverableMissingLinkedPr);
        Assert.Equal(820, result.InferredIssue);
        Assert.Equal("github-closing-reference", result.RecoverySource);
        Assert.Equal("recover-linked-pr-from-github-closing-reference", result.RecoveryAction);

        // The host-owned linked_pr projection is repaired on write.
        var persisted = File.ReadAllText(workspace.Context.GetQueueStatePath());
        Assert.Contains("https://github.com/J-Tech-Japan/intent-system/pull/821", persisted, StringComparison.Ordinal);
        Assert.Contains("\"state\": \"completed\"", persisted, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G477_MissingLinkedPrDeterministicDryRun_ReportsRecoverableFields()
    {
        // Dry-run must surface recoverable_missing_linked_pr, the inferred issue,
        // and the recovery action without mutating state.
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueStateWithLinkedIssue("G412", "review", linkedIssueNumber: 820));
        var before = File.ReadAllText(workspace.Context.GetQueueStatePath());
        CloseoutPrCommand.PrClosingIssuesFetcherFactory = () => new FakeClosingIssuesFetcher(new[] { 820 });

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "821", "--dry-run", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutPrResult>(writer.ToString())!;
        Assert.True(result.RecoverableMissingLinkedPr);
        Assert.Equal(820, result.InferredIssue);
        Assert.Equal("recover-linked-pr-from-github-closing-reference", result.RecoveryAction);
        // No mutation on dry-run.
        Assert.Equal(before, File.ReadAllText(workspace.Context.GetQueueStatePath()));
        Assert.False(File.Exists(workspace.Context.GetRunLogPath()));
    }

    [Fact]
    public void Execute_G477_MissingLinkedPrAmbiguousClosingIssues_FailsClosed()
    {
        // Two queue items link to two issues the PR closes — ambiguous. Must fail
        // closed with a structured reason, not guess.
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildTwoItemsWithLinkedIssues(
            ("G412", "review", 820),
            ("G413", "review", 822)));
        CloseoutPrCommand.PrClosingIssuesFetcherFactory = () => new FakeClosingIssuesFetcher(new[] { 820, 822 });

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "821", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutPrResult>(writer.ToString())!;
        Assert.Contains("linkage-ambiguous", result.Error!, StringComparison.Ordinal);
        Assert.Contains("refusing to guess", result.Error!, StringComparison.Ordinal);
        // No mutation: both items stay un-completed.
        var persisted = File.ReadAllText(workspace.Context.GetQueueStatePath());
        Assert.DoesNotContain("\"state\": \"completed\"", persisted, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G477_MissingLinkedPrNoClosingIssueMatch_StillHintsRetryWithIssue()
    {
        // GitHub returns a closing issue, but no queue item links to it. The
        // command must fall through to the existing not-found failure (no guess).
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueStateWithLinkedIssue("G412", "review", linkedIssueNumber: 820));
        CloseoutPrCommand.PrClosingIssuesFetcherFactory = () => new FakeClosingIssuesFetcher(new[] { 999 });

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "821", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutPrResult>(writer.ToString())!;
        Assert.Contains("no queue item found with linked_pr matching #821", result.Error!, StringComparison.Ordinal);
        Assert.False(result.RecoverableMissingLinkedPr);
    }

    private static string BuildTwoItemsWithLinkedIssues(
        (string ExecutionUnit, string State, int LinkedIssueNumber) first,
        (string ExecutionUnit, string State, int LinkedIssueNumber) second)
    {
        return $$"""
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "{{first.ExecutionUnit}}",
                  "title": "first",
                  "state": "{{first.State}}",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_issue": {"repo": "J-Tech-Japan/intent-system", "number": {{first.LinkedIssueNumber}}},
                  "linked_pr": null,
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "{{second.ExecutionUnit}}",
                  "title": "second",
                  "state": "{{second.State}}",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_issue": {"repo": "J-Tech-Japan/intent-system", "number": {{second.LinkedIssueNumber}}},
                  "linked_pr": null,
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """;
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

    // --- G327: runtime-scoped state layout integration ---------------------

    [Fact]
    public void Execute_GivenScopedQueueStateOnDisk_WritesUnderRuntimeScopedTree()
    {
        // G327 acceptance: `closeout pr --domain <d> --repo <owner/repo>
        // --write` MUST mutate
        // `.intent-cli/runtime/<domain>/<owner>__<repo>/queue-state.json`
        // and append to
        // `.intent-cli/runtime/<domain>/<owner>__<repo>/runs.jsonl` when
        // a scoped queue-state is on disk — not the root files. This is
        // the explicit fix-comment ask: review-runtime commands must
        // stop rewriting root queue-state as the primary runtime store.
        using var workspace = new CloseoutPrWorkspace();
        var scopedDir = Path.Combine(workspace.Context.RepoRoot, ".intent-cli", "runtime",
            "intent-cli", "J-Tech-Japan__intent-system");
        Directory.CreateDirectory(scopedDir);
        var scopedQueuePath = Path.Combine(scopedDir, "queue-state.json");
        File.WriteAllText(scopedQueuePath,
            BuildQueueState("G327", "review", linkedPr: "758"));
        // Seed a DIFFERENT legacy root queue-state so the test can
        // detect any accidental write to the legacy file.
        var legacyQueuePath = workspace.Context.GetQueueStatePath();
        File.WriteAllText(legacyQueuePath,
            BuildQueueState("OTHER", "review", linkedPr: "999"));
        var legacyBefore = File.ReadAllText(legacyQueuePath);
        var legacyRunsExisted = File.Exists(workspace.Context.GetRunLogPath());

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "758",
                "--domain", "intent-cli", "--write", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutPrResult>(writer.ToString())!;
        Assert.Equal("scoped", result.StateLayout);
        Assert.Equal(scopedQueuePath, result.QueueStatePath);
        Assert.EndsWith(
            Path.Combine("J-Tech-Japan__intent-system", "runs.jsonl"),
            result.RunsLogPath);

        // Scoped queue-state was actually mutated: state="completed".
        var scopedAfter = File.ReadAllText(scopedQueuePath);
        Assert.Contains("\"state\": \"completed\"", scopedAfter, StringComparison.Ordinal);
        // Scoped runs.jsonl received the two events.
        var scopedRuns = File.ReadAllLines(Path.Combine(scopedDir, "runs.jsonl"));
        Assert.Equal(2, scopedRuns.Length);
        foreach (var line in scopedRuns)
        {
            Assert.Contains("\"execution_unit\":\"G327\"", line, StringComparison.Ordinal);
        }

        // Legacy root queue-state must be byte-identical to before.
        Assert.Equal(legacyBefore, File.ReadAllText(legacyQueuePath));
        // Legacy root runs.jsonl was never created by this scoped write.
        Assert.Equal(legacyRunsExisted, File.Exists(workspace.Context.GetRunLogPath()));
    }

    [Fact]
    public void Execute_GivenOnlyLegacyQueueState_FallsBackToLegacyAndReportsLegacyFallbackLayout()
    {
        // G327 transition: callers that haven't migrated to the scoped
        // layout yet must continue to function. When only legacy root
        // queue-state exists, the resolver falls back to legacy for
        // BOTH queue-state and runs.jsonl, and the result surfaces
        // `state_layout: "legacy-fallback"` so operators see they're
        // mid-migration.
        using var workspace = new CloseoutPrWorkspace();
        workspace.WriteQueueState(BuildQueueState("G327", "review", linkedPr: "770"));

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "770",
                "--domain", "intent-cli", "--write", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutPrResult>(writer.ToString())!;
        Assert.Equal("legacy-fallback", result.StateLayout);
        Assert.Equal(workspace.Context.GetQueueStatePath(), result.QueueStatePath);
        Assert.Equal(workspace.Context.GetRunLogPath(), result.RunsLogPath);
        // Legacy queue-state mutated, legacy runs.jsonl created with events.
        Assert.Contains("\"state\": \"completed\"", workspace.QueueStateOnDisk(),
            StringComparison.Ordinal);
        Assert.Equal(2, workspace.RunsLines().Length);

        // The scoped runtime tree was NEVER created (no scoped seed).
        Assert.False(Directory.Exists(Path.Combine(workspace.Context.RepoRoot,
            ".intent-cli", "runtime", "intent-cli", "J-Tech-Japan__intent-system")));
    }

    [Fact]
    public void Execute_GivenIntentSystemAndSekibanScopedQueueStates_WriteToDifferentScopedFiles()
    {
        // G327 acceptance: per-(domain, owner/repo) scopes must NOT
        // share the same active runtime queue. When closeout runs for
        // intent-cli/J-Tech-Japan/intent-system, a Sekiban-scoped
        // queue-state under the same parent host root must be
        // byte-identical after the write.
        using var workspace = new CloseoutPrWorkspace();
        var intentDir = Path.Combine(workspace.Context.RepoRoot, ".intent-cli", "runtime",
            "intent-cli", "J-Tech-Japan__intent-system");
        Directory.CreateDirectory(intentDir);
        File.WriteAllText(Path.Combine(intentDir, "queue-state.json"),
            BuildQueueState("G327", "review", linkedPr: "758"));

        var sekibanDir = Path.Combine(workspace.Context.RepoRoot, ".intent-cli", "runtime",
            "sekiban-as-a-service", "J-Tech-Japan__SekibanAsAService");
        Directory.CreateDirectory(sekibanDir);
        var sekibanQueuePath = Path.Combine(sekibanDir, "queue-state.json");
        File.WriteAllText(sekibanQueuePath,
            BuildQueueState("SEKI-1", "queued", linkedPr: null));
        var sekibanBefore = File.ReadAllText(sekibanQueuePath);

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "758",
                "--domain", "intent-cli", "--write", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        // Sekiban-scoped queue-state is byte-identical to before — the
        // intent-cli closeout MUST NOT cross domains.
        Assert.Equal(sekibanBefore, File.ReadAllText(sekibanQueuePath));
        Assert.False(File.Exists(Path.Combine(sekibanDir, "runs.jsonl")),
            "intent-cli closeout must not create runs.jsonl in the Sekiban scoped tree.");
    }

    [Fact]
    public void Execute_GivenScopedLayout_PreservesPacketLookupUnderIntentCliIssuesDirectory()
    {
        // G327 acceptance: the runtime-scoped state migration MUST NOT
        // affect the design-owned packet backlog. Packets live under
        // `.intent-cli/issues/<execution-unit>/` (G300) and must NOT
        // be copied or rerouted into the scoped runtime tree by
        // closeout writes.
        using var workspace = new CloseoutPrWorkspace();
        var scopedDir = Path.Combine(workspace.Context.RepoRoot, ".intent-cli", "runtime",
            "intent-cli", "J-Tech-Japan__intent-system");
        Directory.CreateDirectory(scopedDir);
        File.WriteAllText(Path.Combine(scopedDir, "queue-state.json"),
            BuildQueueState("G327", "review", linkedPr: "758"));
        // Seed a packet at the canonical legacy location.
        var legacyPacketDir = Path.Combine(workspace.Context.RepoRoot,
            ".intent-cli", "issues", "G327");
        Directory.CreateDirectory(legacyPacketDir);
        var packetPath = Path.Combine(legacyPacketDir, "packet.yaml");
        File.WriteAllText(packetPath, "execution_unit: G327\n");
        var packetBefore = File.ReadAllText(packetPath);

        using var writer = new StringWriter();
        var exitCode = CloseoutPrCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "758",
                "--domain", "intent-cli", "--write", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        // Packet content is unchanged.
        Assert.Equal(packetBefore, File.ReadAllText(packetPath));
        // The scoped runtime tree did NOT gain a packet copy.
        Assert.False(Directory.Exists(Path.Combine(scopedDir, "issues")));
        Assert.False(File.Exists(Path.Combine(scopedDir, "issues", "G327", "packet.yaml")));
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

        public string QueueStateOnDisk() => File.ReadAllText(Context.GetQueueStatePath());

        public string[] RunsLines() => File.Exists(Context.GetRunLogPath())
            ? File.ReadAllLines(Context.GetRunLogPath())
            : Array.Empty<string>();

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
