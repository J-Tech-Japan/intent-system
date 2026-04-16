using System.Text.Json;

namespace IntentSystem.Cli.Commands;

internal static class DirectRunFixOutcomeSupport
{
    private const string DeterministicContractGapStopReason = "deterministic-contract-gap";
    private const string InspectionOnlyExitReason = "fix-session-ended-after-initial-inspection";
    private static readonly string[] StartupWarningMarkers =
    [
        "warn",
        "warn ",
        "warning",
        "state db discrepancy",
        "find_thread_path_by_id_str_in_subdir",
        "read_repair_rollout_path",
        "reconcile_rollout",
        "empty session file",
        "plugin manifest",
        "falling_back",
        "upsert_needed",
        "slow path"
    ];

    public static DirectRunProviderEvent? CreateCanonicalContractGapEventIfNeeded(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        DateTimeOffset timestamp,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSessionId);

        if (!string.Equals(entryKind, "fix", StringComparison.Ordinal)
            || HasExplicitContractGap(providerEvents)
            || !TryResolveInspectionOnlyFailureDetail(providerEvents, executionUnit, out var detail))
        {
            return null;
        }

        return new DirectRunProviderEvent
        {
            Timestamp = timestamp.ToString("O"),
            ExecutionUnit = executionUnit,
            Provider = provider,
            EntryKind = entryKind,
            SessionId = providerSessionId,
            Kind = "provider-event",
            Payload = JsonSerializer.SerializeToElement(new
            {
                type = "contract-gap",
                stop_reason = DeterministicContractGapStopReason,
                reason = InspectionOnlyExitReason,
                detail,
                run_status = "failed"
            })
        };
    }

    public static bool TryResolveContractGapDetail(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string executionUnit,
        out string detail)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        for (var index = providerEvents.Count - 1; index >= 0; index--)
        {
            if (!TryResolveExplicitContractGapDetail(providerEvents[index].Payload, executionUnit, out detail))
            {
                continue;
            }

            return true;
        }

        return TryResolveInspectionOnlyFailureDetail(providerEvents, executionUnit, out detail);
    }

    public static bool TryResolveStartupOnlyFailureDetail(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string executionUnit,
        out string detail)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        detail = string.Empty;
        var failingBackendExitIndex = FindFailingBackendExitIndex(providerEvents);
        if (failingBackendExitIndex < 0)
        {
            return false;
        }

        var sawStartupNoise = false;
        for (var index = 0; index < failingBackendExitIndex; index++)
        {
            var providerEvent = providerEvents[index];
            if (providerEvent.Kind == "session-metadata"
                || IsIgnorableReadyEvent(providerEvent.Payload))
            {
                continue;
            }

            if (TryResolveExplicitContractGapDetail(providerEvent.Payload, executionUnit, out _)
                || ContainsSuccessfulInitialRepoInspection(providerEvent.Payload)
                || !IsIgnorableStartupNoise(providerEvent.Payload))
            {
                return false;
            }

            sawStartupNoise = true;
        }

        if (!sawStartupNoise)
        {
            return false;
        }

        detail =
            $"Fix direct run for '{executionUnit}' exited during provider startup before any bounded repo inspection, edit, test, refusal, or contract-gap output was emitted. Current-session provider output only contained startup warnings or noise before the backend exit.";
        return true;
    }

    private static bool TryResolveInspectionOnlyFailureDetail(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string executionUnit,
        out string detail)
    {
        detail = string.Empty;

        var failingBackendExitIndex = FindFailingBackendExitIndex(providerEvents);
        if (failingBackendExitIndex < 0)
        {
            return false;
        }

        var initialInspectionEventIndex = -1;
        for (var index = 0; index < failingBackendExitIndex; index++)
        {
            var providerEvent = providerEvents[index];
            if (providerEvent.Kind == "session-metadata"
                || IsIgnorableReadyEvent(providerEvent.Payload))
            {
                continue;
            }

            if (TryResolveExplicitContractGapDetail(providerEvent.Payload, executionUnit, out _))
            {
                return false;
            }

            if (ContainsSuccessfulInitialRepoInspection(providerEvent.Payload))
            {
                if (initialInspectionEventIndex >= 0)
                {
                    return false;
                }

                initialInspectionEventIndex = index;
                continue;
            }

            return false;
        }

        if (initialInspectionEventIndex < 0)
        {
            return false;
        }

        detail =
            $"Fix direct run for '{executionUnit}' exited after the initial repo-inspection command completed without any repair, test, refusal, or contract-gap outcome.";
        return true;
    }

    private static int FindFailingBackendExitIndex(IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        for (var index = providerEvents.Count - 1; index >= 0; index--)
        {
            var payload = providerEvents[index].Payload;
            if (payload.ValueKind != JsonValueKind.Object
                || !TryReadString(payload, "type", out var type)
                || !string.Equals(type, "backend-exit", StringComparison.Ordinal)
                || !TryReadInt32(payload, "exit_code", out var exitCode))
            {
                continue;
            }

            if (exitCode != 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool HasExplicitContractGap(IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        return providerEvents.Any(providerEvent =>
            TryResolveExplicitContractGapDetail(providerEvent.Payload, providerEvent.ExecutionUnit ?? "fix", out _));
    }

    private static bool TryResolveExplicitContractGapDetail(
        JsonElement payload,
        string executionUnit,
        out string detail)
    {
        detail = string.Empty;
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryReadString(payload, "stop_reason", out var stopReason)
            && string.Equals(stopReason, DeterministicContractGapStopReason, StringComparison.Ordinal))
        {
            if (!TryReadString(payload, "detail", out detail))
            {
                detail = $"Fix direct run for '{executionUnit}' reported a deterministic contract gap.";
            }

            return true;
        }

        if (TryReadString(payload, "type", out var type)
            && string.Equals(type, "contract-gap", StringComparison.Ordinal))
        {
            if (!TryReadString(payload, "detail", out detail))
            {
                detail = $"Fix direct run for '{executionUnit}' reported a deterministic contract gap.";
            }

            return true;
        }

        return false;
    }

    private static bool ContainsSuccessfulInitialRepoInspection(JsonElement payload)
    {
        var sawRepoListing = false;
        var sawSuccess = false;

        foreach (var value in EnumeratePayloadStrings(payload))
        {
            var normalized = value.Trim().ToLowerInvariant();
            if (normalized.Contains("rg --files", StringComparison.Ordinal))
            {
                sawRepoListing = true;
            }

            if (normalized.Contains("succeeded", StringComparison.Ordinal)
                || normalized.Contains("exit code 0", StringComparison.Ordinal)
                || normalized.Contains("exit_code=0", StringComparison.Ordinal))
            {
                sawSuccess = true;
            }
        }

        if (payload.ValueKind == JsonValueKind.Object
            && (TryReadInt32(payload, "exit_code", out var exitCode)
                || TryReadInt32(payload, "exitCode", out exitCode))
            && exitCode == 0)
        {
            sawSuccess = true;
        }

        return sawRepoListing && sawSuccess;
    }

    private static bool IsIgnorableReadyEvent(JsonElement payload)
    {
        return payload.ValueKind == JsonValueKind.Object
            && TryReadString(payload, "type", out var type)
            && string.Equals(type, "ready", StringComparison.Ordinal);
    }

    private static bool IsIgnorableStartupNoise(JsonElement payload)
    {
        var sawString = false;
        foreach (var value in EnumeratePayloadStrings(payload))
        {
            sawString = true;
            var normalized = value.Trim().ToLowerInvariant();
            if (!StartupWarningMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        return sawString;
    }

    private static IEnumerable<string> EnumeratePayloadStrings(JsonElement payload)
    {
        switch (payload.ValueKind)
        {
            case JsonValueKind.String:
            {
                var value = payload.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }

                yield break;
            }
            case JsonValueKind.Object:
                foreach (var property in payload.EnumerateObject())
                {
                    foreach (var value in EnumeratePayloadStrings(property.Value))
                    {
                        yield return value;
                    }
                }

                yield break;
            case JsonValueKind.Array:
                foreach (var item in payload.EnumerateArray())
                {
                    foreach (var value in EnumeratePayloadStrings(item))
                    {
                        yield return value;
                    }
                }

                yield break;
            default:
                yield break;
        }
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
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadInt32(JsonElement payload, string propertyName, out int value)
    {
        value = default;

        if (!payload.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetInt32(out value);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return int.TryParse(
                element.GetString(),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        return false;
    }
}
