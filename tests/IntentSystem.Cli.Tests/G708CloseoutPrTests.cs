using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G708: closeout output must describe actual runs writes, and the only repair
/// path is an explicit, idempotent runs-only append. The fixture is deliberately
/// a unique repository-local artifact directory and has no cleanup that deletes
/// a temporary or /tmp path.
/// </summary>
public sealed class G708CloseoutPrTests
{
    private const string Repo = "J-Tech-Japan/intent-system";

    [Fact]
    public void WriteReportsOnlyActualAppendedLines_AndCompletedSkipIsEmpty()
    {
        var workspace = new G708Workspace();
        var queueBefore = workspace.WriteQueue("G708", "review", 1532);

        using var firstWriter = new StringWriter();
        Assert.Equal(0, CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--pr", "1532", "--write", "--format", "json"],
            firstWriter));

        var first = Deserialize(firstWriter);
        Assert.True(first.GetProperty("runs_appended").GetBoolean());
        Assert.Equal(2, first.GetProperty("runs_events").GetArrayLength());
        Assert.False(first.TryGetProperty("runs_skip_reason", out _));
        var firstEvents = first.GetProperty("runs_events")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        Assert.Equal(string.Concat(firstEvents.Select(line => line + Environment.NewLine)), workspace.RunsText());

        var queueAfterWrite = workspace.QueueText();
        Assert.Contains("\"state\": \"completed\"", queueAfterWrite, StringComparison.Ordinal);

