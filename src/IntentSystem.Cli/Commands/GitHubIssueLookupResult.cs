using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G202: Deserialized GitHub issue payload returned by
/// <see cref="IGitHubIssueLookup"/>. Field names match the JSON shape emitted
/// by <c>gh issue view --json number,state,labels,title,body,closed,closedAt</c>
/// so the default adapter can deserialize directly into this record.
/// </summary>
internal sealed record GitHubIssueLookupResult
{
    [JsonPropertyName("number")]
    public int Number { get; init; }

    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

    [JsonPropertyName("closed")]
    public bool Closed { get; init; }

    [JsonPropertyName("closedAt")]
    public string? ClosedAt { get; init; }

    [JsonPropertyName("labels")]
    public IReadOnlyList<GitHubIssueLabel> Labels { get; init; } = Array.Empty<GitHubIssueLabel>();
}

/// <summary>
/// G202: Single GitHub issue label as returned by <c>gh issue view --json labels</c>.
/// Only <c>name</c> is consumed by the preflight classifier.
/// </summary>
internal sealed record GitHubIssueLabel
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}
