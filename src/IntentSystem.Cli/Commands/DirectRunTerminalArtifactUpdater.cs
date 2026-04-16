using System.Globalization;

namespace IntentSystem.Cli.Commands;

internal static class DirectRunTerminalArtifactUpdater
{
    public static void PersistTerminalRunStatusIfCurrent(
        string providerEventLogPath,
        string providerSessionId,
        DateTimeOffset launchedAt,
        int exitCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventLogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSessionId);

        if (!TryResolveSiblingArtifactPath(providerEventLogPath, ".provider.jsonl", ".result.json", out var resultArtifactPath)
            || !TryResolveSiblingArtifactPath(providerEventLogPath, ".provider.jsonl", ".request.json", out var requestArtifactPath)
            || !File.Exists(resultArtifactPath)
            || !File.Exists(requestArtifactPath))
        {
            return;
        }

        DirectRunRequestArtifact requestArtifact;
        DirectRunResultArtifact resultArtifact;
        try
        {
            requestArtifact = DirectRunRequestArtifactJson.Deserialize(File.ReadAllText(requestArtifactPath));
            resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
        }
        catch (Exception exception) when (
            exception is IOException
            or InvalidOperationException
            or ArgumentException
            or System.Text.Json.JsonException)
        {
            return;
        }

        if (!string.Equals(requestArtifact.ProviderSessionId, providerSessionId, StringComparison.Ordinal)
            || !string.Equals(resultArtifact.SessionId, providerSessionId, StringComparison.Ordinal)
            || !DateTimeOffset.TryParse(
                requestArtifact.LaunchedAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var requestLaunchedAt)
            || requestLaunchedAt != launchedAt)
        {
            return;
        }

        var terminalStatus = exitCode == 0 ? "succeeded" : "failed";
        if (string.Equals(resultArtifact.RunStatus, terminalStatus, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            File.WriteAllText(
                resultArtifactPath,
                DirectRunResultArtifactJson.Serialize(resultArtifact with
                {
                    RunStatus = terminalStatus
                }));
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
        }
    }

    private static bool TryResolveSiblingArtifactPath(
        string providerEventLogPath,
        string expectedSuffix,
        string replacementSuffix,
        out string artifactPath)
    {
        artifactPath = string.Empty;
        if (!providerEventLogPath.EndsWith(expectedSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        artifactPath = providerEventLogPath[..^expectedSuffix.Length] + replacementSuffix;
        return true;
    }
}
