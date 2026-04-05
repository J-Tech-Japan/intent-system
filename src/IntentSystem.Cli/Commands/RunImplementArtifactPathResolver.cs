namespace IntentSystem.Cli.Commands;

internal static class RunImplementArtifactPathResolver
{
    private const string ImplementDirectory = ".intent-cli/implement";

    public static string Resolve(string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        return $"{ImplementDirectory}/{executionUnit.Trim()}.request.md";
    }
}
