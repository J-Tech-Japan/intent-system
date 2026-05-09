using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G302: structured clarification artifact. One TOML file per clarification
/// under <c>intents/&lt;domain&gt;/clarifications/&lt;id&gt;.toml</c>. The
/// shape is product-owner friendly: background, question, options with
/// pros/cons, recommendation, and a list of execution units the
/// clarification blocks. The <c>answer</c> table is populated by
/// <c>intent-cli clarification answer --write</c> and never by free-form
/// markdown editing.
///
/// Coexists with the legacy markdown <c>clarifications/open.md</c>: the
/// existing <see cref="ClarificationOpenDetector"/> still parses that file,
/// and <see cref="IntentStatusCommand"/> /
/// <see cref="IntentNextSliceCommand"/> OR the two sources together so a
/// domain with EITHER a markdown blocker OR an open structured
/// clarification reports <c>clarification_open: true</c>.
/// </summary>
internal sealed record StructuredClarification
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("background")]
    public string Background { get; init; } = string.Empty;

    [JsonPropertyName("question")]
    public required string Question { get; init; }

    [JsonPropertyName("options")]
    public required IReadOnlyList<StructuredClarificationOption> Options { get; init; }

    [JsonPropertyName("recommendation")]
    public string? Recommendation { get; init; }

    [JsonPropertyName("blocks")]
    public required IReadOnlyList<string> Blocks { get; init; }

    [JsonPropertyName("answer")]
    public StructuredClarificationAnswer? Answer { get; init; }

    [JsonPropertyName("source_path")]
    public string? SourcePath { get; init; }

    public bool IsOpen() => string.Equals(Status, StructuredClarificationStatus.Open, StringComparison.Ordinal);
}

internal sealed record StructuredClarificationOption
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("pros")]
    public IReadOnlyList<string> Pros { get; init; } = Array.Empty<string>();

    [JsonPropertyName("cons")]
    public IReadOnlyList<string> Cons { get; init; } = Array.Empty<string>();
}

internal sealed record StructuredClarificationAnswer
{
    [JsonPropertyName("choice")]
    public required string Choice { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }

    [JsonPropertyName("answered_at")]
    public required string AnsweredAt { get; init; }
}

internal static class StructuredClarificationStatus
{
    public const string Open = "open";
    public const string Answered = "answered";
}
