using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Tests;

// G569 audit: joins the non-parallel collection that already owns the
// process-global statics this class assigns, so it can no longer interleave
// with the other class that assigns them.
[Collection(RunSubmitCommandCollection.Name)]
public sealed class IssueStatusCommandTests
{
    [Fact]
    public void Execute_GivenDraftedArtifact_PrintsDraftStateWithoutGitHubLookup()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var publishPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "publish.yaml"),
            CreatePublishYaml("drafted", "null", "null", "null"));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            "{\"event\":\"issue-drafted\"}" + Environment.NewLine);
        using var writer = new StringWriter();
        var publisher = new CapturingPublisher([]);
        var originalPublisherFactory = IssueStatusCommand.PublisherFactory;

        try
        {
            IssueStatusCommand.PublisherFactory = () => publisher;
            var originalPublishYaml = File.ReadAllText(publishPath);
            var originalRunLog = File.ReadAllText(runLogPath);

            var exitCode = IssueStatusCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

            var output = writer.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("Issue status for G13", output, StringComparison.Ordinal);
            Assert.Contains("Publish status: drafted", output, StringComparison.Ordinal);
            Assert.Contains("Created issue: none", output, StringComparison.Ordinal);
            Assert.Contains("Automation state: drafted only", output, StringComparison.Ordinal);
            Assert.Equal(0, publisher.GetIssueLabelsCallCount);
            Assert.False(publisher.CreateIssueCalled);
            Assert.False(publisher.AddLabelCalled);
            Assert.Equal(originalPublishYaml, File.ReadAllText(publishPath));
            Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
        }
        finally
        {
            IssueStatusCommand.PublisherFactory = originalPublisherFactory;
        }
    }

    [Fact]
    public void Execute_GivenIssueCreatedArtifactWithoutIntentTarget_PrintsNotAutomationVisibleState()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = CreateRepoWithPacket(tempDirectory);
        var publishPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "publish.yaml"),
            CreatePublishYaml(
                "issue-created",
                "73",
                "\"https://github.com/J-Tech-Japan/intent-system/issues/73\"",
                "null"));
        using var writer = new StringWriter();
        var publisher = new CapturingPublisher(["bug"]);
        var gitRunner = new CapturingGitCommandRunner();
        var originalPublisherFactory = IssueStatusCommand.PublisherFactory;
        var originalGitCommandRunnerFactory = IssueStatusCommand.GitCommandRunnerFactory;

        try
        {
            IssueStatusCommand.PublisherFactory = () => publisher;
            IssueStatusCommand.GitCommandRunnerFactory = () => gitRunner;
            var originalPublishYaml = File.ReadAllText(publishPath);

            var exitCode = IssueStatusCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

            var output = writer.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("Created issue: #73 https://github.com/J-Tech-Japan/intent-system/issues/73", output, StringComparison.Ordinal);
            Assert.Contains("intent-target label: missing", output, StringComparison.Ordinal);
            Assert.Contains("Automation state: issue-created but not published / not automation-visible", output, StringComparison.Ordinal);
            Assert.Equal("J-Tech-Japan/intent-system", publisher.TargetRepo);
            Assert.Equal(73, publisher.IssueNumber);
            Assert.Equal(1, publisher.GetIssueLabelsCallCount);
            Assert.False(publisher.CreateIssueCalled);
            Assert.False(publisher.AddLabelCalled);
            Assert.Single(gitRunner.Calls);
            Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            Assert.Equal(originalPublishYaml, File.ReadAllText(publishPath));
        }
        finally
        {
            IssueStatusCommand.PublisherFactory = originalPublisherFactory;
            IssueStatusCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
        }
    }

    [Fact]
    public void Execute_GivenPublishedArtifactWithIntentTarget_PrintsPublishedReadyState()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = CreateRepoWithPacket(tempDirectory);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "publish.yaml"),
            CreatePublishYaml(
                "published",
                "73",
                "\"https://github.com/J-Tech-Japan/intent-system/issues/73\"",
                "\"intent-target\""));
        using var writer = new StringWriter();
        var publisher = new CapturingPublisher(["intent-target"]);
        var originalPublisherFactory = IssueStatusCommand.PublisherFactory;
        var originalGitCommandRunnerFactory = IssueStatusCommand.GitCommandRunnerFactory;

        try
        {
            IssueStatusCommand.PublisherFactory = () => publisher;
            IssueStatusCommand.GitCommandRunnerFactory = () => new CapturingGitCommandRunner();

            var exitCode = IssueStatusCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Published label in artifact: intent-target", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("intent-target label: present", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Automation state: published and label present", writer.ToString(), StringComparison.Ordinal);
            Assert.False(publisher.CreateIssueCalled);
            Assert.False(publisher.AddLabelCalled);
        }
        finally
        {
            IssueStatusCommand.PublisherFactory = originalPublisherFactory;
            IssueStatusCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
        }
    }

    [Fact]
    public void Execute_GivenPublishedArtifactWithMissingIntentTarget_PrintsDriftState()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = CreateRepoWithPacket(tempDirectory);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "publish.yaml"),
            CreatePublishYaml(
                "published",
                "73",
                "\"https://github.com/J-Tech-Japan/intent-system/issues/73\"",
                "\"intent-target\""));
        using var writer = new StringWriter();
        var publisher = new CapturingPublisher(["documentation"]);
        var originalPublisherFactory = IssueStatusCommand.PublisherFactory;
        var originalGitCommandRunnerFactory = IssueStatusCommand.GitCommandRunnerFactory;

        try
        {
            IssueStatusCommand.PublisherFactory = () => publisher;
            IssueStatusCommand.GitCommandRunnerFactory = () => new CapturingGitCommandRunner();

            var exitCode = IssueStatusCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("intent-target label: missing", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains(
                "Automation state: drift: artifact is published but live intent-target label is missing",
                writer.ToString(),
                StringComparison.Ordinal);
            Assert.False(publisher.CreateIssueCalled);
            Assert.False(publisher.AddLabelCalled);
        }
        finally
        {
            IssueStatusCommand.PublisherFactory = originalPublisherFactory;
            IssueStatusCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
        }
    }

    [Fact]
    public void Execute_GivenMissingPublishArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = IssueStatusCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Issue publish artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMismatchedPublishArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "publish.yaml"),
            """
            execution_unit: G99
            publish_status: drafted
            packet_path: ".intent-cli/issues/G13/packet.yaml"
            issue_body_path: ".intent-cli/issues/G13/github-body.md"
            created_issue_number: null
            created_issue_url: null
            published_label_name: null
            """);
        using var writer = new StringWriter();

        var exitCode = IssueStatusCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("does not match requested execution unit 'G13'", writer.ToString(), StringComparison.Ordinal);
    }

    private static string CreateRepoWithPacket(TemporaryDirectory tempDirectory)
    {
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            """
            execution_unit: G13
            implementation_issue:
              issue_title: "[G13] Add issue status foundation"
              target_repo: "submodules/intent-system"
              target_path: "src/IntentSystem.Cli"
              target_part: "issue status command"
              dependencies: []
            """);

        return repoRoot;
    }

    private static string CreatePublishYaml(
        string publishStatus,
        string createdIssueNumber,
        string createdIssueUrl,
        string publishedLabelName)
    {
        return
            $$"""
            execution_unit: G13
            publish_status: {{publishStatus}}
            packet_path: ".intent-cli/issues/G13/packet.yaml"
            issue_body_path: ".intent-cli/issues/G13/github-body.md"
            created_issue_number: {{createdIssueNumber}}
            created_issue_url: {{createdIssueUrl}}
            published_label_name: {{publishedLabelName}}
            """;
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

    private sealed class CapturingPublisher(IReadOnlyList<string> labels) : IQueueDispatchPublisher
    {
        public string TargetRepo { get; private set; } = string.Empty;

        public int IssueNumber { get; private set; }

        public int GetIssueLabelsCallCount { get; private set; }

        public bool CreateIssueCalled { get; private set; }

        public bool AddLabelCalled { get; private set; }

        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            CreateIssueCalled = true;
            throw new InvalidOperationException("CreateIssue should not be called during issue status.");
        }

        public void AddLabel(string targetRepo, int issueNumber, string labelName)
        {
            AddLabelCalled = true;
            throw new InvalidOperationException("AddLabel should not be called during issue status.");
        }

        public IReadOnlyList<string> GetIssueLabels(string targetRepo, int issueNumber)
        {
            TargetRepo = targetRepo;
            IssueNumber = issueNumber;
            GetIssueLabelsCallCount++;
            return labels;
        }
    }

    private sealed class CapturingGitCommandRunner : IGitRemoteCommandRunner
    {
        public List<GitCommandCall> Calls { get; } = [];

        public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            Calls.Add(new GitCommandCall
            {
                WorkingDirectory = workingDirectory,
                Arguments = [..arguments]
            });

            return new GitRemoteCommandResult
            {
                ExitCode = 0,
                StdOut = "git@github.com:J-Tech-Japan/intent-system.git" + Environment.NewLine,
                StdErr = string.Empty
            };
        }
    }

    private sealed record GitCommandCall
    {
        public required string WorkingDirectory { get; init; }

        public required IReadOnlyList<string> Arguments { get; init; }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-issue-status-tests-").FullName;

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
