namespace IntentSystem.Cli.Commands;

internal static class RunSupervisionSessionArtifactPathResolver
{
    public static string Resolve(string artifactRoot, string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var normalizedRoot = artifactRoot.Trim().TrimEnd('/', '\\');
        var normalizedUnit = executionUnit.Trim();

        return $"{normalizedRoot.Replace('\\', '/')}/{normalizedUnit}.session.json";
    }
}
