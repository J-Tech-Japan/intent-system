using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G374: result of <c>intent-cli worker signal &lt;kind&gt;</c>. Records
/// the planned (dry-run) or executed (write) comment post + label
/// transition for sending a structured worker signal.
/// </summary>
internal sealed record WorkerSignalResult
{
    [JsonPropertyName("signal_kind")]
    public required string SignalKind { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("target")]
    public required string Target { get; init; }

    [JsonPropertyName("number")]
    public required int Number { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    /// <summary>Whether the signal is valid to send (kind/target legal, body present).</summary>
    [JsonPropertyName("proceed")]
    public required bool Proceed { get; init; }

    /// <summary>Whether a comment was actually posted (write mode only).</summary>
    [JsonPropertyName("posted")]
    public required bool Posted { get; init; }

    /// <summary>Whether the label transition was actually applied (write mode only).</summary>
    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }

    /// <summary>The created comment reference (URL) when posted; null in dry-run.</summary>
    [JsonPropertyName("comment_ref")]
    public string? CommentRef { get; init; }

    /// <summary>The machine marker line embedded (or that would be embedded) in the comment.</summary>
    [JsonPropertyName("marker")]
    public required string Marker { get; init; }

    [JsonPropertyName("add_labels")]
    public required IReadOnlyList<string> AddLabels { get; init; }

    [JsonPropertyName("remove_labels")]
    public required IReadOnlyList<string> RemoveLabels { get; init; }

    [JsonPropertyName("current_labels")]
    public required IReadOnlyList<string> CurrentLabels { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    [JsonPropertyName("github_only")]
    public bool? GithubOnly { get; init; }
}
