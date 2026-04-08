namespace IntentSystem.Cli.Commands;

internal static class RunSupervisionSessionArtifactPathResolver
{
    public static string Resolve(string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        return $".intent-cli/supervision/{executionUnit.Trim()}.session.json";
    }
}
