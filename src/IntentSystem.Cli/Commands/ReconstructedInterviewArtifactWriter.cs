namespace IntentSystem.Cli.Commands;

internal static class ReconstructedInterviewArtifactWriter
{
    public static string Write(string repoRoot, string domain, string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(markdown);

        var relativePath = ReconstructedInterviewArtifactPathResolver.Resolve(domain);
        var artifactPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directoryPath = Path.GetDirectoryName(artifactPath)
            ?? throw new InvalidOperationException("Reconstructed interview artifact path did not contain a directory.");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(artifactPath, markdown);
        return artifactPath;
    }
}
