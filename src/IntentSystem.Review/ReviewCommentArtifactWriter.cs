using IntentSystem.Review.Models;
using IntentSystem.Review.Serialization;

namespace IntentSystem.Review;

public static class ReviewCommentArtifactWriter
{
    public static void Write(ReviewCommentArtifact artifact, string executionUnit, string repoRoot, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        var relativePath = ReviewCommentArtifactPathResolver.Resolve(executionUnit);
        var absolutePath = Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!overwrite && File.Exists(absolutePath))
        {
            throw new InvalidOperationException($"Review comment artifact already exists at {absolutePath}.");
        }

        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException($"Review comment artifact path '{absolutePath}' did not contain a directory.");

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, ReviewCommentArtifactSerializer.Serialize(artifact));
    }
}
