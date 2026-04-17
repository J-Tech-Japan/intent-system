using System.Globalization;
using System.Text.Json;

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

        var terminalStatus = ResolveEffectiveTerminalRunStatus(
            providerEventLogPath,
            providerSessionId,
            launchedAt,
            exitCode);
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

    private static string ResolveEffectiveTerminalRunStatus(
        string providerEventLogPath,
        string providerSessionId,
        DateTimeOffset launchedAt,
        int exitCode)
    {
        try
        {
            var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
            var currentProviderEvents = DirectRunSessionBoundary.SelectEvents(
                providerEvents,
                providerSessionId,
                launchedAt);
            if (currentProviderEvents.Any(providerEvent => IsExplicitFailureBoundary(providerEvent.Payload)))
            {
                return "failed";
            }
        }
        catch (Exception exception) when (
            exception is IOException
            or InvalidOperationException
            or ArgumentException
            or JsonException)
        {
        }

        return exitCode == 0 ? "succeeded" : "failed";
    }

    private static bool IsExplicitFailureBoundary(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryReadString(payload, "run_status", out var runStatus)
            && string.Equals(runStatus, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (TryReadString(payload, "status", out var status)
            && string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (TryReadString(payload, "disposition", out var disposition))
        {
            var normalized = disposition.Trim().ToLowerInvariant();
            if (normalized is "comment" or "commented" or "fix-requested" or "changes-requested" or "failed")
            {
                return true;
            }
        }

        return TryReadString(payload, "type", out var type)
            && string.Equals(type, "contract-gap", StringComparison.Ordinal);
    }

    private static bool TryReadString(JsonElement payload, string propertyName, out string value)
    {
        value = string.Empty;
        if (!payload.TryGetProperty(propertyName, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return value.Length > 0;
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
