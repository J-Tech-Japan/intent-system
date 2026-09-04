using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Opaque specialist ruling carried through the notify envelope.  The CLI
/// authenticates the bytes and their producer, but intentionally does not
/// interpret the ruling text.
/// </summary>
internal sealed record NotifyRuling
{
    [JsonPropertyName("payload")]
    public required string Payload { get; init; }

    [JsonPropertyName("digest")]
    public required string Digest { get; init; }

    [JsonPropertyName("origin")]
    public required string Origin { get; init; }

    [JsonIgnore]
    public IReadOnlyList<byte> PayloadBytes => Encoding.UTF8.GetBytes(Payload);

    public bool Verifies() =>
        string.Equals(Digest, NotifyRulingRelay.ComputeDigest(Payload), StringComparison.OrdinalIgnoreCase);

    public static bool TryCreate(
        string? payload,
        string? origin,
        string? suppliedDigest,
        out NotifyRuling? ruling,
        out string error)
    {
        ruling = null;
        if (payload is null)
        {
            error = "ruling payload is required when ruling metadata is supplied.";
            return false;
        }

        if (!LogicalRoleNormalizer.TryNormalize(origin, out var canonicalOrigin, out error)
            || canonicalOrigin is null)
        {
            return false;
        }

        var digest = NotifyRulingRelay.ComputeDigest(payload);
        if (suppliedDigest is not null)
        {
            if (!NotifyRulingRelay.IsDigest(suppliedDigest))
            {
                error = "ruling digest must be a 64-character SHA-256 hexadecimal value.";
                return false;
            }

            if (!string.Equals(suppliedDigest, digest, StringComparison.OrdinalIgnoreCase))
            {
                error = $"ruling digest mismatch: supplied '{suppliedDigest.ToLowerInvariant()}', computed '{digest}'.";
                return false;
            }

            digest = suppliedDigest.ToLowerInvariant();
        }

        ruling = new NotifyRuling
        {
            Payload = payload,
            Digest = digest,
            Origin = canonicalOrigin,
        };
        error = string.Empty;
        return true;
    }
}

/// <summary>
/// Envelope used when a Steward relays an opaque ruling.  Additional fields
/// are permitted, but reserved ruling fields cannot be shadowed by an
/// envelope extension.
/// </summary>
internal sealed record NotifyRulingEnvelope
{
    [JsonPropertyName("ruling")]
    public required NotifyRuling Ruling { get; init; }

