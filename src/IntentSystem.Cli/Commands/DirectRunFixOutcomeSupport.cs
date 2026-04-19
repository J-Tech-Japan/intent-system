using System.Text.Json;

namespace IntentSystem.Cli.Commands;

internal static class DirectRunFixOutcomeSupport
{
    private const string DeterministicContractGapStopReason = "deterministic-contract-gap";
    private const string ExplicitContractGapRefusalReason = "provider-explicit-contract-gap-refusal";
    private const string InspectionOnlyExitReason = "fix-session-ended-after-initial-inspection";
    private const string ProviderBackendEndedBeforeSpecSourceReadReason = "fix-session-ended-before-spec-source-test-read";
    private const string MissingTerminalCaptureAfterRequestReadReason = "fix-session-terminal-boundary-missing-after-request-reread";
    private const string MissingTerminalCaptureAfterDeepProgressReason = "fix-session-terminal-boundary-missing-after-deep-progress";
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
    private static readonly string[] StartupPreamblePrefixes =
    [
        "openai codex v",
        "--------",
        "workdir:",
        "model:",
        "provider:",
        "approval:",
        "sandbox:",
        "reasoning effort:",
        "reasoning summaries:",
        "session id:",
        "user"
    ];
    private static readonly string[] EchoedStartupRequestMarkers =
    [
        "please ",
        "use the request artifact at",
        "bounded source of truth for this direct run",
        "continue beyond initial repository inspection",
        "do not stop after a single inspection command"
    ];
    private static readonly string[] PlanningPreambleContractGapPrefixes =
    [
        "close this run as",
        "close the run as",
        "mark this run as",
        "mark the run as"
    ];
    private static readonly string[] PlanningPreambleContractGapMarkers =
    [
        "opening the request artifact",
        "decide whether this is a repair or a contract-gap refusal",
        "decide whether this is a repair or a contract gap refusal"
    ];

