namespace IntentSystem.Cli.Commands;

internal static class BugIntentReviewArtifactPathResolver
{
    public static string Resolve(string bugId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bugId);

        return $".intent-cli/bugs/{bugId}.intent-review.yaml";
    }
}
