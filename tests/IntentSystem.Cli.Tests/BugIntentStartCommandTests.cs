using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class BugIntentStartCommandTests
{
    [Fact]
    public void Execute_GivenReadyIntentEnqueue_StartsAllocatedExecutionUnitAndWritesArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.intent-enqueue.yaml"),
            BugIntentEnqueueArtifactYaml.Serialize(
                new BugIntentEnqueueArtifact
                {
                    BugId = "BUG-123",
                    IntentIssueRef = ".intent-cli/bugs/BUG-123.intent-issue.yaml",
                    AllocatedExecutionUnit = "G41",
                    LinkedIssueUrl = "https://github.com/J-Tech-Japan/MyIntentHost/issues/53",
                    LinkedIssueNumber = 53,
                    PacketPaths =
                    [
                        ".intent-cli/issues/G41/implementation.md",
                        ".intent-cli/issues/G41/review-context.md",
                        ".intent-cli/issues/G41/packet.yaml"
                    ],
                    ReadyToEnqueue = true
                }));
        using var writer = new StringWriter();
        var originalExecutor = BugIntentStartCommand.RunStartExecutor;

        try
        {
            BugIntentStartCommand.RunStartExecutor = (_, executionUnit) => new RunStartResult
            {
                ExecutionUnit = executionUnit,
                WorktreePath = $"/tmp/worktrees/{executionUnit}",
                BranchName = $"issue-53-{executionUnit.ToLowerInvariant()}"
            };

            var exitCode = BugIntentStartCommand.Execute(CreateContext(repoRoot), ["BUG-123"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Bug intent-start artifact generated for 'BUG-123'.", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Started execution unit: G41", writer.ToString(), StringComparison.Ordinal);

            var artifact = BugIntentStartArtifactYaml.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-123.intent-start.yaml")));
            Assert.Equal(".intent-cli/bugs/BUG-123.intent-enqueue.yaml", artifact.IntentEnqueueRef);
            Assert.Equal("G41", artifact.StartedExecutionUnit);
            Assert.Equal("/tmp/worktrees/G41", artifact.WorktreePath);
            Assert.Equal("issue-53-g41", artifact.BranchName);
            Assert.True(artifact.ReadyToStart);
        }
        finally
        {
            BugIntentStartCommand.RunStartExecutor = originalExecutor;
        }
    }

    [Fact]
    public void Execute_GivenNotReadyIntentEnqueue_WritesNotReadyArtifactWithoutStarting()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-124.intent-enqueue.yaml"),
            BugIntentEnqueueArtifactYaml.Serialize(
                new BugIntentEnqueueArtifact
                {
                    BugId = "BUG-124",
                    IntentIssueRef = ".intent-cli/bugs/BUG-124.intent-issue.yaml",
                    AllocatedExecutionUnit = null,
                    LinkedIssueUrl = null,
                    LinkedIssueNumber = null,
                    PacketPaths = [],
                    ReadyToEnqueue = false
                }));
        using var writer = new StringWriter();
        var originalExecutor = BugIntentStartCommand.RunStartExecutor;

        try
        {
            BugIntentStartCommand.RunStartExecutor = (_, _) =>
                throw new InvalidOperationException("run start should not execute for not-ready artifact.");

            var exitCode = BugIntentStartCommand.Execute(CreateContext(repoRoot), ["BUG-124"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Ready to start: false", writer.ToString(), StringComparison.Ordinal);

            var artifact = BugIntentStartArtifactYaml.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-124.intent-start.yaml")));
            Assert.Null(artifact.StartedExecutionUnit);
            Assert.Null(artifact.WorktreePath);
            Assert.Null(artifact.BranchName);
            Assert.False(artifact.ReadyToStart);
        }
        finally
        {
            BugIntentStartCommand.RunStartExecutor = originalExecutor;
        }
    }

    [Fact]
    public void Execute_GivenMissingIntentEnqueueArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = BugIntentStartCommand.Execute(CreateContext(repoRoot), ["BUG-123"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Bug intent-enqueue artifact was not found", writer.ToString(), StringComparison.Ordinal);
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

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-bug-intent-start-tests-").FullName;

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
