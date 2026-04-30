using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G204: Deserialized GitHub PR comment / review payload returned by
/// <see cref="IGitHubPrCommentsLookup"/>. Field names match the JSON shape
/// emitted by <c>gh pr view --json reviews,comments,reviewThreads</c> with a
/// fallback to <c>gh api repos/&lt;repo&gt;/pulls/&lt;num&gt;/comments</c>
/// when richer review-thread data is needed. Tests inject this record
/// directly to avoid GitHub network access.
/// </summary>
internal sealed record GitHubPrCommentsLookupResult
{
    [JsonPropertyName("reviews")]
    public IReadOnlyList<GitHubPrReview> Reviews { get; init; } = Array.Empty<GitHubPrReview>();

    [JsonPropertyName("comments")]
    public IReadOnlyList<GitHubPrIssueComment> Comments { get; init; } = Array.Empty<GitHubPrIssueComment>();

    [JsonPropertyName("review_threads")]
    public IReadOnlyList<GitHubPrReviewThread> ReviewThreads { get; init; } = Array.Empty<GitHubPrReviewThread>();
}

/// <summary>
/// G204: Single PR-level review (summary review on the PR, not a thread).
/// </summary>
internal sealed record GitHubPrReview
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; init; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    [JsonPropertyName("submitted_at")]
    public string? SubmittedAt { get; init; }
}

/// <summary>
/// G204: Single PR-level issue comment (a comment on the conversation tab,
/// not a review-thread reply).
/// </summary>
internal sealed record GitHubPrIssueComment
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; init; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; init; }
}

/// <summary>
/// G204: Single PR review thread (inline review comment chain on a diff).
/// </summary>
internal sealed record GitHubPrReviewThread
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("is_resolved")]
    public bool IsResolved { get; init; }

    [JsonPropertyName("comments")]
    public IReadOnlyList<GitHubPrReviewThreadComment> Comments { get; init; }
        = Array.Empty<GitHubPrReviewThreadComment>();
}

/// <summary>
/// G204: Single comment within a PR review thread.
/// </summary>
internal sealed record GitHubPrReviewThreadComment
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; init; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;
}
