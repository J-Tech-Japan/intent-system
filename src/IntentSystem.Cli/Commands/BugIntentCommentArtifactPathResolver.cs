namespace IntentSystem.Cli.Commands;

internal static class BugIntentCommentArtifactPathResolver
{
    public static string Resolve(string bugId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bugId);

        return $".intent-cli/bugs/{bugId}.intent-comment.yaml";
    }
}
