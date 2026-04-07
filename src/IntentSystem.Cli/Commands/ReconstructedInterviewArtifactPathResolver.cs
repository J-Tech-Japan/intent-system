namespace IntentSystem.Cli.Commands;

internal static class ReconstructedInterviewArtifactPathResolver
{
    public static string Resolve(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        return $".intent-cli/intake/{domain}.reconstructed-interview.md";
    }
}