        var runsBeforeSkip = workspace.RunsText();
        using var secondWriter = new StringWriter();
        Assert.Equal(0, CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--pr", "1532", "--write", "--format", "json"],
            secondWriter));

        var second = Deserialize(secondWriter);
        Assert.False(second.GetProperty("runs_appended").GetBoolean());
        Assert.Empty(second.GetProperty("runs_events").EnumerateArray());
        Assert.Equal("queue-already-completed", second.GetProperty("runs_skip_reason").GetString());
        Assert.Equal(queueAfterWrite, workspace.QueueText());
        Assert.Equal(runsBeforeSkip, workspace.RunsText());
    }

    [Fact]
    public void DryRunReportsNoActualRunsWrite_WithNamedSkipReason()
    {
        var workspace = new G708Workspace(createMetadata: false);
        var queueBefore = workspace.WriteQueue("G708-dry", "review", 1533);

        using var writer = new StringWriter();
        Assert.Equal(0, CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--pr", "1533", "--dry-run", "--format", "json"],
            writer));

        var result = Deserialize(writer);
        Assert.False(result.GetProperty("runs_appended").GetBoolean());
        Assert.Empty(result.GetProperty("runs_events").EnumerateArray());
        Assert.Equal("dry-run-no-write", result.GetProperty("runs_skip_reason").GetString());
        Assert.Equal(queueBefore, workspace.QueueText());
        Assert.False(File.Exists(workspace.RunsPath));
    }

    [Fact]
    public void CompletedMissingRunsEventsIsNamedAndDoesNotAutoRepair_InJsonAndMarkdown()
    {
        var workspace = new G708Workspace();
        workspace.WriteQueue("G708-gap", "completed", 1534);
        File.WriteAllText(workspace.RunsPath, string.Empty);
        var queueBefore = workspace.QueueText();
        var runsBefore = workspace.RunsText();

        using var jsonWriter = new StringWriter();
        Assert.Equal(0, CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--pr", "1534", "--write", "--format", "json"],
            jsonWriter));

        var json = Deserialize(jsonWriter);
        Assert.False(json.GetProperty("runs_appended").GetBoolean());
        Assert.Empty(json.GetProperty("runs_events").EnumerateArray());
        Assert.Equal("queue-already-completed", json.GetProperty("runs_skip_reason").GetString());
        var finding = Assert.Single(json.GetProperty("findings").EnumerateArray());
        Assert.Equal("queue-completed-missing-closeout-runs-events", finding.GetProperty("kind").GetString());
        Assert.Contains("pr-merged", finding.GetProperty("summary").GetString()!, StringComparison.Ordinal);
        Assert.Contains("closeout-recorded", finding.GetProperty("summary").GetString()!, StringComparison.Ordinal);
        Assert.Contains("--repair-runs --write", finding.GetProperty("recommended_action").GetString()!, StringComparison.Ordinal);
        Assert.Equal(queueBefore, workspace.QueueText());
        Assert.Equal(runsBefore, workspace.RunsText());

        using var markdownWriter = new StringWriter();
        Assert.Equal(0, CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--pr", "1534", "--write", "--format", "markdown"],
            markdownWriter));
        var markdown = markdownWriter.ToString();
        Assert.Contains("runs_appended: no", markdown, StringComparison.Ordinal);
        Assert.Contains("runs_skip_reason: queue-already-completed", markdown, StringComparison.Ordinal);
        Assert.Contains("queue-completed-missing-closeout-runs-events", markdown, StringComparison.Ordinal);
        Assert.Contains("runs_events: []", markdown, StringComparison.Ordinal);
        Assert.Equal(queueBefore, workspace.QueueText());
        Assert.Equal(runsBefore, workspace.RunsText());
    }

    [Fact]
    public void RepairAppendsOnlyMissingRunsEvents_PreservesQueueBytes_AndIsIdempotent()
    {
        var workspace = new G708Workspace();
        workspace.WriteQueue("G708-repair", "completed", 1535);
        var existing = SerializeRunEvent("G708-repair", "pr-merged", 1535);
        var unrelated = SerializeRunEvent("other-unit", "issue-created", 9999);
        File.WriteAllText(workspace.RunsPath, unrelated + Environment.NewLine + existing + Environment.NewLine);
        var queueBefore = workspace.QueueText();
        var runsBefore = workspace.RunsText();

        using var repairWriter = new StringWriter();
        Assert.Equal(0, CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--pr", "1535", "--repair-runs", "--write", "--format", "json"],
            repairWriter));

        var repaired = Deserialize(repairWriter);
        Assert.True(repaired.GetProperty("runs_appended").GetBoolean());
        var appended = Assert.Single(repaired.GetProperty("runs_events").EnumerateArray()).GetString()!;
        Assert.Equal("closeout-recorded", RunLogSerializer.DeserializeLine(appended).Event);
        Assert.Equal(runsBefore + appended + Environment.NewLine, workspace.RunsText());
        Assert.Equal(queueBefore, workspace.QueueText());
        Assert.Contains("queue-completed-missing-closeout-runs-events", repairWriter.ToString(), StringComparison.Ordinal);

        var runsAfterRepair = workspace.RunsText();
        using var secondRepairWriter = new StringWriter();
        Assert.Equal(0, CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--pr", "1535", "--repair-runs", "--write", "--format", "json"],
            secondRepairWriter));

        var secondRepair = Deserialize(secondRepairWriter);
        Assert.False(secondRepair.GetProperty("runs_appended").GetBoolean());
        Assert.Empty(secondRepair.GetProperty("runs_events").EnumerateArray());
        Assert.Equal("runs-events-already-present", secondRepair.GetProperty("runs_skip_reason").GetString());
        Assert.Equal(queueBefore, workspace.QueueText());
        Assert.Equal(runsAfterRepair, workspace.RunsText());
    }

    [Fact]
    public void RepairFlagCannotReplaceAnUncompletedCloseout()
    {
        var workspace = new G708Workspace();
        var queueBefore = workspace.WriteQueue("G708-invalid-repair", "review", 1536);

        using var writer = new StringWriter();
        Assert.Equal(1, CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--pr", "1536", "--repair-runs", "--write", "--format", "json"],
            writer));

        var result = Deserialize(writer);
        Assert.Contains("only valid for a queue item that is already completed", result.GetProperty("error").GetString()!, StringComparison.Ordinal);
        Assert.Equal(queueBefore, workspace.QueueText());
        Assert.False(File.Exists(workspace.RunsPath));
    }

    [Fact]
    public void BareMetadataFreeOrchestratorGuideExposesStructuredCloseoutContractInBothFormats()
    {
        var workspace = new G708Workspace(createMetadata: false);
        Assert.False(Directory.Exists(Path.Combine(workspace.Context.RepoRoot, ".intent-cli")));

        using var jsonWriter = new StringWriter();
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(
            workspace.Context,
            ["--target-repo", Repo, "--agent", "codex", "--format", "json"],
            jsonWriter));

        using var document = JsonDocument.Parse(jsonWriter.ToString());
        var contract = document.RootElement.GetProperty("closeout_runs_contract");
        var outputRules = string.Join(" ", contract.GetProperty("output_contract").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("runs_events", outputRules, StringComparison.Ordinal);
        Assert.Contains("runs_appended", outputRules, StringComparison.Ordinal);
        Assert.Equal("queue-completed-missing-closeout-runs-events", contract.GetProperty("finding_kind").GetString());
        Assert.Contains("--repair-runs --write", contract.GetProperty("repair_command").GetString()!, StringComparison.Ordinal);
        var repairRules = string.Join(" ", contract.GetProperty("repair_rules").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("never writes queue-state", repairRules, StringComparison.Ordinal);
        Assert.Contains("idempotent", repairRules, StringComparison.Ordinal);
        Assert.DoesNotContain("automatically performs repair", repairRules, StringComparison.OrdinalIgnoreCase);

        using var markdownWriter = new StringWriter();
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(
            workspace.Context,
            ["--target-repo", Repo, "--agent", "codex", "--format", "markdown"],
            markdownWriter));
        var markdown = markdownWriter.ToString();
        Assert.Contains("## Closeout runs write-truth and repair (G708)", markdown, StringComparison.Ordinal);
        Assert.Contains("queue-completed-missing-closeout-runs-events", markdown, StringComparison.Ordinal);
        Assert.Contains("--repair-runs --write", markdown, StringComparison.Ordinal);
        Assert.Contains("never writes queue-state", markdown, StringComparison.Ordinal);
        Assert.Contains("second repair invocation", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("--repair-runs --write --auto", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void EnglishAndJapaneseOperationalDocsKeepTheG708ContractInParity()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(root, "docs", "en", "12-agent-message-orchestration.md"));
        var japanese = File.ReadAllText(Path.Combine(root, "docs", "ja", "12-agent-message-orchestration.md"));
        var englishLedger = File.ReadAllText(Path.Combine(root, "docs", "en", "1.0-compatibility-ledger.md"));
        var japaneseLedger = File.ReadAllText(Path.Combine(root, "docs", "ja", "1.0-compatibility-ledger.md"));

        foreach (var marker in new[]
        {
            "G708",
            "runs_events",
            "runs_appended",
            "runs_skip_reason",
            "queue-completed-missing-closeout-runs-events",
            "--repair-runs --write",
            "queue-state",
        })
        {
            Assert.Contains(marker, english, StringComparison.Ordinal);
            Assert.Contains(marker, japanese, StringComparison.Ordinal);
            Assert.Contains(marker, englishLedger, StringComparison.Ordinal);
            Assert.Contains(marker, japaneseLedger, StringComparison.Ordinal);
        }
        Assert.Contains("idempotent", english, StringComparison.Ordinal);
        Assert.Contains("idempotent", japanese, StringComparison.Ordinal);
        Assert.Contains("idempotent", englishLedger, StringComparison.Ordinal);
        Assert.Contains("no-op", japaneseLedger, StringComparison.Ordinal);
    }

    private static JsonElement Deserialize(StringWriter writer)
    {
        using var document = JsonDocument.Parse(writer.ToString());
        return document.RootElement.Clone();
    }

    private static string SerializeRunEvent(string executionUnit, string @event, int pr)
    {
        return RunLogSerializer.SerializeLine(new RunEvent
        {
            Ts = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero),
            ExecutionUnit = executionUnit,
            Event = @event,
            By = "g708-test",
            Repo = Repo,
            Pr = pr,
        });
    }

    private sealed class G708Workspace
    {
        public G708Workspace(bool createMetadata = true)
        {
            var artifactRoot = Path.Combine(
                AppContext.BaseDirectory,
                ".artifacts",
                $"g708-closeout-{Guid.NewGuid():N}");
            if (createMetadata)
            {
                Directory.CreateDirectory(Path.Combine(artifactRoot, ".intent-cli"));
            }
            Context = new CliContext
            {
                RepoRoot = artifactRoot,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            };
        }

        public CliContext Context { get; }

        public string RunsPath => Context.GetRunLogPath();

        public string WriteQueue(string executionUnit, string state, int pr)
        {
            var content = $$"""
                {
                  "schema_version": "1",
                  "updated_at": "2026-08-15T12:00:00Z",
                  "items": [
                    {
                      "execution_unit": "{{executionUnit}}",
                      "title": "G708 test item",
                      "state": "{{state}}",
                      "dependencies": [],
                      "blocked_by": [],
                      "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                      "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                      "linked_pr": "https://github.com/J-Tech-Japan/intent-system/pull/{{pr}}",
                      "worker_role": "coder",
                      "review_role": "reviewer",
                      "priority": "normal"
                    }
                  ]
                }
                """;
            Directory.CreateDirectory(Path.GetDirectoryName(Context.GetQueueStatePath())!);
            File.WriteAllText(Context.GetQueueStatePath(), content);
            return content;
        }

        public string QueueText() => File.ReadAllText(Context.GetQueueStatePath());

        public string RunsText() => File.Exists(RunsPath) ? File.ReadAllText(RunsPath) : string.Empty;
    }
}
