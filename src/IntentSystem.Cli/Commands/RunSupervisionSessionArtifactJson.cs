using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class RunSupervisionSessionArtifactJson
{
    private static readonly JsonSerializerOptions IndentedOptions = CreateOptions(writeIndented: true);

    private static readonly JsonSerializerOptions CompactOptions = CreateOptions(writeIndented: false);

    public static string Serialize(RunSupervisionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return JsonSerializer.Serialize(session, IndentedOptions);
    }

    public static RunSupervisionSession Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize<RunSupervisionSession>(json, CompactOptions)
            ?? throw new InvalidOperationException("Run supervision session artifact deserialized to null.");
    }

    private static JsonSerializerOptions CreateOptions(bool writeIndented)
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
