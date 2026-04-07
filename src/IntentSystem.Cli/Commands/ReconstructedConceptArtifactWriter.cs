namespace IntentSystem.Cli.Commands;

internal static class ReconstructedConceptArtifactWriter
{
    public static string Write(string repoRoot, string domain, ReconstructedConceptArtifact artifact)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(artifact);

        var relativePath = ReconstructedConceptArtifactPathResolver.Resolve(domain);
        var artifactPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directoryPath = Path.GetDirectoryName(artifactPath)
            ?? throw new InvalidOperationException("Reconstructed concept artifact path did not contain a directory.");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(artifactPath, ReconstructedConceptArtifactYaml.Serialize(artifact));
        return artifactPath;
    }
}
