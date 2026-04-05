using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Review;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class RunResubmitCommandTests
{
    [Fact]
    public void Execute_GivenFixingItemWithWorktreeAndLatestLinkedPr_PushesAndAppendsResubmittedEventWithoutMutatingQueue()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        var worktreePath = tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G21"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G21", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();
        var gitRunner = new FakeGitRunner(branchName: "issue-70-g21");
        var originalGitFactory = RunResubmitCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = RunResubmitCommand.TimestampFactory;

        try
        {
            RunResubmitCommand.GitCommandRunnerFactory = () => gitRunner;
            RunResubmitCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-10T07:15:00Z");

            var originalQueueState = File.ReadAllText(queueStatePath);
            var exitCode = RunResubmitCommand.Execute(CreateContext(repoRoot), ["G21"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Run resubmitted for G21", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Branch: issue-70-g21", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Worktree path: " + worktreePath, writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Latest linked PR: https://github.com/J-Tech-Japan/intent-system/pull/71", writer.ToString(), StringComparison.Ordinal);
            Assert.Equal(
                [
                    $"{worktreePath}::rev-parse --abbrev-ref HEAD",
                    $"{worktreePath}::push -u origin issue-70-g21"
                ],
                gitRunner.Calls);

            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal(3, runEvents.Count);
            Assert.Equal("resubmitted", runEvents[^1].Event);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/71", runEvents[^1].LinkedPr);
        }
        finally
        {
            RunResubmitCommand.GitCommandRunnerFactory = originalGitFactory;
            RunResubmitCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenMissingExecutionUnit_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = RunResubmitCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

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

        var exitCode = RunResubmitCommand.Execute(CreateContext(repoRoot), ["G99"], writer);

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
            Path.Combine("repo", ".intent-cli", "issues", "G21", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);
        var exitCode = RunResubmitCommand.Execute(CreateContext(repoRoot), ["G21"], writer);

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
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G21", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);
        var exitCode = RunResubmitCommand.Execute(CreateContext(repoRoot), ["G21"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must have a linked issue", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
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
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        using var writer = new StringWriter();

        var exitCode = RunResubmitCommand.Execute(CreateContext(repoRoot), ["G21"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Projection packet artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingRunLog_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G21", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var exitCode = RunResubmitCommand.Execute(CreateContext(repoRoot), ["G21"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Run log was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingChildRepoPath_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G21"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G21", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);
        var exitCode = RunResubmitCommand.Execute(CreateContext(repoRoot), ["G21"], writer);

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
            Path.Combine("repo", ".intent-cli", "issues", "G21", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);
        var exitCode = RunResubmitCommand.Execute(CreateContext(repoRoot), ["G21"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Worktree path was not found", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenMissingLatestLinkedPr_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G21"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """{"ts":"2026-04-10T07:10:00Z","execution_unit":"G21","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/71#issuecomment-3"}""" + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G21", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);
        var exitCode = RunResubmitCommand.Execute(CreateContext(repoRoot), ["G21"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("No linked PR found", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenBranchMismatch_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G21"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G21", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();
        var originalGitFactory = RunResubmitCommand.GitCommandRunnerFactory;

        try
        {
            RunResubmitCommand.GitCommandRunnerFactory = () => new FakeGitRunner(branchName: "wrong-branch");

            var originalQueueState = File.ReadAllText(queueStatePath);
            var originalRunLog = File.ReadAllText(runLogPath);
            var exitCode = RunResubmitCommand.Execute(CreateContext(repoRoot), ["G21"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("must match expected branch", writer.ToString(), StringComparison.Ordinal);
            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
            Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
        }
        finally
        {
            RunResubmitCommand.GitCommandRunnerFactory = originalGitFactory;
        }
    }

    [Fact]
    public void Execute_GivenGitPushFailure_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G21"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G21", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();
        var originalGitFactory = RunResubmitCommand.GitCommandRunnerFactory;

        try
        {
            RunResubmitCommand.GitCommandRunnerFactory = () => new FakeGitRunner(branchName: "issue-70-g21", failOnPush: true);

            var originalQueueState = File.ReadAllText(queueStatePath);
            var originalRunLog = File.ReadAllText(runLogPath);
            var exitCode = RunResubmitCommand.Execute(CreateContext(repoRoot), ["G21"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("git push failed", writer.ToString(), StringComparison.Ordinal);
            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
            Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
        }
        finally
        {
            RunResubmitCommand.GitCommandRunnerFactory = originalGitFactory;
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
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
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
            UpdatedAt = DateTimeOffset.Parse("2026-04-10T07:12:34Z"),
            Items =
            [
                CreateItem("G21", selectedState, withLinkedIssue),
                CreateItem("G22", QueueItemState.Blocked, false) with
                {
                    Dependencies = ["G21"],
                    BlockedBy = ["G21"]
                }
            ]
        };
    }

    private static QueueItem CreateItem(string executionUnit, QueueItemState state, bool withLinkedIssue)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = "[G21] Run Resubmit Command",
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
                    Number = 70,
                    Url = "https://github.com/J-Tech-Japan/intent-system/issues/70"
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
          issue_title: "[G21] Run Resubmit Command"
          issue_kind: "feature"
          source_execution_unit: "G21"
          goal: "Push the repair branch and append a resubmitted event."
          in_scope:
            - "run resubmit command"
            - "repair branch push"
          out_of_scope:
            - "queue state mutation"
            - "PR creation"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli run resubmit command"
          dependencies:
            - "G20"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "run resubmit stays push-only"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/05-intent-cli-surface.md"
          acceptance_criteria:
            - "resubmitted event appended"
          verification_evidence:
            - "dotnet test IntentSystem.sln"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "G21"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/05-intent-cli-surface.md"
          acceptance_criteria:
            - "resubmitted event appended"
          deterministic_review_checks:
            - "run resubmit remains push-only"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateRunLog()
    {
        return """
        {"ts":"2026-04-10T07:00:00Z","execution_unit":"G21","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/71"}
        {"ts":"2026-04-10T07:10:00Z","execution_unit":"G21","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/71#issuecomment-3"}
        """ + Environment.NewLine;
    }

    private sealed class FakeGitRunner(string branchName, bool failOnPush = false) : IGitCommandRunner
    {
        public List<string> Calls { get; } = [];

        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            Calls.Add($"{workingDirectory}::{string.Join(' ', arguments)}");

            if (arguments.Count >= 2
                && arguments[0] == "push"
                && arguments[1] == "-u"
                && failOnPush)
            {
                return new GitCommandResult
                {
                    ExitCode = 1,
                    StdOut = string.Empty,
                    StdErr = "git push failed."
                };
            }

            if (arguments.SequenceEqual(["rev-parse", "--abbrev-ref", "HEAD"]))
            {
                return new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = branchName + Environment.NewLine,
                    StdErr = string.Empty
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-run-resubmit-tests-").FullName;

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
