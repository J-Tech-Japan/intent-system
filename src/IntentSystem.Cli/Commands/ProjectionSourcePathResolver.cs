namespace IntentSystem.Cli.Commands;

internal static class ProjectionSourcePathResolver
{
    private const string SourcesDirectoryName = "sources";
    private const string SourceFileName = "source.json";

    public static string Resolve(string repoRoot, string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        return Path.Combine(
            CliRuntimeContracts.GetIntentCliDirectoryPath(repoRoot),
            SourcesDirectoryName,
            executionUnit,
            SourceFileName);
    }
}
