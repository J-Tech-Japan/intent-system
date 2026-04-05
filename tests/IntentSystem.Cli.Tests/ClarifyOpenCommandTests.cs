using IntentSystem.Clarify.Serialization;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class ClarifyOpenCommandTests
{
    [Fact]
    public void Execute_GivenQueueItemPacketAndReviewContext_TransitionsToClarifyBlockedWritesArtifactAndAppendsRunLog()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """{"ts":"2026-04-11T06:00:00Z","execution_unit":"G21","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/71"}""" + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G22", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G22", "review-context.md"),
            CreateReviewContextMarkdown());
        using var writer = new StringWriter();
        var originalTimestampFactory = ClarifyOpenCommand.TimestampFactory;

        try
        {
            ClarifyOpenCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-11T06:10:00Z");

            var exitCode = ClarifyOpenCommand.Execute(CreateContext(repoRoot), ["G22"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Clarification opened for G22", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Clarification return path: intents/intent-cli/clarifications/open.md", writer.ToString(), StringComparison.Ordinal);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = updatedState.Items.Single(item => item.ExecutionUnit == "G22");
            Assert.Equal(QueueItemState.ClarifyBlocked, selectedItem.State);
            Assert.Equal(
                ["Clarification requested for [G22] Clarify Open Command: Open a clarification request for the current queue loop."],
                selectedItem.BlockedBy);
            Assert.Equal(QueueItemState.Blocked, updatedState.Items.Single(item => item.ExecutionUnit == "G23").State);
            Assert.Equal(["G22"], updatedState.Items.Single(item => item.ExecutionUnit == "G23").BlockedBy);

            var artifactPath = Path.Combine(repoRoot, ".intent-cli", "clarifications", "G22", "request.json");
            Assert.True(File.Exists(artifactPath));
            var artifact = ClarificationSerializer.Deserialize(File.ReadAllText(artifactPath));
            Assert.Equal("execution", artifact.ClarificationSource);
            Assert.Equal("request", artifact.QuestionId);
            Assert.Equal("G22", artifact.ExecutionUnit);
            Assert.Equal("blocking", artifact.BlockingOrNonblocking);
            Assert.Equal("intents/intent-cli/clarifications/open.md", artifact.ClarificationReturnPath);
            Assert.Equal(["ICL.P.PRODUCT_GOAL"], artifact.AffectedIntents);
            Assert.Equal(["G22"], artifact.AffectedExecutionUnits);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal(2, runEvents.Count);
            Assert.Equal("clarify-requested", runEvents[^1].Event);
            Assert.Equal("intent-cli", runEvents[^1].By);
            Assert.Equal(
                "Clarification requested for [G22] Clarify Open Command: Open a clarification request for the current queue loop.",
                runEvents[^1].Reason);
        }
        finally
        {
            ClarifyOpenCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenMissingExecutionUnit_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = ClarifyOpenCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires an execution unit", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenMissingQueueItem_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = ClarifyOpenCommand.Execute(CreateContext(repoRoot), ["G99"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("was not found in queue state", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingPacketArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G22", "review-context.md"),
            CreateReviewContextMarkdown());
        using var writer = new StringWriter();

        var exitCode = ClarifyOpenCommand.Execute(CreateContext(repoRoot), ["G22"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Projection packet artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingReviewContextArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G22", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var exitCode = ClarifyOpenCommand.Execute(CreateContext(repoRoot), ["G22"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Review context artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenReviewContextPacketExecutionUnitMismatch_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G22", "packet.yaml"),
            CreatePacketYaml(packetExecutionUnit: "G99"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G22", "review-context.md"),
            CreateReviewContextMarkdown());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);
        var exitCode = ClarifyOpenCommand.Execute(CreateContext(repoRoot), ["G22"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Review context packet execution unit", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenClarificationReturnPathMismatch_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G22", "packet.yaml"),
            CreatePacketYaml(clarificationReturnPath: "intents/intent-cli/clarifications/other.md"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G22", "review-context.md"),
            CreateReviewContextMarkdown());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);
        var exitCode = ClarifyOpenCommand.Execute(CreateContext(repoRoot), ["G22"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("clarification return path", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenReviewContextMarkdownExecutionUnitMismatch_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G22", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G22", "review-context.md"),
            CreateReviewContextMarkdown(executionUnit: "G99"));
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);
        var exitCode = ClarifyOpenCommand.Execute(CreateContext(repoRoot), ["G22"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Review context execution unit", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
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
            UpdatedAt = DateTimeOffset.Parse("2026-04-11T06:05:00Z"),
            Items =
            [
                CreateItem("G22", QueueItemState.Review),
                CreateItem("G23", QueueItemState.Blocked) with
                {
                    Dependencies = ["G22"],
                    BlockedBy = ["G22"]
                }
            ]
        };
    }

    private static QueueItem CreateItem(string executionUnit, QueueItemState state)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = $"[{executionUnit}] Clarify Open Command",
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
            Priority = "high"
        };
    }

    private static string CreatePacketYaml(
        string packetExecutionUnit = "G22",
        string clarificationReturnPath = "intents/intent-cli/clarifications/open.md")
    {
        return $$"""
        implementation_issue_packet:
          issue_title: "[G22] Clarify Open Command"
          issue_kind: "feature"
          source_execution_unit: "{{packetExecutionUnit}}"
          goal: "Open a clarification request for the current queue loop."
          in_scope:
            - "clarify open command"
          out_of_scope:
            - "clarify answer"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli clarify open command"
          dependencies:
            - "G8"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "clarify open stays entry-only"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/06-interview-and-clarification-artifact-contract.md"
          acceptance_criteria:
            - "clarification artifact generated"
          verification_evidence:
            - "dotnet test IntentSystem.sln"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "{{packetExecutionUnit}}"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/06-interview-and-clarification-artifact-contract.md"
          acceptance_criteria:
            - "clarification artifact generated"
          deterministic_review_checks:
            - "clarify open command remains entry-only"
          clarification_return_path: "{{clarificationReturnPath}}"
        """;
    }

    private static string CreateReviewContextMarkdown(string executionUnit = "G22")
    {
        return $$"""
        # Execution Unit

        `{{executionUnit}}`

        # Acceptance Criteria

        - clarification artifact generated

        # Deterministic Review Checks

        - clarify open command remains entry-only
        """;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-clarify-open-tests-").FullName;

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
