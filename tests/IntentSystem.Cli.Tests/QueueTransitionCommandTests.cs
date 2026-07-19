using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class QueueTransitionCommandTests
{
    [Fact]
    public void Execute_GivenValidTransition_UpdatesSelectedItemAndAppendsRunLog()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """{"ts":"2026-04-03T10:00:00Z","execution_unit":"A1","event":"queued","by":"supervisor"}""" + Environment.NewLine);
        using var writer = new StringWriter();

        var exitCode = QueueTransitionCommand.Execute(CreateContext(repoRoot), ["A2", "completed"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Transitioned A2 to completed", writer.ToString(), StringComparison.Ordinal);

        var updatedState = QueueStateSerializer.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
        Assert.Equal(QueueItemState.Completed, updatedState.Items.Single(item => item.ExecutionUnit == "A2").State);
        Assert.Equal(QueueItemState.Blocked, updatedState.Items.Single(item => item.ExecutionUnit == "B1").State);
        Assert.Equal(["A2"], updatedState.Items.Single(item => item.ExecutionUnit == "B1").BlockedBy);

        var runEvents = RunLogSerializer.DeserializeAll(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
        Assert.Equal(2, runEvents.Count);
        Assert.Equal("completed", runEvents[^1].Event);
        Assert.Equal("A2", runEvents[^1].ExecutionUnit);
        Assert.Equal("intent-cli", runEvents[^1].By);
    }

    [Fact]
    public void Execute_GivenBlockedTransitionWithReason_UpdatesBlockedByAndAppendsReasonedRunLog()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """{"ts":"2026-04-03T10:00:00Z","execution_unit":"A1","event":"queued","by":"supervisor"}""" + Environment.NewLine);
        using var writer = new StringWriter();

        var exitCode = QueueTransitionCommand.Execute(
            CreateContext(repoRoot),
            ["A2", "blocked", "--reason", "waiting on infra approval"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Transitioned A2 to blocked", writer.ToString(), StringComparison.Ordinal);

        var updatedState = QueueStateSerializer.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
        var selectedItem = updatedState.Items.Single(item => item.ExecutionUnit == "A2");
        Assert.Equal(QueueItemState.Blocked, selectedItem.State);
        Assert.Equal(["waiting on infra approval"], selectedItem.BlockedBy);
        Assert.Equal(QueueItemState.Blocked, updatedState.Items.Single(item => item.ExecutionUnit == "B1").State);
        Assert.Equal(["A2"], updatedState.Items.Single(item => item.ExecutionUnit == "B1").BlockedBy);

        var runEvents = RunLogSerializer.DeserializeAll(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
        Assert.Equal(2, runEvents.Count);
        Assert.Equal("blocked", runEvents[^1].Event);
        Assert.Equal("waiting on infra approval", runEvents[^1].Reason);
    }

    [Fact]
    public void Execute_GivenClarifyBlockedTransitionWithReason_UpdatesBlockedByAndAppendsReasonedRunLog()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """{"ts":"2026-04-03T10:00:00Z","execution_unit":"A1","event":"queued","by":"supervisor"}""" + Environment.NewLine);
        using var writer = new StringWriter();

        var exitCode = QueueTransitionCommand.Execute(
            CreateContext(repoRoot),
            ["A2", "clarify-blocked", "--reason", "need product clarification"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Transitioned A2 to clarify-blocked", writer.ToString(), StringComparison.Ordinal);

        var updatedState = QueueStateSerializer.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
        var selectedItem = updatedState.Items.Single(item => item.ExecutionUnit == "A2");
        Assert.Equal(QueueItemState.ClarifyBlocked, selectedItem.State);
        Assert.Equal(["need product clarification"], selectedItem.BlockedBy);

        var runEvents = RunLogSerializer.DeserializeAll(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
        Assert.Equal("clarify-requested", runEvents[^1].Event);
        Assert.Equal("need product clarification", runEvents[^1].Reason);
    }

    [Fact]
    public void Execute_GivenRetiredTarget_BackfillsRetiredStateWithoutHandEditingQueueState()
    {
        // G534 field finding: the SKS-G815 case — a packet retired outside
        // queue tracking (G525 lifecycle) needed to be backfilled into
        // queue-state.json, but `retired` was rejected as a transition
        // target, forcing the field workaround of hand-editing
        // queue-state.json directly (the exact mutation class this
        // tooling exists to prevent).
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = QueueTransitionCommand.Execute(CreateContext(repoRoot), ["A2", "retired"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Transitioned A2 to retired", writer.ToString(), StringComparison.Ordinal);

        var updatedState = QueueStateSerializer.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
        Assert.Equal(QueueItemState.Retired, updatedState.Items.Single(item => item.ExecutionUnit == "A2").State);

        var runEvents = RunLogSerializer.DeserializeAll(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
        Assert.Equal("retired", runEvents[^1].Event);
        Assert.Equal("A2", runEvents[^1].ExecutionUnit);
    }

    // ─── G534 review repair: guarded/idempotent/terminal `retired` ───────────

    [Fact]
    public void Execute_GivenRetiredTargetOnQueuedItem_Succeeds()
    {
        // Pins the "queued" legal source state alongside the existing
        // "review" legal source state (Execute_GivenRetiredTarget...above).
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(new QueueState
            {
                SchemaVersion = "1",
                UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
                Items = [CreateItem("A2", QueueItemState.Queued)]
            }));
        using var writer = new StringWriter();

        var exitCode = QueueTransitionCommand.Execute(CreateContext(repoRoot), ["A2", "retired"], writer);

        Assert.Equal(0, exitCode);
        var updatedState = QueueStateSerializer.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
        Assert.Equal(QueueItemState.Retired, updatedState.Items.Single(item => item.ExecutionUnit == "A2").State);
    }

    [Fact]
    public void Execute_GivenRetiredTargetOnCompletedItem_RefusesWithoutMutation()
    {
        // G534 review repair (blocker #1): a Completed item (merged/finished
        // work) must never be reclassified as retired — retirement only
        // applies to work that can never be completed as authored.
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        var queueStateContents = QueueStateSerializer.Serialize(new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items = [CreateItem("A2", QueueItemState.Completed)]
        });
        tempDirectory.CreateFile(queueStatePath, queueStateContents);
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        tempDirectory.CreateFile(runLogPath, string.Empty);
        using var writer = new StringWriter();

        var exitCode = QueueTransitionCommand.Execute(CreateContext(repoRoot), ["A2", "retired"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("completed", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(queueStateContents, File.ReadAllText(queueStatePath));
        Assert.Empty(File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenRetiredTargetOnCompletedItemWithMergedLinkedPr_RefusesWithoutMutation()
    {
        // G534 review repair: pins the "merged-linked" phrasing explicitly —
        // a Completed item carrying a linked (merged) PR refuses exactly
        // like any other Completed item.
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        var queueStateContents = QueueStateSerializer.Serialize(new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items = [CreateItem("A2", QueueItemState.Completed) with { LinkedPr = "https://github.com/org/repo/pull/42" }]
        });
        tempDirectory.CreateFile(queueStatePath, queueStateContents);
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        tempDirectory.CreateFile(runLogPath, string.Empty);
        using var writer = new StringWriter();

        var exitCode = QueueTransitionCommand.Execute(CreateContext(repoRoot), ["A2", "retired"], writer);

        Assert.Equal(1, exitCode);
        Assert.Equal(queueStateContents, File.ReadAllText(queueStatePath));
        Assert.Empty(File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenRetiredTargetOnAlreadyRetiredItem_IsIdempotentWithNoDuplicateEvent()
    {
        // G534 review repair: Retired -> Retired must be a safe no-op — no
        // state churn, and critically no duplicate run event on repeated
        // calls (a naive re-run must never grow runs.jsonl).
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        var queueStateContents = QueueStateSerializer.Serialize(new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items = [CreateItem("A2", QueueItemState.Retired)]
        });
        tempDirectory.CreateFile(queueStatePath, queueStateContents);
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        tempDirectory.CreateFile(runLogPath, string.Empty);
        using var writer = new StringWriter();

        var firstExitCode = QueueTransitionCommand.Execute(CreateContext(repoRoot), ["A2", "retired"], writer);
        var secondExitCode = QueueTransitionCommand.Execute(CreateContext(repoRoot), ["A2", "retired"], writer);

        Assert.Equal(0, firstExitCode);
        Assert.Equal(0, secondExitCode);
        Assert.Contains("already retired", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(queueStateContents, File.ReadAllText(queueStatePath));
        Assert.Empty(File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenReactivationAttemptOnRetiredItem_RefusesWithoutMutation()
    {
        // G534 review repair (blocker #1): Retired is terminal — a retired
        // item can never be transitioned back to queued/active/etc through
        // this surface.
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        var queueStateContents = QueueStateSerializer.Serialize(new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items = [CreateItem("A2", QueueItemState.Retired)]
        });
        tempDirectory.CreateFile(queueStatePath, queueStateContents);
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        tempDirectory.CreateFile(runLogPath, string.Empty);
        using var writer = new StringWriter();

        var exitCode = QueueTransitionCommand.Execute(CreateContext(repoRoot), ["A2", "active"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("terminal", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(queueStateContents, File.ReadAllText(queueStatePath));
        Assert.Empty(File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenMissingRunLog_CreatesRunLogFile()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = QueueTransitionCommand.Execute(CreateContext(repoRoot), ["A2", "active"], writer);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));

        var runEvents = RunLogSerializer.DeserializeAll(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
        Assert.Single(runEvents);
        Assert.Equal("activated", runEvents[0].Event);
    }

    [Fact]
    public void Execute_GivenInvalidState_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = QueueTransitionCommand.Execute(CreateContext(repoRoot), ["A2", "paused"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Supported states", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenBlockingStateWithoutReason_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        var queueStateContents = QueueStateSerializer.Serialize(CreateQueueState());
        tempDirectory.CreateFile(queueStatePath, queueStateContents);
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        tempDirectory.CreateFile(runLogPath, string.Empty);
        using var writer = new StringWriter();

        var exitCode = QueueTransitionCommand.Execute(CreateContext(repoRoot), ["A2", "blocked"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("require --reason", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(queueStateContents, File.ReadAllText(queueStatePath));
        Assert.Empty(File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenReasonForNonBlockingState_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        var queueStateContents = QueueStateSerializer.Serialize(CreateQueueState());
        tempDirectory.CreateFile(queueStatePath, queueStateContents);
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        tempDirectory.CreateFile(runLogPath, string.Empty);
        using var writer = new StringWriter();

        var exitCode = QueueTransitionCommand.Execute(
            CreateContext(repoRoot),
            ["A2", "completed", "--reason", "not allowed here"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("only supported for blocked and clarify-blocked", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(queueStateContents, File.ReadAllText(queueStatePath));
        Assert.Empty(File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenMalformedReasonFlag_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = QueueTransitionCommand.Execute(CreateContext(repoRoot), ["A2", "blocked", "--why", "missing"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("only supports optional '--reason <text>'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownExecutionUnit_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        var queueStateContents = QueueStateSerializer.Serialize(CreateQueueState());
        tempDirectory.CreateFile(queueStatePath, queueStateContents);
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        tempDirectory.CreateFile(
            runLogPath,
            """{"ts":"2026-04-03T10:00:00Z","execution_unit":"A1","event":"queued","by":"supervisor"}""" + Environment.NewLine);
        using var writer = new StringWriter();

        var exitCode = QueueTransitionCommand.Execute(CreateContext(repoRoot), ["MISSING", "active"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("not found in queue state", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(queueStateContents, File.ReadAllText(queueStatePath));
        Assert.Single(RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath)));
    }

    [Fact]
    public void Execute_GivenMissingArguments_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = QueueTransitionCommand.Execute(CreateContext("/tmp/intent-system"), ["A2"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires an execution unit and target state", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static CliContext CreateContext(string repoRoot)
    {
        return new CliContext
        {
            RepoRoot = repoRoot,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-system",
                    ArtifactRoot = ".intent-cli"
                }
            }
        };
    }

    private static QueueState CreateQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                CreateItem("A2", QueueItemState.Review),
                CreateItem("B1", QueueItemState.Blocked) with
                {
                    Dependencies = ["A2"],
                    BlockedBy = ["A2"]
                }
            ]
        };
    }

    private static QueueItem CreateItem(string executionUnit, QueueItemState state)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = $"[{executionUnit}] Queue Item",
            State = state,
            Dependencies = [],
            BlockedBy = [],
            ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
            PacketPaths = new PacketPaths
            {
                Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
                Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml"
            },
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "normal"
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-queue-transition-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public string CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Temporary file path did not contain a directory.");

            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(fullPath, contents);
            return fullPath;
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
