using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G382: the readiness verdict produced by
/// <see cref="InterviewReadinessAnalyzer"/> — the classification, the
/// per-dimension checklist, the concrete missing dimensions, and the
/// next highest-value question to ask when not ready.
/// </summary>
internal sealed record InterviewReadinessResult
{
    [JsonPropertyName("classification")]
    public required string Classification { get; init; }

    [JsonPropertyName("dimensions")]
    public required IReadOnlyList<InterviewReadinessDimensionStatus> Dimensions { get; init; }

    [JsonPropertyName("missing_dimensions")]
    public required IReadOnlyList<string> MissingDimensions { get; init; }

    [JsonPropertyName("next_question")]
    public string? NextQuestion { get; init; }

    [JsonPropertyName("next_question_dimension")]
    public string? NextQuestionDimension { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}

internal sealed record InterviewReadinessDimensionStatus
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("tier")]
    public required string Tier { get; init; }

    [JsonPropertyName("resolved")]
    public required bool Resolved { get; init; }
}
