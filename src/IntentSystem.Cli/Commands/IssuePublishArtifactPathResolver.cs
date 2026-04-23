namespace IntentSystem.Cli.Commands;

internal static class IssuePublishArtifactPathResolver
{
    public static string Resolve(string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        return $".intent-cli/issues/{executionUnit}/publish.yaml";
    }
}
