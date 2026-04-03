using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class CommandRouterTests
{
    [Fact]
    public void Execute_GivenNoArguments_WritesHelpIncludingAllCommandGroups()
    {
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(Array.Empty<string>(), CreateContext("/tmp/intent-system"), writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("project", output, StringComparison.Ordinal);
        Assert.Contains("projection", output, StringComparison.Ordinal);
        Assert.Contains("queue", output, StringComparison.Ordinal);
        Assert.Contains("run", output, StringComparison.Ordinal);
        Assert.Contains("review", output, StringComparison.Ordinal);
        Assert.Contains("interview", output, StringComparison.Ordinal);
        Assert.Contains("clarify", output, StringComparison.Ordinal);
        Assert.Contains("workflow", output, StringComparison.Ordinal);
        Assert.Contains("intake", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenKnownGroupAndUnknownSubcommand_WritesNotYetImplementedMessage()
    {
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["projection", "status"], CreateContext("/tmp/intent-system"), writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("not yet implemented", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenProjectStatusCommand_DispatchesToProjectStatusRenderer()
    {
        using var writer = new StringWriter();
        var context = CreateContext("/tmp/intent-system");

        var exitCode = CommandRouter.Execute(["project", "status"], context, writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("intent-cli", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenQueueListCommand_DispatchesToQueueRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["queue", "list"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("A2", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenQueueShowCommand_DispatchesToQueueShowRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["queue", "show", "A2"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Execution unit: A2", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenQueueNextCommand_DispatchesToQueueNextRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["queue", "next"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Next candidate", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenWorkflowRenderCommand_DispatchesToWorkflowRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateWorkflowQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "C2", "packet.yaml"),
            CreateWorkflowPacketYaml());
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["workflow", "render", "C2"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Workflow definition rendered for C2", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenQueueTransitionCommand_DispatchesToQueueTransitionRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["queue", "transition", "A2", "completed"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Transitioned A2 to completed", writer.ToString(), StringComparison.Ordinal);
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
                    Domain = "intent-cli",
                    WorkflowEngine = "takt",
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
                },
                new QueueItem
                {
                    ExecutionUnit = "A3",
                    Title = "Queue read commands",
                    State = QueueItemState.Queued,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = ".takt/runs/20260403-101234-issue-33-g3-queue-show-and-next/context/task/order.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/A3/implementation.md",
                        ReviewContext = ".intent-cli/issues/A3/review-context.md",
                        Yaml = ".intent-cli/issues/A3/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "normal"
                }
            ]
        };
    }

    private static QueueState CreateWorkflowQueueState()
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

    private static string CreateWorkflowPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[C2] Workflow Render Command"
          issue_kind: "feature"
          source_execution_unit: "C2"
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
          verification_evidence:
            - "contract-reviewed"
            - "tests-passing"
            - "acceptance-criteria-checked"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"
        
        review_context_packet:
          source_execution_unit: "C2"
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
