using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;
using IntentSystem.Workflow.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class WorkflowRenderCommandTests
{
    [Fact]
    public void Execute_GivenQueueItemAndPacketYaml_WritesWorkflowArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "C2", "packet.yaml"),
            CreatePacketYaml("C2"));
        using var writer = new StringWriter();

        var exitCode = WorkflowRenderCommand.Execute(CreateContext(repoRoot), ["C2"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Workflow definition rendered for C2", writer.ToString(), StringComparison.Ordinal);

        var workflowPath = Path.Combine(repoRoot, ".intent-cli", "workflows", "C2.yaml");
        Assert.True(File.Exists(workflowPath));

        var definition = WorkflowDefinitionSerializer.Deserialize(File.ReadAllText(workflowPath));
        Assert.Equal("C2", definition.ExecutionUnit);
        Assert.Equal(".intent-cli/issues/C2/packet.yaml", definition.PacketPaths.Yaml);
        Assert.Equal("coder", definition.WorkerRoles.Worker);
        Assert.Equal(["A1"], definition.DependencySnapshot);
        Assert.Equal(["A1 completed"], definition.EntryConditions);
        Assert.Equal("workflow render writes workflow artifact", definition.SuccessSignal);
        Assert.Equal("deterministic-review", definition.ReviewMode);
        Assert.Equal("wait-for-deterministic-review", definition.CompletionAction);
    }

    [Fact]
    public void Execute_GivenMissingExecutionUnit_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = WorkflowRenderCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires an execution unit", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenMissingQueueState_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = WorkflowRenderCommand.Execute(CreateContext(repoRoot), ["C2"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("No queue state found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingPacketYaml_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = WorkflowRenderCommand.Execute(CreateContext(repoRoot), ["C2"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("packet YAML was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMismatchedPacketExecutionUnit_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "C2", "packet.yaml"),
            CreatePacketYaml("C3"));
        using var writer = new StringWriter();

        var exitCode = WorkflowRenderCommand.Execute(CreateContext(repoRoot), ["C2"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must match packet execution unit", writer.ToString(), StringComparison.Ordinal);
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
                    Title = "Workflow render command",
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

    private static string CreatePacketYaml(string executionUnit)
    {
        return $$"""
        implementation_issue_packet:
          issue_title: "[{{executionUnit}}] Workflow Render Command"
          issue_kind: "feature"
          source_execution_unit: "{{executionUnit}}"
          goal: "Render workflow definition artifact from queue and packet sources."
          in_scope:
            - "cli workflow render command"
          out_of_scope:
            - "workflow execution"
          target_repo: "J-Tech-Japan/intent-system"
          target_path: "."
          target_part: "cli workflow render command"
          dependencies:
            - "G1"
            - "B2"
            - "C1"
            - "C2"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "C1 and C2 are fixed baselines"
          intent_references:
            - "ICL.E.SLICES"
          rules_and_specs:
            - "intents/intent-cli/specs/07-workflow-definition-and-takt-adapter.md"
          acceptance_criteria:
            - "workflow render writes workflow artifact"
            - "generated workflow artifact follows current definition contract"
          verification_evidence:
            - "contract-reviewed"
            - "tests-passing"
            - "acceptance-criteria-checked"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"
        
        review_context_packet:
          source_execution_unit: "{{executionUnit}}"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.E.SLICES"
          rules_and_specs:
            - "intents/intent-cli/specs/07-workflow-definition-and-takt-adapter.md"
          acceptance_criteria:
            - "workflow render writes workflow artifact"
          deterministic_review_checks:
            - "definition shape stays canonical"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-workflow-render-tests-").FullName;

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
