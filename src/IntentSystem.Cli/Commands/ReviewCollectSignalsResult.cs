using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G374: result of <c>intent-cli review collect-signals</c>. Lists the
/// pending structured worker signals a host review/design loop should
/// triage, and reports how many label-matched items were skipped because
/// they were already handled or carried no parseable marker.
/// </summary>
internal sealed record ReviewCollectSignalsResult
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("pending_signals")]
    public required IReadOnlyList<PendingSignal> PendingSignals { get; init; }

    [JsonPropertyName("pending_count")]
    public int PendingCount => PendingSignals.Count;

    /// <summary>Items carrying intent-signal-handled (already processed) that were skipped.</summary>
    [JsonPropertyName("handled_skipped_count")]
    public required int HandledSkippedCount { get; init; }

    /// <summary>Items labelled intent-signal-sent but with no parseable marker comment.</summary>
    [JsonPropertyName("unmarked_count")]
    public required int UnmarkedCount { get; init; }

    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>G374: one pending structured signal awaiting host triage.</summary>
internal sealed record PendingSignal
{
    [JsonPropertyName("target")]
    public required string Target { get; init; }

    [JsonPropertyName("number")]
    public required int Number { get; init; }

    [JsonPropertyName("signal_kind")]
    public required string SignalKind { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("comment_ref")]
    public string CommentRef { get; init; } = string.Empty;

    [JsonPropertyName("comment_created_at")]
    public string CommentCreatedAt { get; init; } = string.Empty;
}
