using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

// G569 audit: joins the non-parallel collection that already owns the
// process-global statics this class assigns, so it can no longer interleave
// with the other class that assigns them.
[Collection(RunSubmitCommandCollection.Name)]
public sealed class IssueCreateCommandTests
{
    [Fact]
    public void Execute_GivenDraftedArtifact_CreatesIssueAndAdvancesPublishArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "github-body.md"),
            "# Goal");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "publish.yaml"),
            CreateDraftedPublishYaml());
        using var writer = new StringWriter();
        var publisher = new CapturingPublisher();
        var gitRunner = new CapturingGitCommandRunner();
        var originalPublisherFactory = IssueCreateCommand.PublisherFactory;
        var originalGitCommandRunnerFactory = IssueCreateCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = IssueCreateCommand.TimestampFactory;

        try
        {
            IssueCreateCommand.PublisherFactory = () => publisher;
            IssueCreateCommand.GitCommandRunnerFactory = () => gitRunner;
            IssueCreateCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-23T00:10:00Z");

            var exitCode = IssueCreateCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Issue created for G13", writer.ToString(), StringComparison.Ordinal);

            var artifact = IssuePublishArtifactYaml.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G13", "publish.yaml")));
            Assert.Equal("issue-created", artifact.PublishStatus);
            Assert.Equal(73, artifact.CreatedIssueNumber);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/73", artifact.CreatedIssueUrl);
            Assert.Null(artifact.PublishedLabelName);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            var createdEvent = Assert.Single(runEvents);
            Assert.Equal("issue-created", createdEvent.Event);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/73", createdEvent.LinkedIssue);
            Assert.Equal(".intent-cli/issues/G13/publish.yaml", createdEvent.ResultRef);

            Assert.Equal("J-Tech-Japan/intent-system", publisher.TargetRepo);
            Assert.Equal("[G13] Add issue create foundation", publisher.Title);
            Assert.Equal("# Goal", publisher.Body);
            Assert.Single(gitRunner.Calls);
            Assert.Equal(["remote", "get-url", "origin"], gitRunner.Calls[0].Arguments);
        }
        finally
        {
            IssueCreateCommand.PublisherFactory = originalPublisherFactory;
            IssueCreateCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
            IssueCreateCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenMissingDraftedArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = IssueCreateCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Drafted publish artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenNonDraftedArtifact_ReturnsExitCodeOneWithoutCreatingIssue()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "github-body.md"),
            "# Goal");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "publish.yaml"),
            """
            execution_unit: G13
            publish_status: issue-created
            packet_path: ".intent-cli/issues/G13/packet.yaml"
            issue_body_path: ".intent-cli/issues/G13/github-body.md"
            created_issue_number: 73
            created_issue_url: "https://github.com/J-Tech-Japan/intent-system/issues/73"
            published_label_name: null
            """);
        using var writer = new StringWriter();
        var originalPublisherFactory = IssueCreateCommand.PublisherFactory;

        try
        {
            IssueCreateCommand.PublisherFactory = () => new ThrowingPublisher();

            var exitCode = IssueCreateCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("must be in 'drafted' status", writer.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
        }
        finally
        {
            IssueCreateCommand.PublisherFactory = originalPublisherFactory;
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
              issue_title: "[G13] Add issue create foundation"
              target_repo: "submodules/intent-system"
              target_path: "src/IntentSystem.Cli"
              target_part: "issue create command"
              dependencies: []
            """;
    }

    private static string CreateDraftedPublishYaml()
    {
        return
            """
            execution_unit: G13
            publish_status: drafted
            packet_path: ".intent-cli/issues/G13/packet.yaml"
            issue_body_path: ".intent-cli/issues/G13/github-body.md"
            created_issue_number: null
            created_issue_url: null
            published_label_name: null
            """;
    }

    private sealed class CapturingPublisher : IQueueDispatchPublisher
    {
        public string TargetRepo { get; private set; } = string.Empty;

        public string Title { get; private set; } = string.Empty;

        public string Body { get; private set; } = string.Empty;

        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            TargetRepo = targetRepo;
            Title = title;
            Body = body;

            return new LinkedIssue
            {
                Repo = targetRepo,
                Number = 73,
                Url = "https://github.com/J-Tech-Japan/intent-system/issues/73"
            };
        }

        public void AddLabel(string targetRepo, int issueNumber, string labelName)
        {
            throw new InvalidOperationException("AddLabel should not be called during issue create.");
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-issue-create-tests-").FullName;

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
