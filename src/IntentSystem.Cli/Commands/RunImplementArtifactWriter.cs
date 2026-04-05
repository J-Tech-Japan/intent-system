namespace IntentSystem.Cli.Commands;

internal static class RunImplementArtifactWriter
{
    public static string Write(string markdown, string executionUnit, string repoRoot, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        var relativePath = RunImplementArtifactPathResolver.Resolve(executionUnit);
        var absolutePath = Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!overwrite && File.Exists(absolutePath))
        {
            throw new InvalidOperationException($"Run implement artifact already exists at {absolutePath}");
        }

        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException("Run implement artifact path did not contain a directory.");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, markdown);

        return absolutePath;
    }
}
