namespace IntentSystem.Cli.Commands;

internal static class ReconstructedConceptArtifactPathResolver
{
    public static string Resolve(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        return $".intent-cli/intake/{domain}.reconstructed-concept.yaml";
    }
}
