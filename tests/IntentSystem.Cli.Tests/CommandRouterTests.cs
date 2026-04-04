using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Review;
using IntentSystem.Review.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;
using IntentSystem.WorkerAdapter.Serialization;

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
    public void Execute_GivenQueueDispatchCommand_DispatchesToQueueDispatchRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueDispatchQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            CreateQueueDispatchPacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "github-body.md"),
            "# Goal");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var originalPublisherFactory = QueueDispatchCommand.PublisherFactory;
        var originalGitFactory = QueueDispatchCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = QueueDispatchCommand.TimestampFactory;

        try
        {
            QueueDispatchCommand.PublisherFactory = () => new FakeQueueDispatchPublisher();
            QueueDispatchCommand.GitCommandRunnerFactory = () => new FakeQueueDispatchGitRunner();
            QueueDispatchCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T06:00:00Z");

            var exitCode = CommandRouter.Execute(["queue", "dispatch", "G13"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Queue item G13 dispatched", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            QueueDispatchCommand.PublisherFactory = originalPublisherFactory;
            QueueDispatchCommand.GitCommandRunnerFactory = originalGitFactory;
            QueueDispatchCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenRunStartCommand_DispatchesToRunStartRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateRunStartQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G14", "packet.yaml"),
            CreateRunStartPacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var originalGitFactory = RunStartCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = RunStartCommand.TimestampFactory;

        try
        {
            RunStartCommand.GitCommandRunnerFactory = () => new FakeRunStartGitRunner();
            RunStartCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T09:30:00Z");

            var exitCode = CommandRouter.Execute(["run", "start", "G14"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Run started for G14", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            RunStartCommand.GitCommandRunnerFactory = originalGitFactory;
            RunStartCommand.TimestampFactory = originalTimestampFactory;
        }
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
    public void Execute_GivenWorkflowRunCommand_DispatchesToWorkflowRunRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateWorkflowQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "workflows", "C2.yaml"),
            CreateWorkflowDefinitionJson());
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["workflow", "run", "C2"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Workflow run artifact generated for C2", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenWorkflowStatusCommand_DispatchesToWorkflowStatusRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "workflows", "C2.yaml"),
            CreateWorkflowDefinitionJson());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "workflows", "C2.run.json"),
            CreateWorkflowRunArtifactJson());
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["workflow", "status", "C2"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Run status: Running", writer.ToString(), StringComparison.Ordinal);
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

    [Fact]
    public void Execute_GivenReviewRunCommand_DispatchesToReviewRunRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateReviewQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G9", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateReviewRunLog());
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["review", "run", "G9"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Review request artifact generated for G9", writer.ToString(), StringComparison.Ordinal);

        var artifact = ReviewRequestSerializer.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "reviews", "G9.request.json")));
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/45", artifact.LinkedPr);
    }

    [Fact]
    public void Execute_GivenReviewCommentCommand_DispatchesToReviewCommentRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateReviewCommentQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G10.request.json"),
            CreateReviewCommentRequestJson());
        tempDirectory.CreateFile(
            Path.Combine("repo", "prepared-comment.md"),
            "repair in place");
        using var writer = new StringWriter();
        var originalFactory = ReviewCommentCommand.PublisherFactory;
        var originalTimestampFactory = ReviewCommentCommand.TimestampFactory;

        try
        {
            ReviewCommentCommand.PublisherFactory = () => new FakeReviewCommentPublisher();
            ReviewCommentCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-04T04:40:00Z");

            var exitCode = CommandRouter.Execute(
                ["review", "comment", "G10", "--from-file", "prepared-comment.md"],
                CreateContext(repoRoot),
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Review comment posted for G10", writer.ToString(), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "reviews", "G10.comment.json")));
        }
        finally
        {
            ReviewCommentCommand.PublisherFactory = originalFactory;
            ReviewCommentCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenReviewAcceptCommand_DispatchesToReviewAcceptRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "child-repo"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateReviewAcceptQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G12", "packet.yaml"),
            CreateReviewAcceptPacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateReviewAcceptRunLog());
        using var writer = new StringWriter();
        var originalClientFactory = ReviewAcceptCommand.AcceptClientFactory;
        var originalGitFactory = ReviewAcceptCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = ReviewAcceptCommand.TimestampFactory;

        try
        {
            ReviewAcceptCommand.AcceptClientFactory = () => new FakeReviewAcceptClient();
            ReviewAcceptCommand.GitCommandRunnerFactory = () => new FakeReviewAcceptGitRunner();
            ReviewAcceptCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T01:02:03Z");

            var exitCode = CommandRouter.Execute(["review", "accept", "G12"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Review accepted for G12", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            ReviewAcceptCommand.AcceptClientFactory = originalClientFactory;
            ReviewAcceptCommand.GitCommandRunnerFactory = originalGitFactory;
            ReviewAcceptCommand.TimestampFactory = originalTimestampFactory;
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

    private static QueueState CreateQueueDispatchQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G13",
                    Title = "Queue dispatch command",
                    State = QueueItemState.Queued,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G13/implementation.md",
                        ReviewContext = ".intent-cli/issues/G13/review-context.md",
                        Yaml = ".intent-cli/issues/G13/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateReviewQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G9",
                    Title = "Review run command",
                    State = QueueItemState.Review,
                    Dependencies = ["G7"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G9/implementation.md",
                        ReviewContext = ".intent-cli/issues/G9/review-context.md",
                        Yaml = ".intent-cli/issues/G9/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateRunStartQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G14",
                    Title = "Run start command",
                    State = QueueItemState.Queued,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G14/implementation.md",
                        ReviewContext = ".intent-cli/issues/G14/review-context.md",
                        Yaml = ".intent-cli/issues/G14/packet.yaml"
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = 56,
                        Url = "https://github.com/J-Tech-Japan/intent-system/issues/56"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateReviewCommentQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G10",
                    Title = "Review comment command",
                    State = QueueItemState.Review,
                    Dependencies = ["G9"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G10/implementation.md",
                        ReviewContext = ".intent-cli/issues/G10/review-context.md",
                        Yaml = ".intent-cli/issues/G10/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateReviewAcceptQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G12",
                    Title = "Review accept command",
                    State = QueueItemState.Review,
                    Dependencies = ["G10"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G12/implementation.md",
                        ReviewContext = ".intent-cli/issues/G12/review-context.md",
                        Yaml = ".intent-cli/issues/G12/packet.yaml"
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
          target_repo: "submodules/intent-system"
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

    private static string CreateQueueDispatchPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G13] Queue Dispatch Command"
          issue_kind: "feature"
          source_execution_unit: "G13"
          goal: "Dispatch queue item into GitHub issue."
          in_scope:
            - "queue dispatch command"
          out_of_scope:
            - "branch creation"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli queue dispatch command"
          dependencies:
            - "G3"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "dispatch stays thin"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/issue-lifecycle-and-landing.md"
          acceptance_criteria:
            - "issue created"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"
        
        review_context_packet:
          source_execution_unit: "G13"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/issue-lifecycle-and-landing.md"
          acceptance_criteria:
            - "issue created"
          deterministic_review_checks:
            - "dispatch remains thin"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateWorkflowDefinitionJson()
    {
        return """
        {
          "execution_unit": "C2",
          "packet_paths": {
            "implementation": ".intent-cli/issues/C2/implementation.md",
            "review_context": ".intent-cli/issues/C2/review-context.md",
            "yaml": ".intent-cli/issues/C2/packet.yaml"
          },
          "worker_roles": {
            "worker": "coder",
            "reviewer": "reviewer"
          },
          "dependency_snapshot": ["A1"],
          "entry_conditions": ["A1 completed"],
          "steps": [
            {
              "kind": "implement",
              "role": "coder",
              "on_success": ["review"],
              "on_failure": []
            },
            {
              "kind": "review",
              "role": "reviewer",
              "on_success": ["complete"],
              "on_failure": ["comment-findings"]
            }
          ],
          "success_signal": "workflow render writes workflow artifact",
          "review_mode": "deterministic-review",
          "completion_action": "wait-for-deterministic-review"
        }
        """;
    }

    private static string CreateWorkflowRunArtifactJson()
    {
        return WorkerAdapterSerializer.SerializeResult(
            new WorkerAdapter.Models.WorkerAdapterResult
            {
                RunStatus = WorkerAdapter.Models.WorkerAdapterRunStatus.Running,
                StepStatuses =
                [
                    new WorkerAdapter.Models.WorkerAdapterStepStatus
                    {
                        Step = Workflow.Models.WorkflowStepKind.Implement,
                        Status = WorkerAdapter.Models.WorkerAdapterStepState.Running
                    },
                    new WorkerAdapter.Models.WorkerAdapterStepStatus
                    {
                        Step = Workflow.Models.WorkflowStepKind.Review,
                        Status = WorkerAdapter.Models.WorkerAdapterStepState.Pending
                    }
                ],
                ReviewResult = new WorkerAdapter.Models.WorkerReviewResult
                {
                    Disposition = WorkerAdapter.Models.WorkerReviewDisposition.Pending
                },
                ReviewCommentRefs = [],
                ClarificationRequests = [],
                ResultSummary = "Workflow run artifact initialized for C2.",
                RunLogRefs = [".intent-cli/workflows/C2.run.json"]
            });
    }

    private static string CreateReviewContextMarkdown()
    {
        return """
        # Execution Unit

        `G9`

        # Goal

        `intent-cli review run <execution-unit>` を working command として実装し、
        review context packet と latest linked PR をもとに
        deterministic review request artifact を `.intent-cli/reviews/<execution-unit>.request.json` へ生成できるようにする。

        # Parent References

        - [Intent CLI Surface](/Users/tomohisa/dev/GitHub/MyIntentHost/intents/intent-cli/specs/05-intent-cli-surface.md)
        - [Config And Run Model](/Users/tomohisa/dev/GitHub/MyIntentHost/intents/intent-cli/specs/08-config-and-run-model.md)

        # Deterministic Review Checks

        - review run command が PR comment 投稿や closeout の責務へ広がっていない

        # Expected Evidence

        - dotnet test IntentSystem.sln
        - review run command tests
        """;
    }

    private static string CreateRunStartPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G14] Run Start Command"
          issue_kind: "feature"
          source_execution_unit: "G14"
          goal: "Create isolated worktree and activate queue item."
          in_scope:
            - "run start command"
          out_of_scope:
            - "worker start"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli run start command"
          dependencies:
            - "G13"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "run start stays thin"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "isolated worktree created"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"
        
        review_context_packet:
          source_execution_unit: "G14"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "isolated worktree created"
          deterministic_review_checks:
            - "run start remains thin"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateReviewRunLog()
    {
        return """
        {"ts":"2026-04-03T10:00:00Z","execution_unit":"G9","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/44"}
        {"ts":"2026-04-03T10:20:00Z","execution_unit":"G9","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/45"}
        """ + Environment.NewLine;
    }

    private static string CreateReviewCommentRequestJson()
    {
        return """
        {
          "execution_unit": "G10",
          "review_context_ref": ".intent-cli/issues/G10/review-context.md",
          "linked_pr": "https://github.com/J-Tech-Japan/intent-system/pull/46",
          "deterministic_review_checks": [
            "review comment command が deterministic diff review の実行, merge, closeout の責務へ広がっていない"
          ],
          "acceptance_criteria": [],
          "expected_evidence": [
            "dotnet test IntentSystem.sln"
          ]
        }
        """;
    }

    private static string CreateReviewAcceptPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G12] Review Accept Command"
          issue_kind: "feature"
          source_execution_unit: "G12"
          goal: "Close out accepted review."
          in_scope:
            - "review accept command"
          out_of_scope:
            - "review comment"
          target_repo: "submodules/child-repo"
          target_path: "."
          target_part: "cli review accept command"
          dependencies:
            - "G10"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "closeout stays thin"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/issue-lifecycle-and-landing.md"
          acceptance_criteria:
            - "review accept merges and closes"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"
        
        review_context_packet:
          source_execution_unit: "G12"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/issue-lifecycle-and-landing.md"
          acceptance_criteria:
            - "review accept merges and closes"
          deterministic_review_checks:
            - "selected item only"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateReviewAcceptRunLog()
    {
        return """
        {"ts":"2026-04-03T10:00:00Z","execution_unit":"G12","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/51"}
        {"ts":"2026-04-03T10:10:00Z","execution_unit":"G12","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/52"}
        """ + Environment.NewLine;
    }

    private sealed class FakeReviewCommentPublisher : IReviewCommentPublisher
    {
        public string PostComment(string linkedPr, string body)
        {
            return "https://github.com/J-Tech-Japan/intent-system/pull/46#issuecomment-1";
        }
    }

    private sealed class FakeQueueDispatchPublisher : IQueueDispatchPublisher
    {
        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            return new LinkedIssue
            {
                Repo = targetRepo,
                Number = 53,
                Url = "https://github.com/J-Tech-Japan/intent-system/issues/53"
            };
        }
    }

    private sealed class FakeQueueDispatchGitRunner : IGitRemoteCommandRunner
    {
        public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            return new GitRemoteCommandResult
            {
                ExitCode = 0,
                StdOut = "git@github.com:J-Tech-Japan/intent-system.git" + Environment.NewLine,
                StdErr = string.Empty
            };
        }
    }

    private sealed class FakeRunStartGitRunner : IGitCommandRunner
    {
        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            return new GitCommandResult
            {
                ExitCode = 0,
                StdOut = string.Empty,
                StdErr = string.Empty
            };
        }
    }

    private sealed class FakeReviewAcceptClient : IReviewAcceptClient
    {
        public string MergePullRequest(string linkedPr)
        {
            return "abc123";
        }

        public void CloseIssue(string linkedIssue)
        {
        }
    }

    private sealed class FakeReviewAcceptGitRunner : IGitCommandRunner
    {
        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            return new GitCommandResult
            {
                ExitCode = 0,
                StdOut = arguments.SequenceEqual(["rev-parse", "HEAD"])
                    ? "abc123" + Environment.NewLine
                    : string.Empty,
                StdErr = string.Empty
            };
        }
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
