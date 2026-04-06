using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Review;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class IntakeAutostartCommandTests
{
    [Fact]
    public void Execute_GivenQueuedItem_DispatchesAndStartsSelectedExecutionUnit()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G31", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G31", "github-body.md"),
            "# Goal");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var originalPublisherFactory = QueueDispatchCommand.PublisherFactory;
        var originalRemoteGitFactory = QueueDispatchCommand.GitCommandRunnerFactory;
        var originalDispatchTimestampFactory = QueueDispatchCommand.TimestampFactory;
        var originalStartGitFactory = RunStartCommand.GitCommandRunnerFactory;
        var originalStartTimestampFactory = RunStartCommand.TimestampFactory;
        var startGitRunner = new FakeStartGitRunner();

        try
        {
            QueueDispatchCommand.PublisherFactory = () => new FakePublisher();
            QueueDispatchCommand.GitCommandRunnerFactory = () => new FakeRemoteGitRunner();
            QueueDispatchCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T06:00:00Z");
            RunStartCommand.GitCommandRunnerFactory = () => startGitRunner;
            RunStartCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T06:10:00Z");

            var exitCode = IntakeAutostartCommand.Execute(CreateContext(repoRoot), ["G31"], writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Intake autostart completed for G31.", output, StringComparison.Ordinal);
            Assert.Contains("Linked issue: https://github.com/J-Tech-Japan/intent-system/issues/88", output, StringComparison.Ordinal);

            var queueState = QueueStateSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
            var selectedItem = queueState.Items.Single(item => item.ExecutionUnit == "G31");
            Assert.Equal(QueueItemState.Active, selectedItem.State);
            Assert.NotNull(selectedItem.LinkedIssue);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/88", selectedItem.LinkedIssue!.Url);

            var unrelatedItem = queueState.Items.Single(item => item.ExecutionUnit == "G32");
            Assert.Equal(QueueItemState.Queued, unrelatedItem.State);
            Assert.Null(unrelatedItem.LinkedIssue);

            var worktreePath = Path.GetFullPath(Path.Combine(repoRoot, ".intent-cli", "worktrees", "G31"));
            var childRepoPath = Path.Combine(repoRoot, "submodules", "intent-system");
            Assert.Equal(
                [
                    $"{childRepoPath}::fetch origin main",
                    $"{childRepoPath}::worktree add -b issue-88-g31 {worktreePath} origin/main"
                ],
                startGitRunner.Calls);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            Assert.Equal(2, runEvents.Count);
            Assert.Equal("issue-created", runEvents[0].Event);
            Assert.Equal("activated", runEvents[1].Event);
            Assert.All(runEvents, runEvent => Assert.Equal("G31", runEvent.ExecutionUnit));
        }
        finally
        {
            QueueDispatchCommand.PublisherFactory = originalPublisherFactory;
            QueueDispatchCommand.GitCommandRunnerFactory = originalRemoteGitFactory;
            QueueDispatchCommand.TimestampFactory = originalDispatchTimestampFactory;
            RunStartCommand.GitCommandRunnerFactory = originalStartGitFactory;
            RunStartCommand.TimestampFactory = originalStartTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenDispatchFailure_ReturnsExitCodeOneAndDoesNotStartRun()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G31", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var originalStartGitFactory = RunStartCommand.GitCommandRunnerFactory;
        var startGitRunner = new FakeStartGitRunner();

        try
        {
            RunStartCommand.GitCommandRunnerFactory = () => startGitRunner;

            var exitCode = IntakeAutostartCommand.Execute(CreateContext(repoRoot), ["G31"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("GitHub issue body artifact was not found", writer.ToString(), StringComparison.Ordinal);
            Assert.Empty(startGitRunner.Calls);
            Assert.Empty(File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
        }
        finally
        {
            RunStartCommand.GitCommandRunnerFactory = originalStartGitFactory;
        }
    }

    [Fact]
    public void Execute_GivenMissingExecutionUnitArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = IntakeAutostartCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires an execution unit", writer.ToString(), StringComparison.OrdinalIgnoreCase);
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

    private static QueueState CreateQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                CreateItem("G31", QueueItemState.Queued),
                CreateItem("G32", QueueItemState.Queued)
            ]
        };
    }

    private static QueueItem CreateItem(string executionUnit, QueueItemState state)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = $"[{executionUnit}] Intake Autostart",
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
            LinkedIssue = null,
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "high"
        };
    }

    private static string CreatePacketYaml()
    {
        return """
        execution_unit: G31
        implementation_issue:
          issue_title: "G31 Intake Autostart Command"
          goal: "Bridge intake queue item into dispatch and run start."
          in_scope:
            - "intake autostart command"
          out_of_scope:
            - "worker launch"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli intake autostart command"
          dependencies:
            - "G30"
          technical_baseline:
            - "C# / .NET"
          project_local_guidance:
            - "AGENTS.md"
          intent_baseline:
            - "autostart stays thin"
          acceptance_criteria:
            - "queued item autostarts"
          verification:
            - "tests-passing"
        review:
          summarize_first: true
          require_explicit_diff_check: true
          require_explicit_scope_check: true
          require_explicit_contract_check: true
          required_checks:
            - "autostart remains thin"
        """;
    }

    private sealed class FakePublisher : IQueueDispatchPublisher
    {
        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            return new LinkedIssue
            {
                Repo = targetRepo,
                Number = 88,
                Url = "https://github.com/J-Tech-Japan/intent-system/issues/88"
            };
        }
    }

    private sealed class FakeRemoteGitRunner : IGitRemoteCommandRunner
    {
        public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            return new GitRemoteCommandResult
            {
                ExitCode = 0,
                StdOut = "https://github.com/J-Tech-Japan/intent-system.git",
                StdErr = string.Empty
            };
        }
    }

    private sealed class FakeStartGitRunner : IGitCommandRunner
    {
        public List<string> Calls { get; } = [];

        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            Calls.Add($"{workingDirectory}::{string.Join(' ', arguments)}");
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intake-autostart-tests-").FullName;

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
