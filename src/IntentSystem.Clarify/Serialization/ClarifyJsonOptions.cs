using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Clarify.Serialization;

internal static class ClarifyJsonOptions
{
    public static JsonSerializerOptions Indented { get; } = Create(writeIndented: true);

    public static JsonSerializerOptions Compact { get; } = Create(writeIndented: false);

    private static JsonSerializerOptions Create(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = writeIndented
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));

        return options;
    }
}
