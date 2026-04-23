using System.Text.Json;

namespace IntentSystem.Cli.Commands;

internal static class DirectRunFixOutcomeSupport
{
    private const string DeterministicContractGapStopReason = "deterministic-contract-gap";
    private const string ExplicitContractGapRefusalReason = "provider-explicit-contract-gap-refusal";
    private const string InspectionOnlyExitReasonSuffix = "session-ended-after-initial-inspection";
    private const string ProviderBackendEndedBeforeSpecSourceReadReasonSuffix = "session-ended-before-spec-source-test-read";
    private const string ImplementBackendEndedAfterProductReadReasonSuffix = "session-ended-after-product-source-test-read";
    private const string MissingTerminalCaptureAfterRequestReadReasonSuffix = "session-terminal-boundary-missing-after-request-reread";
    private const string MissingTerminalCaptureAfterDeepProgressReasonSuffix = "session-terminal-boundary-missing-after-deep-progress";
    private const string EvidenceOnlyReviewFollowUpMissingOutcomeReasonSuffix = "evidence-only-review-follow-up-ended-without-bounded-repair-outcome";
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
    private static readonly string[] StartupPlanningContractGapReadMarkers =
    [
        "i’m reading the request artifact first",
        "i'm reading the request artifact first"
    ];
    private static readonly string[] StartupPlanningContractGapActionMarkers =
    [
        "i’ll inspect",
        "i'll inspect",
        "after that i’ll either",
        "after that i'll either",
        "either patch it",
        "either implement the fix and verify it",
        "pin down the bounded scope",
        "reproduce and repair it",
        "validate the bounded fix"
    ];
    private static readonly string[] StartupPlanningContractGapDecisionMarkers =
    [
        "either patch it or give a concrete contract-gap refusal",
        "either patch it or give a concrete contract gap refusal",
        "give a deterministic contract-gap refusal if",
        "give a deterministic contract gap refusal if",
        "give a concrete contract-gap refusal if",
        "give a concrete contract gap refusal if"
    ];
    private static readonly string[] EvidenceOnlyReviewFollowUpMarkers =
    [
        "stronger verification",
        "real process-boundary test",
        "real process boundary test",
        "process-boundary test",
        "process boundary test",
        "invalid-usage assertions",
        "invalid usage assertions",
        "exact exit 1",
        "exact exit code",
        "exit code == 1",
        "empty stdout",
        "canonical stderr"
    ];
    private static readonly string[] EvidenceOnlyReviewFollowUpContextMarkers =
    [
        "review asks",
        "review comment asks",
        "comment asks",
        "narrower contract detail",
        "repo-local intent/spec artifacts lag implementation",
        "repo-local intent/spec artifacts",
        "repo-local spec artifacts lag implementation"
    ];
    private static readonly string[] ProviderConfigurationFailureMarkers =
    [
        "failed to authenticate",
        "authentication_error",
        "authentication error",
        "invalid authentication credentials",
        "invalid credentials",
        "unauthorized",
        "401",
        "api key",
        "apikey",
        "credential",
        "credentials",
        "not configured",
        "misconfigured",
        "missing configuration",
        "configuration error"
    ];
    private static readonly string[] NoOpEditFreeMarkers =
    [
        "without requiring code changes",
        "no repair edits were needed"
    ];
    private static readonly string[] NoOpSatisfiedMarkers =
    [
        "already matches",
        "already satisfies the bounded acceptance criteria",
        "satisfies the bounded acceptance criteria"
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

        if (!SupportsEntryKind(entryKind)
            || HasCanonicalContractGap(providerEvents))
        {
            return null;
        }

        var orderedProviderEvents = OrderEventsForAnalysis(providerEvents);

        if (TryCreateExplicitContractGapEvent(
                orderedProviderEvents,
                timestamp,
                executionUnit,
                entryKind,
                provider,
                providerSessionId,
                out var explicitContractGapEvent))
        {
            return explicitContractGapEvent;
        }

        if (TryCreateEvidenceOnlyReviewFollowUpFailureEvent(
                orderedProviderEvents,
                timestamp,
                executionUnit,
                entryKind,
                provider,
                providerSessionId,
                out var evidenceOnlyReviewFollowUpFailureEvent))
        {
            return evidenceOnlyReviewFollowUpFailureEvent;
        }

        if (!TryResolveCanonicalFailureDetail(
                orderedProviderEvents,
                executionUnit,
                entryKind,
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
        string entryKind,
        out string detail)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKind);

        var orderedProviderEvents = OrderEventsForAnalysis(providerEvents);

        for (var index = orderedProviderEvents.Count - 1; index >= 0; index--)
        {
            if (!TryResolveExplicitContractGapDetail(orderedProviderEvents[index].Payload, executionUnit, entryKind, out detail))
            {
                continue;
            }

            return true;
        }

        return TryResolveInspectionOnlyFailureDetail(orderedProviderEvents, executionUnit, entryKind, out detail);
    }

