using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Review;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class RunResumeCommandTests
{
    [Fact]
    public void Execute_GivenActiveItemWithExistingContext_WritesDeterministicResumeContext()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var childRepoPath = tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        var worktreePath = tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G17"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G17", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);
        var exitCode = RunResumeCommand.Execute(CreateContext(repoRoot), ["G17"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Execution unit: G17", output, StringComparison.Ordinal);
        Assert.Contains("State: active", output, StringComparison.Ordinal);
        Assert.Contains($"Worktree path: {worktreePath}", output, StringComparison.Ordinal);
        Assert.Contains($"Child repo path: {childRepoPath}", output, StringComparison.Ordinal);
        Assert.Contains("Branch: issue-62-g17", output, StringComparison.Ordinal);
        Assert.Contains("Linked issue: https://github.com/J-Tech-Japan/intent-system/issues/62", output, StringComparison.Ordinal);
        Assert.Contains("Latest linked PR: https://github.com/J-Tech-Japan/intent-system/pull/63", output, StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenFixingItemWithoutLinkedPr_WritesResumeContextWithoutLatestLinkedPr()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G17"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Fixing)));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G17", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """{"ts":"2026-04-06T08:00:00Z","execution_unit":"G17","event":"fix-requested","by":"intent-cli"}""" + Environment.NewLine);
        using var writer = new StringWriter();

        var exitCode = RunResumeCommand.Execute(CreateContext(repoRoot), ["G17"], writer);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("Latest linked PR:", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("State: fixing", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingExecutionUnit_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = RunResumeCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

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

        var exitCode = RunResumeCommand.Execute(CreateContext(repoRoot), ["G99"], writer);

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
            Path.Combine("repo", ".intent-cli", "issues", "G17", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);
        var exitCode = RunResumeCommand.Execute(CreateContext(repoRoot), ["G17"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must be active or fixing", writer.ToString(), StringComparison.Ordinal);
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
            Path.Combine("repo", ".intent-cli", "issues", "G17", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var exitCode = RunResumeCommand.Execute(CreateContext(repoRoot), ["G17"], writer);

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
        using var writer = new StringWriter();

        var exitCode = RunResumeCommand.Execute(CreateContext(repoRoot), ["G17"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Projection packet artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingChildRepoPath_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G17"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G17", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);
        var exitCode = RunResumeCommand.Execute(CreateContext(repoRoot), ["G17"], writer);

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
            Path.Combine("repo", ".intent-cli", "issues", "G17", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);
        var exitCode = RunResumeCommand.Execute(CreateContext(repoRoot), ["G17"], writer);

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
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };
    }

    private static QueueState CreateQueueState(
        QueueItemState selectedState = QueueItemState.Active,
        bool withLinkedIssue = true)
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-06T08:12:34Z"),
            Items =
            [
                CreateItem("G17", selectedState, withLinkedIssue),
                CreateItem("G18", QueueItemState.Blocked, false) with
                {
                    Dependencies = ["G17"],
                    BlockedBy = ["G17"]
                }
            ]
        };
    }

    private static QueueItem CreateItem(string executionUnit, QueueItemState state, bool withLinkedIssue)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = $"[{executionUnit}] Run Resume Command",
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
                    Number = 62,
                    Url = "https://github.com/J-Tech-Japan/intent-system/issues/62"
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
          issue_title: "[G17] Run Resume Command"
          issue_kind: "feature"
          source_execution_unit: "G17"
          goal: "Render resumable context for an existing run."
          in_scope:
            - "run resume command"
          out_of_scope:
            - "queue mutation"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli run resume command"
          dependencies:
            - "G16"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "run resume stays read-only"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "resumable context displayed"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "G17"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "resumable context displayed"
          deterministic_review_checks:
            - "run resume remains read-only"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateRunLog()
    {
        return """
        {"ts":"2026-04-06T08:00:00Z","execution_unit":"G17","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/62"}
        {"ts":"2026-04-06T08:10:00Z","execution_unit":"A1","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/12"}
        {"ts":"2026-04-06T08:20:00Z","execution_unit":"G17","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/62#issuecomment-1"}
        {"ts":"2026-04-06T08:30:00Z","execution_unit":"G17","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/63"}
        """ + Environment.NewLine;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-run-resume-tests-").FullName;

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
