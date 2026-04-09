namespace IntentSystem.Cli.Commands;

internal static class BugExecutionArtifactPathResolver
{
    public static string Resolve(string bugId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bugId);

        return $".intent-cli/bugs/{bugId}.plan.yaml";
    }
}
