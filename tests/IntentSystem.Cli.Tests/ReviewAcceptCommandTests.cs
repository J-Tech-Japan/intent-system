using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Review;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class ReviewAcceptCommandTests
{
    [Fact]
    public void Execute_GivenCloseoutInputs_MergesClosesSyncsStagesAndCompletesSelectedItem()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "child-repo"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G12", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        using var writer = new StringWriter();
        var client = new FakeAcceptClient();
        var gitRunner = new FakeGitRunner(headCommit: "abc123");

        var originalClientFactory = ReviewAcceptCommand.AcceptClientFactory;
        var originalGitFactory = ReviewAcceptCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = ReviewAcceptCommand.TimestampFactory;

        try
        {
            ReviewAcceptCommand.AcceptClientFactory = () => client;
            ReviewAcceptCommand.GitCommandRunnerFactory = () => gitRunner;
            ReviewAcceptCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T01:02:03Z");

            var exitCode = ReviewAcceptCommand.Execute(CreateContext(repoRoot), ["G12"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Review accepted for G12", writer.ToString(), StringComparison.Ordinal);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/52", client.LinkedPr);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/51", client.LinkedIssue);

            var queueState = QueueStateSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
            Assert.Equal(QueueItemState.Completed, queueState.Items.Single(item => item.ExecutionUnit == "G12").State);
            Assert.Equal(QueueItemState.Blocked, queueState.Items.Single(item => item.ExecutionUnit == "B1").State);
            Assert.Equal(["G12"], queueState.Items.Single(item => item.ExecutionUnit == "B1").BlockedBy);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            Assert.Equal(5, runEvents.Count);
            Assert.Equal("pr-merged", runEvents[^3].Event);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/52", runEvents[^3].LinkedPr);
            Assert.Equal("issue-closed", runEvents[^2].Event);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/51", runEvents[^2].LinkedIssue);
            Assert.Equal("completed", runEvents[^1].Event);

            var childRepoPath = Path.Combine(repoRoot, "submodules", "child-repo");
            Assert.Equal(
                [
                    $"{childRepoPath}::fetch origin main",
                    $"{childRepoPath}::switch main",
                    $"{childRepoPath}::merge --ff-only origin/main",
                    $"{childRepoPath}::rev-parse HEAD",
                    $"{repoRoot}::add submodules/child-repo"
                ],
                gitRunner.Calls);
        }
        finally
        {
            ReviewAcceptCommand.AcceptClientFactory = originalClientFactory;
            ReviewAcceptCommand.GitCommandRunnerFactory = originalGitFactory;
            ReviewAcceptCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenMissingLinkedIssue_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """
            {"ts":"2026-04-03T10:00:00Z","execution_unit":"G12","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/52"}
            """ + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G12", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = ReviewAcceptCommand.Execute(CreateContext(repoRoot), ["G12"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("No linked issue found", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenMissingLinkedPr_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """
            {"ts":"2026-04-03T10:00:00Z","execution_unit":"G12","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/51"}
            """ + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G12", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = ReviewAcceptCommand.Execute(CreateContext(repoRoot), ["G12"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("No linked PR found", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenMergeFailure_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G12", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var originalClientFactory = ReviewAcceptCommand.AcceptClientFactory;

        try
        {
            ReviewAcceptCommand.AcceptClientFactory = () => new FakeAcceptClient
            {
                MergeException = new InvalidOperationException("merge failed")
            };

            var originalQueueState = File.ReadAllText(queueStatePath);
            var originalRunLog = File.ReadAllText(runLogPath);
            var exitCode = ReviewAcceptCommand.Execute(CreateContext(repoRoot), ["G12"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("merge failed", writer.ToString(), StringComparison.Ordinal);
            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
            Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
        }
        finally
        {
            ReviewAcceptCommand.AcceptClientFactory = originalClientFactory;
        }
    }

    [Fact]
    public void Execute_GivenChildSyncFailure_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "child-repo"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G12", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var originalClientFactory = ReviewAcceptCommand.AcceptClientFactory;
        var originalGitFactory = ReviewAcceptCommand.GitCommandRunnerFactory;

        try
        {
            ReviewAcceptCommand.AcceptClientFactory = () => new FakeAcceptClient();
            ReviewAcceptCommand.GitCommandRunnerFactory = () => new FakeGitRunner(headCommit: "def456");

            var originalQueueState = File.ReadAllText(queueStatePath);
            var originalRunLog = File.ReadAllText(runLogPath);
            var exitCode = ReviewAcceptCommand.Execute(CreateContext(repoRoot), ["G12"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("must match merged commit", writer.ToString(), StringComparison.Ordinal);
            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
            Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
        }
        finally
        {
            ReviewAcceptCommand.AcceptClientFactory = originalClientFactory;
            ReviewAcceptCommand.GitCommandRunnerFactory = originalGitFactory;
        }
    }

    [Fact]
    public void Execute_GivenDraftLinkedPr_MarksReadyBeforeMergeAndCompletesCloseout()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "child-repo"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G12", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        using var writer = new StringWriter();
        var client = new FakeAcceptClient
        {
            RequireReadyBeforeMerge = true
        };
        var gitRunner = new FakeGitRunner(headCommit: "abc123");

        var originalClientFactory = ReviewAcceptCommand.AcceptClientFactory;
        var originalGitFactory = ReviewAcceptCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = ReviewAcceptCommand.TimestampFactory;

        try
        {
            ReviewAcceptCommand.AcceptClientFactory = () => client;
            ReviewAcceptCommand.GitCommandRunnerFactory = () => gitRunner;
            ReviewAcceptCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T01:02:03Z");

            var exitCode = ReviewAcceptCommand.Execute(CreateContext(repoRoot), ["G12"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Review accepted for G12", writer.ToString(), StringComparison.Ordinal);
            Assert.Equal(2, client.MergeAttempts);
            Assert.Equal(["https://github.com/J-Tech-Japan/intent-system/pull/52"], client.ReadyMarkedPrs);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/52", client.LinkedPr);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/51", client.LinkedIssue);

            var queueState = QueueStateSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
            Assert.Equal(QueueItemState.Completed, queueState.Items.Single(item => item.ExecutionUnit == "G12").State);
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
                    Domain = "intent-system",
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
                CreateItem("G12", QueueItemState.Review),
                CreateItem("B1", QueueItemState.Blocked) with
                {
                    Dependencies = ["G12"],
                    BlockedBy = ["G12"]
                }
            ]
        };
    }

    private static QueueItem CreateItem(string executionUnit, QueueItemState state)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = $"[{executionUnit}] Review Accept",
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
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "high"
        };
    }

    private static string CreateRunLog()
    {
        return """
        {"ts":"2026-04-03T10:00:00Z","execution_unit":"G12","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/51"}
        {"ts":"2026-04-03T10:10:00Z","execution_unit":"G12","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/52"}
        """ + Environment.NewLine;
    }

    private static string CreatePacketYaml()
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

    private sealed class FakeAcceptClient : IReviewAcceptClient
    {
        public string LinkedPr { get; private set; } = string.Empty;

        public string LinkedIssue { get; private set; } = string.Empty;

        public Exception? MergeException { get; init; }

        public bool RequireReadyBeforeMerge { get; init; }

        public int MergeAttempts { get; private set; }

        public List<string> ReadyMarkedPrs { get; } = [];

        public void MarkPullRequestReady(string linkedPr)
        {
            ReadyMarkedPrs.Add(linkedPr);
        }

        public string MergePullRequest(string linkedPr)
        {
            MergeAttempts++;
            if (MergeException is not null)
            {
                throw MergeException;
            }

            if (RequireReadyBeforeMerge && ReadyMarkedPrs.Count == 0)
            {
                throw new InvalidOperationException("gh: Pull Request is still a draft (HTTP 405)");
            }

            LinkedPr = linkedPr;
            return "abc123";
        }

        public void CloseIssue(string linkedIssue)
        {
            LinkedIssue = linkedIssue;
        }
    }

    private sealed class FakeGitRunner(string headCommit) : IGitCommandRunner
    {
        public List<string> Calls { get; } = [];

        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            Calls.Add($"{workingDirectory}::{string.Join(' ', arguments)}");

            return new GitCommandResult
            {
                ExitCode = 0,
                StdOut = arguments.SequenceEqual(["rev-parse", "HEAD"])
                    ? headCommit + Environment.NewLine
                    : string.Empty,
                StdErr = string.Empty
            };
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-review-accept-tests-").FullName;

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
