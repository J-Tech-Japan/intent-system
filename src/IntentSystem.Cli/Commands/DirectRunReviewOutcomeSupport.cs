using System.Text.Json;

namespace IntentSystem.Cli.Commands;

internal static class DirectRunReviewOutcomeSupport
{
    public static bool IsAcceptOutcome(string outcome)
    {
        return string.Equals(outcome, "accepted", StringComparison.Ordinal)
            || string.Equals(outcome, "approved", StringComparison.Ordinal);
    }

    public static bool IsCommentOutcome(string outcome)
    {
        return string.Equals(outcome, "comment", StringComparison.Ordinal)
            || string.Equals(outcome, "commented", StringComparison.Ordinal)
            || string.Equals(outcome, "fix-requested", StringComparison.Ordinal)
            || string.Equals(outcome, "changes-requested", StringComparison.Ordinal);
    }

    public static bool TryResolveCanonicalReviewOutcome(
        string runStatus,
        string? existingReviewOutcome,
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        out string reviewOutcome)
    {
        reviewOutcome = string.Empty;

        if (!string.IsNullOrWhiteSpace(existingReviewOutcome))
        {
            reviewOutcome = NormalizeRunStatus(existingReviewOutcome);
            return true;
        }

        if (TryResolveExplicitReviewOutcome(providerEvents, out reviewOutcome))
        {
            return true;
        }

        if (string.Equals(runStatus, "succeeded", StringComparison.Ordinal))
        {
            reviewOutcome = "accepted";
            return true;
        }

        if (IsAcceptOutcome(runStatus) || IsCommentOutcome(runStatus))
        {
            reviewOutcome = runStatus;
            return true;
        }

        return false;
    }

    public static bool TryResolveExplicitReviewOutcome(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        out string explicitReviewOutcome)
    {
        explicitReviewOutcome = string.Empty;

        for (var index = providerEvents.Count - 1; index >= 0; index--)
        {
            if (TryResolveRunStatus(providerEvents[index].Payload, out var runStatus)
                && !string.Equals(runStatus, "succeeded", StringComparison.Ordinal)
                && !string.Equals(runStatus, "failed", StringComparison.Ordinal)
                && !string.Equals(runStatus, "running", StringComparison.Ordinal))
            {
                explicitReviewOutcome = runStatus;
                return true;
            }
        }

        return false;
    }

    public static bool TryResolveReviewCommentBodyPath(
        CliContext context,
        string executionUnit,
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        out string commentBodyPath)
    {
        commentBodyPath = string.Empty;

        for (var index = providerEvents.Count - 1; index >= 0; index--)
        {
            if (!TryResolveCommentBody(providerEvents[index].Payload, out var bodyOrPath, out var isPath))
            {
                continue;
            }

            if (isPath)
            {
                commentBodyPath = bodyOrPath;
                return true;
            }

            var relativePath = $".intent-cli/reviews/{executionUnit}.comment.md";
            var absolutePath = Path.GetFullPath(Path.Combine(
                context.RepoRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var directoryPath = Path.GetDirectoryName(absolutePath)
                ?? throw new InvalidOperationException("Review comment body path did not contain a directory.");
            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(absolutePath, bodyOrPath);
            commentBodyPath = relativePath;
            return true;
        }

        return false;
    }

    public static DirectRunProviderEvent? CreateCanonicalReviewOutcomeEventIfNeeded(
        IReadOnlyList<DirectRunProviderEvent> currentProviderEvents,
        DateTimeOffset timestamp,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId,
        string reviewOutcome,
        string? reviewCommentBodyPath)
    {
        if (HasCanonicalReviewOutcomeEvent(currentProviderEvents, reviewOutcome, reviewCommentBodyPath))
        {
            return null;
        }

        object payload = reviewCommentBodyPath is null
            ? new
            {
                disposition = reviewOutcome
            }
            : new
            {
                disposition = reviewOutcome,
                body_path = reviewCommentBodyPath
            };

        return new DirectRunProviderEvent
        {
            Timestamp = timestamp.ToString("O"),
            ExecutionUnit = executionUnit,
            Provider = provider,
            EntryKind = entryKind,
            SessionId = providerSessionId,
            Kind = "provider-event",
            Payload = JsonSerializer.SerializeToElement(payload)
        };
    }

    private static bool HasCanonicalReviewOutcomeEvent(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string reviewOutcome,
        string? reviewCommentBodyPath)
    {
        foreach (var providerEvent in providerEvents)
        {
            if (!TryResolveRunStatus(providerEvent.Payload, out var resolvedOutcome)
                || !string.Equals(resolvedOutcome, reviewOutcome, StringComparison.Ordinal))
            {
                continue;
            }

            if (reviewCommentBodyPath is null)
            {
                return true;
            }

            if (TryResolveCommentBody(providerEvent.Payload, out var bodyOrPath, out var isPath)
                && isPath
                && string.Equals(bodyOrPath, reviewCommentBodyPath, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveCommentBody(JsonElement payload, out string bodyOrPath, out bool isPath)
    {
        bodyOrPath = string.Empty;
        isPath = false;

        if (payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryReadString(payload, "body_path", out var bodyPath))
        {
            bodyOrPath = bodyPath;
            isPath = true;
            return true;
        }

        if (TryReadString(payload, "comment_body", out var commentBody)
            || TryReadString(payload, "body", out commentBody)
            || TryReadString(payload, "markdown", out commentBody))
        {
            bodyOrPath = commentBody;
            return true;
        }

        return false;
    }

    private static bool TryResolveRunStatus(JsonElement payload, out string runStatus)
    {
        runStatus = string.Empty;

        if (payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryReadString(payload, "run_status", out var payloadRunStatus))
        {
            runStatus = NormalizeRunStatus(payloadRunStatus);
            return true;
        }

        if (TryReadString(payload, "status", out var status))
        {
            runStatus = NormalizeRunStatus(status);
            return true;
        }

        if (TryReadString(payload, "disposition", out var disposition))
        {
            runStatus = NormalizeRunStatus(disposition);
            return true;
        }

        if (TryReadInt32(payload, "exit_code", out var exitCode)
            || TryReadInt32(payload, "exitCode", out exitCode))
        {
            runStatus = exitCode == 0 ? "succeeded" : "failed";
            return true;
        }

        return false;
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
            var raw = element.GetString();
            return !string.IsNullOrWhiteSpace(raw)
                && int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        return false;
    }

    private static string NormalizeRunStatus(string status)
    {
        return status.Trim().ToLowerInvariant() switch
        {
            "success" or "completed" => "succeeded",
            "error" => "failed",
            var normalized when !string.IsNullOrWhiteSpace(normalized) => normalized,
            _ => "running"
        };
    }
}
