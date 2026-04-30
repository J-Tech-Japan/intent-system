using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G211: Read-only result emitted by <c>intent-cli worker complete</c>.
/// Mirrors the shape of <see cref="WorkerClaimResult"/>: kind, number,
/// mode, proceed/applied flags, add/remove transition, current labels,
/// errors, warnings, summary. The complete-side adds the originating
/// outcome (an issue-to-pr / pr-comment-fix outcome from G205) so
/// downstream summaries can tie the claim/complete pair together.
///
/// In dry-run mode (the default), the record describes the transition
/// without applying it. The command never launches an AI provider,
/// never creates branches, PRs, issues, or comments, and never touches
/// the queue or runs logs.
/// </summary>
internal sealed record WorkerCompleteResult
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("number")]
    public required int Number { get; init; }

    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("proceed")]
    public required bool Proceed { get; init; }

    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }

    [JsonPropertyName("add_labels")]
    public required IReadOnlyList<string> AddLabels { get; init; }

    /// <summary>G211 contract alias — camelCase for AI-thread JSON ingestion.</summary>
    [JsonPropertyName("addLabels")]
    public IReadOnlyList<string> AddLabelsCamelCase => AddLabels;

    [JsonPropertyName("remove_labels")]
    public required IReadOnlyList<string> RemoveLabels { get; init; }

    /// <summary>G211 contract alias — camelCase for AI-thread JSON ingestion.</summary>
    [JsonPropertyName("removeLabels")]
    public IReadOnlyList<string> RemoveLabelsCamelCase => RemoveLabels;

    [JsonPropertyName("current_labels")]
    public required IReadOnlyList<string> CurrentLabels { get; init; }

    /// <summary>G211 contract alias — camelCase for AI-thread JSON ingestion.</summary>
    [JsonPropertyName("currentLabels")]
    public IReadOnlyList<string> CurrentLabelsCamelCase => CurrentLabels;

    [JsonPropertyName("errors")]
    public required IReadOnlyList<string> Errors { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}
