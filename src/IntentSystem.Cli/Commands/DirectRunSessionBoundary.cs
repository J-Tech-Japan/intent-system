namespace IntentSystem.Cli.Commands;

internal static class DirectRunSessionBoundary
{
    public static IReadOnlyList<DirectRunProviderEvent> SelectEvents(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string launchedSessionId,
        DateTimeOffset? launchedAt)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);

        if (string.IsNullOrWhiteSpace(launchedSessionId))
        {
            return providerEvents;
        }

        var matchedEvents = providerEvents
            .Where(providerEvent => IsMatchingSessionEvent(providerEvent, launchedSessionId, launchedAt))
            .ToArray();

        return matchedEvents.Length > 0
            ? matchedEvents
            : providerEvents;
    }

    public static bool HasBackendExitEvent(
        string providerEventLogPath,
        string providerSessionId,
        DateTimeOffset? launchedAt)
    {
        if (!File.Exists(providerEventLogPath))
        {
            return false;
        }

        foreach (var line in File.ReadLines(providerEventLogPath))
        {
            DirectRunProviderEvent providerEvent;
            try
            {
                providerEvent = DirectRunProviderEventJsonl.DeserializeLine(line);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                or ArgumentException
                or System.Text.Json.JsonException)
            {
                continue;
            }

            if (IsMatchingSessionEvent(providerEvent, providerSessionId, launchedAt)
                && providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryResolveBackendExitCode(
        string providerEventLogPath,
        string providerSessionId,
        DateTimeOffset? launchedAt,
        out int exitCode)
    {
        exitCode = default;
        if (!File.Exists(providerEventLogPath))
        {
            return false;
        }

        var resolved = false;
        foreach (var line in File.ReadLines(providerEventLogPath))
        {
            DirectRunProviderEvent providerEvent;
            try
            {
                providerEvent = DirectRunProviderEventJsonl.DeserializeLine(line);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                or ArgumentException
                or System.Text.Json.JsonException)
            {
                continue;
            }

            if (!IsMatchingSessionEvent(providerEvent, providerSessionId, launchedAt)
                || providerEvent.Kind != "provider-event"
                || providerEvent.Payload.ValueKind != System.Text.Json.JsonValueKind.Object
                || !providerEvent.Payload.TryGetProperty("type", out var typeElement)
                || !string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
                || !providerEvent.Payload.TryGetProperty("exit_code", out var exitCodeElement)
                || !exitCodeElement.TryGetInt32(out var parsedExitCode))
            {
                continue;
            }

            exitCode = parsedExitCode;
            resolved = true;
        }

        return resolved;
    }

    public static bool IsMatchingSessionEvent(
        DirectRunProviderEvent providerEvent,
        string providerSessionId,
        DateTimeOffset? launchedAt)
    {
        ArgumentNullException.ThrowIfNull(providerEvent);

        if (!string.Equals(providerEvent.SessionId, providerSessionId, StringComparison.Ordinal))
        {
            return false;
        }

        if (launchedAt is null)
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(
                providerEvent.Timestamp,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var providerEventTimestamp))
        {
            return true;
        }

        return providerEventTimestamp >= launchedAt.Value;
    }

    public static bool TryParseLaunchedAt(string launchedAt, out DateTimeOffset parsed)
    {
        return DateTimeOffset.TryParse(
            launchedAt,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out parsed);
    }
}
