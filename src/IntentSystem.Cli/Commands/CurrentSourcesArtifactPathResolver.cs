namespace IntentSystem.Cli.Commands;

internal static class CurrentSourcesArtifactPathResolver
{
    public static string Resolve(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        return $".intent-cli/intake/{domain}.current-sources.yaml";
    }
}
