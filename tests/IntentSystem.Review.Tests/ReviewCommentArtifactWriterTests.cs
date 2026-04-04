using IntentSystem.Review.Models;
using IntentSystem.Review.Serialization;

namespace IntentSystem.Review.Tests;

public sealed class ReviewCommentArtifactWriterTests
{
    [Fact]
    public void Resolve_GivenExecutionUnit_UsesCommentJsonBaselinePath()
    {
        Assert.Equal(".intent-cli/reviews/G10.comment.json", ReviewCommentArtifactPathResolver.Resolve("G10"));
    }

    [Fact]
    public void Write_GivenArtifact_WritesSerializedArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var artifact = CreateArtifact();

        ReviewCommentArtifactWriter.Write(artifact, "G10", repoRoot, overwrite: false);

        var artifactPath = Path.Combine(repoRoot, ".intent-cli", "reviews", "G10.comment.json");
        Assert.True(File.Exists(artifactPath));
        Assert.Equal(ReviewCommentArtifactSerializer.Serialize(artifact), File.ReadAllText(artifactPath));
    }

    private static ReviewCommentArtifact CreateArtifact()
    {
        return new ReviewCommentArtifact
        {
            ExecutionUnit = "G10",
            ReviewRequestRef = ".intent-cli/reviews/G10.request.json",
            LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/46",
            CommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/46#issuecomment-1",
            BodyPath = "/tmp/G10-comment.md"
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-review-comment-writer-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
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
