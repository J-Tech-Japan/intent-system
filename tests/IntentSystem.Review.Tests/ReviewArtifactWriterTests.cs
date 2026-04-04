using IntentSystem.Review.Models;
using IntentSystem.Review.Serialization;

namespace IntentSystem.Review.Tests;

public sealed class ReviewArtifactWriterTests
{
    [Fact]
    public void Resolve_GivenExecutionUnit_UsesRequestJsonBaselinePath()
    {
        Assert.Equal(".intent-cli/reviews/G9.request.json", ReviewArtifactPathResolver.Resolve("G9"));
    }

    [Fact]
    public void Write_GivenRequest_WritesSerializedArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var request = CreateRequest();

        ReviewArtifactWriter.Write(request, "G9", repoRoot, overwrite: false);

        var artifactPath = Path.Combine(repoRoot, ".intent-cli", "reviews", "G9.request.json");
        Assert.True(File.Exists(artifactPath));
        Assert.Equal(ReviewRequestSerializer.Serialize(request), File.ReadAllText(artifactPath));
    }

    private static ReviewRequest CreateRequest()
    {
        return new ReviewRequest
        {
            ExecutionUnit = "G9",
            ReviewContextRef = ".intent-cli/issues/G9/review-context.md",
            LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/45",
            DeterministicReviewChecks = ["input is deterministic"],
            AcceptanceCriteria = ["review request artifact exists"],
            ExpectedEvidence = ["dotnet test IntentSystem.sln"]
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-review-writer-tests-").FullName;

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
