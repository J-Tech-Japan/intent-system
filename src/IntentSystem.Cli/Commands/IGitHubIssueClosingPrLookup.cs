using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G358: Testability seam for querying which PRs closed a GitHub issue.
/// The production implementation shells out to <c>gh api graphql</c> using
/// the <c>closedByPullRequestsReferences</c> field on the GraphQL
/// <c>Issue</c> type; tests inject a fake to avoid GitHub network access.
/// </summary>
internal interface IGitHubIssueClosingPrLookup
{
    GitHubIssueClosingPrLookupResult Lookup(string repo, int issueNumber);
}

/// <summary>
/// G358: Default <see cref="IGitHubIssueClosingPrLookup"/> that shells out to
/// <c>gh api graphql</c> with the GitHub GraphQL
/// <c>closedByPullRequestsReferences</c> field on the <c>Issue</c> type to
/// discover merged PRs that closed a given issue. This is the only place
/// permitted to call <c>Process.Start</c> for issue-closing-PR lookups —
/// command and analyzer layers remain pure.
/// </summary>
internal sealed class GhCliGitHubIssueClosingPrLookup : IGitHubIssueClosingPrLookup
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Compact single-line query to avoid shell quoting issues. Fetches up to
    // 10 closing PRs — in practice there is at most one per issue.
    private const string GraphQlQuery =
        "query($owner:String!,$name:String!,$number:Int!)" +
        "{repository(owner:$owner,name:$name)" +
        "{issue(number:$number)" +
        "{state closedByPullRequestsReferences(includeClosedPrs:true first:10)" +
        "{nodes{number state merged mergedAt baseRefName repository{name owner{login}}}}}}}";

    public GitHubIssueClosingPrLookupResult Lookup(string repo, int issueNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        var slashIdx = repo.IndexOf('/', StringComparison.Ordinal);
        if (slashIdx < 0 || slashIdx + 1 >= repo.Length)
        {
            throw new InvalidOperationException(
                $"repo must be in 'owner/name' format (got '{repo}').");
        }
        var owner = repo[..slashIdx];
        var name = repo[(slashIdx + 1)..];

        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            // G484: decode gh stdout/stderr as UTF-8 regardless of the ambient
            // console code page (Windows cp932) so Japanese payloads stay valid.
            StandardOutputEncoding = ProcessOutputEncoding.Utf8NoBom,
            StandardErrorEncoding = ProcessOutputEncoding.Utf8NoBom,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("api");
        startInfo.ArgumentList.Add("graphql");
        startInfo.ArgumentList.Add("-F");
        startInfo.ArgumentList.Add($"owner={owner}");
        startInfo.ArgumentList.Add("-F");
        startInfo.ArgumentList.Add($"name={name}");
        startInfo.ArgumentList.Add("-F");
        startInfo.ArgumentList.Add($"number={issueNumber}");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add($"query={GraphQlQuery}");

        string stdout;
        string stderr;
        int exitCode;

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "failed to start `gh` process for issue closing-PR lookup");
            stdout = process.StandardOutput.ReadToEnd();
            stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            exitCode = process.ExitCode;
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or IOException)
        {
            throw new InvalidOperationException(
                $"could not invoke `gh api graphql` for issue #{issueNumber} in {repo}: {exception.Message}",
                exception);
        }

        if (exitCode != 0)
        {
            var errorBody = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"`gh api graphql` failed (exit {exitCode}) for issue #{issueNumber} in {repo}: {errorBody.Trim()}");
        }

        try
        {
            var response = JsonSerializer.Deserialize<GraphQlResponse>(stdout, JsonOptions);
            var issue = response?.Data?.Repository?.Issue;
            if (issue is null)
            {
                throw new InvalidOperationException(
                    $"`gh api graphql` returned an empty payload for issue #{issueNumber} in {repo}");
            }

            var closingPrs = (issue.ClosedByPullRequestsReferences?.Nodes
                ?? Array.Empty<ClosingPrNode>())
                .Select(n => new GitHubIssueClosingPrRef
                {
                    Number = n.Number,
                    State = n.State ?? string.Empty,
                    Merged = n.Merged,
                    MergedAt = n.MergedAt,
                    BaseRefName = n.BaseRefName ?? string.Empty,
                    RepoOwner = n.Repository?.Owner?.Login ?? string.Empty,
                    RepoName = n.Repository?.Name ?? string.Empty,
                })
                .ToArray();

            return new GitHubIssueClosingPrLookupResult
            {
                IssueNumber = issueNumber,
                State = issue.State ?? string.Empty,
                ClosingPullRequests = closingPrs,
            };
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"could not parse `gh api graphql` JSON for issue #{issueNumber} in {repo}: {exception.Message}",
                exception);
        }
    }

    // ── Internal deserialization types (private to this file) ───────────────

    private sealed class GraphQlResponse
    {
        [JsonPropertyName("data")]
        public GraphQlData? Data { get; init; }
    }

    private sealed class GraphQlData
    {
        [JsonPropertyName("repository")]
        public GraphQlRepository? Repository { get; init; }
    }

    private sealed class GraphQlRepository
    {
        [JsonPropertyName("issue")]
        public GraphQlIssue? Issue { get; init; }
    }

    private sealed class GraphQlIssue
    {
        [JsonPropertyName("state")]
        public string? State { get; init; }

        [JsonPropertyName("closedByPullRequestsReferences")]
        public ClosingPrConnection? ClosedByPullRequestsReferences { get; init; }
    }

    private sealed class ClosingPrConnection
    {
        [JsonPropertyName("nodes")]
        public IReadOnlyList<ClosingPrNode>? Nodes { get; init; }
    }

    private sealed class ClosingPrNode
    {
        [JsonPropertyName("number")]
        public int Number { get; init; }

        [JsonPropertyName("state")]
        public string? State { get; init; }

        [JsonPropertyName("merged")]
        public bool Merged { get; init; }

        [JsonPropertyName("mergedAt")]
        public string? MergedAt { get; init; }

        [JsonPropertyName("baseRefName")]
        public string? BaseRefName { get; init; }

        [JsonPropertyName("repository")]
        public ClosingPrRepository? Repository { get; init; }
    }

    private sealed class ClosingPrRepository
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("owner")]
        public ClosingPrOwner? Owner { get; init; }
    }

    private sealed class ClosingPrOwner
    {
        [JsonPropertyName("login")]
        public string? Login { get; init; }
    }
}

