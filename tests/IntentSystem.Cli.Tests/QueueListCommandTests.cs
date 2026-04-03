using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class QueueListCommandTests
{
    [Fact]
    public void Execute_GivenQueueStateFile_WritesExecutionUnitsAndState()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));

        var context = CreateContext(repoRoot);
        using var writer = new StringWriter();

        var exitCode = QueueListCommand.Execute(context, [], writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("A2", output, StringComparison.Ordinal);
        Assert.Contains("CLI shell baseline", output, StringComparison.Ordinal);
        Assert.Contains("Review", output, StringComparison.Ordinal);
        Assert.Contains("high", output, StringComparison.Ordinal);
        Assert.Contains("A1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingQueueStateFile_WritesNoQueueStateMessage()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli"));
        var context = CreateContext(repoRoot);
        using var writer = new StringWriter();

        var exitCode = QueueListCommand.Execute(context, [], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("No queue state found", writer.ToString(), StringComparison.Ordinal);
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
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "A2",
                    Title = "CLI shell baseline",
                    State = QueueItemState.Review,
                    Dependencies = ["A1"],
                    BlockedBy = [],
                    ClarificationReturnPath = ".takt/runs/20260403-101234-issue-29-g1-cli-shell-and-root/context/task/order.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/a2/implementation.md",
                        ReviewContext = ".intent-cli/issues/a2/review-context.md",
                        Yaml = ".intent-cli/issues/a2/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-tests-").FullName;

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
