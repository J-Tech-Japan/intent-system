using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Review;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class RunSubmitCommandTests
{
    [Fact]
    public void Execute_GivenActiveItemWithWorktreeAndBranch_PushesCreatesDraftPrAndTransitionsToReview()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var childRepoPath = tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        var worktreePath = tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G14"));
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
        var gitRunner = new FakeGitRunner(branchName: "issue-56-g14");
        var publisher = new FakePublisher();
        var originalGitFactory = RunSubmitCommand.GitCommandRunnerFactory;
        var originalPublisherFactory = RunSubmitCommand.PublisherFactory;
        var originalTimestampFactory = RunSubmitCommand.TimestampFactory;

        try
        {
            RunSubmitCommand.GitCommandRunnerFactory = () => gitRunner;
            RunSubmitCommand.PublisherFactory = () => publisher;
            RunSubmitCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T10:15:00Z");

            var exitCode = RunSubmitCommand.Execute(CreateContext(repoRoot), ["G14"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Run submitted for G14", writer.ToString(), StringComparison.Ordinal);
            Assert.Equal("J-Tech-Japan/intent-system", publisher.TargetRepo);
            Assert.Equal("issue-56-g14", publisher.HeadBranch);
            Assert.Equal("[G14] Run Start Command", publisher.Title);
            Assert.Contains("https://github.com/J-Tech-Japan/intent-system/issues/56", publisher.Body, StringComparison.Ordinal);

            var queueState = QueueStateSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
            Assert.Equal(QueueItemState.Review, queueState.Items.Single(item => item.ExecutionUnit == "G14").State);
            Assert.Equal(QueueItemState.Blocked, queueState.Items.Single(item => item.ExecutionUnit == "G15").State);

            Assert.Equal(
                [
                    $"{worktreePath}::rev-parse --abbrev-ref HEAD",
                    $"{worktreePath}::push -u origin issue-56-g14",
                    $"{childRepoPath}::remote get-url origin"
                ],
                gitRunner.Calls);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            var reviewEvent = Assert.Single(runEvents);
            Assert.Equal("review", reviewEvent.Event);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/58", reviewEvent.LinkedPr);
        }
        finally
        {
            RunSubmitCommand.GitCommandRunnerFactory = originalGitFactory;
            RunSubmitCommand.PublisherFactory = originalPublisherFactory;
            RunSubmitCommand.TimestampFactory = originalTimestampFactory;
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
        var exitCode = RunSubmitCommand.Execute(CreateContext(repoRoot), ["G14"], writer);

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

        var exitCode = RunSubmitCommand.Execute(CreateContext(repoRoot), ["G14"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Projection packet artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingChildRepoPath_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
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
        var exitCode = RunSubmitCommand.Execute(CreateContext(repoRoot), ["G14"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Child repo path was not found", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Empty(File.ReadAllText(runLogPath));
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
            string.Empty);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G14", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var exitCode = RunSubmitCommand.Execute(CreateContext(repoRoot), ["G14"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Worktree path was not found", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Empty(File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenBranchMismatch_ReturnsExitCodeOneWithoutMutatingFiles()
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
        var originalGitFactory = RunSubmitCommand.GitCommandRunnerFactory;

        try
        {
            RunSubmitCommand.GitCommandRunnerFactory = () => new FakeGitRunner(branchName: "wrong-branch");

            var originalQueueState = File.ReadAllText(queueStatePath);
            var exitCode = RunSubmitCommand.Execute(CreateContext(repoRoot), ["G14"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("must match expected branch", writer.ToString(), StringComparison.Ordinal);
            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
            Assert.Empty(File.ReadAllText(runLogPath));
        }
        finally
        {
            RunSubmitCommand.GitCommandRunnerFactory = originalGitFactory;
        }
    }

    [Fact]
    public void Execute_GivenGitPushFailure_ReturnsExitCodeOneWithoutMutatingFiles()
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
        var originalGitFactory = RunSubmitCommand.GitCommandRunnerFactory;

        try
        {
            RunSubmitCommand.GitCommandRunnerFactory = () => new FakeGitRunner(branchName: "issue-56-g14", failOnPush: true);

            var originalQueueState = File.ReadAllText(queueStatePath);
            var exitCode = RunSubmitCommand.Execute(CreateContext(repoRoot), ["G14"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("git push failed", writer.ToString(), StringComparison.Ordinal);
            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
            Assert.Empty(File.ReadAllText(runLogPath));
        }
        finally
        {
            RunSubmitCommand.GitCommandRunnerFactory = originalGitFactory;
        }
    }

    [Fact]
    public void Execute_GivenPrCreateFailure_ReturnsExitCodeOneWithoutMutatingFiles()
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
        var originalGitFactory = RunSubmitCommand.GitCommandRunnerFactory;
        var originalPublisherFactory = RunSubmitCommand.PublisherFactory;

        try
        {
            RunSubmitCommand.GitCommandRunnerFactory = () => new FakeGitRunner(branchName: "issue-56-g14");
            RunSubmitCommand.PublisherFactory = () => new FailingPublisher();

            var originalQueueState = File.ReadAllText(queueStatePath);
            var exitCode = RunSubmitCommand.Execute(CreateContext(repoRoot), ["G14"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("gh pr create failed", writer.ToString(), StringComparison.Ordinal);
            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
            Assert.Empty(File.ReadAllText(runLogPath));
        }
        finally
        {
            RunSubmitCommand.GitCommandRunnerFactory = originalGitFactory;
            RunSubmitCommand.PublisherFactory = originalPublisherFactory;
        }
    }

    [Fact]
    public void ResolvePullRequestTitle_GivenQueueItem_UsesQueueTitle()
    {
        var title = RunSubmitCommand.ResolvePullRequestTitle(CreateQueueState().Items[0]);

        Assert.Equal("[G14] Run Start Command", title);
    }

    [Fact]
    public void ResolvePullRequestBody_GivenQueueItem_UsesMinimalLinkedIssueBody()
    {
        var body = RunSubmitCommand.ResolvePullRequestBody(CreateQueueState().Items[0]);

        Assert.Contains("[G14] Run Start Command", body, StringComparison.Ordinal);
        Assert.Contains("https://github.com/J-Tech-Japan/intent-system/issues/56", body, StringComparison.Ordinal);
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
                CreateItem("G14", QueueItemState.Active, withLinkedIssue),
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
            Title = "[G14] Run Start Command",
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

    private static string CreatePacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "G15 Run Submit Command"
          issue_kind: "feature"
          source_execution_unit: "G15"
          goal: "Submit active worktree for review."
          in_scope:
            - "run submit command"
          out_of_scope:
            - "review execution"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli run submit command"
          dependencies:
            - "G14"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "run submit stays thin"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "draft pr created"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "G15"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "draft pr created"
          deterministic_review_checks:
            - "run submit remains thin"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
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

            if (arguments.SequenceEqual(["remote", "get-url", "origin"]))
            {
                return new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = "git@github.com:J-Tech-Japan/intent-system.git" + Environment.NewLine,
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

    private sealed class FakePublisher : IRunSubmitPublisher
    {
        public string TargetRepo { get; private set; } = string.Empty;
        public string HeadBranch { get; private set; } = string.Empty;
        public string Title { get; private set; } = string.Empty;
        public string Body { get; private set; } = string.Empty;

        public string CreateDraftPullRequest(string targetRepo, string headBranch, string title, string body)
        {
            TargetRepo = targetRepo;
            HeadBranch = headBranch;
            Title = title;
            Body = body;

            return "https://github.com/J-Tech-Japan/intent-system/pull/58";
        }
    }

    private sealed class FailingPublisher : IRunSubmitPublisher
    {
        public string CreateDraftPullRequest(string targetRepo, string headBranch, string title, string body)
        {
            throw new InvalidOperationException("gh pr create failed.");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-run-submit-tests-").FullName;

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
