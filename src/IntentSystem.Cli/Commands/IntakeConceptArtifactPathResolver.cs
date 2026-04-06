namespace IntentSystem.Cli.Commands;

internal static class IntakeConceptArtifactPathResolver
{
    private const string IntakeDirectory = ".intent-cli/intake";

    public static string Resolve(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        return $"{IntakeDirectory}/{domain.Trim()}.concept.yaml";
    }
}
