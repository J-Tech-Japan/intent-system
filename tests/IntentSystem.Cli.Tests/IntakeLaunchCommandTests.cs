using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Review;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class IntakeLaunchCommandTests
{
    [Fact]
    public void Execute_GivenGeneratedUnits_LaunchesSelectedDomainUnits()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            CreateExecutionArtifact("auth", ["AUTH-01", "AUTH-02"]));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "AUTH-01", "packet.yaml"),
            CreatePacketYaml("AUTH-01"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "AUTH-01", "github-body.md"),
            "# AUTH-01");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "AUTH-02", "packet.yaml"),
            CreatePacketYaml("AUTH-02"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "AUTH-02", "github-body.md"),
            "# AUTH-02");
        using var writer = new StringWriter();
        var originalEnqueueTimestampFactory = QueueEnqueueCommand.TimestampFactory;
        var originalPublisherFactory = QueueDispatchCommand.PublisherFactory;
        var originalRemoteGitFactory = QueueDispatchCommand.GitCommandRunnerFactory;
        var originalDispatchTimestampFactory = QueueDispatchCommand.TimestampFactory;
        var originalStartGitFactory = RunStartCommand.GitCommandRunnerFactory;
        var originalStartTimestampFactory = RunStartCommand.TimestampFactory;
        var startGitRunner = new FakeStartGitRunner();
        var publisher = new FakePublisher();

        try
        {
            QueueEnqueueCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T11:00:00Z");
            QueueDispatchCommand.PublisherFactory = () => publisher;
            QueueDispatchCommand.GitCommandRunnerFactory = () => new FakeRemoteGitRunner();
            QueueDispatchCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T11:05:00Z");
            RunStartCommand.GitCommandRunnerFactory = () => startGitRunner;
            RunStartCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T11:10:00Z");

            var exitCode = IntakeLaunchCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Intake launch processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
            Assert.Contains("- AUTH-02", output, StringComparison.Ordinal);
            Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/issues/401", output, StringComparison.Ordinal);
            Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/issues/402", output, StringComparison.Ordinal);

            var queueState = QueueStateSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
            Assert.Equal(QueueItemState.Active, queueState.Items.Single(item => item.ExecutionUnit == "AUTH-01").State);
            Assert.Equal(QueueItemState.Active, queueState.Items.Single(item => item.ExecutionUnit == "AUTH-02").State);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            Assert.Equal(
                ["queued", "issue-created", "activated", "queued", "issue-created", "activated"],
                runEvents.Select(runEvent => runEvent.Event).ToArray());
            Assert.Equal(
                ["AUTH-01", "AUTH-01", "AUTH-01", "AUTH-02", "AUTH-02", "AUTH-02"],
                runEvents.Select(runEvent => runEvent.ExecutionUnit).ToArray());

            var auth01Worktree = Path.GetFullPath(Path.Combine(repoRoot, ".intent-cli", "worktrees", "AUTH-01"));
            var auth02Worktree = Path.GetFullPath(Path.Combine(repoRoot, ".intent-cli", "worktrees", "AUTH-02"));
            var childRepoPath = Path.Combine(repoRoot, "submodules", "intent-system");
            Assert.Equal(
                [
                    $"{childRepoPath}::fetch origin main",
                    $"{childRepoPath}::worktree add -b issue-401-auth-01 {auth01Worktree} origin/main",
                    $"{childRepoPath}::fetch origin main",
                    $"{childRepoPath}::worktree add -b issue-402-auth-02 {auth02Worktree} origin/main"
                ],
                startGitRunner.Calls);
        }
        finally
        {
            QueueEnqueueCommand.TimestampFactory = originalEnqueueTimestampFactory;
            QueueDispatchCommand.PublisherFactory = originalPublisherFactory;
            QueueDispatchCommand.GitCommandRunnerFactory = originalRemoteGitFactory;
            QueueDispatchCommand.TimestampFactory = originalDispatchTimestampFactory;
            RunStartCommand.GitCommandRunnerFactory = originalStartGitFactory;
            RunStartCommand.TimestampFactory = originalStartTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenExistingQueueItem_SkipsExistingUnitAndLaunchesRemainingUnits()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(includeExisting: true)));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            CreateExecutionArtifact("auth", ["AUTH-01", "AUTH-02"]));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "AUTH-01", "packet.yaml"),
            CreatePacketYaml("AUTH-01"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "AUTH-01", "github-body.md"),
            "# AUTH-01");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "AUTH-02", "packet.yaml"),
            CreatePacketYaml("AUTH-02"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "AUTH-02", "github-body.md"),
            "# AUTH-02");
        using var writer = new StringWriter();
        var originalEnqueueTimestampFactory = QueueEnqueueCommand.TimestampFactory;
        var originalPublisherFactory = QueueDispatchCommand.PublisherFactory;
        var originalRemoteGitFactory = QueueDispatchCommand.GitCommandRunnerFactory;
        var originalDispatchTimestampFactory = QueueDispatchCommand.TimestampFactory;
        var originalStartGitFactory = RunStartCommand.GitCommandRunnerFactory;
        var originalStartTimestampFactory = RunStartCommand.TimestampFactory;
        var startGitRunner = new FakeStartGitRunner();
        var publisher = new FakePublisher();

        try
        {
            QueueEnqueueCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T11:00:00Z");
            QueueDispatchCommand.PublisherFactory = () => publisher;
            QueueDispatchCommand.GitCommandRunnerFactory = () => new FakeRemoteGitRunner();
            QueueDispatchCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T11:05:00Z");
            RunStartCommand.GitCommandRunnerFactory = () => startGitRunner;
            RunStartCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T11:10:00Z");

            var exitCode = IntakeLaunchCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Skipped units:", output, StringComparison.Ordinal);
            Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
            Assert.Contains("- AUTH-02", output, StringComparison.Ordinal);
            Assert.DoesNotContain("issue-401-auth-01", string.Join(Environment.NewLine, startGitRunner.Calls), StringComparison.Ordinal);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            Assert.Equal(["AUTH-02", "AUTH-02", "AUTH-02"], runEvents.Select(runEvent => runEvent.ExecutionUnit).ToArray());
        }
        finally
        {
            QueueEnqueueCommand.TimestampFactory = originalEnqueueTimestampFactory;
            QueueDispatchCommand.PublisherFactory = originalPublisherFactory;
            QueueDispatchCommand.GitCommandRunnerFactory = originalRemoteGitFactory;
            QueueDispatchCommand.TimestampFactory = originalDispatchTimestampFactory;
            RunStartCommand.GitCommandRunnerFactory = originalStartGitFactory;
            RunStartCommand.TimestampFactory = originalStartTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenMissingGitHubBodyArtifact_ReturnsExitCodeOneWithoutMutatingQueueArtifacts()
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
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            CreateExecutionArtifact("auth", ["AUTH-01"]));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "AUTH-01", "packet.yaml"),
            CreatePacketYaml("AUTH-01"));
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var exitCode = IntakeLaunchCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("GitHub issue body artifact was not found", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Empty(File.ReadAllText(runLogPath));
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
                    Review = "Codex"
                }
            }
        };
    }

    private static QueueState CreateQueueState(bool includeExisting = false)
    {
        var items = new List<QueueItem>
        {
            CreateItem("G3", QueueItemState.Completed)
        };

        if (includeExisting)
        {
            items.Add(CreateItem("AUTH-01", QueueItemState.Queued));
        }

        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            Items = items
        };
    }

    private static QueueItem CreateItem(string executionUnit, QueueItemState state)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = $"[{executionUnit}] Existing Item",
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
            WorkerRole = "Claude",
            ReviewRole = "Codex",
            Priority = "high"
        };
    }

    private static string CreateExecutionArtifact(string domain, IReadOnlyList<string> executionUnits)
    {
        var sections = executionUnits.Select(executionUnit =>
            $$"""

            ### `{{executionUnit}}`
            source_file_path: intents/intent-cli/concepts/{{executionUnit.ToLowerInvariant()}}.md
            target_part: concepts
            dependencies:
            - none
            readiness_notes:
            - Current heading: # {{executionUnit}}
            verification_hints:
            - dotnet test IntentSystem.sln
            """);

        return $$"""
            # Intake Execution Draft

            ## Domain
            `{{domain}}`

            ## Proposed Execution Units{{string.Concat(sections)}}
            """;
    }

    private static string CreatePacketYaml(string executionUnit)
    {
        return $$"""
            execution_unit: {{executionUnit}}
            implementation_issue:
              issue_title: "{{executionUnit}} Intake Launch"
              goal: "Launch generated issue-ready execution unit into queue and autostart flow."
              in_scope:
                - "queue insertion"
                - "issue creation"
                - "run start"
              out_of_scope:
                - "review execution"
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "cli intake launch command"
              dependencies:
                - "G3"
              technical_baseline:
                - "C# / .NET"
              project_local_guidance:
                - "AGENTS.md"
              intent_baseline:
                - "intake launch stays thin"
              acceptance_criteria:
                - "issue-ready unit launches deterministically"
              verification:
                - "tests-passing"

            review:
              summarize_first: true
              require_explicit_diff_check: true
              require_explicit_scope_check: true
              require_explicit_contract_check: true
              required_checks:
                - "intake launch remains thin"
            """;
    }

    private sealed class FakePublisher : IQueueDispatchPublisher
    {
        private int nextIssueNumber = 401;

        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            var issueNumber = nextIssueNumber++;
            return new LinkedIssue
            {
                Repo = targetRepo,
                Number = issueNumber,
                Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{issueNumber}"
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intake-launch-tests-").FullName;

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
