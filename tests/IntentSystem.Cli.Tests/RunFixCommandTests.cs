using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class RunFixCommandTests
{
    [Fact]
    public void Execute_GivenFixingItemWithInputs_GeneratesRepairHandoffArtifactWithoutMutatingStateOrRunLog()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();
        var originalTimestampFactory = RunFixCommand.TimestampFactory;
        var originalLauncherFactory = RunFixCommand.DirectRunLauncherFactory;

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        try
        {
            RunFixCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-09T10:25:00Z");
            RunFixCommand.DirectRunLauncherFactory = () => new FakeDirectRunLauncher(
                "pid:8765",
                "Claude",
                "default",
                "stdio",
                "stdio transport launched via 'claude' in '/repo/.intent-cli/worktrees/G20' for provider 'Claude'.");

            var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Repair handoff artifact generated for G20", output, StringComparison.Ordinal);
            Assert.Contains("Implement role: Claude", output, StringComparison.Ordinal);
            Assert.Contains("Branch: issue-68-g20", output, StringComparison.Ordinal);
            Assert.Contains("Latest linked PR: https://github.com/J-Tech-Japan/intent-system/pull/69", output, StringComparison.Ordinal);
            Assert.Contains("Latest comment ref: https://github.com/J-Tech-Japan/intent-system/pull/69#issuecomment-2", output, StringComparison.Ordinal);
            Assert.Contains("Direct run request artifact: .intent-cli/runs/G20.request.json", output, StringComparison.Ordinal);
            Assert.Contains("Direct provider: Claude", output, StringComparison.Ordinal);
            Assert.Contains("Direct model: default", output, StringComparison.Ordinal);
            Assert.Contains("Direct transport: stdio", output, StringComparison.Ordinal);
            Assert.Contains("Provider session: pid:8765", output, StringComparison.Ordinal);

            var artifactPath = Path.Combine(repoRoot, ".intent-cli", "fix", "G20.request.md");
            Assert.True(File.Exists(artifactPath));
            var markdown = File.ReadAllText(artifactPath);
            Assert.Contains("- packet_ref: .intent-cli/issues/G20/packet.yaml", markdown, StringComparison.Ordinal);
            Assert.Contains("- review_context_ref: .intent-cli/issues/G20/review-context.md", markdown, StringComparison.Ordinal);
            Assert.Contains("- review_comment_artifact_ref: .intent-cli/reviews/G20.comment.json", markdown, StringComparison.Ordinal);
            Assert.Contains("- review_request_ref: .intent-cli/reviews/G20.request.json", markdown, StringComparison.Ordinal);
            Assert.Contains("- latest_linked_pr: https://github.com/J-Tech-Japan/intent-system/pull/69", markdown, StringComparison.Ordinal);
            Assert.Contains("- latest_comment_ref: https://github.com/J-Tech-Japan/intent-system/pull/69#issuecomment-2", markdown, StringComparison.Ordinal);

            var directRunArtifactPath = Path.Combine(repoRoot, ".intent-cli", "runs", "G20.request.json");
            Assert.True(File.Exists(directRunArtifactPath));
            var directRunArtifact = DirectRunRequestArtifactJson.Deserialize(File.ReadAllText(directRunArtifactPath));
            Assert.Equal("G20", directRunArtifact.ExecutionUnit);
            Assert.Equal("fix", directRunArtifact.EntryKind);
            Assert.Equal(".intent-cli/fix/G20.request.md", directRunArtifact.UpstreamRequestRef);
            Assert.Equal("Claude", directRunArtifact.Provider);
            Assert.Equal("default", directRunArtifact.Model);
            Assert.Equal("stdio", directRunArtifact.Transport);
            Assert.Equal("pid:8765", directRunArtifact.ProviderSessionId);

            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
            Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
        }
        finally
        {
            RunFixCommand.TimestampFactory = originalTimestampFactory;
            RunFixCommand.DirectRunLauncherFactory = originalLauncherFactory;
        }
    }

    [Fact]
    public void Execute_GivenMissingExecutionUnit_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = RunFixCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires an execution unit", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeDirectRunLauncher(
        string providerSessionId,
        string provider,
        string model,
        string transport,
        string transportSummary) : IDirectRunLauncher
    {
        public DirectRunLaunchResult Launch(
            string executionUnit,
            string entryKind,
            string requestArtifactPath,
            string providerArg,
            string modelArg,
            string transportArg,
            DateTimeOffset launchedAt,
            string workingDirectory,
            string absoluteRequestArtifactPath)
        {
            Assert.Equal("G20", executionUnit);
            Assert.Equal("fix", entryKind);
            Assert.Equal(".intent-cli/runs/G20.request.json", requestArtifactPath);
            Assert.Equal(provider, providerArg);
            Assert.Equal(model, modelArg);
            Assert.Equal(transport, transportArg);
            Assert.EndsWith("/.intent-cli/worktrees/G20", workingDirectory, StringComparison.Ordinal);
            Assert.EndsWith("/.intent-cli/fix/G20.request.md", absoluteRequestArtifactPath, StringComparison.Ordinal);

            return new DirectRunLaunchResult
            {
                RequestArtifactPath = ".intent-cli/runs/G20.request.json",
                Provider = providerArg,
                Model = modelArg,
                Transport = transportArg,
                ProviderSessionId = providerSessionId,
                TransportSummary = transportSummary
            };
        }
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

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G99"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("was not found in queue state", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenInvalidState_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Review)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must be fixing", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenMissingLinkedIssue_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(withLinkedIssue: false)));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must have a linked issue", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
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
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

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
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Review context artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingReviewCommentArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        using var writer = new StringWriter();

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Review comment artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenReviewContextMismatch_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown("G21"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must match queue item execution unit", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenReviewCommentArtifactMismatch_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson(executionUnit: "G21"));
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Review comment artifact execution unit", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenMissingLatestLinkedPr_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """{"ts":"2026-04-09T09:40:00Z","execution_unit":"G20","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/69#issuecomment-2"}""" + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("No linked PR found", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenCommentArtifactLinkedPrMismatch_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson(linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/68"));
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must match latest linked PR", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenMissingChildRepoPath_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Child repo path was not found", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenMissingWorktreePath_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Worktree path was not found", writer.ToString(), StringComparison.Ordinal);
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
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                },
                Roles = new RoleMappings
                {
                    Implement = "Claude",
                    Review = "Codex",
                    Interview = "Claude",
                    Clarify = "Codex"
                }
            }
        };
    }

    private static QueueState CreateQueueState(
        QueueItemState selectedState = QueueItemState.Fixing,
        bool withLinkedIssue = true)
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-09T09:42:34Z"),
            Items =
            [
                CreateItem("G20", selectedState, withLinkedIssue),
                CreateItem("G21", QueueItemState.Blocked, false) with
                {
                    Dependencies = ["G20"],
                    BlockedBy = ["G20"]
                }
            ]
        };
    }

    private static QueueItem CreateItem(string executionUnit, QueueItemState state, bool withLinkedIssue)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = $"[{executionUnit}] Run Fix Command",
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
            LinkedIssue = withLinkedIssue
                ? new LinkedIssue
                {
                    Repo = "J-Tech-Japan/intent-system",
                    Number = 68,
                    Url = "https://github.com/J-Tech-Japan/intent-system/issues/68"
                }
                : null,
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "high"
        };
    }

    private static string CreatePacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G20] Run Fix Command"
          issue_kind: "feature"
          source_execution_unit: "G20"
          goal: "Generate a repair worker handoff artifact."
          in_scope:
            - "run fix command"
            - "repair handoff artifact generation"
          out_of_scope:
            - "queue mutation"
            - "worker start"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli run fix command"
          dependencies:
            - "G19"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "run fix stays handoff-only"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/review-recovery-and-retry.md"
          acceptance_criteria:
            - "repair handoff artifact generated"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "G20"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/review-recovery-and-retry.md"
          acceptance_criteria:
            - "repair handoff artifact generated"
          deterministic_review_checks:
            - "run fix command remains handoff-only"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateReviewContextMarkdown(string executionUnit = "G20")
    {
        return $$"""
        # Execution Unit

        `{{executionUnit}}`

        # Goal

        `intent-cli run fix <execution-unit>` を working command にする。

        # Acceptance Criteria

        - repair handoff artifact generated

        # Deterministic Review Checks

        - run fix command remains handoff-only

        # Expected Evidence

        - dotnet test IntentSystem.sln
        """;
    }

    private static string CreateReviewCommentArtifactJson(
        string executionUnit = "G20",
        string linkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/69")
    {
        return $$"""
        {
          "execution_unit": "{{executionUnit}}",
          "review_request_ref": ".intent-cli/reviews/G20.request.json",
          "linked_pr": "{{linkedPr}}",
          "comment_ref": "https://github.com/J-Tech-Japan/intent-system/pull/69#issuecomment-2",
          "body_path": "/repo/prepared-comment.md"
        }
        """;
    }

    private static string CreateRunLog()
    {
        return """
        {"ts":"2026-04-09T09:00:00Z","execution_unit":"G20","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/69"}
        {"ts":"2026-04-09T09:10:00Z","execution_unit":"A1","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/12"}
        {"ts":"2026-04-09T09:20:00Z","execution_unit":"G20","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/69#issuecomment-2"}
        """ + Environment.NewLine;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-run-fix-tests-").FullName;

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
