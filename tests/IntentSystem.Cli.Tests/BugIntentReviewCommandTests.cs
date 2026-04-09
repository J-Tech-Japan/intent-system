using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class BugIntentReviewCommandTests
{
    [Fact]
    public void Execute_GivenReadyIntentSubmit_GeneratesReviewRequestAndWritesArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.intent-submit.yaml"),
            BugIntentSubmitArtifactYaml.Serialize(
                new BugIntentSubmitArtifact
                {
                    BugId = "BUG-123",
                    IntentStartRef = ".intent-cli/bugs/BUG-123.intent-start.yaml",
                    SubmittedExecutionUnit = "G41",
                    LinkedPrUrl = "https://github.com/J-Tech-Japan/intent-system/pull/58",
                    LinkedPrNumber = 58,
                    ReadyToSubmit = true
                }));
        using var writer = new StringWriter();
        var originalExecutor = BugIntentReviewCommand.ReviewRunExecutor;

        try
        {
            BugIntentReviewCommand.ReviewRunExecutor = (_, executionUnit) => new ReviewRunResult
            {
                ExecutionUnit = executionUnit,
                ArtifactPath = $".intent-cli/reviews/{executionUnit}.request.json"
            };

            var exitCode = BugIntentReviewCommand.Execute(CreateContext(repoRoot), ["BUG-123"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Bug intent-review artifact generated for 'BUG-123'.", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Reviewed execution unit: G41", writer.ToString(), StringComparison.Ordinal);

            var artifact = BugIntentReviewArtifactYaml.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-123.intent-review.yaml")));
            Assert.Equal(".intent-cli/bugs/BUG-123.intent-submit.yaml", artifact.IntentSubmitRef);
            Assert.Equal("G41", artifact.ReviewedExecutionUnit);
            Assert.Equal(".intent-cli/reviews/G41.request.json", artifact.ReviewRequestRef);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/58", artifact.LinkedPrUrl);
            Assert.True(artifact.ReadyToReview);
        }
        finally
        {
            BugIntentReviewCommand.ReviewRunExecutor = originalExecutor;
        }
    }

    [Fact]
    public void Execute_GivenNotReadyIntentSubmit_WritesNotReadyArtifactWithoutReviewRun()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-124.intent-submit.yaml"),
            BugIntentSubmitArtifactYaml.Serialize(
                new BugIntentSubmitArtifact
                {
                    BugId = "BUG-124",
                    IntentStartRef = ".intent-cli/bugs/BUG-124.intent-start.yaml",
                    SubmittedExecutionUnit = null,
                    LinkedPrUrl = null,
                    LinkedPrNumber = null,
                    ReadyToSubmit = false
                }));
        using var writer = new StringWriter();
        var originalExecutor = BugIntentReviewCommand.ReviewRunExecutor;

        try
        {
            BugIntentReviewCommand.ReviewRunExecutor = (_, _) =>
                throw new InvalidOperationException("review run should not execute for not-ready artifact.");

            var exitCode = BugIntentReviewCommand.Execute(CreateContext(repoRoot), ["BUG-124"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Ready to review: false", writer.ToString(), StringComparison.Ordinal);

            var artifact = BugIntentReviewArtifactYaml.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-124.intent-review.yaml")));
            Assert.Null(artifact.ReviewedExecutionUnit);
            Assert.Null(artifact.ReviewRequestRef);
            Assert.Null(artifact.LinkedPrUrl);
            Assert.False(artifact.ReadyToReview);
        }
        finally
        {
            BugIntentReviewCommand.ReviewRunExecutor = originalExecutor;
        }
    }

    [Fact]
    public void Execute_GivenMissingIntentSubmitArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = BugIntentReviewCommand.Execute(CreateContext(repoRoot), ["BUG-123"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Bug intent-submit artifact was not found", writer.ToString(), StringComparison.Ordinal);
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-bug-intent-review-tests-").FullName;

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
