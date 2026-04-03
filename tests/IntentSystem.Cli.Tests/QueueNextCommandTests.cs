using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class QueueNextCommandTests
{
    [Fact]
    public void Execute_GivenEligibleQueuedItem_WritesNextCandidateDetails()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = QueueNextCommand.Execute(CreateContext(repoRoot), [], writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Next candidate", output, StringComparison.Ordinal);
        Assert.Contains("Execution unit: B1", output, StringComparison.Ordinal);
        Assert.Contains("Priority: high", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Execution unit: C1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenBlockedAndUnresolvedQueuedItems_SkipsIneligibleCandidates()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueStateWithIneligibleItemsAhead()));
        using var writer = new StringWriter();

        var exitCode = QueueNextCommand.Execute(CreateContext(repoRoot), [], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Execution unit: D1", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenNoEligibleQueuedItems_WritesNoEligibleMessage()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueStateWithoutEligibleItem()));
        using var writer = new StringWriter();

        var exitCode = QueueNextCommand.Execute(CreateContext(repoRoot), [], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("No eligible queued item found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnexpectedArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = QueueNextCommand.Execute(CreateContext("/tmp/intent-system"), ["A1"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("does not accept arguments", writer.ToString(), StringComparison.OrdinalIgnoreCase);
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
                    WorkflowEngine = "intent-cli",
                    ArtifactRoot = ".intent-cli"
                }
            }
        };
    }

    private static QueueState CreateQueueState()
    {
        return CreateState(
        [
            CreateItem("A1", QueueItemState.Completed),
            CreateItem("B1", QueueItemState.Queued) with
            {
                Dependencies = ["A1"],
                Priority = "high"
            },
            CreateItem("C1", QueueItemState.Queued) with
            {
                Priority = "low"
            }
        ]);
    }

    private static QueueState CreateQueueStateWithIneligibleItemsAhead()
    {
        return CreateState(
        [
            CreateItem("A1", QueueItemState.Active),
            CreateItem("B1", QueueItemState.Queued) with
            {
                Dependencies = ["A1"],
                Priority = "high"
            },
            CreateItem("C1", QueueItemState.Queued) with
            {
                BlockedBy = ["manual-hold"],
                Priority = "high"
            },
            CreateItem("D1", QueueItemState.Queued) with
            {
                Priority = "normal"
            }
        ]);
    }

    private static QueueState CreateQueueStateWithoutEligibleItem()
    {
        return CreateState(
        [
            CreateItem("A1", QueueItemState.Active),
            CreateItem("B1", QueueItemState.Queued) with
            {
                Dependencies = ["A1"]
            },
            CreateItem("C1", QueueItemState.Blocked) with
            {
                Dependencies = ["A1"],
                BlockedBy = ["A1"]
            }
        ]);
    }

    private static QueueState CreateState(QueueItem[] items)
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items = items
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-queue-next-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public void CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Temporary file path did not contain a directory.");

            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(fullPath, contents);
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
