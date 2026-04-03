using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;
using IntentSystem.WorkerAdapter.Serialization;
using IntentSystem.Workflow.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class WorkflowRunCommandTests
{
    [Fact]
    public void Execute_GivenQueueItemAndRenderedWorkflow_WritesRunArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "workflows", "C2.yaml"),
            WorkflowDefinitionSerializer.Serialize(CreateWorkflowDefinition("C2")));
        using var writer = new StringWriter();

        var exitCode = WorkflowRunCommand.Execute(CreateContext(repoRoot), ["C2"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Workflow run artifact generated for C2", writer.ToString(), StringComparison.Ordinal);

        var artifactPath = Path.Combine(repoRoot, ".intent-cli", "workflows", "C2.run.json");
        Assert.True(File.Exists(artifactPath));
        var result = WorkerAdapterSerializer.DeserializeResult(File.ReadAllText(artifactPath));
        Assert.Equal(WorkerAdapter.Models.WorkerAdapterRunStatus.Running, result.RunStatus);
        Assert.Equal(Workflow.Models.WorkflowStepKind.Implement, result.StepStatuses[0].Step);
        Assert.Equal(WorkerAdapter.Models.WorkerAdapterStepState.Running, result.StepStatuses[0].Status);
        Assert.Equal(WorkerAdapter.Models.WorkerReviewDisposition.Pending, result.ReviewResult.Disposition);
        Assert.Empty(result.ReviewCommentRefs);
        Assert.Empty(result.ClarificationRequests);
        Assert.Equal([".intent-cli/workflows/C2.run.json"], result.RunLogRefs);
    }

    [Fact]
    public void Execute_GivenMissingExecutionUnit_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = WorkflowRunCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires an execution unit", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenMissingWorkflowDefinition_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = WorkflowRunCommand.Execute(CreateContext(repoRoot), ["C2"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Workflow definition artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMismatchedWorkflowExecutionUnit_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "workflows", "C2.yaml"),
            WorkflowDefinitionSerializer.Serialize(CreateWorkflowDefinition("C3")));
        using var writer = new StringWriter();

        var exitCode = WorkflowRunCommand.Execute(CreateContext(repoRoot), ["C2"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must match queue item execution unit", writer.ToString(), StringComparison.Ordinal);
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
                    ExecutionUnit = "C2",
                    Title = "Workflow run command",
                    State = QueueItemState.Queued,
                    Dependencies = ["A1"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/C2/implementation.md",
                        ReviewContext = ".intent-cli/issues/C2/review-context.md",
                        Yaml = ".intent-cli/issues/C2/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static Workflow.Models.WorkflowDefinition CreateWorkflowDefinition(string executionUnit)
    {
        var roles = new Workflow.Models.WorkerRoles
        {
            Worker = "coder",
            Reviewer = "reviewer"
        };

        return new Workflow.Models.WorkflowDefinition
        {
            ExecutionUnit = executionUnit,
            PacketPaths = new Workflow.Models.WorkflowPacketPaths
            {
                Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
                Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml"
            },
            WorkerRoles = roles,
            DependencySnapshot = ["A1"],
            EntryConditions = ["A1 completed"],
            Steps = Workflow.MvpWorkflowTemplate.CreateSteps(roles),
            SuccessSignal = "workflow render writes workflow artifact",
            ReviewMode = "deterministic-review",
            CompletionAction = "wait-for-deterministic-review"
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-workflow-run-tests-").FullName;

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
