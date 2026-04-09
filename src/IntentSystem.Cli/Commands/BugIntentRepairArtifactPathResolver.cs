namespace IntentSystem.Cli.Commands;

internal static class BugIntentRepairArtifactPathResolver
{
    public static string Resolve(string bugId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bugId);
        return $".intent-cli/bugs/{bugId}.intent-repair.yaml";
    }
}
