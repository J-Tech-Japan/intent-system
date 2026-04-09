namespace IntentSystem.Cli.Commands;

internal static class BugIntentSubmitArtifactPathResolver
{
    public static string Resolve(string bugId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bugId);

        return $".intent-cli/bugs/{bugId}.intent-submit.yaml";
    }
}
