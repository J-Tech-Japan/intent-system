using IntentSystem.Clarify.Models;
using IntentSystem.Clarify.Serialization;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class ClarifyAnswerCommandTests
{
    [Fact]
    public void Execute_GivenInteractiveAnswer_AppliesClarificationResumesToReviewAndAppendsRunLog()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(blocking: true)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """{"ts":"2026-04-12T06:00:00Z","execution_unit":"G24","event":"clarify-requested","by":"intent-cli","reason":"need clarification"}""" + Environment.NewLine);
        var artifactPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "clarifications", "G24", "request.json"),
            ClarificationSerializer.Serialize(CreateOpenClarification("G24", blocking: true)));
        using var writer = new StringWriter();
        var originalTimestampFactory = ClarifyAnswerCommand.TimestampFactory;
        var originalInputReaderFactory = ClarifyAnswerCommand.InputReaderFactory;

        try
        {
            ClarifyAnswerCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-12T06:10:00Z");
            ClarifyAnswerCommand.InputReaderFactory = () => new StringReader("Use queue snapshot as the canonical source." + Environment.NewLine);

            var exitCode = ClarifyAnswerCommand.Execute(CreateContext(repoRoot), ["G24"], writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Clarification answered for G24.", output, StringComparison.Ordinal);
            Assert.Contains("Artifact status: applied", output, StringComparison.Ordinal);
            Assert.Contains("Queue state: review", output, StringComparison.Ordinal);
            Assert.Contains(artifactPath, output, StringComparison.Ordinal);

            var updatedArtifact = ClarificationSerializer.Deserialize(File.ReadAllText(artifactPath));
            Assert.Equal(ClarificationStatus.Applied, updatedArtifact.Status);
            Assert.Equal("Use queue snapshot as the canonical source.", updatedArtifact.Answer);
            Assert.Equal(DateTimeOffset.Parse("2026-04-12T06:10:00Z"), updatedArtifact.AnsweredAt);

            var updatedQueueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            Assert.Equal(QueueItemState.Review, updatedQueueState.Items.Single(item => item.ExecutionUnit == "G24").State);
            Assert.Equal(QueueItemState.Blocked, updatedQueueState.Items.Single(item => item.ExecutionUnit == "G25").State);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal(3, runEvents.Count);
            Assert.Equal("clarify-applied", runEvents[^2].Event);
            Assert.Equal("Applied clarification request", runEvents[^2].Reason);
            Assert.Equal("clarify-resumed", runEvents[^1].Event);
            Assert.Equal("Resumed to review", runEvents[^1].Reason);
        }
        finally
        {
            ClarifyAnswerCommand.TimestampFactory = originalTimestampFactory;
            ClarifyAnswerCommand.InputReaderFactory = originalInputReaderFactory;
        }
    }

    [Fact]
    public void Execute_GivenAnswerFile_AppliesClarificationAndResumesToQueuedForNonblockingItem()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(blocking: false)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        var artifactPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "clarifications", "G24", "request.json"),
            ClarificationSerializer.Serialize(CreateOpenClarification("G24", blocking: false)));
        tempDirectory.CreateFile(
            Path.Combine("repo", "answers", "g24.txt"),
            "Clarification noted.\nUse the existing field.");
        using var writer = new StringWriter();
        var originalTimestampFactory = ClarifyAnswerCommand.TimestampFactory;

        try
        {
            ClarifyAnswerCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-12T07:00:00Z");

            var exitCode = ClarifyAnswerCommand.Execute(
                CreateContext(repoRoot),
                ["G24", "--from-file", "answers/g24.txt"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Queue state: queued", writer.ToString(), StringComparison.Ordinal);

            var updatedArtifact = ClarificationSerializer.Deserialize(File.ReadAllText(artifactPath));
            Assert.Equal(ClarificationStatus.Applied, updatedArtifact.Status);
            Assert.Equal("Clarification noted.\nUse the existing field.", updatedArtifact.Answer);

            var updatedQueueState = QueueStateSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
            Assert.Equal(QueueItemState.Queued, updatedQueueState.Items.Single(item => item.ExecutionUnit == "G24").State);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal(["clarify-applied", "clarify-resumed"], runEvents.Select(runEvent => runEvent.Event));
            Assert.Equal("Resumed to queued", runEvents[^1].Reason);
        }
        finally
        {
            ClarifyAnswerCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenMissingArtifact_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(blocking: true)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);
        var exitCode = ClarifyAnswerCommand.Execute(CreateContext(repoRoot), ["G24"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Clarification artifact was not found", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenAppliedArtifact_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(blocking: true)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        var artifactPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "clarifications", "G24", "request.json"),
            ClarificationSerializer.Serialize(CreateAppliedClarification("G24")));
        using var writer = new StringWriter();
        var originalInputReaderFactory = ClarifyAnswerCommand.InputReaderFactory;

        try
        {
            ClarifyAnswerCommand.InputReaderFactory = () => new StringReader("Another answer" + Environment.NewLine);
            var originalQueueState = File.ReadAllText(queueStatePath);
            var originalRunLog = File.ReadAllText(runLogPath);
            var originalArtifact = File.ReadAllText(artifactPath);

            var exitCode = ClarifyAnswerCommand.Execute(CreateContext(repoRoot), ["G24"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("must be open before answering", writer.ToString(), StringComparison.Ordinal);
            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
            Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
            Assert.Equal(originalArtifact, File.ReadAllText(artifactPath));
        }
        finally
        {
            ClarifyAnswerCommand.InputReaderFactory = originalInputReaderFactory;
        }
    }

    [Fact]
    public void Execute_GivenEmptyInteractiveAnswer_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(blocking: true)));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "clarifications", "G24", "request.json"),
            ClarificationSerializer.Serialize(CreateOpenClarification("G24", blocking: true)));
        using var writer = new StringWriter();
        var originalInputReaderFactory = ClarifyAnswerCommand.InputReaderFactory;

        try
        {
            ClarifyAnswerCommand.InputReaderFactory = () => new StringReader(Environment.NewLine);

            var exitCode = ClarifyAnswerCommand.Execute(CreateContext(repoRoot), ["G24"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("Clarification answer must not be empty.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            ClarifyAnswerCommand.InputReaderFactory = originalInputReaderFactory;
        }
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

    private static QueueState CreateQueueState(bool blocking)
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-12T05:55:00Z"),
            Items =
            [
                CreateItem("G24"),
                CreateDependentItem(blocking)
            ]
        };
    }

    private static QueueItem CreateItem(string executionUnit)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = $"[{executionUnit}] Clarify Answer Command",
            State = QueueItemState.ClarifyBlocked,
            Dependencies = [],
            BlockedBy = ["need clarification"],
            ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
            PacketPaths = new PacketPaths
            {
                Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
                Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml"
            },
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "high"
        };
    }

    private static QueueItem CreateDependentItem(bool blocking)
    {
        return new QueueItem
        {
            ExecutionUnit = "G25",
            Title = "[G25] Dependent Item",
            State = blocking ? QueueItemState.Blocked : QueueItemState.Active,
            Dependencies = ["G24"],
            BlockedBy = blocking ? ["G24"] : [],
            ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
            PacketPaths = new PacketPaths
            {
                Implementation = ".intent-cli/issues/G25/implementation.md",
                ReviewContext = ".intent-cli/issues/G25/review-context.md",
                Yaml = ".intent-cli/issues/G25/packet.yaml"
            },
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "medium"
        };
    }

    private static ClarificationItem CreateOpenClarification(string executionUnit, bool blocking)
    {
        return new ClarificationItem
        {
            ClarificationSource = "execution",
            QuestionId = "request",
            ExecutionUnit = executionUnit,
            QuestionText = "Which field should remain canonical?",
            Reason = "Clarification requested for [G24] Clarify Answer Command: Resolve the queue blocker.",
            AffectedIntents = ["ICL.P.PRODUCT_GOAL"],
            AffectedExecutionUnits = [executionUnit],
            BlockingOrNonblocking = blocking ? "blocking" : "nonblocking",
            ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
            Status = ClarificationStatus.Open,
            CreatedAt = DateTimeOffset.Parse("2026-04-12T05:50:00Z"),
            Answer = null
        };
    }

    private static ClarificationItem CreateAppliedClarification(string executionUnit)
    {
        return CreateOpenClarification(executionUnit, blocking: true) with
        {
            Status = ClarificationStatus.Applied,
            Answer = "Already applied.",
            AnsweredAt = DateTimeOffset.Parse("2026-04-12T06:00:00Z")
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-clarify-answer-tests-").FullName;

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
