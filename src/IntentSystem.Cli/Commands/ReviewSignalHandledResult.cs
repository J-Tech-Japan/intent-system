using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G374: result of <c>intent-cli review signal-handled</c>. Records the
/// planned (dry-run) or executed (write) label convergence that marks a
/// structured worker signal processed: add <c>intent-signal-handled</c>,
/// remove <c>intent-signal-sent</c>.
/// </summary>
internal sealed record ReviewSignalHandledResult
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("target")]
    public required string Target { get; init; }

    [JsonPropertyName("number")]
    public required int Number { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    /// <summary>Whether there was a pending signal to converge (false ⇒ nothing to do).</summary>
    [JsonPropertyName("proceed")]
    public required bool Proceed { get; init; }

    /// <summary>Whether the label transition was actually applied (write mode only).</summary>
    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }

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
}
