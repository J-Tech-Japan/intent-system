using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Review;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class RunStartCommandTests
{
    [Fact]
    public void Execute_GivenQueuedItemWithLinkedIssue_CreatesWorktreeAndActivatesItem()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G14", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var gitRunner = new FakeGitRunner();
        var originalGitFactory = RunStartCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = RunStartCommand.TimestampFactory;

        try
        {
            RunStartCommand.GitCommandRunnerFactory = () => gitRunner;
            RunStartCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T09:30:00Z");

            var exitCode = RunStartCommand.Execute(CreateContext(repoRoot), ["G14"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Run started for G14", writer.ToString(), StringComparison.Ordinal);

            var queueState = QueueStateSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
            Assert.Equal(QueueItemState.Active, queueState.Items.Single(item => item.ExecutionUnit == "G14").State);
            Assert.Equal(QueueItemState.Blocked, queueState.Items.Single(item => item.ExecutionUnit == "G15").State);

            var worktreePath = Path.GetFullPath(Path.Combine(repoRoot, ".intent-cli", "worktrees", "G14"));
            var childRepoPath = Path.Combine(repoRoot, "submodules", "intent-system");
            Assert.Equal(
                [
                    $"{childRepoPath}::fetch origin main",
                    $"{childRepoPath}::worktree add -b issue-56-g14 {worktreePath} origin/main"
                ],
                gitRunner.Calls);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            var activated = Assert.Single(runEvents);
            Assert.Equal("activated", activated.Event);
            Assert.Equal("G14", activated.ExecutionUnit);
        }
        finally
        {
            RunStartCommand.GitCommandRunnerFactory = originalGitFactory;
            RunStartCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenMissingLinkedIssue_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(withLinkedIssue: false)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G14", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var exitCode = RunStartCommand.Execute(CreateContext(repoRoot), ["G14"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must have a linked issue", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Empty(File.ReadAllText(runLogPath));
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

        var exitCode = RunStartCommand.Execute(CreateContext(repoRoot), ["G14"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Projection packet artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingChildRepoPath_ReturnsExitCodeOneWithoutMutatingFiles()
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
            Path.Combine("repo", ".intent-cli", "issues", "G14", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var exitCode = RunStartCommand.Execute(CreateContext(repoRoot), ["G14"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Child repo path was not found", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Empty(File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenExistingWorktreePath_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G14"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G14", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var exitCode = RunStartCommand.Execute(CreateContext(repoRoot), ["G14"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Worktree path already exists", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Empty(File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenGitWorktreeFailure_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G14", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();
        var originalGitFactory = RunStartCommand.GitCommandRunnerFactory;

        try
        {
            RunStartCommand.GitCommandRunnerFactory = () => new FakeGitRunner(failOnWorktreeAdd: true);

            var originalQueueState = File.ReadAllText(queueStatePath);
            var exitCode = RunStartCommand.Execute(CreateContext(repoRoot), ["G14"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("git worktree add failed", writer.ToString(), StringComparison.Ordinal);
            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
            Assert.Empty(File.ReadAllText(runLogPath));
        }
        finally
        {
            RunStartCommand.GitCommandRunnerFactory = originalGitFactory;
        }
    }

    [Fact]
    public void ResolveBranchName_GivenExecutionUnitAndLinkedIssue_UsesDeterministicShape()
    {
        var linkedIssue = new LinkedIssue
        {
            Repo = "J-Tech-Japan/intent-system",
            Number = 56,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/56"
        };

        var branchName = RunStartCommand.ResolveBranchName("G14", linkedIssue);

        Assert.Equal("issue-56-g14", branchName);
    }

    [Fact]
    public void ResolveWorktreePath_GivenRelativeConfiguredRoot_UsesConfiguredWorktreeRoot()
    {
        var context = CreateContext("/tmp/repo");

        var worktreePath = RunStartCommand.ResolveWorktreePath(context, "G14");

        Assert.Equal(
            Path.GetFullPath("/tmp/repo/.intent-cli/worktrees/G14"),
            worktreePath);
    }

    [Fact]
    public void ExecuteCore_GivenQueuedItemWithLinkedIssue_ReturnsDeterministicResult()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G14", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        var gitRunner = new FakeGitRunner();
        var originalGitFactory = RunStartCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = RunStartCommand.TimestampFactory;

        try
        {
            RunStartCommand.GitCommandRunnerFactory = () => gitRunner;
            RunStartCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T09:30:00Z");

            var result = RunStartCommand.ExecuteCore(CreateContext(repoRoot), "G14");

            Assert.Equal("G14", result.ExecutionUnit);
            Assert.Equal(
                Path.GetFullPath(Path.Combine(repoRoot, ".intent-cli", "worktrees", "G14")),
                result.WorktreePath);
            Assert.Equal("issue-56-g14", result.BranchName);
        }
        finally
        {
            RunStartCommand.GitCommandRunnerFactory = originalGitFactory;
            RunStartCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenRuntimeOnlyTargetPart_ReturnsExitCodeOneWithoutCreatingWorktreeOrMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G14", "packet.yaml"),
            CreatePacketYaml(targetPart: ".intent-cli/intake"));
        using var writer = new StringWriter();
        var gitRunner = new FakeGitRunner();
        var originalGitFactory = RunStartCommand.GitCommandRunnerFactory;

        try
        {
            RunStartCommand.GitCommandRunnerFactory = () => gitRunner;
            var originalQueueState = File.ReadAllText(queueStatePath);

            var exitCode = RunStartCommand.Execute(CreateContext(repoRoot), ["G14"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("host runtime-only '.intent-cli/**' content", writer.ToString(), StringComparison.Ordinal);
            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
            Assert.Empty(File.ReadAllText(runLogPath));
            Assert.Empty(gitRunner.Calls);
        }
        finally
        {
            RunStartCommand.GitCommandRunnerFactory = originalGitFactory;
        }
    }

    [Fact]
    public void Execute_GivenRuntimeOnlyTargetRepo_ReturnsExitCodeOneWithoutCreatingWorktreeOrMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G14", "packet.yaml"),
            CreatePacketYaml(targetRepo: ".intent-cli"));
        using var writer = new StringWriter();
        var gitRunner = new FakeGitRunner();
        var originalGitFactory = RunStartCommand.GitCommandRunnerFactory;

        try
        {
            RunStartCommand.GitCommandRunnerFactory = () => gitRunner;
            var originalQueueState = File.ReadAllText(queueStatePath);

            var exitCode = RunStartCommand.Execute(CreateContext(repoRoot), ["G14"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("Child target repo '.intent-cli'", writer.ToString(), StringComparison.Ordinal);
            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
            Assert.Empty(File.ReadAllText(runLogPath));
            Assert.Empty(gitRunner.Calls);
        }
        finally
        {
            RunStartCommand.GitCommandRunnerFactory = originalGitFactory;
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
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };
    }

    private static QueueState CreateQueueState(bool withLinkedIssue = true)
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                CreateItem("G14", QueueItemState.Queued, withLinkedIssue),
                CreateItem("G15", QueueItemState.Blocked, false) with
                {
                    Dependencies = ["G14"],
                    BlockedBy = ["G14"]
                }
            ]
        };
    }

    private static QueueItem CreateItem(string executionUnit, QueueItemState state, bool withLinkedIssue)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = $"[{executionUnit}] Run Start",
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
                    Number = 56,
                    Url = "https://github.com/J-Tech-Japan/intent-system/issues/56"
                }
                : null,
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "high"
        };
    }

    private static string CreatePacketYaml(
        string targetPart = "cli run start command",
        string targetRepo = "submodules/intent-system")
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
          target_repo: "__TARGET_REPO__"
          target_path: "."
          target_part: "__TARGET_PART__"
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
        """
            .Replace("__TARGET_PART__", targetPart, StringComparison.Ordinal)
            .Replace("__TARGET_REPO__", targetRepo, StringComparison.Ordinal);
    }

    private sealed class FakeGitRunner(bool failOnWorktreeAdd = false) : IGitCommandRunner
    {
        public List<string> Calls { get; } = [];

        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            Calls.Add($"{workingDirectory}::{string.Join(' ', arguments)}");

            if (failOnWorktreeAdd && arguments.Count >= 2 && arguments[0] == "worktree" && arguments[1] == "add")
            {
                return new GitCommandResult
                {
                    ExitCode = 1,
                    StdOut = string.Empty,
                    StdErr = "git worktree add failed."
                };
            }

            return new GitCommandResult
            {
                ExitCode = 0,
                StdOut = string.Empty,
                StdErr = string.Empty
            };
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-run-start-tests-").FullName;

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
