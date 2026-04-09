using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class BugIntentSubmitCommandTests
{
    [Fact]
    public void Execute_GivenReadyIntentStart_SubmitsStartedExecutionUnitAndWritesArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.intent-start.yaml"),
            BugIntentStartArtifactYaml.Serialize(
                new BugIntentStartArtifact
                {
                    BugId = "BUG-123",
                    IntentEnqueueRef = ".intent-cli/bugs/BUG-123.intent-enqueue.yaml",
                    StartedExecutionUnit = "G41",
                    WorktreePath = "/tmp/worktrees/G41",
                    BranchName = "issue-53-g41",
                    ReadyToStart = true
                }));
        using var writer = new StringWriter();
        var originalExecutor = BugIntentSubmitCommand.RunSubmitExecutor;

        try
        {
            BugIntentSubmitCommand.RunSubmitExecutor = (_, executionUnit) => new RunSubmitResult
            {
                ExecutionUnit = executionUnit,
                LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/58"
            };

            var exitCode = BugIntentSubmitCommand.Execute(CreateContext(repoRoot), ["BUG-123"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Bug intent-submit artifact generated for 'BUG-123'.", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Submitted execution unit: G41", writer.ToString(), StringComparison.Ordinal);

            var artifact = BugIntentSubmitArtifactYaml.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-123.intent-submit.yaml")));
            Assert.Equal(".intent-cli/bugs/BUG-123.intent-start.yaml", artifact.IntentStartRef);
            Assert.Equal("G41", artifact.SubmittedExecutionUnit);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/58", artifact.LinkedPrUrl);
            Assert.Equal(58, artifact.LinkedPrNumber);
            Assert.True(artifact.ReadyToSubmit);
        }
        finally
        {
            BugIntentSubmitCommand.RunSubmitExecutor = originalExecutor;
        }
    }

    [Fact]
    public void Execute_GivenNotReadyIntentStart_WritesNotReadyArtifactWithoutSubmitting()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-124.intent-start.yaml"),
            BugIntentStartArtifactYaml.Serialize(
                new BugIntentStartArtifact
                {
                    BugId = "BUG-124",
                    IntentEnqueueRef = ".intent-cli/bugs/BUG-124.intent-enqueue.yaml",
                    StartedExecutionUnit = null,
                    WorktreePath = null,
                    BranchName = null,
                    ReadyToStart = false
                }));
        using var writer = new StringWriter();
        var originalExecutor = BugIntentSubmitCommand.RunSubmitExecutor;

        try
        {
            BugIntentSubmitCommand.RunSubmitExecutor = (_, _) =>
                throw new InvalidOperationException("run submit should not execute for not-ready artifact.");

            var exitCode = BugIntentSubmitCommand.Execute(CreateContext(repoRoot), ["BUG-124"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Ready to submit: false", writer.ToString(), StringComparison.Ordinal);

            var artifact = BugIntentSubmitArtifactYaml.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-124.intent-submit.yaml")));
            Assert.Null(artifact.SubmittedExecutionUnit);
            Assert.Null(artifact.LinkedPrUrl);
            Assert.Null(artifact.LinkedPrNumber);
            Assert.False(artifact.ReadyToSubmit);
        }
        finally
        {
            BugIntentSubmitCommand.RunSubmitExecutor = originalExecutor;
        }
    }

    [Fact]
    public void Execute_GivenSubmitResultWithoutNumericPullRequestNumber_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-125.intent-start.yaml"),
            BugIntentStartArtifactYaml.Serialize(
                new BugIntentStartArtifact
                {
                    BugId = "BUG-125",
                    IntentEnqueueRef = ".intent-cli/bugs/BUG-125.intent-enqueue.yaml",
                    StartedExecutionUnit = "G42",
                    WorktreePath = "/tmp/worktrees/G42",
                    BranchName = "issue-53-g42",
                    ReadyToStart = true
                }));
        using var writer = new StringWriter();
        var originalExecutor = BugIntentSubmitCommand.RunSubmitExecutor;

        try
        {
            BugIntentSubmitCommand.RunSubmitExecutor = (_, executionUnit) => new RunSubmitResult
            {
                ExecutionUnit = executionUnit,
                LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/not-a-number"
            };

            var exitCode = BugIntentSubmitCommand.Execute(CreateContext(repoRoot), ["BUG-125"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("must end with a numeric pull request number", writer.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-125.intent-submit.yaml")));
        }
        finally
        {
            BugIntentSubmitCommand.RunSubmitExecutor = originalExecutor;
        }
    }

    [Fact]
    public void Execute_GivenMissingIntentStartArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = BugIntentSubmitCommand.Execute(CreateContext(repoRoot), ["BUG-123"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Bug intent-start artifact was not found", writer.ToString(), StringComparison.Ordinal);
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

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-bug-intent-submit-tests-").FullName;

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
