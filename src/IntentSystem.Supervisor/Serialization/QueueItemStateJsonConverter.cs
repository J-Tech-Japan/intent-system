using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Supervisor.Serialization;

/// <summary>
/// Reads <see cref="QueueItemState"/> as canonical kebab-case strings (matching the
/// project-wide <c>JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower)</c>) plus
/// recognized legacy compatibility aliases. Writes back the canonical kebab-case form.
/// </summary>
/// <remarks>
/// G178: parent-host queue-state may persist <c>state: "pending"</c> as a legacy
/// synonym for canonical <see cref="QueueItemState.Queued"/>.
/// G472: parent-host queue-state may persist <c>state: "issue-published"</c>
/// for a unit whose child issue has been cut and is now in flight; this is a
/// read-only compatibility alias for canonical <see cref="QueueItemState.Active"/>.
/// Tolerate these aliases at the deserialize boundary so submit/seed/numbering
/// pipelines do not crash on a row that was emitted by an older or external
/// producer. Any unknown value outside the recognized canonical set + aliases
/// still surfaces a clear <see cref="JsonException"/>.
/// </remarks>
internal sealed class QueueItemStateJsonConverter : JsonConverter<QueueItemState>
{
    public override QueueItemState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"Unexpected token {reader.TokenType} when reading queue item state; expected string.");
        }

        var raw = reader.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new JsonException("Queue item state value was null or empty.");
        }

        if (string.Equals(raw, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return QueueItemState.Queued;
        }

        if (string.Equals(raw, "issue-published", StringComparison.OrdinalIgnoreCase))
        {
            return QueueItemState.Active;
        }

        return raw switch
        {
            "queued" => QueueItemState.Queued,
            "active" => QueueItemState.Active,
            "review" => QueueItemState.Review,
            "fixing" => QueueItemState.Fixing,
            "clarify-blocked" => QueueItemState.ClarifyBlocked,
            "blocked" => QueueItemState.Blocked,
            "completed" => QueueItemState.Completed,
            _ => throw new JsonException(
                $"Unrecognized queue item state value '{raw}'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, QueueItemState value, JsonSerializerOptions options)
    {
        var canonical = value switch
        {
            QueueItemState.Queued => "queued",
            QueueItemState.Active => "active",
            QueueItemState.Review => "review",
            QueueItemState.Fixing => "fixing",
            QueueItemState.ClarifyBlocked => "clarify-blocked",
            QueueItemState.Blocked => "blocked",
            QueueItemState.Completed => "completed",
            _ => throw new JsonException(
                $"Unrecognized queue item state value '{value}'.")
        };

        writer.WriteStringValue(canonical);
    }
}
