namespace IntentSystem.Cli.Commands;

internal static class IntakeConceptArtifactWriter
{
    public static string Write(string yaml, string domain, string repoRoot)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        var relativePath = IntakeConceptArtifactPathResolver.Resolve(domain);
        var absolutePath = Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException("Intake concept artifact path did not contain a directory.");

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, yaml);

        return absolutePath;
    }
}
