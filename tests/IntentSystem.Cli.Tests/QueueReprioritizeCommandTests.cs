using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class QueueReprioritizeCommandTests : IDisposable
{
    public QueueReprioritizeCommandTests()
    {
        QueueReprioritizeCommand.UtcNowFactory = null;
    }

    public void Dispose()
    {
        QueueReprioritizeCommand.UtcNowFactory = null;
    }

    [Fact]
    public void Execute_DryRunDefault_ReportsWouldChangeWithoutMutating()
    {
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));
        var queueStateBefore = File.ReadAllText(workspace.QueueStatePath);

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "publish ahead of G530", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("dry-run", root.GetProperty("mode").GetString());
        Assert.Equal("normal", root.GetProperty("old_priority").GetString());
        Assert.Equal("high", root.GetProperty("requested_priority").GetString());
        Assert.True(root.GetProperty("changed").GetBoolean());

        // No mutation on dry-run.
        Assert.Equal(queueStateBefore, File.ReadAllText(workspace.QueueStatePath));
        Assert.False(File.Exists(workspace.RunsLogPath));
    }

    [Fact]
    public void Execute_Write_MutatesPriorityAndAppendsReasonedRunEvent()
    {
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));
        var changedAt = new DateTimeOffset(2026, 7, 19, 3, 0, 0, TimeSpan.Zero);
        QueueReprioritizeCommand.UtcNowFactory = () => changedAt;

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "publish ahead of G530", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("write", root.GetProperty("mode").GetString());
        Assert.True(root.GetProperty("changed").GetBoolean());

        var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("high", updatedState.Items.Single().Priority);
        Assert.Equal(changedAt, updatedState.UpdatedAt);

        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath));
        var runEvent = Assert.Single(events);
        Assert.Equal("priority-changed", runEvent.Event);
        Assert.Equal("G537", runEvent.ExecutionUnit);
        Assert.Equal("intent-cli", runEvent.By);
        Assert.Contains("normal", runEvent.Reason, StringComparison.Ordinal);
        Assert.Contains("high", runEvent.Reason, StringComparison.Ordinal);
        Assert.Contains("publish ahead of G530", runEvent.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RefusesOnNonQueuedState()
    {
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Active, "normal", linkedIssue: null)));

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "x", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.Contains("not queued", error, StringComparison.Ordinal);
        Assert.False(File.Exists(workspace.RunsLogPath));

        var stateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("normal", stateAfter.Items.Single().Priority);
    }

    [Fact]
    public void Execute_RefusesOnAlreadyPublishedUnit()
    {
        using var workspace = new ReprioritizeWorkspace();
        var linkedIssue = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 1176, Url = "https://github.com/J-Tech-Japan/intent-system/issues/1176" };
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue)));

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "x", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.Contains("already has a linked GitHub issue", error, StringComparison.Ordinal);

        var stateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("normal", stateAfter.Items.Single().Priority);
    }

    [Fact]
    public void Execute_RefusesOnUnknownExecutionUnit()
    {
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G999", "--priority", "high", "--reason", "x", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.Contains("no item with execution_unit", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RefusesWithoutReason()
    {
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--reason", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RefusesUnsupportedPriorityValue()
    {
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "urgent", "--reason", "x", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unsupported --priority value", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_SamePriorityRequested_IsIdempotentNoOp()
    {
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "high", linkedIssue: null)));

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "no-op", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.False(document.RootElement.GetProperty("changed").GetBoolean());
        Assert.False(File.Exists(workspace.RunsLogPath));
    }

    private static string BuildQueueState((string ExecutionUnit, QueueItemState State, string Priority, LinkedIssue? LinkedIssue) item)
    {
        var state = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.Zero),
            Items = new[]
            {
                new QueueItem
                {
                    ExecutionUnit = item.ExecutionUnit,
                    Title = $"{item.ExecutionUnit} title",
                    State = item.State,
                    Dependencies = Array.Empty<string>(),
                    BlockedBy = Array.Empty<string>(),
                    ClarificationReturnPath = string.Empty,
                    PacketPaths = new PacketPaths
                    {
                        Yaml = $".intent-cli/issues/{item.ExecutionUnit}/packet.yaml",
                        Implementation = $".intent-cli/issues/{item.ExecutionUnit}/implementation.md",
                        ReviewContext = $".intent-cli/issues/{item.ExecutionUnit}/review-context.md"
                    },
                    LinkedIssue = item.LinkedIssue,
                    WorkerRole = "Claude",
                    ReviewRole = "Codex",
                    Priority = item.Priority
                }
            }
        };
        return QueueStateSerializer.Serialize(state);
    }

    private sealed class ReprioritizeWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("queue-reprioritize-tests-")
            .FullName;

        public ReprioritizeWorkspace()
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

        public string QueueStatePath => Context.GetQueueStatePath();

        public string RunsLogPath => Context.GetRunLogPath();

        public void WriteQueueState(string json) => File.WriteAllText(QueueStatePath, json);

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
