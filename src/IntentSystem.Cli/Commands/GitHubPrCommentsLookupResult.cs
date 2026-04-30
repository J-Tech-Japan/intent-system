using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G204: Deserialized GitHub PR comment / review payload returned by
/// <see cref="IGitHubPrCommentsLookup"/>. Field names match the JSON shape
/// emitted by <c>gh pr view --json reviews,comments</c> plus a
/// <c>gh api graphql</c> fallback for review threads (see
/// <see cref="GhCliGitHubPrCommentsLookup.ReviewThreadsGraphqlQuery"/>).
/// Tests inject this record directly to avoid GitHub network access.
///
/// G204 follow-up: the installed <c>gh</c> CLI returns <c>author</c> as an
/// object <c>{"login":"&lt;user&gt;"}</c> rather than a bare string, and
/// emits camelCase keys (<c>createdAt</c>, <c>submittedAt</c>) rather than
/// snake_case. The DTOs below match that real shape so the live adapter
/// path can deserialize successfully; tests construct these records via
/// initializers and continue to pass <c>Author</c> as a plain string thanks
/// to <see cref="GitHubAuthorLoginJsonConverter"/>.
/// </summary>
internal sealed record GitHubPrCommentsLookupResult
{
    [JsonPropertyName("reviews")]
    public IReadOnlyList<GitHubPrReview> Reviews { get; init; } = Array.Empty<GitHubPrReview>();

    [JsonPropertyName("comments")]
    public IReadOnlyList<GitHubPrIssueComment> Comments { get; init; } = Array.Empty<GitHubPrIssueComment>();

    [JsonPropertyName("reviewThreads")]
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
    [JsonConverter(typeof(GitHubAuthorLoginJsonConverter))]
    public string Author { get; init; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    [JsonPropertyName("submittedAt")]
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
    [JsonConverter(typeof(GitHubAuthorLoginJsonConverter))]
    public string Author { get; init; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; init; }
}

/// <summary>
/// G204: Single PR review thread (inline review comment chain on a diff).
/// </summary>
internal sealed record GitHubPrReviewThread
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("isResolved")]
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
    [JsonConverter(typeof(GitHubAuthorLoginJsonConverter))]
    public string Author { get; init; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;
}

/// <summary>
/// G204 follow-up: deserializes a GitHub <c>author</c> field — which the
/// installed <c>gh</c> CLI emits as an object <c>{"login":"&lt;user&gt;"}</c>
/// — into a flat login string. Also tolerates a bare-string author (for
/// fixtures and tests) and a <c>null</c> author (deleted account, etc.).
/// </summary>
internal sealed class GitHubAuthorLoginJsonConverter : JsonConverter<string>
{
    // For reference types, JsonConverter<T>.HandleNull defaults to false, so
    // the runtime would short-circuit JSON null to a null .NET string before
    // ever calling Read. We want null authors (deleted accounts) to project
    // to the empty string, so we opt in.
    public override bool HandleNull => true;

    public override string Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return string.Empty;

            case JsonTokenType.String:
                return reader.GetString() ?? string.Empty;

            case JsonTokenType.StartObject:
            {
                string login = string.Empty;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        return login;
                    }

                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        var propertyName = reader.GetString();
                        reader.Read();
                        if (string.Equals(propertyName, "login", StringComparison.Ordinal))
                        {
                            login = reader.TokenType == JsonTokenType.String
                                ? reader.GetString() ?? string.Empty
                                : string.Empty;
                        }
                        else
                        {
                            reader.Skip();
                        }
                    }
                }

                return login;
            }

            default:
                throw new JsonException(
                    $"unexpected token {reader.TokenType} for GitHub author field");
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        string value,
        JsonSerializerOptions options)
    {
        // We never serialize back out; tests assert on the projected string.
        writer.WriteStringValue(value);
    }
}
