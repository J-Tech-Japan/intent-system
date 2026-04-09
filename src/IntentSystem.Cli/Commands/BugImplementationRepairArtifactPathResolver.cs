namespace IntentSystem.Cli.Commands;

internal static class BugImplementationRepairArtifactPathResolver
{
    public static string Resolve(string bugId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bugId);
        return $".intent-cli/bugs/{bugId}.implementation-repair.yaml";
    }
}
