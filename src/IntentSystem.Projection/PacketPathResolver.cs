namespace IntentSystem.Projection;

public static class PacketPathResolver
{
    private const string PacketsDirectory = ".intent-cli/issues";
    private const string ImplementationFileName = "implementation.md";
    private const string ReviewContextFileName = "review-context.md";
    private const string PacketYamlFileName = "packet.yaml";

    public static ResolvedPacketPaths Resolve(string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var sanitized = SanitizeExecutionUnit(executionUnit);

        return new ResolvedPacketPaths
        {
            Implementation = $"{PacketsDirectory}/{sanitized}/{ImplementationFileName}",
            ReviewContext = $"{PacketsDirectory}/{sanitized}/{ReviewContextFileName}",
            Yaml = $"{PacketsDirectory}/{sanitized}/{PacketYamlFileName}"
        };
    }

    private static string SanitizeExecutionUnit(string executionUnit)
    {
        return executionUnit.Trim();
    }
}

public sealed record ResolvedPacketPaths
{
    public required string Implementation { get; init; }
    public required string ReviewContext { get; init; }
    public required string Yaml { get; init; }
}
