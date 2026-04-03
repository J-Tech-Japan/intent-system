using IntentSystem.Clarify.Models;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.WorkerAdapter.Models;
using IntentSystem.WorkerAdapter.Serialization;
using IntentSystem.Workflow.Models;
using IntentSystem.Workflow.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class WorkflowStatusCommandTests
{
    [Fact]
    public void Execute_GivenWorkflowDefinitionAndRunArtifact_WritesHumanFacingStatus()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "workflows", "C2.yaml"),
            WorkflowDefinitionSerializer.Serialize(CreateWorkflowDefinition("C2")));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "workflows", "C2.run.json"),
            WorkerAdapterSerializer.SerializeResult(CreateRunArtifact()));
        using var writer = new StringWriter();

        var exitCode = WorkflowStatusCommand.Execute(CreateContext(repoRoot), ["C2"], writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Execution unit: C2", output, StringComparison.Ordinal);
        Assert.Contains("Run status: ClarificationRequested", output, StringComparison.Ordinal);
        Assert.Contains("Result summary: Reviewer requested clarification on the backend contract.", output, StringComparison.Ordinal);
        Assert.Contains("Review disposition: ClarificationRequested", output, StringComparison.Ordinal);
        Assert.Contains("Reviewed by: deterministic-reviewer", output, StringComparison.Ordinal);
        Assert.Contains("Review comment refs: https://github.com/J-Tech-Japan/intent-system/pull/40#discussion_r1", output, StringComparison.Ordinal);
        Assert.Contains("Run log refs: .intent-cli/workflows/C2.run.json, .intent-cli/logs/C2-run.log", output, StringComparison.Ordinal);
        Assert.Contains("- Implement | role=coder | status=Completed | detail=Implemented workflow status command", output, StringComparison.Ordinal);
        Assert.Contains("- Review | role=reviewer | status=Completed | detail=Clarification required before approval", output, StringComparison.Ordinal);
        Assert.Contains("- Clarify | role=reviewer | status=Running | detail=Waiting for answer on backend ownership", output, StringComparison.Ordinal);
        Assert.Contains("- Q-1 | execution_unit=C2 | status=Open | blocking=blocking | return=intents/intent-cli/clarifications/open.md | reason=Need backend owner confirmation", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingExecutionUnit_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = WorkflowStatusCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires an execution unit", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenMissingWorkflowDefinition_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "workflows", "C2.run.json"),
            WorkerAdapterSerializer.SerializeResult(CreateRunArtifact()));
        using var writer = new StringWriter();

        var exitCode = WorkflowStatusCommand.Execute(CreateContext(repoRoot), ["C2"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Workflow definition artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingRunArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "workflows", "C2.yaml"),
            WorkflowDefinitionSerializer.Serialize(CreateWorkflowDefinition("C2")));
        using var writer = new StringWriter();

        var exitCode = WorkflowStatusCommand.Execute(CreateContext(repoRoot), ["C2"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Workflow run artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMismatchedWorkflowExecutionUnit_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "workflows", "C2.yaml"),
            WorkflowDefinitionSerializer.Serialize(CreateWorkflowDefinition("C3")));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "workflows", "C2.run.json"),
            WorkerAdapterSerializer.SerializeResult(CreateRunArtifact()));
        using var writer = new StringWriter();

        var exitCode = WorkflowStatusCommand.Execute(CreateContext(repoRoot), ["C2"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must match requested execution unit", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenRunArtifactMissingStepStatus_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "workflows", "C2.yaml"),
            WorkflowDefinitionSerializer.Serialize(CreateWorkflowDefinition("C2")));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "workflows", "C2.run.json"),
            WorkerAdapterSerializer.SerializeResult(CreateRunArtifactWithoutClarifyStep()));
        using var writer = new StringWriter();

        var exitCode = WorkflowStatusCommand.Execute(CreateContext(repoRoot), ["C2"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("did not contain status for step 'Clarify'", writer.ToString(), StringComparison.Ordinal);
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

    private static WorkflowDefinition CreateWorkflowDefinition(string executionUnit)
    {
        return new WorkflowDefinition
        {
            ExecutionUnit = executionUnit,
            PacketPaths = new WorkflowPacketPaths
            {
                Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
                Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml"
            },
            WorkerRoles = new WorkerRoles
            {
                Worker = "coder",
                Reviewer = "reviewer"
            },
            DependencySnapshot = ["A1"],
            EntryConditions = ["A1 completed"],
            Steps =
            [
                new WorkflowStep
                {
                    Kind = WorkflowStepKind.Implement,
                    Role = "coder",
                    OnSuccess = [WorkflowStepKind.Review],
                    OnFailure = []
                },
                new WorkflowStep
                {
                    Kind = WorkflowStepKind.Review,
                    Role = "reviewer",
                    OnSuccess = [WorkflowStepKind.Complete],
                    OnFailure = [WorkflowStepKind.Clarify]
                },
                new WorkflowStep
                {
                    Kind = WorkflowStepKind.Clarify,
                    Role = "reviewer",
                    OnSuccess = [WorkflowStepKind.Review],
                    OnFailure = []
                }
            ],
            SuccessSignal = "workflow status renders workflow state",
            ReviewMode = "deterministic-review",
            CompletionAction = "wait-for-deterministic-review"
        };
    }

    private static WorkerAdapterResult CreateRunArtifact()
    {
        return new WorkerAdapterResult
        {
            RunStatus = WorkerAdapterRunStatus.ClarificationRequested,
            StepStatuses =
            [
                new WorkerAdapterStepStatus
                {
                    Step = WorkflowStepKind.Implement,
                    Status = WorkerAdapterStepState.Completed,
                    Detail = "Implemented workflow status command"
                },
                new WorkerAdapterStepStatus
                {
                    Step = WorkflowStepKind.Review,
                    Status = WorkerAdapterStepState.Completed,
                    Detail = "Clarification required before approval"
                },
                new WorkerAdapterStepStatus
                {
                    Step = WorkflowStepKind.Clarify,
                    Status = WorkerAdapterStepState.Running,
                    Detail = "Waiting for answer on backend ownership"
                }
            ],
            ReviewResult = new WorkerReviewResult
            {
                Disposition = WorkerReviewDisposition.ClarificationRequested,
                ReviewedBy = "deterministic-reviewer"
            },
            ReviewCommentRefs =
            [
                "https://github.com/J-Tech-Japan/intent-system/pull/40#discussion_r1"
            ],
            ClarificationRequests =
            [
                new ClarificationItem
                {
                    ClarificationSource = "review",
                    QuestionId = "Q-1",
                    ExecutionUnit = "C2",
                    QuestionText = "Who owns the backend contract?",
                    Reason = "Need backend owner confirmation",
                    AffectedIntents = ["ICL.E.SLICES"],
                    AffectedExecutionUnits = ["C2"],
                    BlockingOrNonblocking = "blocking",
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    Status = ClarificationStatus.Open,
                    CreatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z")
                }
            ],
            ResultSummary = "Reviewer requested clarification on the backend contract.",
            RunLogRefs =
            [
                ".intent-cli/workflows/C2.run.json",
                ".intent-cli/logs/C2-run.log"
            ]
        };
    }

    private static WorkerAdapterResult CreateRunArtifactWithoutClarifyStep()
    {
        var result = CreateRunArtifact();
        return result with
        {
            StepStatuses = result.StepStatuses
                .Where(status => status.Step != WorkflowStepKind.Clarify)
                .ToArray()
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-workflow-status-tests-").FullName;

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