    public static bool TryResolveStartupOnlyFailureDetail(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string executionUnit,
        string entryKind,
        out string detail)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKind);

        var orderedProviderEvents = OrderEventsForAnalysis(providerEvents);

        detail = string.Empty;
        var failingBackendExitIndex = FindFailingBackendExitIndex(orderedProviderEvents);
        if (failingBackendExitIndex < 0)
        {
            return false;
        }

        var sawStartupNoise = false;
        for (var index = 0; index < failingBackendExitIndex; index++)
        {
            var providerEvent = orderedProviderEvents[index];
            if (providerEvent.Kind == "session-metadata"
                || IsIgnorableReadyEvent(providerEvent.Payload)
                || IsIgnorableStartupPreamble(providerEvent.Payload))
            {
                continue;
            }

            if (IsIgnorableStartupNoise(providerEvent.Payload))
            {
                sawStartupNoise = true;
                continue;
            }

            if (TryResolveExplicitContractGapDetail(providerEvent.Payload, executionUnit, entryKind, out _)
                || ContainsSuccessfulInitialRepoInspection(providerEvent.Payload))
            {
                return false;
            }

            if (ContainsBoundedFixProgressSignal(providerEvent.Payload))
            {
                return false;
            }

            return false;
        }

        if (!sawStartupNoise)
        {
            return false;
        }

        detail =
            $"{ResolveEntryLabel(entryKind)} direct run for '{executionUnit}' exited during provider startup before any bounded repo inspection, edit, test, refusal, or contract-gap output was emitted. Current-session provider output only contained startup warnings or noise before the backend exit.";
        return true;
    }

    public static bool TryResolveProviderConfigurationFailureDetail(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string executionUnit,
        string entryKind,
        out string detail)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKind);

        var orderedProviderEvents = OrderEventsForAnalysis(providerEvents);

        detail = string.Empty;
        var failingBackendExitIndex = FindFailingBackendExitIndex(orderedProviderEvents);
        if (failingBackendExitIndex < 0)
        {
            return false;
        }

        string? providerConfigurationSignal = null;
        for (var index = 0; index < failingBackendExitIndex; index++)
        {
            var providerEvent = orderedProviderEvents[index];
            if (providerEvent.Kind == "session-metadata"
                || IsIgnorableReadyEvent(providerEvent.Payload)
                || IsIgnorableStartupPreamble(providerEvent.Payload)
                || IsIgnorableStartupNoise(providerEvent.Payload))
            {
                continue;
            }

            if (ContainsInitialRepoInventory(providerEvent.Payload)
                || ContainsRepoLocalSpecReadAttempt(providerEvent.Payload)
                || ContainsProductSourceOrTestReadAttempt(providerEvent.Payload)
                || ContainsBoundedFixProgressSignal(providerEvent.Payload))
            {
                return false;
            }

            providerConfigurationSignal ??= TryResolveProviderConfigurationSignal(providerEvent.Payload);
        }

        if (providerConfigurationSignal is null)
        {
            return false;
        }

        detail =
            $"{ResolveEntryLabel(entryKind)} direct run for '{executionUnit}' failed because the selected provider could not authenticate or was misconfigured before repo inventory, repo-local spec reads, or product source/test reads. Provider configuration failure detail: {providerConfigurationSignal}";
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

        var orderedProviderEvents = OrderEventsForAnalysis(providerEvents);

        detail = string.Empty;
        if (!HasSuccessfulBackendExit(orderedProviderEvents))
        {
            return false;
        }

        for (var index = orderedProviderEvents.Count - 1; index >= 0; index--)
        {
            if (!TryResolveNoOpSuccessDetail(orderedProviderEvents[index].Payload, out detail))
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

    internal static bool HasRecoveredSpecWithoutProductReadSignal(IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);

        var orderedProviderEvents = OrderEventsForAnalysis(providerEvents);
        var sawRequestReread = orderedProviderEvents.Any(providerEvent => ContainsRequestArtifactRead(providerEvent.Payload));
        var sawSpecRead = HasSuccessfulRepoLocalSpecRead(orderedProviderEvents);
        var sawProductRead = orderedProviderEvents.Any(providerEvent =>
            !IsIgnorableStartupPreamble(providerEvent.Payload)
            && ContainsProductSourceOrTestReadAttempt(providerEvent.Payload));

        return sawRequestReread && sawSpecRead && !sawProductRead;
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

    internal static bool HasVerificationCommandSignal(IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);

        return providerEvents.Any(providerEvent =>
            !IsIgnorableStartupPreamble(providerEvent.Payload)
            && ContainsDotNetTestAttempt(providerEvent.Payload));
    }

    private static bool TryResolveCanonicalFailureDetail(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string executionUnit,
        string entryKind,
        bool providerSessionAlive,
        out string reason,
        out string detail)
    {
        if (TryResolveMissingTerminalAfterDeepProgressDetail(
                providerEvents,
                executionUnit,
                entryKind,
                providerSessionAlive,
                out reason,
                out detail))
        {
            return true;
        }

        if (TryResolveImplementBackendExitAfterProductReadDetail(
                providerEvents,
                executionUnit,
                entryKind,
                out reason,
                out detail))
        {
            return true;
        }

        if (TryResolvePostRequestParityBoundaryDetail(
                providerEvents,
                executionUnit,
                entryKind,
                providerSessionAlive,
                out reason,
                out detail))
        {
            return true;
        }

        reason = ResolveReason(entryKind, InspectionOnlyExitReasonSuffix);
        return TryResolveInspectionOnlyFailureDetail(providerEvents, executionUnit, entryKind, out detail);
    }

    private static bool TryResolveMissingTerminalAfterDeepProgressDetail(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string executionUnit,
        string entryKind,
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

        reason = ResolveReason(entryKind, MissingTerminalCaptureAfterDeepProgressReasonSuffix);
        detail =
            $"{ResolveEntryLabel(entryKind)} direct run for '{executionUnit}' reached deeper bounded work before the provider session died, but no same-session terminal outcome was captured. Current-session evidence observed request_reread={observedRequestReread}, repo_inventory={observedInventory}, repo_local_spec_read={observedSpecRead}, product_source_or_test_read={observedProductRead}, dotnet_test={observedDotNetTest}. The provider session is no longer alive, but neither backend-exit nor an explicit contract-gap was persisted for that same session, so the child runtime must synthesize a deterministic missing-terminal boundary instead of leaving run_status=running.";
        return true;
    }

    private static bool TryResolvePostRequestParityBoundaryDetail(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string executionUnit,
        string entryKind,
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
            reason = ResolveReason(entryKind, ProviderBackendEndedBeforeSpecSourceReadReasonSuffix);
            detail =
                $"{ResolveEntryLabel(entryKind)} direct run for '{executionUnit}' stopped before repo-local spec/source/test planning reads. Current-session evidence observed request_reread={observedRequestReread}, repo_inventory={observedInventory}, repo_local_spec_read={observedSpecRead}, product_source_or_test_read={observedProductRead}. A failing backend-exit was captured before any repo-local spec or product source/test read, which indicates the provider backend itself exited before the next bounded read.";
            return true;
        }

        if (providerSessionAlive)
        {
            return false;
        }

        reason = ResolveReason(entryKind, MissingTerminalCaptureAfterRequestReadReasonSuffix);
        detail =
            $"{ResolveEntryLabel(entryKind)} direct run for '{executionUnit}' stopped before repo-local spec/source/test planning reads. Current-session evidence observed request_reread={observedRequestReread}, repo_inventory={observedInventory}, repo_local_spec_read={observedSpecRead}, product_source_or_test_read={observedProductRead}. The provider session is no longer alive, but no backend-exit or later bounded-read event was captured for the current session. This indicates the detached helper/current-session synthesis event capture dropped after the request reread layer rather than a completed repair attempt.";
        return true;
    }

    private static bool TryResolveImplementBackendExitAfterProductReadDetail(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string executionUnit,
        string entryKind,
        out string reason,
        out string detail)
    {
        detail = string.Empty;
        reason = string.Empty;

        if (!string.Equals(entryKind, "implement", StringComparison.Ordinal))
        {
            return false;
        }

        var observedRequestReread = providerEvents.Any(providerEvent => ContainsRequestArtifactRead(providerEvent.Payload));
        var observedInventory = providerEvents.Any(providerEvent => ContainsInitialRepoInventory(providerEvent.Payload));
        var observedSpecRead = HasSuccessfulRepoLocalSpecRead(providerEvents);
        var observedProductRead = providerEvents.Any(providerEvent => ContainsProductSourceOrTestReadAttempt(providerEvent.Payload));
        if (!observedRequestReread
            || !observedProductRead
            || HasCapturedSuccessfulTerminalOutcome(providerEvents)
            || FindFailingBackendExitIndex(providerEvents) < 0)
        {
            return false;
        }

        reason = ResolveReason(entryKind, ImplementBackendEndedAfterProductReadReasonSuffix);
        detail =
            $"{ResolveEntryLabel(entryKind)} direct run for '{executionUnit}' ended after current-session product source/test read activity but before a bounded repair outcome. Current-session evidence observed request_reread={observedRequestReread}, repo_inventory={observedInventory}, repo_local_spec_read={observedSpecRead}, product_source_or_test_read={observedProductRead}. A failing backend-exit was captured after product source/test read activity, so the normalized failure must preserve that later evidence rather than collapsing it into an earlier pre-read backend-exit boundary.";
        return true;
    }

    private static bool TryResolveInspectionOnlyFailureDetail(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string executionUnit,
        string entryKind,
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
                || IsIgnorableReadyEvent(providerEvent.Payload)
                || IsIgnorableStartupPreamble(providerEvent.Payload)
                || IsIgnorableStartupNoise(providerEvent.Payload))
            {
                continue;
            }

            if (TryResolveExplicitContractGapDetail(providerEvent.Payload, executionUnit, entryKind, out _))
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
            $"{ResolveEntryLabel(entryKind)} direct run for '{executionUnit}' exited after the initial repo-inspection command completed without any repair, test, refusal, or contract-gap outcome.";
        return true;
    }

    private static IReadOnlyList<DirectRunProviderEvent> OrderEventsForAnalysis(
        IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);

        return providerEvents
            .Select(
                (providerEvent, index) => new
                {
                    Event = providerEvent,
                    Index = index,
                    ParsedTimestamp = DateTimeOffset.TryParse(
                        providerEvent.Timestamp,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind,
                        out var parsedTimestamp)
                        ? parsedTimestamp
                        : (DateTimeOffset?)null
                })
            .OrderBy(item => item.ParsedTimestamp is null ? 1 : 0)
            .ThenBy(item => item.ParsedTimestamp)
            .ThenBy(item => item.Index)
            .Select(item => item.Event)
            .ToArray();
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
        if (!NoOpEditFreeMarkers.Any(marker => lower.Contains(marker, StringComparison.Ordinal))
            || !NoOpSatisfiedMarkers.Any(marker => lower.Contains(marker, StringComparison.Ordinal)))
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
        if (!TryResolveExplicitContractGapDetail(providerEvents, executionUnit, entryKind, out var detail))
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

    private static bool TryCreateEvidenceOnlyReviewFollowUpFailureEvent(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        DateTimeOffset timestamp,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId,
        out DirectRunProviderEvent? providerEvent)
    {
        providerEvent = null;
        if (!string.Equals(entryKind, "fix", StringComparison.Ordinal)
            || !HasSuccessfulBackendExit(providerEvents)
            || !HasEvidenceOnlyReviewFollowUpContractGapReference(providerEvents)
            || TryResolveNoOpSuccessDetail(providerEvents, executionUnit, out _)
            || HasBoundedRepairOutcomeSignal(providerEvents))
        {
            return false;
        }

        var observedRequestReread = providerEvents.Any(providerEvent => ContainsRequestArtifactRead(providerEvent.Payload));
        var observedInventory = providerEvents.Any(providerEvent => ContainsInitialRepoInventory(providerEvent.Payload));
        var observedRepoLocalSpecRead = HasSuccessfulRepoLocalSpecRead(providerEvents);
        var observedProductSourceOrTestRead = providerEvents.Any(providerEvent => ContainsProductSourceOrTestReadAttempt(providerEvent.Payload));
        var observedBoundedRepairOutcome = HasBoundedRepairOutcomeSignal(providerEvents);

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
                reason = ResolveReason(entryKind, EvidenceOnlyReviewFollowUpMissingOutcomeReasonSuffix),
                detail =
                    $"{ResolveEntryLabel(entryKind)} direct run for '{executionUnit}' ended after evidence-only review-follow-up ambiguity without a bounded repair outcome. Current-session evidence observed request_reread={observedRequestReread}, repo_inventory={observedInventory}, repo_local_spec_read={observedRepoLocalSpecRead}, product_source_or_test_read={observedProductSourceOrTestRead}, bounded_repair_outcome={observedBoundedRepairOutcome}. The session emitted evidence-only uncertainty about whether repo-local intent/spec artifacts lag implementation or whether the review asks for a narrower contract detail, then exited successfully without a same-session repair, verification command, no-op success boundary, or explicit refusal. Root runtime handling must preserve this as a bounded repair failure instead of silently treating exit_code=0 as success.",
                run_status = "failed"
            })
        };

        return true;
    }

    private static bool HasExplicitContractGap(IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        return providerEvents.Any(providerEvent =>
            TryResolveExplicitContractGapDetail(
                providerEvent.Payload,
                providerEvent.ExecutionUnit ?? "fix",
                providerEvent.EntryKind ?? "fix",
                out _));
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
        string entryKind,
        out string detail)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKind);

        for (var index = providerEvents.Count - 1; index >= 0; index--)
        {
            if (TryResolveExplicitContractGapDetail(providerEvents[index].Payload, executionUnit, entryKind, out detail))
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
        string entryKind,
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
                detail = $"{ResolveEntryLabel(entryKind)} direct run for '{executionUnit}' reported a deterministic contract gap.";
            }
            else if (IsEvidenceOnlyReviewFollowUpContractGapReference(detail))
            {
                detail = string.Empty;
                return false;
            }

            return true;
        }

        if (TryReadString(payload, "type", out var type)
            && string.Equals(type, "contract-gap", StringComparison.Ordinal))
        {
            if (!TryReadString(payload, "detail", out detail))
            {
                detail = $"{ResolveEntryLabel(entryKind)} direct run for '{executionUnit}' reported a deterministic contract gap.";
            }
            else if (IsEvidenceOnlyReviewFollowUpContractGapReference(detail))
            {
                detail = string.Empty;
                return false;
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

        if (IsEvidenceOnlyReviewFollowUpContractGapReference(lower))
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

    private static bool IsEvidenceOnlyReviewFollowUpContractGapReference(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = value.Trim().ToLowerInvariant();
        return EvidenceOnlyReviewFollowUpMarkers.Any(marker =>
                normalized.Contains(marker, StringComparison.Ordinal))
            && EvidenceOnlyReviewFollowUpContextMarkers.Any(marker =>
                normalized.Contains(marker, StringComparison.Ordinal));
    }

    private static bool HasEvidenceOnlyReviewFollowUpContractGapReference(IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);

        foreach (var providerEvent in providerEvents)
        {
            foreach (var value in EnumeratePayloadStrings(providerEvent.Payload))
            {
                if (IsEvidenceOnlyReviewFollowUpContractGapReference(value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasBoundedRepairOutcomeSignal(IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);

        return providerEvents.Any(providerEvent => ContainsBoundedRepairOutcomeSignal(providerEvent.Payload));
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
                normalized.Contains(marker, StringComparison.Ordinal))
            || (StartupPlanningContractGapReadMarkers.Any(marker =>
                    normalized.Contains(marker, StringComparison.Ordinal))
                && StartupPlanningContractGapActionMarkers.Any(marker =>
                    normalized.Contains(marker, StringComparison.Ordinal))
                && StartupPlanningContractGapDecisionMarkers.Any(marker =>
                    normalized.Contains(marker, StringComparison.Ordinal)));
    }

    private static bool SupportsEntryKind(string entryKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKind);

        return string.Equals(entryKind, "fix", StringComparison.Ordinal)
            || string.Equals(entryKind, "implement", StringComparison.Ordinal);
    }

    private static string ResolveEntryLabel(string entryKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKind);

        return string.Equals(entryKind, "implement", StringComparison.Ordinal)
            ? "Implement"
            : "Fix";
    }

    private static string ResolveReason(string entryKind, string suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(suffix);

        var prefix = string.Equals(entryKind, "implement", StringComparison.Ordinal)
            ? "implement"
            : "fix";

        return $"{prefix}-{suffix}";
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

    private static string? TryResolveProviderConfigurationSignal(JsonElement payload)
    {
        foreach (var value in EnumeratePayloadStrings(payload))
        {
            var trimmed = value.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            var normalized = trimmed.ToLowerInvariant();
            if (!ProviderConfigurationFailureMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal)))
            {
                continue;
            }

            const int maxDetailLength = 240;
            return trimmed.Length <= maxDetailLength
                ? trimmed
                : trimmed[..maxDetailLength];
        }

        return null;
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

    private static bool ContainsBoundedRepairOutcomeSignal(JsonElement payload)
    {
        foreach (var value in EnumeratePayloadStrings(payload))
        {
            var normalized = value.Trim().ToLowerInvariant();
            if (normalized.Contains("apply_patch", StringComparison.Ordinal)
                || normalized.Contains("dotnet test", StringComparison.Ordinal))
            {
                return true;
            }

            if (!LooksLikeCommandInvocation(normalized)
                || ContainsReadCommand(normalized)
                || normalized.Contains("rg --files", StringComparison.Ordinal)
                || normalized.Contains("git status", StringComparison.Ordinal)
                || normalized.Contains("git diff", StringComparison.Ordinal)
                || normalized.Contains("pwd", StringComparison.Ordinal)
                || normalized.Contains("ls ", StringComparison.Ordinal))
            {
                continue;
            }

            return true;
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

    private static bool LooksLikeCommandInvocation(string normalized)
    {
        return normalized.StartsWith("/bin/", StringComparison.Ordinal)
            || normalized.StartsWith("exec /bin/", StringComparison.Ordinal)
            || normalized.Contains(" -lc ", StringComparison.Ordinal)
            || normalized.Contains("dotnet ", StringComparison.Ordinal)
            || normalized.Contains("dnx ", StringComparison.Ordinal);
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