    public static DirectRunProviderEvent? CreateCanonicalContractGapEventIfNeeded(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        DateTimeOffset timestamp,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId,
        bool providerSessionAlive = true)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSessionId);

        if (!string.Equals(entryKind, "fix", StringComparison.Ordinal)
            || HasCanonicalContractGap(providerEvents))
        {
            return null;
        }

        if (TryCreateExplicitContractGapEvent(
                providerEvents,
                timestamp,
                executionUnit,
                entryKind,
                provider,
                providerSessionId,
                out var explicitContractGapEvent))
        {
            return explicitContractGapEvent;
        }

        if (!TryResolveCanonicalFailureDetail(
                providerEvents,
                executionUnit,
                providerSessionAlive,
                out var reason,
                out var detail))
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
                reason,
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
                || ContainsSuccessfulInitialRepoInspection(providerEvent.Payload))
            {
                return false;
            }

            if (!sawStartupNoise
                && IsIgnorableStartupPreamble(providerEvent.Payload))
            {
                continue;
            }

            if (ContainsBoundedFixProgressSignal(providerEvent.Payload))
            {
                return false;
            }

            if (IsIgnorableStartupNoise(providerEvent.Payload))
            {
                sawStartupNoise = true;
                continue;
            }

            return false;
        }

        if (!sawStartupNoise)
        {
            return false;
        }

        detail =
            $"Fix direct run for '{executionUnit}' exited during provider startup before any bounded repo inspection, edit, test, refusal, or contract-gap output was emitted. Current-session provider output only contained startup warnings or noise before the backend exit.";
        return true;
    }

    public static bool HasBoundedProgressSignal(IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);

        return providerEvents.Any(providerEvent => ContainsBoundedFixProgressSignal(providerEvent.Payload));
    }

    public static bool HasPlanningProgressSignalBeyondInitialInventory(IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);

        return HasSpecAndProductReadProgressSignal(providerEvents);
    }

    public static bool TryResolveNoOpSuccessDetail(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string executionUnit,
        out string detail)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        detail = string.Empty;
        if (!HasSuccessfulBackendExit(providerEvents))
        {
            return false;
        }

        for (var index = providerEvents.Count - 1; index >= 0; index--)
        {
            if (!TryResolveNoOpSuccessDetail(providerEvents[index].Payload, out detail))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    internal static bool HasExplicitContractGapSignal(IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);

        return HasExplicitContractGap(providerEvents);
    }

    internal static bool HasSpecAndProductReadProgressSignal(IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);

        var sawSpecRead = HasSuccessfulRepoLocalSpecRead(providerEvents);
        var sawProductRead = providerEvents.Any(providerEvent =>
                !IsIgnorableStartupPreamble(providerEvent.Payload)
                && ContainsProductSourceOrTestReadAttempt(providerEvent.Payload));

        return sawSpecRead && sawProductRead;
    }

    internal static bool HasDeepExecutionProgressSignal(IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);

        var observedRequestReread = providerEvents.Any(providerEvent => ContainsRequestArtifactRead(providerEvent.Payload));
        var observedProductRead = providerEvents.Any(providerEvent =>
            !IsIgnorableStartupPreamble(providerEvent.Payload)
            && ContainsProductSourceOrTestReadAttempt(providerEvent.Payload));
        var observedDotNetTest = providerEvents.Any(providerEvent =>
            !IsIgnorableStartupPreamble(providerEvent.Payload)
            && ContainsDotNetTestAttempt(providerEvent.Payload));

        return observedRequestReread
            && (observedProductRead || observedDotNetTest);
    }

    private static bool TryResolveCanonicalFailureDetail(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string executionUnit,
        bool providerSessionAlive,
        out string reason,
        out string detail)
    {
        if (TryResolveMissingTerminalAfterDeepProgressDetail(
                providerEvents,
                executionUnit,
                providerSessionAlive,
                out reason,
                out detail))
        {
            return true;
        }

        if (TryResolvePostRequestParityBoundaryDetail(
                providerEvents,
                executionUnit,
                providerSessionAlive,
                out reason,
                out detail))
        {
            return true;
        }

        reason = InspectionOnlyExitReason;
        return TryResolveInspectionOnlyFailureDetail(providerEvents, executionUnit, out detail);
    }

    private static bool TryResolveMissingTerminalAfterDeepProgressDetail(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string executionUnit,
        bool providerSessionAlive,
        out string reason,
        out string detail)
    {
        detail = string.Empty;
        reason = string.Empty;

        if (providerSessionAlive
            || HasCapturedSuccessfulTerminalOutcome(providerEvents)
            || FindFailingBackendExitIndex(providerEvents) >= 0
            || !HasDeepExecutionProgressSignal(providerEvents))
        {
            return false;
        }

        var observedRequestReread = providerEvents.Any(providerEvent => ContainsRequestArtifactRead(providerEvent.Payload));
        var observedInventory = providerEvents.Any(providerEvent => ContainsInitialRepoInventory(providerEvent.Payload));
        var observedSpecRead = HasSuccessfulRepoLocalSpecRead(providerEvents);
        var observedProductRead = providerEvents.Any(providerEvent => ContainsProductSourceOrTestReadAttempt(providerEvent.Payload));
        var observedDotNetTest = providerEvents.Any(providerEvent => ContainsDotNetTestAttempt(providerEvent.Payload));

        reason = MissingTerminalCaptureAfterDeepProgressReason;
        detail =
            $"Fix direct run for '{executionUnit}' reached deeper bounded work before the provider session died, but no same-session terminal outcome was captured. Current-session evidence observed request_reread={observedRequestReread}, repo_inventory={observedInventory}, repo_local_spec_read={observedSpecRead}, product_source_or_test_read={observedProductRead}, dotnet_test={observedDotNetTest}. The provider session is no longer alive, but neither backend-exit nor an explicit contract-gap was persisted for that same session, so the child runtime must synthesize a deterministic missing-terminal boundary instead of leaving run_status=running.";
        return true;
    }

    private static bool TryResolvePostRequestParityBoundaryDetail(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string executionUnit,
        bool providerSessionAlive,
        out string reason,
        out string detail)
    {
        detail = string.Empty;
        reason = string.Empty;

        var observedRequestReread = providerEvents.Any(providerEvent => ContainsRequestArtifactRead(providerEvent.Payload));
        if (!observedRequestReread
            || HasCapturedSuccessfulTerminalOutcome(providerEvents)
            || HasSpecAndProductReadProgressSignal(providerEvents))
        {
            return false;
        }

        var observedInventory = providerEvents.Any(providerEvent => ContainsInitialRepoInventory(providerEvent.Payload));
        var observedSpecRead = HasSuccessfulRepoLocalSpecRead(providerEvents);
        var observedProductRead = providerEvents.Any(providerEvent => ContainsProductSourceOrTestReadAttempt(providerEvent.Payload));
        var failingBackendExitIndex = FindFailingBackendExitIndex(providerEvents);
        if (failingBackendExitIndex >= 0)
        {
            reason = ProviderBackendEndedBeforeSpecSourceReadReason;
            detail =
                $"Fix direct run for '{executionUnit}' stopped before repo-local spec/source/test planning reads. Current-session evidence observed request_reread={observedRequestReread}, repo_inventory={observedInventory}, repo_local_spec_read={observedSpecRead}, product_source_or_test_read={observedProductRead}. A failing backend-exit was captured before any repo-local spec or product source/test read, which indicates the provider backend itself exited before the next bounded read.";
            return true;
        }

        if (providerSessionAlive)
        {
            return false;
        }

        reason = MissingTerminalCaptureAfterRequestReadReason;
        detail =
            $"Fix direct run for '{executionUnit}' stopped before repo-local spec/source/test planning reads. Current-session evidence observed request_reread={observedRequestReread}, repo_inventory={observedInventory}, repo_local_spec_read={observedSpecRead}, product_source_or_test_read={observedProductRead}. The provider session is no longer alive, but no backend-exit or later bounded-read event was captured for the current session. This indicates the detached helper/current-session synthesis event capture dropped after the request reread layer rather than a completed repair attempt.";
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

    private static bool HasCapturedSuccessfulTerminalOutcome(IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        return HasSuccessfulBackendExit(providerEvents)
            && providerEvents.Any(providerEvent => ContainsSuccessfulTerminalNarrative(providerEvent.Payload));
    }

    private static bool HasSuccessfulBackendExit(IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        return providerEvents.Any(providerEvent =>
        {
            var payload = providerEvent.Payload;
            return payload.ValueKind == JsonValueKind.Object
                && TryReadString(payload, "type", out var type)
                && string.Equals(type, "backend-exit", StringComparison.Ordinal)
                && TryReadInt32(payload, "exit_code", out var exitCode)
                && exitCode == 0;
        });
    }

    private static bool ContainsSuccessfulTerminalNarrative(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var value = payload.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        var lower = normalized.ToLowerInvariant();
        if (IsIgnorableStartupPreamble(payload)
            || IsIgnorableStartupNoise(payload)
            || lower == "exec"
            || lower == "tokens used"
            || lower.StartsWith("succeeded in ", StringComparison.Ordinal)
            || lower.StartsWith("failed in ", StringComparison.Ordinal)
            || lower.StartsWith("exited ", StringComparison.Ordinal)
            || lower.StartsWith("/bin/", StringComparison.Ordinal)
            || lower.StartsWith("sed: ", StringComparison.Ordinal)
            || lower.Contains("rg --files", StringComparison.Ordinal)
            || lower.Contains("sed -n", StringComparison.Ordinal)
            || lower.Contains("dotnet test", StringComparison.Ordinal)
            || lower.Contains("git status", StringComparison.Ordinal)
            || lower.Contains("git diff", StringComparison.Ordinal)
            || lower.Contains("apply_patch", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static bool TryResolveNoOpSuccessDetail(JsonElement payload, out string detail)
    {
        detail = string.Empty;
        if (payload.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var value = payload.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        var lower = normalized.ToLowerInvariant();
        if (!lower.Contains("without requiring code changes", StringComparison.Ordinal)
            || !lower.Contains("already matches", StringComparison.Ordinal))
        {
            return false;
        }

        detail = normalized;
        return true;
    }

    private static bool TryCreateExplicitContractGapEvent(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        DateTimeOffset timestamp,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId,
        out DirectRunProviderEvent? providerEvent)
    {
        providerEvent = null;
        if (!TryResolveExplicitContractGapDetail(providerEvents, executionUnit, out var detail))
        {
            return false;
        }

        providerEvent = new DirectRunProviderEvent
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
                reason = ExplicitContractGapRefusalReason,
                detail,
                run_status = "failed"
            })
        };

        return true;
    }

    private static bool HasExplicitContractGap(IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        return providerEvents.Any(providerEvent =>
            TryResolveExplicitContractGapDetail(providerEvent.Payload, providerEvent.ExecutionUnit ?? "fix", out _));
    }

    private static bool HasCanonicalContractGap(IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        return providerEvents.Any(providerEvent =>
            providerEvent.Payload.ValueKind == JsonValueKind.Object
            && ((TryReadString(providerEvent.Payload, "stop_reason", out var stopReason)
                    && string.Equals(stopReason, DeterministicContractGapStopReason, StringComparison.Ordinal))
                || (TryReadString(providerEvent.Payload, "type", out var type)
                    && string.Equals(type, "contract-gap", StringComparison.Ordinal))));
    }

    private static bool TryResolveExplicitContractGapDetail(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string executionUnit,
        out string detail)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        for (var index = providerEvents.Count - 1; index >= 0; index--)
        {
            if (TryResolveExplicitContractGapDetail(providerEvents[index].Payload, executionUnit, out detail))
            {
                return true;
            }
        }

        detail = string.Empty;
        return false;
    }

    private static bool TryResolveExplicitContractGapDetail(
        JsonElement payload,
        string executionUnit,
        out string detail)
    {
        detail = string.Empty;
        if (payload.ValueKind == JsonValueKind.String
            && IsExplicitContractGapRefusalText(payload.GetString(), out var explicitDetail))
        {
            detail = explicitDetail;
            return true;
        }

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

    private static bool IsExplicitContractGapRefusalText(string? value, out string detail)
    {
        detail = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        var lower = normalized.ToLowerInvariant();
        if (IsPlanningPreambleContractGapReference(lower))
        {
            return false;
        }

        if (lower.Contains("contract-gap refusal", StringComparison.Ordinal)
            || lower.Contains("contract gap refusal", StringComparison.Ordinal)
            || lower.Contains("stopped with a contract-gap explanation", StringComparison.Ordinal)
            || lower.Contains("stopped with a contract gap explanation", StringComparison.Ordinal)
            || lower.Contains("reported a deterministic contract gap", StringComparison.Ordinal))
        {
            detail = normalized;
            return true;
        }

        return false;
    }

    private static bool IsPlanningPreambleContractGapReference(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = value.TrimStart();
        while (normalized.Length > 0
            && (char.IsDigit(normalized[0]) || normalized[0] is '.' or ')' or '-' or ':' || char.IsWhiteSpace(normalized[0])))
        {
            normalized = normalized[1..].TrimStart();
        }

        return PlanningPreambleContractGapPrefixes.Any(prefix =>
                normalized.StartsWith(prefix, StringComparison.Ordinal))
            || PlanningPreambleContractGapMarkers.Any(marker =>
                normalized.Contains(marker, StringComparison.Ordinal));
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

    private static bool IsIgnorableStartupPreamble(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var value = payload.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (StartupPreamblePrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.Ordinal))
            || EchoedStartupRequestMarkers.Any(marker => normalized.StartsWith(marker, StringComparison.Ordinal)
                || normalized.Contains(marker, StringComparison.Ordinal)))
        {
            return true;
        }

        return false;
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

    private static bool ContainsBoundedFixProgressSignal(JsonElement payload)
    {
        foreach (var value in EnumeratePayloadStrings(payload))
        {
            var normalized = value.Trim().ToLowerInvariant();
            if (normalized.Contains("rg --files", StringComparison.Ordinal)
                || normalized.Contains("apply_patch", StringComparison.Ordinal)
                || normalized.Contains("dotnet test", StringComparison.Ordinal)
                || normalized.Contains("git diff", StringComparison.Ordinal)
                || normalized.Contains("git status", StringComparison.Ordinal)
                || normalized.Contains("ls ", StringComparison.Ordinal)
                || normalized.Contains("cat ", StringComparison.Ordinal)
                || normalized.Contains("sed -n", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsPlanningFixProgressSignal(JsonElement payload)
    {
        foreach (var value in EnumeratePayloadStrings(payload))
        {
            var normalized = value.Trim().ToLowerInvariant();
            if (normalized.Contains("apply_patch", StringComparison.Ordinal)
                || normalized.Contains("dotnet test", StringComparison.Ordinal)
                || normalized.Contains("git diff", StringComparison.Ordinal)
                || normalized.Contains("git status", StringComparison.Ordinal)
                || normalized.Contains("cat ", StringComparison.Ordinal)
                || normalized.Contains("sed -n", StringComparison.Ordinal)
                || normalized.Contains("head ", StringComparison.Ordinal)
                || normalized.Contains("tail ", StringComparison.Ordinal)
                || (normalized.Contains("rg ", StringComparison.Ordinal)
                    && !normalized.Contains("rg --files", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsRequestArtifactRead(JsonElement payload)
    {
        foreach (var value in EnumeratePayloadStrings(payload))
        {
            var normalized = value.Trim().ToLowerInvariant();
            if (!ContainsReadCommand(normalized))
            {
                continue;
            }

            if (normalized.Contains(".intent-cli/fix/", StringComparison.Ordinal)
                || normalized.Contains(".request.md", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsInitialRepoInventory(JsonElement payload)
    {
        foreach (var value in EnumeratePayloadStrings(payload))
        {
            if (value.Trim().ToLowerInvariant().Contains("rg --files", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsRepoLocalSpecReadAttempt(JsonElement payload)
    {
        foreach (var value in EnumeratePayloadStrings(payload))
        {
            var normalized = value.Trim().ToLowerInvariant();
            if (!ContainsReadCommand(normalized))
            {
                continue;
            }

            if (normalized.Contains("/specs/", StringComparison.Ordinal)
                || normalized.Contains("01-cli-surface.md", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSuccessfulRepoLocalSpecRead(IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        for (var index = 0; index < providerEvents.Count; index++)
        {
            if (IsIgnorableStartupPreamble(providerEvents[index].Payload)
                || !ContainsRepoLocalSpecReadAttempt(providerEvents[index].Payload))
            {
                continue;
            }

            if (TryResolveCommandOutcome(providerEvents, index, out var succeeded)
                && succeeded)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsProductSourceOrTestReadAttempt(JsonElement payload)
    {
        foreach (var value in EnumeratePayloadStrings(payload))
        {
            var normalized = value.Trim().ToLowerInvariant();
            if (!ContainsReadCommand(normalized))
            {
                continue;
            }

            if (normalized.Contains("src/", StringComparison.Ordinal)
                || normalized.Contains("tests/", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsDotNetTestAttempt(JsonElement payload)
    {
        foreach (var value in EnumeratePayloadStrings(payload))
        {
            if (value.Trim().ToLowerInvariant().Contains("dotnet test", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsReadCommand(string normalized)
    {
        return normalized.Contains("cat ", StringComparison.Ordinal)
            || normalized.Contains("sed -n", StringComparison.Ordinal)
            || normalized.Contains("head ", StringComparison.Ordinal)
            || normalized.Contains("tail ", StringComparison.Ordinal);
    }

    private static bool TryResolveCommandOutcome(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        int commandIndex,
        out bool succeeded)
    {
        succeeded = false;

        for (var index = commandIndex + 1; index < providerEvents.Count; index++)
        {
            foreach (var value in EnumeratePayloadStrings(providerEvents[index].Payload))
            {
                var normalized = value.Trim().ToLowerInvariant();
                if (normalized == "exec")
                {
                    return false;
                }

                if (normalized.StartsWith("succeeded in ", StringComparison.Ordinal))
                {
                    succeeded = true;
                    return true;
                }

                if (normalized.StartsWith("failed in ", StringComparison.Ordinal))
                {
                    succeeded = false;
                    return true;
                }
            }
        }

        return false;
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
