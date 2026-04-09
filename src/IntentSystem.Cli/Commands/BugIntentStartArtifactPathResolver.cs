namespace IntentSystem.Cli.Commands;

internal static class BugIntentStartArtifactPathResolver
{
    public static string Resolve(string bugId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bugId);

        return $".intent-cli/bugs/{bugId}.intent-start.yaml";
    }
}
