using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class BugIntentCommentCommandTests
{
    [Fact]
    public void Execute_GivenGeneratedIntentReview_PostsCommentAndWritesArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var bodyPath = tempDirectory.CreateFile(Path.Combine("repo", "prepared-comment.md"), "repair in place");
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
        var originalReviewExecutor = BugIntentReviewCommand.ReviewRunExecutor;
        var originalExecutor = BugIntentCommentCommand.ReviewCommentExecutor;

        try
        {
            BugIntentReviewCommand.ReviewRunExecutor = (_, executionUnit) => new ReviewRunResult
            {
                ExecutionUnit = executionUnit,
                ArtifactPath = $".intent-cli/reviews/{executionUnit}.request.json"
            };
            BugIntentCommentCommand.ReviewCommentExecutor = (_, executionUnit, _) => new ReviewCommentResult
            {
                ExecutionUnit = executionUnit,
                ArtifactPath = $".intent-cli/reviews/{executionUnit}.comment.json",
                CommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/58#issuecomment-1"
            };

            var reviewExitCode = BugIntentReviewCommand.Execute(CreateContext(repoRoot), ["BUG-123"], TextWriter.Null);

            Assert.Equal(0, reviewExitCode);
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-123.intent-review.yaml")));

            var exitCode = BugIntentCommentCommand.Execute(
                CreateContext(repoRoot),
                ["BUG-123", "--from-file", "prepared-comment.md"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Bug intent-comment artifact generated for 'BUG-123'.", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Commented execution unit: G41", writer.ToString(), StringComparison.Ordinal);

            var artifact = BugIntentCommentArtifactYaml.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-123.intent-comment.yaml")));
            Assert.Equal(".intent-cli/bugs/BUG-123.intent-review.yaml", artifact.IntentReviewRef);
            Assert.Equal("G41", artifact.CommentedExecutionUnit);
            Assert.Equal(".intent-cli/reviews/G41.comment.json", artifact.ReviewCommentRef);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/58#issuecomment-1", artifact.CommentRef);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/58", artifact.LinkedPrUrl);
            Assert.True(artifact.ReadyToComment);
            Assert.Equal(Path.GetFullPath(bodyPath), Path.GetFullPath(bodyPath));
        }
        finally
        {
            BugIntentReviewCommand.ReviewRunExecutor = originalReviewExecutor;
            BugIntentCommentCommand.ReviewCommentExecutor = originalExecutor;
        }
    }

    [Fact]
    public void Execute_GivenNotReadyIntentReview_WritesNotReadyArtifactWithoutComment()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-124.intent-review.yaml"),
            BugIntentReviewArtifactYaml.Serialize(
                new BugIntentReviewArtifact
                {
                    BugId = "BUG-124",
                    IntentSubmitRef = ".intent-cli/bugs/BUG-124.intent-submit.yaml",
                    ReviewedExecutionUnit = null,
                    ReviewRequestRef = null,
                    LinkedPrUrl = null,
                    ReadyToReview = false
                }));
        var bodyPath = tempDirectory.CreateFile(Path.Combine("repo", "prepared-comment.md"), "repair in place");
        using var writer = new StringWriter();
        var originalExecutor = BugIntentCommentCommand.ReviewCommentExecutor;

        try
        {
            BugIntentCommentCommand.ReviewCommentExecutor = (_, _, _) =>
                throw new InvalidOperationException("review comment should not execute for not-ready artifact.");

            var exitCode = BugIntentCommentCommand.Execute(
                CreateContext(repoRoot),
                ["BUG-124", "--from-file", bodyPath],
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Ready to comment: false", writer.ToString(), StringComparison.Ordinal);

            var artifact = BugIntentCommentArtifactYaml.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-124.intent-comment.yaml")));
            Assert.Null(artifact.CommentedExecutionUnit);
            Assert.Null(artifact.ReviewCommentRef);
            Assert.Null(artifact.CommentRef);
            Assert.Null(artifact.LinkedPrUrl);
            Assert.False(artifact.ReadyToComment);
        }
        finally
        {
            BugIntentCommentCommand.ReviewCommentExecutor = originalExecutor;
        }
    }

    [Fact]
    public void Execute_GivenMissingIntentReviewArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "prepared-comment.md"), "repair in place");
        using var writer = new StringWriter();

        var exitCode = BugIntentCommentCommand.Execute(
            CreateContext(repoRoot),
            ["BUG-123", "--from-file", "prepared-comment.md"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Bug intent-review artifact was not found", writer.ToString(), StringComparison.Ordinal);
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-bug-intent-comment-tests-").FullName;

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
