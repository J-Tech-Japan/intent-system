using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G203: Deserialized GitHub PR payload returned by
/// <see cref="IGitHubPrLookup"/>. Field names match the JSON shape emitted by
/// <c>gh pr view --json number,state,title,body,labels,isDraft,closed,mergedAt,closedAt,closingIssuesReferences</c>
/// (see <see cref="GhCliGitHubPrLookup.PrViewJsonFields"/>) so the default
/// adapter can deserialize directly into this record.
///
/// G204 follow-up: the installed <c>gh</c> CLI rejects <c>merged</c> as a
/// JSON field, so the adapter no longer requests it. <see cref="Merged"/> is
/// derived post-deserialization from the supported <c>state</c> field by
/// <see cref="GhCliGitHubPrLookup.DeriveMergedFromState"/>. Tests that
/// construct this record directly (without going through the adapter) may
/// continue to set <see cref="Merged"/> explicitly via the initializer; the
/// JSON attribute is retained as a defensive no-op.
/// </summary>
internal sealed record GitHubPrLookupResult
{
    [JsonPropertyName("number")]
    public int Number { get; init; }

    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

    [JsonPropertyName("isDraft")]
    public bool IsDraft { get; init; }

    [JsonPropertyName("closed")]
    public bool Closed { get; init; }

    [JsonPropertyName("merged")]
    public bool Merged { get; init; }

    [JsonPropertyName("mergedAt")]
    public string? MergedAt { get; init; }

    [JsonPropertyName("closedAt")]
    public string? ClosedAt { get; init; }

    [JsonPropertyName("labels")]
    public IReadOnlyList<GitHubPrLabel> Labels { get; init; } = Array.Empty<GitHubPrLabel>();

    [JsonPropertyName("closingIssuesReferences")]
    public IReadOnlyList<GitHubPrClosingIssueReference> ClosingIssuesReferences { get; init; }
        = Array.Empty<GitHubPrClosingIssueReference>();

    /// <summary>
    /// G347: the PR's base branch name as returned by <c>gh pr view --json baseRefName</c>.
    /// Used by <c>worker complete</c> to validate the PR targets the expected branch.
    /// </summary>
    [JsonPropertyName("baseRefName")]
    public string BaseRefName { get; init; } = string.Empty;
}

/// <summary>
/// G203: Single GitHub PR label as returned by <c>gh pr view --json labels</c>.
/// Only <c>name</c> is consumed by the preflight classifier.
/// </summary>
internal sealed record GitHubPrLabel
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// G203: Single closing-issue reference entry as returned by
/// <c>gh pr view --json closingIssuesReferences</c>. Captures the issue
/// number plus an optional repo descriptor for cross-repo links.
/// </summary>
internal sealed record GitHubPrClosingIssueReference
{
    [JsonPropertyName("number")]
    public int Number { get; init; }

    [JsonPropertyName("repository")]
    public GitHubPrClosingIssueRepository? Repository { get; init; }
}

/// <summary>
/// G203: Repository descriptor for a closing-issue reference. Mirrors
/// <c>{ "name": "&lt;repo&gt;", "owner": { "login": "&lt;owner&gt;" } }</c>.
/// </summary>
internal sealed record GitHubPrClosingIssueRepository
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("owner")]
    public GitHubPrClosingIssueRepositoryOwner? Owner { get; init; }
}

internal sealed record GitHubPrClosingIssueRepositoryOwner
{
    [JsonPropertyName("login")]
    public string Login { get; init; } = string.Empty;
}
