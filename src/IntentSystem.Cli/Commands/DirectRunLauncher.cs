namespace IntentSystem.Cli.Commands;

internal sealed class DirectRunLauncher : IDirectRunLauncher
{
    public DirectRunLaunchResult Launch(
        string executionUnit,
        string entryKind,
        string requestArtifactPath,
        string provider,
        string model,
        string transport,
        DateTimeOffset launchedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestArtifactPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);

        return new DirectRunLaunchResult
        {
            RequestArtifactPath = requestArtifactPath,
            Provider = provider,
            Model = model,
            Transport = transport,
            ProviderSessionId = $"{Normalize(provider)}-{Normalize(entryKind)}-{Normalize(executionUnit)}-{launchedAt:yyyyMMddHHmmss}",
            TransportSummary = $"{transport} transport selected for provider '{provider}' with model '{model}'."
        };
    }

    private static string Normalize(string value)
    {
        var builder = new List<char>(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Add(char.ToLowerInvariant(character));
                continue;
            }

            if (builder.Count == 0 || builder[^1] == '-')
            {
                continue;
            }

            builder.Add('-');
        }

        while (builder.Count > 0 && builder[^1] == '-')
        {
            builder.RemoveAt(builder.Count - 1);
        }

        return builder.Count == 0 ? "session" : new string(builder.ToArray());
    }
}
