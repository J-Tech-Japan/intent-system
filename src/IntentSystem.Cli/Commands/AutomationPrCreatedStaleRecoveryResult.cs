using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G836: JSON result for <c>intent-cli automation pr-created-stale-recovery</c>.
/// </summary>
internal sealed record AutomationPrCreatedStaleRecoveryResult
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("issue")]
    public required int Issue { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("team")]
    public required string Team { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("cause")]
    public string? Cause { get; init; }

    [JsonPropertyName("linked_pr")]
    public int? LinkedPr { get; init; }

    [JsonPropertyName("applied")]
    public bool Applied { get; init; }

    [JsonPropertyName("planned_mutations")]
    public IReadOnlyList<string> PlannedMutations { get; init; } = Array.Empty<string>();

    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}
