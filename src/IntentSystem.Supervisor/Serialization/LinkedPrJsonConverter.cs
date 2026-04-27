using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Supervisor.Serialization;

/// <summary>
/// Tolerates both legacy string-shaped and structured object-shaped <c>linked_pr</c>
/// values in queue-state payloads. The structured form mirrors <c>linked_issue</c>
/// (<c>{ repo, number, url }</c>); we extract the <c>url</c> field and keep the
/// internal model as a <c>string?</c> so existing consumers continue to work.
/// </summary>
internal sealed class LinkedPrJsonConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                return reader.GetString();

            case JsonTokenType.StartObject:
                using (var document = JsonDocument.ParseValue(ref reader))
                {
                    if (!document.RootElement.TryGetProperty("url", out var urlElement))
                    {
                        return null;
                    }

                    return urlElement.ValueKind == JsonValueKind.Null ? null : urlElement.GetString();
                }

            default:
                throw new JsonException(
                    $"Unexpected token {reader.TokenType} when reading linked_pr; expected string, object, or null.");
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value);
    }
}
