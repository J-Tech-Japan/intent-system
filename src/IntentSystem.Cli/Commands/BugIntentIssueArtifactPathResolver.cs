namespace IntentSystem.Cli.Commands;

internal static class BugIntentIssueArtifactPathResolver
{
    public static string Resolve(string bugId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bugId);

        return $".intent-cli/bugs/{bugId}.intent-issue.yaml";
    }
}
