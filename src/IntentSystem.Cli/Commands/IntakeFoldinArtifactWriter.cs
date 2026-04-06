namespace IntentSystem.Cli.Commands;

internal static class IntakeFoldinArtifactWriter
{
    public static string Write(string markdown, string domain, string repoRoot)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        var relativePath = IntakeFoldinArtifactPathResolver.Resolve(domain);
        var absolutePath = Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException("Intake fold-in artifact path did not contain a directory.");

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, markdown);

        return absolutePath;
    }
}
