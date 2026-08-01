using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

// G569 audit: joins the non-parallel collection that already owns the
// process-global statics this class assigns, so it can no longer interleave
// with the other class that assigns them.
[Collection(RunSubmitCommandCollection.Name)]
public sealed class IssueDraftCommandTests
{
    [Fact]
    public void Execute_GivenPacketAndGitHubBody_WritesPublishArtifactAndAppendsRunLog()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "github-body.md"),
            "# Goal");
        using var writer = new StringWriter();
        var originalTimestampFactory = IssueDraftCommand.TimestampFactory;

        try
        {
            IssueDraftCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-23T00:00:00Z");

            var exitCode = IssueDraftCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Issue draft prepared for G13.", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains(".intent-cli/issues/G13/publish.yaml", writer.ToString(), StringComparison.Ordinal);

            var artifact = IssuePublishArtifactYaml.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G13", "publish.yaml")));
            Assert.Equal("G13", artifact.ExecutionUnit);
            Assert.Equal("drafted", artifact.PublishStatus);
            Assert.Equal(".intent-cli/issues/G13/packet.yaml", artifact.PacketPath);
            Assert.Equal(".intent-cli/issues/G13/github-body.md", artifact.IssueBodyPath);
            Assert.Null(artifact.CreatedIssueNumber);
            Assert.Null(artifact.CreatedIssueUrl);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            var draftedEvent = Assert.Single(runEvents);
            Assert.Equal("issue-drafted", draftedEvent.Event);
            Assert.Equal(".intent-cli/issues/G13/packet.yaml", draftedEvent.PacketRef);
            Assert.Equal(".intent-cli/issues/G13/publish.yaml", draftedEvent.ResultRef);
        }
        finally
        {
            IssueDraftCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenExistingPublishArtifact_OverwritesItWithDraftedState()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
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
            publish_status: published
            packet_path: ".intent-cli/issues/G13/old-packet.yaml"
            issue_body_path: ".intent-cli/issues/G13/old-body.md"
            created_issue_number: 91
            created_issue_url: "https://github.com/J-Tech-Japan/intent-system/issues/91"
            """);

        var exitCode = IssueDraftCommand.Execute(CreateContext(repoRoot), ["G13"], TextWriter.Null);

        Assert.Equal(0, exitCode);

        var artifact = IssuePublishArtifactYaml.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G13", "publish.yaml")));
        Assert.Equal("drafted", artifact.PublishStatus);
        Assert.Equal(".intent-cli/issues/G13/packet.yaml", artifact.PacketPath);
        Assert.Equal(".intent-cli/issues/G13/github-body.md", artifact.IssueBodyPath);
        Assert.Null(artifact.CreatedIssueNumber);
        Assert.Null(artifact.CreatedIssueUrl);
    }

    [Fact]
    public void Execute_GivenMissingGitHubBodyArtifact_ReturnsExitCodeOneWithoutWritingPublishArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var exitCode = IssueDraftCommand.Execute(CreateContext(repoRoot), ["G13"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("GitHub issue body artifact was not found", writer.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "G13", "publish.yaml")));
        Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
    }

    [Fact]
    public void Execute_GivenOnlyLocalArtifacts_SucceedsWithoutQueueStateOrGitHubMutation()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "github-body.md"),
            "# Goal");

        var exitCode = IssueDraftCommand.Execute(CreateContext(repoRoot), ["G13"], TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
        Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "G13", "publish.yaml")));
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
              issue_title: "[G13] Add issue draft foundation"
              target_repo: "submodules/intent-system"
              target_path: "src/IntentSystem.Cli"
              target_part: "issue draft command"
              dependencies: []
            """;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-issue-draft-tests-").FullName;

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
