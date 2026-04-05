namespace IntentSystem.Cli.Commands;

internal static class IntakeCompileArtifactWriter
{
    public static string Write(string markdown, string domain, string repoRoot)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        var relativePath = IntakeCompileArtifactPathResolver.Resolve(domain);
        var absolutePath = Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException("Intake compile artifact path did not contain a directory.");

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, markdown);

        return absolutePath;
    }
}
