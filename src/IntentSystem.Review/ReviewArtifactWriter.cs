using IntentSystem.Review.Models;
using IntentSystem.Review.Serialization;

namespace IntentSystem.Review;

public static class ReviewArtifactWriter
{
    public static void Write(ReviewRequest request, string executionUnit, string repoRoot, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        var relativePath = ReviewArtifactPathResolver.Resolve(executionUnit);
        var absolutePath = Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!overwrite && File.Exists(absolutePath))
        {
            throw new InvalidOperationException($"Review request artifact already exists at {absolutePath}.");
        }

        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException($"Review request artifact path '{absolutePath}' did not contain a directory.");

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, ReviewRequestSerializer.Serialize(request));
    }
}
