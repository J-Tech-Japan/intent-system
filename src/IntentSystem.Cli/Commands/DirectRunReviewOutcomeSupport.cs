using System.Text.Json;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

internal static class DirectRunReviewOutcomeSupport
{
    private static readonly Regex ReviewCommentUrlPattern = new(
        @"https://github\.com/[^\s/]+/[^\s/]+/pull/\d+#issuecomment-\d+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
            if (TryResolveReviewOutcomeFromPayload(providerEvents[index].Payload, out var runStatus))
            {
                explicitReviewOutcome = runStatus;
                return true;
            }
        }

        return false;
    }

    public static string ResolveEffectiveReviewRunStatus(string runStatus, string? reviewOutcome)
    {
        if (!string.IsNullOrWhiteSpace(reviewOutcome)
            && (IsAcceptOutcome(reviewOutcome) || IsCommentOutcome(reviewOutcome)))
        {
            return "succeeded";
        }

        return runStatus;
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

    public static bool TryResolvePublishedReviewCommentRef(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string? linkedPr,
        out string reviewCommentRef)
    {
        reviewCommentRef = string.Empty;

        for (var index = providerEvents.Count - 1; index >= 0; index--)
        {
            if (!TryResolvePublishedReviewCommentRef(providerEvents[index].Payload, linkedPr, out reviewCommentRef))
            {
                continue;
            }

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

    public static DirectRunProviderEvent? TryCreateReviewOutcomeEventFromCapturedMessage(
        IReadOnlyList<DirectRunProviderEvent> currentProviderEvents,
        string capturedMessagePath,
        DateTimeOffset timestamp,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId)
    {
        ArgumentNullException.ThrowIfNull(currentProviderEvents);
        ArgumentException.ThrowIfNullOrWhiteSpace(capturedMessagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSessionId);

        if (TryResolveExplicitReviewOutcome(currentProviderEvents, out _)
            || !TryReadReviewOutcomeFromCapturedMessagePath(
                capturedMessagePath,
                out var payload,
                out var reviewOutcome,
                out var reviewCommentBodyPath)
            || HasCanonicalReviewOutcomeEvent(currentProviderEvents, reviewOutcome, reviewCommentBodyPath))
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
            Payload = payload
        };
    }

    public static bool TryReadReviewOutcomeFromCapturedMessagePath(
        string capturedMessagePath,
        out JsonElement payload,
        out string reviewOutcome,
        out string? reviewCommentBodyPath)
    {
        payload = default;
        reviewOutcome = string.Empty;
        reviewCommentBodyPath = null;

        if (!File.Exists(capturedMessagePath)
            || !TryParseCapturedReviewOutcomePayload(
                File.ReadAllText(capturedMessagePath),
                out payload,
                out reviewOutcome,
                out reviewCommentBodyPath))
        {
            return false;
        }

        return true;
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

    private static bool TryParseCapturedReviewOutcomePayload(
        string capturedMessage,
        out JsonElement payload,
        out string reviewOutcome,
        out string? reviewCommentBodyPath)
    {
        payload = default;
        reviewOutcome = string.Empty;
        reviewCommentBodyPath = null;

        if (string.IsNullOrWhiteSpace(capturedMessage))
        {
            return false;
        }

        var normalizedMessage = StripMarkdownCodeFence(capturedMessage);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(normalizedMessage);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            if (!TryResolveReviewPayloadSource(document.RootElement, out var source))
            {
                return false;
            }

            if (!TryResolveReviewOutcomeFromPayload(source, out reviewOutcome))
            {
                return false;
            }

            var normalizedPayload = new Dictionary<string, object?>
            {
                ["disposition"] = reviewOutcome
            };

            if (TryReadString(source, "body_path", out var bodyPath)
                || TryReadString(source, "review_comment_body_path", out bodyPath))
            {
                normalizedPayload["body_path"] = bodyPath;
                reviewCommentBodyPath = bodyPath;
            }
            else if (TryReadString(source, "comment_body", out var commentBody)
                     || TryReadString(source, "body", out commentBody)
                     || TryReadString(source, "markdown", out commentBody)
                     || TryReadString(source, "detail", out commentBody)
                     || TryReadString(source, "message", out commentBody)
                     || TryReadString(source, "summary", out commentBody))
            {
                normalizedPayload["comment_body"] = commentBody;
            }
            else if (IsCommentOutcome(reviewOutcome))
            {
                normalizedPayload["comment_body"] = "Deterministic review follow-up is required.";
            }

            payload = JsonSerializer.SerializeToElement(normalizedPayload);
            return true;
        }
    }

    private static bool TryResolveFallbackReviewDisposition(JsonElement source, out string disposition)
    {
        disposition = string.Empty;

        if (!TryReadString(source, "stop_reason", out var stopReason))
        {
            return false;
        }

        if (string.Equals(stopReason, "deterministic-contract-gap", StringComparison.Ordinal))
        {
            disposition = "fix-requested";
            return true;
        }

        if (string.Equals(stopReason, "no-actionable-item", StringComparison.Ordinal))
        {
            disposition = "accepted";
            return true;
        }

        return false;
    }

    private static bool TryResolveReviewPayloadSource(JsonElement payload, out JsonElement source)
    {
        source = default;

        if (payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        source = payload;
        if (payload.TryGetProperty("review_result", out var reviewResult)
            && reviewResult.ValueKind == JsonValueKind.Object)
        {
            source = reviewResult;
            return true;
        }

        if (payload.TryGetProperty("type", out var typeElement)
            && typeElement.ValueKind == JsonValueKind.String
            && string.Equals(typeElement.GetString(), "item.completed", StringComparison.Ordinal)
            && payload.TryGetProperty("item", out var itemElement)
            && itemElement.ValueKind == JsonValueKind.Object
            && TryReadString(itemElement, "text", out var itemText))
        {
            try
            {
                using var itemDocument = JsonDocument.Parse(StripMarkdownCodeFence(itemText));
                if (itemDocument.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                source = itemDocument.RootElement.Clone();
                if (source.TryGetProperty("review_result", out var nestedReviewResult)
                    && nestedReviewResult.ValueKind == JsonValueKind.Object)
                {
                    source = nestedReviewResult;
                }

                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryResolveReviewOutcomeFromPayload(JsonElement payload, out string reviewOutcome)
    {
        reviewOutcome = string.Empty;

        if (!TryResolveReviewPayloadSource(payload, out var source))
        {
            return false;
        }

        if (!TryReadString(source, "disposition", out var disposition)
            && !TryReadString(source, "status", out disposition)
            && !TryReadString(source, "run_status", out disposition)
            && !TryResolveFallbackReviewDisposition(source, out disposition))
        {
            return false;
        }

        reviewOutcome = NormalizeRunStatus(disposition);
        return IsAcceptOutcome(reviewOutcome) || IsCommentOutcome(reviewOutcome);
    }

    private static string StripMarkdownCodeFence(string capturedMessage)
    {
        var normalized = capturedMessage.Trim();
        if (!normalized.StartsWith("```", StringComparison.Ordinal))
        {
            return normalized;
        }

        var firstLineBreak = normalized.IndexOf('\n');
        if (firstLineBreak < 0)
        {
            return normalized;
        }

        normalized = normalized[(firstLineBreak + 1)..];
        var closingFence = normalized.LastIndexOf("```", StringComparison.Ordinal);
        if (closingFence >= 0)
        {
            normalized = normalized[..closingFence];
        }

        return normalized.Trim();
    }

    private static bool TryResolveCommentBody(JsonElement payload, out string bodyOrPath, out bool isPath)
    {
        bodyOrPath = string.Empty;
        isPath = false;

        if (!TryResolveReviewPayloadSource(payload, out var source))
        {
            return false;
        }

        if (TryReadString(source, "body_path", out var bodyPath))
        {
            bodyOrPath = bodyPath;
            isPath = true;
            return true;
        }

        if (TryReadString(source, "comment_body", out var commentBody)
            || TryReadString(source, "body", out commentBody)
            || TryReadString(source, "markdown", out commentBody)
            || TryReadString(source, "detail", out commentBody)
            || TryReadString(source, "message", out commentBody)
            || TryReadString(source, "summary", out commentBody))
        {
            bodyOrPath = commentBody;
            return true;
        }

        return false;
    }

    private static bool TryResolvePublishedReviewCommentRef(
        JsonElement payload,
        string? linkedPr,
        out string reviewCommentRef)
    {
        reviewCommentRef = string.Empty;

        foreach (var candidate in EnumeratePayloadStrings(payload))
        {
            var match = ReviewCommentUrlPattern.Matches(candidate)
                .Select(match => match.Value)
                .LastOrDefault(value => IsMatchingLinkedPr(value, linkedPr));
            if (!string.IsNullOrWhiteSpace(match))
            {
                reviewCommentRef = match;
                return true;
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

    private static bool IsMatchingLinkedPr(string reviewCommentRef, string? linkedPr)
    {
        if (string.IsNullOrWhiteSpace(linkedPr))
        {
            return true;
        }

        return reviewCommentRef.StartsWith($"{linkedPr}#issuecomment-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveRunStatus(JsonElement payload, out string runStatus)
    {
        runStatus = string.Empty;

        if (!TryResolveReviewPayloadSource(payload, out var source))
        {
            return false;
        }

        if (TryReadString(source, "run_status", out var payloadRunStatus))
        {
            runStatus = NormalizeRunStatus(payloadRunStatus);
            return true;
        }

        if (TryReadString(source, "status", out var status))
        {
            runStatus = NormalizeRunStatus(status);
            return true;
        }

        if (TryReadString(source, "disposition", out var disposition))
        {
            runStatus = NormalizeRunStatus(disposition);
            return true;
        }

        if (TryReadInt32(source, "exit_code", out var exitCode)
            || TryReadInt32(source, "exitCode", out exitCode))
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
