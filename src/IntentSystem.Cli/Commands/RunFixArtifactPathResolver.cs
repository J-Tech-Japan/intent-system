namespace IntentSystem.Cli.Commands;

internal static class RunFixArtifactPathResolver
{
    public static string Resolve(string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        return $".intent-cli/fix/{executionUnit}.request.md";
    }
}