    [JsonPropertyName("fields")]
    public IReadOnlyDictionary<string, string> Fields { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public static bool TryCreate(
        NotifyRuling ruling,
        IReadOnlyDictionary<string, string>? fields,
        out NotifyRulingEnvelope? envelope,
        out string error)
    {
        envelope = null;
        if (ruling is null || !ruling.Verifies())
        {
            error = "ruling digest mismatch: the supplied ruling does not verify before relay.";
            return false;
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        if (fields is not null)
        {
            foreach (var entry in fields)
            {
                if (string.IsNullOrWhiteSpace(entry.Key)
                    || entry.Key.IndexOfAny(['\r', '\n']) >= 0
                    || entry.Key.Equals("payload", StringComparison.OrdinalIgnoreCase)
                    || entry.Key.Equals("digest", StringComparison.OrdinalIgnoreCase)
                    || entry.Key.Equals("origin", StringComparison.OrdinalIgnoreCase)
                    || entry.Key.Equals("ruling", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"ruling envelope field '{entry.Key}' is reserved or empty.";
                    return false;
                }

                if (entry.Value is null
                    || entry.Value.IndexOfAny(['\r', '\n']) >= 0)
                {
                    error = $"ruling envelope field '{entry.Key}' must be a one-line value.";
                    return false;
                }

                normalized[entry.Key] = entry.Value;
            }
        }

        envelope = new NotifyRulingEnvelope { Ruling = ruling, Fields = normalized };
        error = string.Empty;
        return true;
    }
}

internal sealed record NotifyRulingRelayResult
{
    public required bool Accepted { get; init; }
    public string? Cause { get; init; }
    public string? Summary { get; init; }
    public NotifyRuling? Ruling { get; init; }
    public NotifyRulingEnvelope? Envelope { get; init; }
}

/// <summary>
/// Shared ruling relay and Steward boundary checks.  This is deliberately
/// independent from transport and model/runtime identity.
/// </summary>
internal static class NotifyRulingRelay
{
    public static string ComputeDigest(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

    public static bool IsDigest(string? value) =>
        value is { Length: 64 } && value.All(char.IsAsciiHexDigit);

    /// <summary>
    /// Relays a ruling that was reconstructed by a downstream command.  The
    /// source record is authoritative: payload bytes, digest, and origin must
    /// remain identical, while the envelope may add only non-reserved fields.
    /// </summary>
    public static bool TryRelay(
        NotifyRuling source,
        NotifyRuling relayedRuling,
        IReadOnlyDictionary<string, string>? envelopeFields,
        out NotifyRulingRelayResult result)
    {
        if (source is null || !source.Verifies())
        {
            result = Refused("ruling-digest-mismatch", "The recorded upstream ruling does not verify before Steward relay.");
            return false;
        }

        if (relayedRuling is null || !relayedRuling.Verifies())
        {
            result = Refused("ruling-digest-mismatch", "Steward relay refused: the relayed ruling digest does not verify.");
            return false;
        }

        if (!string.Equals(source.Origin, relayedRuling.Origin, StringComparison.OrdinalIgnoreCase))
        {
            result = Refused(
                "ruling-origin-mismatch",
                $"Steward relay refused: ruling origin must remain '{source.Origin}', not '{relayedRuling.Origin}'.");
            return false;
        }

        if (!source.PayloadBytes.SequenceEqual(relayedRuling.PayloadBytes)
            || !string.Equals(source.Digest, relayedRuling.Digest, StringComparison.OrdinalIgnoreCase))
        {
            result = Refused(
                "ruling-digest-mismatch",
                $"Steward relay refused: ruling payload bytes or digest changed (expected '{source.Digest}', relayed '{relayedRuling.Digest}').");
            return false;
        }

        return TryRelay(source, relayedRuling.Payload, envelopeFields, out result);
    }

    public static bool TryRelay(
        NotifyRuling ruling,
        string? relayedPayload,
        IReadOnlyDictionary<string, string>? envelopeFields,
        out NotifyRulingRelayResult result)
    {
        if (ruling is null)
        {
            result = Refused("ruling-missing", "A ruling is required before Steward relay.");
            return false;
        }

        if (relayedPayload is null
            || !string.Equals(relayedPayload, ruling.Payload, StringComparison.Ordinal)
            || !string.Equals(ComputeDigest(relayedPayload ?? string.Empty), ruling.Digest, StringComparison.OrdinalIgnoreCase))
        {
            var computed = ComputeDigest(relayedPayload ?? string.Empty);
            result = Refused(
                "ruling-digest-mismatch",
                $"Steward relay refused: ruling digest mismatch (expected '{ruling.Digest}', computed '{computed}').");
            return false;
        }

        if (!NotifyRulingEnvelope.TryCreate(ruling, envelopeFields, out var envelope, out var envelopeError)
            || envelope is null)
        {
            result = Refused("ruling-envelope-invalid", envelopeError);
            return false;
        }

        result = new NotifyRulingRelayResult
        {
            Accepted = true,
            Summary = "Steward relay accepted: ruling payload bytes and digest remain unchanged; envelope fields are additive.",
            Ruling = ruling,
            Envelope = envelope,
        };
        return true;
    }

    /// <summary>
    /// A Steward is allowed to answer routine events.  Judgement events must
    /// name the specialist downstream delegation that carries the answer.
    /// The check intentionally consumes logical role/event kind only: runtime
    /// and model declarations cannot bypass it.
    /// </summary>
    public static bool TryValidateStewardAnswer(
        string? fromRole,
        string eventKind,
        string? toRole,
        string? downstreamDelegationReference,
        out string error)
        => TryValidateStewardAnswer(
            fromRole,
            eventKind,
            toRole,
            downstreamDelegationReference,
            downstreamEvidenceResolved: !string.IsNullOrWhiteSpace(downstreamDelegationReference),
            out error);

    internal static bool TryValidateStewardAnswer(
        string? fromRole,
        string eventKind,
        string? toRole,
        string? downstreamDelegationReference,
        bool downstreamEvidenceResolved,
        out string error)
    {
        error = string.Empty;
        if (!LogicalRoleNormalizer.TryNormalize(fromRole, out var canonicalFrom, out error)
            || canonicalFrom is null
            || !string.Equals(canonicalFrom, LogicalRoleNormalizer.Steward, StringComparison.Ordinal))
        {
            error = string.Empty;
            return true;
        }

        if (!NotifyEventKindRouting.TryNormalize(eventKind, out var normalizedKind, out error)
            || normalizedKind is null)
        {
            return false;
        }

        if (!NotifyEventKindRouting.IsJudgement(normalizedKind))
        {
            return true;
        }

        var requiredTarget = RequiredTarget(normalizedKind, toRole);
        if (string.IsNullOrWhiteSpace(downstreamDelegationReference))
        {
            error = $"Steward answer refused for {normalizedKind}: required downstream delegation reference to {DisplayRole(requiredTarget)}.";
            return false;
        }

        if (!downstreamEvidenceResolved)
        {
            error = $"Steward answer refused for {normalizedKind}: downstream delegation reference '{downstreamDelegationReference}' did not resolve in recorded G788 execution evidence.";
            return false;
        }

        if (toRole is not null
            && LogicalRoleNormalizer.TryNormalize(toRole, out var canonicalTarget, out _)
            && canonicalTarget is not null
            && !string.Equals(canonicalTarget, requiredTarget, StringComparison.Ordinal))
        {
            error = $"Steward answer refused for {normalizedKind}: required downstream target is {DisplayRole(requiredTarget)}, not '{toRole}'.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves the ruling that was originally delivered by Architect to the
    /// Steward.  A Steward must relay this durable source record; it may not
    /// manufacture a new Architect-labelled ruling of its own.
    /// </summary>
    internal static bool TryResolveUpstreamArchitectRuling(
        NotifyPendingDelegation? pending,
        out NotifyRuling? ruling,
        out string error)
    {
        ruling = null;
        if (pending is null)
        {
            error = "Steward judgement refused: no recorded upstream Architect ruling/delegation was found.";
            return false;
        }

        if (!LogicalRoleNormalizer.TryNormalize(pending.DelegatingRole, out var delegatingRole, out _)
            || !string.Equals(delegatingRole, LogicalRoleNormalizer.Architect, StringComparison.Ordinal)
            || !LogicalRoleNormalizer.TryNormalize(pending.RecipientRole, out var recipientRole, out _)
            || !string.Equals(recipientRole, LogicalRoleNormalizer.Steward, StringComparison.Ordinal))
        {
            error = $"Steward judgement refused for task '{pending.TaskId}': the recorded delegation is not an Architect-to-Steward ruling.";
            return false;
        }

        if (pending.Ruling is null)
        {
            error = $"Steward judgement refused for task '{pending.TaskId}': no upstream Architect ruling was recorded.";
            return false;
        }

        if (!pending.Ruling.Verifies())
        {
            error = $"Steward judgement refused for task '{pending.TaskId}': recorded upstream ruling digest does not verify.";
            return false;
        }

        if (!LogicalRoleNormalizer.TryNormalize(pending.Ruling.Origin, out var origin, out _)
            || !string.Equals(origin, LogicalRoleNormalizer.Architect, StringComparison.Ordinal))
        {
            error = $"Steward judgement refused for task '{pending.TaskId}': upstream ruling origin must be Architect.";
            return false;
        }

        ruling = pending.Ruling;
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// G796's downstream check is intentionally a thin call to the G788
    /// recognizer.  Keeping the call here prevents a second token parser from
    /// drifting in field order or case handling.
    /// </summary>
    public static bool HasDownstreamDelegationEvidence(
        string? taskId,
        string? objective,
        IEnumerable<string?>? inputs,
        Regex executionUnitPattern,
        string expectedExecutionUnit)
    {
        var token = NotifyDelegationExecutionEvidence.ExtractExecutionUnitToken(
            taskId,
            objective,
            inputs,
            executionUnitPattern);
        return string.Equals(token, expectedExecutionUnit, StringComparison.OrdinalIgnoreCase);
    }

    private static string RequiredTarget(string eventKind, string? toRole)
    {
        if (eventKind == NotifyEventKindRouting.Question
            && LogicalRoleNormalizer.TryNormalize(toRole, out var normalized, out _)
            && string.Equals(normalized, LogicalRoleNormalizer.Reviewer, StringComparison.Ordinal))
        {
            return LogicalRoleNormalizer.Reviewer;
        }

        return LogicalRoleNormalizer.Architect;
    }

    private static string DisplayRole(string role) =>
        role == LogicalRoleNormalizer.Reviewer ? "Reviewer" : "Architect";

    private static NotifyRulingRelayResult Refused(string cause, string summary) => new()
    {
        Accepted = false,
        Cause = cause,
        Summary = summary,
    };
}
