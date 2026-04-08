namespace IntentSystem.Cli.Commands;

internal static class BugTriageArtifactPathResolver
{
    public static string Resolve(string bugId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bugId);

        return $".intent-cli/bugs/{bugId}.triage.yaml";
    }
}
