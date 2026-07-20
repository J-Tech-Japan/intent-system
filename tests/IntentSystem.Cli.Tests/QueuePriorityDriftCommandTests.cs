using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G543: coverage for the read-only <c>intent-cli queue priority-drift</c>
/// report — item counts per <c>priority</c> value, flagging any value
/// outside the documented <c>high|normal|low</c> enum (e.g. the field-
/// observed <c>"medium"</c>), without ever mutating queue-state.json.
/// </summary>
public sealed class QueuePriorityDriftCommandTests : IDisposable
{
    public void Dispose()
    {
    }

    [Fact]
    public void Execute_MissingQueueState_ReturnsCleanEmptyReport()
    {
        using var workspace = new PriorityDriftWorkspace();

        using var writer = new StringWriter();
        var exitCode = QueuePriorityDriftCommand.Execute(workspace.Context, ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal(0, root.GetProperty("total_items").GetInt32());
        Assert.False(root.GetProperty("has_drift").GetBoolean());
        Assert.Empty(root.GetProperty("out_of_enum_values").EnumerateArray());

        // The three documented values are always listed, even at zero.
        var byPriority = root.GetProperty("by_priority").EnumerateArray().ToArray();
        Assert.Equal(3, byPriority.Length);
        Assert.Equal("high", byPriority[0].GetProperty("priority").GetString());
        Assert.Equal(0, byPriority[0].GetProperty("count").GetInt32());
        Assert.True(byPriority[0].GetProperty("documented").GetBoolean());
        Assert.Equal("normal", byPriority[1].GetProperty("priority").GetString());
        Assert.Equal("low", byPriority[2].GetProperty("priority").GetString());
    }

    [Fact]
    public void Execute_FieldObservedDistributionShape_ReportsCountsAndFlagsMediumAsDrift()
    {
        // G543 field observation, 2026-07-20 (host queue-state, 1467
        // items): high 1405, medium 59, normal 3. This fixture reproduces
        // the SHAPE (one legacy value dwarfing "normal", zero "low") with
        // smaller counts for test speed.
        using var workspace = new PriorityDriftWorkspace();
        workspace.WriteQueueState(BuildQueueState(
            ("G1", "high"), ("G2", "high"), ("G3", "high"), ("G4", "high"), ("G5", "high"),
            ("G6", "medium"), ("G7", "medium"),
            ("G8", "normal")));

        using var writer = new StringWriter();
        var exitCode = QueuePriorityDriftCommand.Execute(workspace.Context, ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal(8, root.GetProperty("total_items").GetInt32());
        Assert.True(root.GetProperty("has_drift").GetBoolean());
        Assert.Equal(new[] { "medium" }, root.GetProperty("out_of_enum_values").EnumerateArray().Select(e => e.GetString()));

        var byPriority = root.GetProperty("by_priority").EnumerateArray().ToArray();
        Assert.Equal(4, byPriority.Length); // high, normal, low (always) + medium (drift)
        Assert.Equal("high", byPriority[0].GetProperty("priority").GetString());
        Assert.Equal(5, byPriority[0].GetProperty("count").GetInt32());
        Assert.Equal("normal", byPriority[1].GetProperty("priority").GetString());
        Assert.Equal(1, byPriority[1].GetProperty("count").GetInt32());
        Assert.Equal("low", byPriority[2].GetProperty("priority").GetString());
        Assert.Equal(0, byPriority[2].GetProperty("count").GetInt32());
        Assert.Equal("medium", byPriority[3].GetProperty("priority").GetString());
        Assert.Equal(2, byPriority[3].GetProperty("count").GetInt32());
        Assert.False(byPriority[3].GetProperty("documented").GetBoolean());
    }

    [Fact]
    public void Execute_OnlyDocumentedValuesPresent_HasDriftFalse()
    {
        using var workspace = new PriorityDriftWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G1", "high"), ("G2", "normal"), ("G3", "low")));

        using var writer = new StringWriter();
        var exitCode = QueuePriorityDriftCommand.Execute(workspace.Context, ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("has_drift").GetBoolean());
        Assert.Empty(root.GetProperty("out_of_enum_values").EnumerateArray());
        Assert.Equal(3, root.GetProperty("by_priority").EnumerateArray().Count());
    }

    [Fact]
    public void Execute_MultipleOutOfEnumValues_OrderedByCountDescendingThenAlphabeticallyOnTies()
    {
        using var workspace = new PriorityDriftWorkspace();
        workspace.WriteQueueState(BuildQueueState(
            ("G1", "urgent"), ("G2", "urgent"),
            ("G3", "zebra"),
            ("G4", "alpha")));

        using var writer = new StringWriter();
        var exitCode = QueuePriorityDriftCommand.Execute(workspace.Context, ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var byPriority = document.RootElement.GetProperty("by_priority").EnumerateArray().ToArray();
        var outOfEnum = byPriority.Skip(3).ToArray(); // after high/normal/low
        Assert.Equal(3, outOfEnum.Length);
        // "urgent" (count 2) sorts first by count descending. "alpha" and
        // "zebra" tie at count 1 -- the deterministic tiebreak is
        // alphabetical (Ordinal), so "alpha" comes before "zebra".
        Assert.Equal("urgent", outOfEnum[0].GetProperty("priority").GetString());
        Assert.Equal("alpha", outOfEnum[1].GetProperty("priority").GetString());
        Assert.Equal("zebra", outOfEnum[2].GetProperty("priority").GetString());
    }

    [Fact]
    public void Execute_MarkdownFormat_ListsCountsAndFlagsDrift()
    {
        using var workspace = new PriorityDriftWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G1", "high"), ("G2", "medium")));

        using var writer = new StringWriter();
        var exitCode = QueuePriorityDriftCommand.Execute(workspace.Context, ["--format", "markdown"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("drift: yes", output, StringComparison.Ordinal);
        Assert.Contains("| medium | 1 | no |", output, StringComparison.Ordinal);
        Assert.Contains("Out-of-enum values present: medium.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverMutatesQueueStateFile()
    {
        using var workspace = new PriorityDriftWorkspace();
        var originalJson = BuildQueueState(("G1", "medium"));
        workspace.WriteQueueState(originalJson);
        var beforeBytes = File.ReadAllBytes(workspace.QueueStatePath);

        using var writer = new StringWriter();
        var exitCode = QueuePriorityDriftCommand.Execute(workspace.Context, ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        var afterBytes = File.ReadAllBytes(workspace.QueueStatePath);
        Assert.Equal(beforeBytes, afterBytes);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var workspace = new PriorityDriftWorkspace();
        using var writer = new StringWriter();
        var exitCode = QueuePriorityDriftCommand.Execute(workspace.Context, ["--help"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("queue priority-drift", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ReturnsUsageError()
    {
        using var workspace = new PriorityDriftWorkspace();
        using var writer = new StringWriter();
        var exitCode = QueuePriorityDriftCommand.Execute(workspace.Context, ["--surprise"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument '--surprise'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_InvalidFormatValue_ReturnsUsageError()
    {
        using var workspace = new PriorityDriftWorkspace();
        using var writer = new StringWriter();
        var exitCode = QueuePriorityDriftCommand.Execute(workspace.Context, ["--format", "xml"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    private static string BuildQueueState(params (string ExecutionUnit, string Priority)[] items)
    {
        var state = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero),
            Items = items.Select(item => new QueueItem
            {
                ExecutionUnit = item.ExecutionUnit,
                Title = $"{item.ExecutionUnit} title",
                State = QueueItemState.Queued,
                Dependencies = Array.Empty<string>(),
                BlockedBy = Array.Empty<string>(),
                ClarificationReturnPath = string.Empty,
                PacketPaths = new PacketPaths
                {
                    Yaml = $".intent-cli/issues/{item.ExecutionUnit}/packet.yaml",
                    Implementation = $".intent-cli/issues/{item.ExecutionUnit}/implementation.md",
                    ReviewContext = $".intent-cli/issues/{item.ExecutionUnit}/review-context.md",
                },
                WorkerRole = "Claude",
                ReviewRole = "Codex",
                Priority = item.Priority,
            }).ToArray(),
        };
        return QueueStateSerializer.Serialize(state);
    }

    private sealed class PriorityDriftWorkspace : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("queue-priority-drift-tests-").FullName;

        public PriorityDriftWorkspace()
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
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            };
        }

        public CliContext Context { get; }

        public string QueueStatePath => Context.GetQueueStatePath();

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
