using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

// G569 audit: joins the non-parallel collection that already owns the
// process-global statics this class assigns, so it can no longer interleave
// with the other class that assigns them.
[Collection(RunSubmitCommandCollection.Name)]
public sealed class IssuePublishCommandTests
{
    [Fact]
    public void Execute_GivenIssueCreatedArtifact_AppliesIntentTargetAndAdvancesPublishArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "publish.yaml"),
            CreateIssueCreatedPublishYaml());
        using var writer = new StringWriter();
        var publisher = new CapturingPublisher();
        var gitRunner = new CapturingGitCommandRunner();
        var originalPublisherFactory = IssuePublishCommand.PublisherFactory;
        var originalGitCommandRunnerFactory = IssuePublishCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = IssuePublishCommand.TimestampFactory;

        try
        {
            IssuePublishCommand.PublisherFactory = () => publisher;
            IssuePublishCommand.GitCommandRunnerFactory = () => gitRunner;
            IssuePublishCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-23T00:20:00Z");

            var exitCode = IssuePublishCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Issue published for G13", writer.ToString(), StringComparison.Ordinal);

            var artifact = IssuePublishArtifactYaml.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G13", "publish.yaml")));
            Assert.Equal("published", artifact.PublishStatus);
            Assert.Equal(73, artifact.CreatedIssueNumber);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/73", artifact.CreatedIssueUrl);
            Assert.Equal("intent-target", artifact.PublishedLabelName);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            var publishedEvent = Assert.Single(runEvents);
            Assert.Equal("issue-published", publishedEvent.Event);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/73", publishedEvent.LinkedIssue);
            Assert.Equal(".intent-cli/issues/G13/publish.yaml", publishedEvent.ResultRef);

            Assert.Equal("J-Tech-Japan/intent-system", publisher.TargetRepo);
            Assert.Equal(73, publisher.IssueNumber);
            Assert.Equal("intent-target", publisher.LabelName);
            Assert.False(publisher.CreateIssueCalled);
            Assert.Single(gitRunner.Calls);
            Assert.Equal(["remote", "get-url", "origin"], gitRunner.Calls[0].Arguments);
        }
        finally
        {
            IssuePublishCommand.PublisherFactory = originalPublisherFactory;
            IssuePublishCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
            IssuePublishCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenMissingIssueCreatedArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = IssuePublishCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Issue-created publish artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenNonIssueCreatedArtifact_ReturnsExitCodeOneWithoutApplyingLabel()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "publish.yaml"),
            """
            execution_unit: G13
            publish_status: drafted
            packet_path: ".intent-cli/issues/G13/packet.yaml"
            issue_body_path: ".intent-cli/issues/G13/github-body.md"
            created_issue_number: 73
            created_issue_url: "https://github.com/J-Tech-Japan/intent-system/issues/73"
            published_label_name: null
            """);
        using var writer = new StringWriter();
        var originalPublisherFactory = IssuePublishCommand.PublisherFactory;

        try
        {
            IssuePublishCommand.PublisherFactory = () => new ThrowingPublisher();

            var exitCode = IssuePublishCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("must be in 'issue-created' status", writer.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
        }
        finally
        {
            IssuePublishCommand.PublisherFactory = originalPublisherFactory;
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

    private static string CreatePacketYaml()
    {
        return
            """
            execution_unit: G13
            implementation_issue:
              issue_title: "[G13] Add issue publish foundation"
              target_repo: "submodules/intent-system"
              target_path: "src/IntentSystem.Cli"
              target_part: "issue publish command"
              dependencies: []
            """;
    }

    private static string CreateIssueCreatedPublishYaml()
    {
        return
            """
            execution_unit: G13
            publish_status: issue-created
            packet_path: ".intent-cli/issues/G13/packet.yaml"
            issue_body_path: ".intent-cli/issues/G13/github-body.md"
            created_issue_number: 73
            created_issue_url: "https://github.com/J-Tech-Japan/intent-system/issues/73"
            published_label_name: null
            """;
    }

    private sealed class CapturingPublisher : IQueueDispatchPublisher
    {
        public string TargetRepo { get; private set; } = string.Empty;

        public int IssueNumber { get; private set; }

        public string LabelName { get; private set; } = string.Empty;

        public bool CreateIssueCalled { get; private set; }

        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            CreateIssueCalled = true;
            throw new InvalidOperationException("CreateIssue should not be called during issue publish.");
        }

        public void AddLabel(string targetRepo, int issueNumber, string labelName)
        {
            TargetRepo = targetRepo;
            IssueNumber = issueNumber;
            LabelName = labelName;
        }
    }

    private sealed class ThrowingPublisher : IQueueDispatchPublisher
    {
        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            throw new InvalidOperationException("CreateIssue should not be called.");
        }

        public void AddLabel(string targetRepo, int issueNumber, string labelName)
        {
            throw new InvalidOperationException("AddLabel should not be called.");
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-issue-publish-tests-").FullName;

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
