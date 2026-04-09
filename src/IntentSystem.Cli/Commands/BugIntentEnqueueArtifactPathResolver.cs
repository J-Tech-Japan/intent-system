namespace IntentSystem.Cli.Commands;

internal static class BugIntentEnqueueArtifactPathResolver
{
    public static string Resolve(string bugId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bugId);

        return $".intent-cli/bugs/{bugId}.intent-enqueue.yaml";
    }
}
