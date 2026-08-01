using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

// G569 audit: joins the non-parallel collection that already owns the
// process-global statics this class assigns, so it can no longer interleave
// with the other class that assigns them.
[Collection(RunSubmitCommandCollection.Name)]
public sealed class QueueDispatchCommandTests
{
    [Fact]
    public void Execute_GivenPacketAndGitHubBody_CreatesIssueUpdatesQueueAndAppendsRunLog()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "github-body.md"),
            "# Goal");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var originalPublisherFactory = QueueDispatchCommand.PublisherFactory;
        var originalGitCommandRunnerFactory = QueueDispatchCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = QueueDispatchCommand.TimestampFactory;

        try
        {
            QueueDispatchCommand.PublisherFactory = () => new FakePublisher();
            QueueDispatchCommand.GitCommandRunnerFactory = () => new FakeGitCommandRunner();
            QueueDispatchCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T06:00:00Z");

            var exitCode = QueueDispatchCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Queue item G13 dispatched", writer.ToString(), StringComparison.Ordinal);

            var queueState = QueueStateSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
            var selectedItem = queueState.Items.Single(item => item.ExecutionUnit == "G13");
            Assert.NotNull(selectedItem.LinkedIssue);
            Assert.Equal("J-Tech-Japan/intent-system", selectedItem.LinkedIssue!.Repo);
            Assert.Equal(53, selectedItem.LinkedIssue.Number);
            Assert.Equal(
                "https://github.com/J-Tech-Japan/intent-system/issues/53",
                selectedItem.LinkedIssue.Url);
            Assert.Null(queueState.Items.Single(item => item.ExecutionUnit == "G14").LinkedIssue);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            var issueCreated = Assert.Single(runEvents);
            Assert.Equal("issue-created", issueCreated.Event);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/53", issueCreated.LinkedIssue);
        }
        finally
        {
            QueueDispatchCommand.PublisherFactory = originalPublisherFactory;
            QueueDispatchCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
            QueueDispatchCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenExistingLinkedIssue_ReusesItWithoutCreatingDuplicateIssueOrMutatingQueue()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(withExistingLinkedIssue: true)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var originalPublisherFactory = QueueDispatchCommand.PublisherFactory;
        var originalTimestampFactory = QueueDispatchCommand.TimestampFactory;

        try
        {
            QueueDispatchCommand.PublisherFactory = () => new ThrowingPublisher();
            QueueDispatchCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T06:00:00Z");

            var originalQueueState = File.ReadAllText(queueStatePath);
            var exitCode = QueueDispatchCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("reused existing linked issue", writer.ToString(), StringComparison.Ordinal);
            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            var reuseEvent = Assert.Single(runEvents);
            Assert.Equal("issue-reused", reuseEvent.Event);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/41", reuseEvent.LinkedIssue);
        }
        finally
        {
            QueueDispatchCommand.PublisherFactory = originalPublisherFactory;
            QueueDispatchCommand.TimestampFactory = originalTimestampFactory;
        }
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
            Path.Combine("repo", ".intent-cli", "issues", "G13", "github-body.md"),
            "# Goal");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        using var writer = new StringWriter();

        var exitCode = QueueDispatchCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Projection packet artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingGitHubBodyArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var exitCode = QueueDispatchCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("GitHub issue body artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingTargetRepo_ReturnsExitCodeOneWithoutMutatingFiles()
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
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            CreatePacketYaml(targetRepo: ""));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "github-body.md"),
            "# Goal");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var exitCode = QueueDispatchCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("target repo", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Empty(File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenEmptyIssueTitle_ReturnsExitCodeOneWithoutMutatingFiles()
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
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            CreatePacketYaml(issueTitle: ""));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "github-body.md"),
            "# Goal");
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var exitCode = QueueDispatchCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("issue title", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Empty(File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_ResolveGitHubBodyPath_UsesPacketDirectory()
    {
        var bodyPath = QueueDispatchCommand.ResolveGitHubBodyPath(
            "/tmp/repo",
            ".intent-cli/issues/G13/packet.yaml");

        Assert.Equal(
            Path.GetFullPath("/tmp/repo/.intent-cli/issues/G13/github-body.md"),
            bodyPath);
    }

    [Fact]
    public void Execute_GivenCanonicalPacketTargetRepo_ResolvesGitHubTargetFromChildOriginRemote()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            CreatePacketYaml(targetRepo: "submodules/intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "github-body.md"),
            "# Goal");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var publisher = new CapturingPublisher();
        var originalPublisherFactory = QueueDispatchCommand.PublisherFactory;
        var originalGitCommandRunnerFactory = QueueDispatchCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = QueueDispatchCommand.TimestampFactory;

        try
        {
            QueueDispatchCommand.PublisherFactory = () => publisher;
            QueueDispatchCommand.GitCommandRunnerFactory = () => new FakeGitCommandRunner();
            QueueDispatchCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T06:00:00Z");

            var exitCode = QueueDispatchCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

            Assert.Equal(0, exitCode);
            Assert.Equal("J-Tech-Japan/intent-system", publisher.TargetRepo);
        }
        finally
        {
            QueueDispatchCommand.PublisherFactory = originalPublisherFactory;
            QueueDispatchCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
            QueueDispatchCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenRuntimeOnlyTargetPart_ReturnsExitCodeOneWithoutCreatingIssue()
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
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            CreatePacketYaml(targetPart: ".intent-cli/intake"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "github-body.md"),
            "# Goal");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        using var writer = new StringWriter();
        var originalPublisherFactory = QueueDispatchCommand.PublisherFactory;

        try
        {
            QueueDispatchCommand.PublisherFactory = () => new ThrowingPublisher();
            var originalQueueState = File.ReadAllText(queueStatePath);

            var exitCode = QueueDispatchCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("host runtime-only '.intent-cli/**' content", writer.ToString(), StringComparison.Ordinal);
            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
            Assert.Empty(File.ReadAllText(runLogPath));
        }
        finally
        {
            QueueDispatchCommand.PublisherFactory = originalPublisherFactory;
        }
    }

    [Fact]
    public void Execute_GivenRuntimeOnlyTargetRepo_ReturnsExitCodeOneWithoutCreatingIssue()
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
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            CreatePacketYaml(targetRepo: ".intent-cli"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "github-body.md"),
            "# Goal");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        using var writer = new StringWriter();
        var originalPublisherFactory = QueueDispatchCommand.PublisherFactory;

        try
        {
            QueueDispatchCommand.PublisherFactory = () => new ThrowingPublisher();
            var originalQueueState = File.ReadAllText(queueStatePath);

            var exitCode = QueueDispatchCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("Child target repo '.intent-cli'", writer.ToString(), StringComparison.Ordinal);
            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
            Assert.Empty(File.ReadAllText(runLogPath));
        }
        finally
        {
            QueueDispatchCommand.PublisherFactory = originalPublisherFactory;
        }
    }

    [Theory]
    [InlineData("https://github.com/J-Tech-Japan/intent-system.git", "J-Tech-Japan/intent-system")]
    [InlineData("git@github.com:J-Tech-Japan/intent-system.git", "J-Tech-Japan/intent-system")]
    public void ParseRemoteUrl_GivenGitHubRemote_ShapesOwnerRepo(string remoteUrl, string expected)
    {
        var actual = GitHubRepositoryTargetResolver.ParseRemoteUrl(remoteUrl);

        Assert.Equal(expected, actual);
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

    private static QueueState CreateQueueState(bool withExistingLinkedIssue = false)
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                CreateItem("G13", QueueItemState.Queued, withExistingLinkedIssue),
                CreateItem("G14", QueueItemState.Queued)
            ]
        };
    }

    private static QueueItem CreateItem(
        string executionUnit,
        QueueItemState state,
        bool withExistingLinkedIssue = false)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = $"[{executionUnit}] Queue Dispatch",
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
            LinkedIssue = withExistingLinkedIssue
                ? new LinkedIssue
                {
                    Repo = "J-Tech-Japan/intent-system",
                    Number = 41,
                    Url = "https://github.com/J-Tech-Japan/intent-system/issues/41"
                }
                : null,
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "high"
        };
    }

    private static string CreatePacketYaml(
        string targetRepo = "submodules/intent-system",
        string issueTitle = "[G13] Queue Dispatch Command",
        string targetPart = "cli queue dispatch command")
    {
        return $"""
        implementation_issue_packet:
          issue_title: "{issueTitle}"
          issue_kind: "feature"
          source_execution_unit: "G13"
          goal: "Create linked child issue from prepared artifacts."
          in_scope:
            - "queue dispatch command"
          out_of_scope:
            - "worker execution"
          target_repo: "{targetRepo}"
          target_path: "."
          target_part: "{targetPart}"
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

    private sealed class FakePublisher : IQueueDispatchPublisher
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

    private sealed class CapturingPublisher : IQueueDispatchPublisher
    {
        public string TargetRepo { get; private set; } = string.Empty;

        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            TargetRepo = targetRepo;

            return new LinkedIssue
            {
                Repo = targetRepo,
                Number = 53,
                Url = "https://github.com/J-Tech-Japan/intent-system/issues/53"
            };
        }
    }

    private sealed class ThrowingPublisher : IQueueDispatchPublisher
    {
        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            throw new InvalidOperationException("CreateIssue should not be called when linked issue already exists.");
        }
    }

    private sealed class FakeGitCommandRunner : IGitRemoteCommandRunner
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

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-queue-dispatch-tests-").FullName;

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