/// <summary>
/// G358: Result of querying which PRs closed a GitHub issue, via
/// <c>gh api graphql</c> <c>closedByPullRequestsReferences</c>.
/// </summary>
internal sealed record GitHubIssueClosingPrLookupResult
{
    /// <summary>The queried issue number.</summary>
    public required int IssueNumber { get; init; }

    /// <summary>GitHub issue state (e.g. <c>OPEN</c>, <c>CLOSED</c>).</summary>
    public required string State { get; init; }

    /// <summary>
    /// PRs that closed (or reference-closed) this issue, as reported by
    /// GitHub's <c>closedByPullRequestsReferences</c> field.
    /// Empty when no closing PRs exist or the issue is still open.
    /// </summary>
    public required IReadOnlyList<GitHubIssueClosingPrRef> ClosingPullRequests { get; init; }
}

/// <summary>
/// G358: A single PR that closed a GitHub issue, returned by the
/// <c>closedByPullRequestsReferences</c> GraphQL field.
/// </summary>
internal sealed record GitHubIssueClosingPrRef
{
    public required int Number { get; init; }
    public required string State { get; init; }
    public required bool Merged { get; init; }
    public string? MergedAt { get; init; }
    public required string BaseRefName { get; init; }

    /// <summary>Owner login of the repo where the PR lives. Empty for same-repo PRs
    /// when the GraphQL response omits the field.</summary>
    public required string RepoOwner { get; init; }

    /// <summary>Repository name (not full path) where the PR lives. Empty for
    /// same-repo PRs when the GraphQL response omits the field.</summary>
    public required string RepoName { get; init; }
}
