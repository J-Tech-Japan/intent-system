namespace IntentSystem.Cli.Commands;

internal static class CurrentSourcesArtifactWriter
{
    public static string Write(string repoRoot, string domain, CurrentSourcesArtifact artifact)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(artifact);

        var relativePath = CurrentSourcesArtifactPathResolver.Resolve(domain);
        var artifactPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directoryPath = Path.GetDirectoryName(artifactPath)
            ?? throw new InvalidOperationException("Current sources artifact path did not contain a directory.");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(artifactPath, CurrentSourcesArtifactYaml.Serialize(artifact));
        return artifactPath;
    }
}
