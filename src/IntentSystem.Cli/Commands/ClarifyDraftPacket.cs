using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Structured clarification draft packet emitted by
/// <c>intent-cli clarify draft</c> (G181). Read-only scaffold the AI tasking
/// thread can review with the owner before recording an accepted decision.
/// Field names are stable snake_case for JSON ingestion.
/// </summary>
internal sealed record ClarifyDraftPacket
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("question")]
    public required string Question { get; init; }

    [JsonPropertyName("background")]
    public required IReadOnlyList<string> Background { get; init; }

    [JsonPropertyName("options")]
    public required IReadOnlyList<ClarifyDraftOption> Options { get; init; }

    [JsonPropertyName("recommendation")]
    public string? Recommendation { get; init; }

    [JsonPropertyName("return_path")]
    public string? ReturnPath { get; init; }

    [JsonPropertyName("notes")]
    public required IReadOnlyList<string> Notes { get; init; }
}

internal sealed record ClarifyDraftOption
{
    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("pros")]
    public required IReadOnlyList<string> Pros { get; init; }

    [JsonPropertyName("cons")]
    public required IReadOnlyList<string> Cons { get; init; }
}
