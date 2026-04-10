using IntentSystem.Clarify.Models;
using IntentSystem.Clarify.Serialization;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class ClarifyListCommandTests
{
    [Fact]
    public void Execute_GivenOpenClarificationArtifacts_RendersOpenItemsWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """{"ts":"2026-04-12T06:00:00Z","execution_unit":"G22","event":"clarify-requested","by":"intent-cli","reason":"need clarification"}""" + Environment.NewLine);
        var openArtifactPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "clarifications", "G22", "request.json"),
            ClarificationSerializer.Serialize(CreateClarification("G22", ClarificationStatus.Open)));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "clarifications", "G23", "request.json"),
            ClarificationSerializer.Serialize(CreateClarification("G23", ClarificationStatus.Answered) with
            {
                Answer = "resolved",
                AnsweredAt = DateTimeOffset.Parse("2026-04-12T06:05:00Z")
            }));
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);
        var originalArtifact = File.ReadAllText(openArtifactPath);

        var exitCode = ClarifyListCommand.Execute(CreateContext(repoRoot), [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Open clarifications:", output, StringComparison.Ordinal);
        Assert.Contains("Execution unit: G22", output, StringComparison.Ordinal);
        Assert.Contains("Status: Open", output, StringComparison.Ordinal);
        Assert.Contains("Question: Clarify blocker for cli clarify open command", output, StringComparison.Ordinal);
        Assert.Contains("Reason: Clarification requested for [G22] Clarify Open Command", output, StringComparison.Ordinal);
        Assert.Contains("Return path: intents/intent-cli/clarifications/open.md", output, StringComparison.Ordinal);
        Assert.Contains("Queue title: [G22] Clarify Open Command", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Execution unit: G23", output, StringComparison.Ordinal);

        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
        Assert.Equal(originalArtifact, File.ReadAllText(openArtifactPath));
    }

    [Fact]
    public void Execute_GivenNoClarificationsDirectory_WritesNoOpenClarificationsMessage()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = ClarifyListCommand.Execute(CreateContext(repoRoot), [], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("No open clarifications found.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenOnlyClosedClarifications_WritesNoOpenClarificationsMessage()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "clarifications", "G23", "request.json"),
            ClarificationSerializer.Serialize(CreateClarification("G23", ClarificationStatus.Applied) with
            {
                Answer = "resolved",
                AnsweredAt = DateTimeOffset.Parse("2026-04-12T06:05:00Z")
            }));
        using var writer = new StringWriter();

        var exitCode = ClarifyListCommand.Execute(CreateContext(repoRoot), [], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("No open clarifications found.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenArguments_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = ClarifyListCommand.Execute(CreateContext("/tmp/intent-system"), ["G22"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("does not accept arguments", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingQueueState_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = ClarifyListCommand.Execute(CreateContext(repoRoot), [], writer);

        Assert.Equal(1, exitCode);
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
            UpdatedAt = DateTimeOffset.Parse("2026-04-12T06:00:00Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G22",
                    Title = "[G22] Clarify Open Command",
                    State = QueueItemState.ClarifyBlocked,
                    Dependencies = [],
                    BlockedBy = ["need clarification"],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G22/implementation.md",
                        ReviewContext = ".intent-cli/issues/G22/review-context.md",
                        Yaml = ".intent-cli/issues/G22/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                },
                new QueueItem
                {
                    ExecutionUnit = "G23",
                    Title = "[G23] Clarify List Command",
                    State = QueueItemState.Review,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G23/implementation.md",
                        ReviewContext = ".intent-cli/issues/G23/review-context.md",
                        Yaml = ".intent-cli/issues/G23/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static ClarificationItem CreateClarification(string executionUnit, ClarificationStatus status)
    {
        return new ClarificationItem
        {
            ClarificationSource = "execution",
            QuestionId = "request",
            ExecutionUnit = executionUnit,
            QuestionText = $"Clarify blocker for cli clarify open command: review check for {executionUnit}",
            Reason = $"Clarification requested for [${executionUnit}] Clarify Open Command".Replace("$", string.Empty),
            AffectedIntents = ["ICL.P.PRODUCT_GOAL"],
            AffectedExecutionUnits = [executionUnit],
            BlockingOrNonblocking = "blocking",
            ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
            Status = status,
            CreatedAt = DateTimeOffset.Parse("2026-04-12T06:00:00Z"),
            Answer = status == ClarificationStatus.Open ? null : "resolved",
            AnsweredAt = status == ClarificationStatus.Open ? null : DateTimeOffset.Parse("2026-04-12T06:05:00Z")
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-clarify-list-tests-").FullName;

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
