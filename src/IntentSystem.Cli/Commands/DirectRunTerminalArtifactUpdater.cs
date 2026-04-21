using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

internal static class DirectRunTerminalArtifactUpdater
{
    private static readonly TimeSpan DeadSessionExitGracePeriod = TimeSpan.FromMilliseconds(250);

    public static string SynchronizeArtifactsToLatestSessionIfCurrent(
        string providerEventLogPath,
        string providerSessionId,
        DateTimeOffset launchedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventLogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSessionId);

        if (!TryResolveLatestSessionId(providerEventLogPath, launchedAt, out var latestSessionId)
            || string.IsNullOrWhiteSpace(latestSessionId)
            || string.Equals(latestSessionId, providerSessionId, StringComparison.Ordinal)
            || !TryResolveSiblingArtifactPath(providerEventLogPath, ".provider.jsonl", ".request.json", out var requestArtifactPath)
            || !File.Exists(requestArtifactPath))
        {
            return providerSessionId;
        }

        try
        {
            var requestArtifact = DirectRunRequestArtifactJson.Deserialize(File.ReadAllText(requestArtifactPath));
            if (!string.Equals(requestArtifact.ProviderSessionId, providerSessionId, StringComparison.Ordinal)
                || !DateTimeOffset.TryParse(
                    requestArtifact.LaunchedAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var requestLaunchedAt)
                || requestLaunchedAt != launchedAt)
            {
                return providerSessionId;
            }

            File.WriteAllText(
                requestArtifactPath,
                DirectRunRequestArtifactJson.Serialize(requestArtifact with
                {
                    ProviderSessionId = latestSessionId
                }));

            if (TryResolveSiblingArtifactPath(providerEventLogPath, ".provider.jsonl", ".result.json", out var resultArtifactPath)
                && File.Exists(resultArtifactPath))
            {
                var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
                if (string.Equals(resultArtifact.SessionId, providerSessionId, StringComparison.Ordinal))
                {
                    File.WriteAllText(
                        resultArtifactPath,
                        DirectRunResultArtifactJson.Serialize(resultArtifact with
                        {
                            SessionId = latestSessionId
                        }));
                }
            }

            return latestSessionId;
        }
        catch (Exception exception) when (
            exception is IOException
            or InvalidOperationException
            or ArgumentException
            or JsonException)
        {
            return providerSessionId;
        }
    }

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

        IReadOnlyList<DirectRunProviderEvent> currentProviderEvents = [];
        if (TryReadCurrentProviderEvents(
                providerEventLogPath,
                providerSessionId,
                launchedAt,
                out var resolvedCurrentProviderEvents))
        {
            currentProviderEvents = resolvedCurrentProviderEvents;
            if ((string.Equals(resultArtifact.EntryKind, "fix", StringComparison.Ordinal)
                    || string.Equals(resultArtifact.EntryKind, "implement", StringComparison.Ordinal))
                && DirectRunFixOutcomeSupport.CreateCanonicalContractGapEventIfNeeded(
                    currentProviderEvents,
                    DateTimeOffset.UtcNow,
                    resultArtifact.ExecutionUnit,
                    resultArtifact.EntryKind,
                    resultArtifact.Provider,
                    providerSessionId,
                    providerSessionAlive: true) is { } boundaryEvent)
            {
                var writer = new DirectRunProviderEventWriter(providerEventLogPath);
                writer.Append(boundaryEvent);
                currentProviderEvents = [.. currentProviderEvents, boundaryEvent];
            }
        }

        var terminalStatus = ResolveEffectiveTerminalRunStatus(
            currentProviderEvents,
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

    public static string FinalizeDeadFixSessionIfCurrent(
        string providerEventLogPath,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId,
        DateTimeOffset launchedAt,
        string currentRunStatus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventLogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentRunStatus);

        if ((!string.Equals(entryKind, "fix", StringComparison.Ordinal)
                && !string.Equals(entryKind, "implement", StringComparison.Ordinal))
            || !string.Equals(currentRunStatus, "running", StringComparison.Ordinal)
            || IsProviderSessionAlive(providerSessionId))
        {
            return currentRunStatus;
        }

        Thread.Sleep(DeadSessionExitGracePeriod);

        IReadOnlyList<DirectRunProviderEvent> currentProviderEvents = [];
        if (TryReadCurrentProviderEvents(
                providerEventLogPath,
                providerSessionId,
                launchedAt,
                out var resolvedCurrentProviderEvents))
        {
            currentProviderEvents = resolvedCurrentProviderEvents;
            var boundaryEvent = DirectRunFixOutcomeSupport.CreateCanonicalContractGapEventIfNeeded(
                currentProviderEvents,
                DateTimeOffset.UtcNow,
                executionUnit,
                entryKind,
                provider,
                providerSessionId,
                providerSessionAlive: false);
            if (boundaryEvent is not null)
            {
                var writer = new DirectRunProviderEventWriter(providerEventLogPath);
                writer.Append(boundaryEvent);
                currentProviderEvents = [.. currentProviderEvents, boundaryEvent];
            }
        }

        if (!DirectRunSessionBoundary.HasBackendExitEvent(providerEventLogPath, providerSessionId, launchedAt))
        {
            var writer = new DirectRunProviderEventWriter(providerEventLogPath);
            writer.Append(DirectRunProviderEventFactory.CreateBackendExitEvent(
                DateTimeOffset.UtcNow,
                executionUnit,
                entryKind,
                provider,
                providerSessionId,
                1));
        }

        PersistTerminalRunStatusIfCurrent(
            providerEventLogPath,
            providerSessionId,
            launchedAt,
            exitCode: 1);

        return TryReadResultRunStatusIfCurrent(
            providerEventLogPath,
            providerSessionId,
            launchedAt,
            out var updatedRunStatus)
            ? updatedRunStatus
            : "failed";
    }

    private static string ResolveEffectiveTerminalRunStatus(
        IReadOnlyList<DirectRunProviderEvent> currentProviderEvents,
        int exitCode)
    {
        if (currentProviderEvents.Any(providerEvent => IsExplicitFailureBoundary(providerEvent.Payload)))
        {
            return "failed";
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

    private static bool TryReadCurrentProviderEvents(
        string providerEventLogPath,
        string providerSessionId,
        DateTimeOffset launchedAt,
        out IReadOnlyList<DirectRunProviderEvent> currentProviderEvents)
    {
        currentProviderEvents = [];
        if (!File.Exists(providerEventLogPath))
        {
            return false;
        }

        try
        {
            var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
            currentProviderEvents = DirectRunSessionBoundary.SelectEvents(providerEvents, providerSessionId, launchedAt);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
            or InvalidOperationException
            or ArgumentException
            or JsonException)
        {
            return false;
        }
    }

    private static bool TryResolveLatestSessionId(
        string providerEventLogPath,
        DateTimeOffset launchedAt,
        out string latestSessionId)
    {
        latestSessionId = string.Empty;
        if (!File.Exists(providerEventLogPath))
        {
            return false;
        }

        try
        {
            var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
            foreach (var providerEvent in providerEvents)
            {
                if (string.IsNullOrWhiteSpace(providerEvent.SessionId)
                    || !DateTimeOffset.TryParse(
                        providerEvent.Timestamp,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var providerEventTimestamp)
                    || providerEventTimestamp < launchedAt)
                {
                    continue;
                }

                latestSessionId = providerEvent.SessionId;
            }

            return latestSessionId.Length > 0;
        }
        catch (Exception exception) when (
            exception is IOException
            or InvalidOperationException
            or ArgumentException
            or JsonException)
        {
            return false;
        }
    }

    private static bool TryReadResultRunStatusIfCurrent(
        string providerEventLogPath,
        string providerSessionId,
        DateTimeOffset launchedAt,
        out string runStatus)
    {
        runStatus = string.Empty;
        if (!TryResolveSiblingArtifactPath(providerEventLogPath, ".provider.jsonl", ".result.json", out var resultArtifactPath)
            || !TryResolveSiblingArtifactPath(providerEventLogPath, ".provider.jsonl", ".request.json", out var requestArtifactPath)
            || !File.Exists(resultArtifactPath)
            || !File.Exists(requestArtifactPath))
        {
            return false;
        }

        try
        {
            var requestArtifact = DirectRunRequestArtifactJson.Deserialize(File.ReadAllText(requestArtifactPath));
            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
            if (!string.Equals(requestArtifact.ProviderSessionId, providerSessionId, StringComparison.Ordinal)
                || !string.Equals(resultArtifact.SessionId, providerSessionId, StringComparison.Ordinal)
                || !DateTimeOffset.TryParse(
                    requestArtifact.LaunchedAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var requestLaunchedAt)
                || requestLaunchedAt != launchedAt)
            {
                return false;
            }

            runStatus = resultArtifact.RunStatus;
            return !string.IsNullOrWhiteSpace(runStatus);
        }
        catch (Exception exception) when (
            exception is IOException
            or InvalidOperationException
            or ArgumentException
            or JsonException)
        {
            return false;
        }
    }

    private static bool IsProviderSessionAlive(string providerSessionId)
    {
        if (!TryParseSessionProcessId(providerSessionId, out var processId))
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            process.Refresh();
            if (process.HasExited)
            {
                return false;
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException
            or NotSupportedException)
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        var processState = TryReadUnixProcessState(processId);
        return !string.IsNullOrWhiteSpace(processState)
            && processState.IndexOf('Z') < 0;
    }

    private static bool TryParseSessionProcessId(string providerSessionId, out int processId)
    {
        processId = default;

        const string prefix = "pid:";
        return providerSessionId.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(
                providerSessionId[prefix.Length..],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out processId);
    }

    private static string? TryReadUnixProcessState(int processId)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/ps",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList =
                {
                    "-o",
                    "stat=",
                    "-p",
                    processId.ToString(CultureInfo.InvariantCulture)
                }
            });

            if (process is null)
            {
                return null;
            }

            using (process)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output.Trim();
            }
        }
        catch (Exception exception) when (
            exception is Win32Exception
            or InvalidOperationException)
        {
            return null;
        }
    }
}
