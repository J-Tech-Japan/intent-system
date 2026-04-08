namespace IntentSystem.Cli.Commands;

internal static class BugReportArtifactPathResolver
{
    public static string Resolve(string bugId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bugId);

        return $".intent-cli/bugs/{bugId.Trim()}.report.yaml";
    }
}
