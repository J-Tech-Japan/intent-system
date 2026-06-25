using System.Diagnostics;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G204: Testability seam for <c>intent-cli worker pr-comment-preflight</c>.
/// Tests inject a fake to avoid GitHub network access. The production
/// implementation shells out to two read-only `gh` calls.
/// </summary>
internal interface IGitHubPrCommentsLookup
{
    GitHubPrCommentsLookupResult Lookup(string repo, int prNumber);
}

/// <summary>
/// G204: Default <see cref="IGitHubPrCommentsLookup"/>. Two read-only calls:
/// <list type="number">
///   <item><c>gh pr view &lt;num&gt; --repo &lt;repo&gt; --json reviews,comments</c>
///         for the supported subset (PR #514 review note: <c>gh pr view</c>
///         does NOT expose <c>reviewThreads</c> as a JSON field — it errors
///         with <c>Unknown JSON field: "reviewThreads"</c>).</item>
///   <item><c>gh api graphql -f query=...</c> for the review-thread chain via
///         the documented GraphQL <c>reviewThreads</c> connection on
///         <c>PullRequest</c>.</item>
/// </list>
/// The two responses are merged into a single
/// <see cref="GitHubPrCommentsLookupResult"/>. This is the only file in the
/// worker pr-comment-preflight surface that is permitted to call
/// <c>Process.Start</c> — the command and analyzer layers must remain pure.
/// </summary>
internal sealed class GhCliGitHubPrCommentsLookup : IGitHubPrCommentsLookup
{
    /// <summary>
    /// G204 review fix: <c>gh pr view --json</c> does NOT expose
    /// <c>reviewThreads</c>. The supported subset for this adapter is
    /// <c>reviews,comments</c> only.
    /// </summary>
    internal const string PrViewJsonFields = "reviews,comments";

    /// <summary>
    /// G204 review fix: review-thread retrieval falls through to GraphQL via
    /// <c>gh api graphql -f query=...</c>. The query intentionally embeds the
    /// literal field name <c>reviewThreads(</c> so adapter-shape regression
    /// tests can pin it without round-tripping a real GitHub call.
    /// </summary>
    internal const string ReviewThreadsGraphqlQuery =
        "query($owner:String!,$repo:String!,$pr:Int!){"
        + "repository(owner:$owner,name:$repo){"
        + "pullRequest(number:$pr){"
        + "reviewThreads(first:100){nodes{"
        + "id isResolved comments(first:100){nodes{id body author{login}}}"
        + "}}"
        + "}}}";

    /// <summary>
    /// Builds the <c>gh pr view</c> argument list for the supported subset.
    /// Exposed <c>internal static</c> for adapter-shape regression tests so
    /// reviewers can lock the requested field list and confirm the
    /// unsupported <c>reviewThreads</c> field is no longer requested here.
    /// </summary>
    internal static IReadOnlyList<string> BuildPrViewArguments(string repo, int prNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        return new[]
        {
            "pr",
            "view",
            prNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--repo",
            repo,
            "--json",
            PrViewJsonFields,
        };
    }

    /// <summary>
    /// Builds the <c>gh api graphql</c> argument list for the review-thread
    /// chain. Exposed <c>internal static</c> for adapter-shape regression
    /// tests so reviewers can lock the GraphQL query body and the variable
    /// bindings.
    /// </summary>
    internal static IReadOnlyList<string> BuildGraphqlArguments(string repo, int prNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        var (owner, name) = SplitRepo(repo);

        return new[]
        {
            "api",
            "graphql",
            "-f",
            $"query={ReviewThreadsGraphqlQuery}",
            "-F",
            $"owner={owner}",
            "-F",
            $"repo={name}",
            "-F",
            $"pr={prNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
        };
    }

    private static (string Owner, string Name) SplitRepo(string repo)
    {
        var slash = repo.IndexOf('/');
        if (slash <= 0 || slash == repo.Length - 1)
        {
            throw new ArgumentException(
                $"repo '{repo}' is not in '<owner>/<name>' form",
                nameof(repo));
        }

        return (repo[..slash], repo[(slash + 1)..]);
    }

    public GitHubPrCommentsLookupResult Lookup(string repo, int prNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        // Call A: reviews + comments (the supported `gh pr view --json` subset).
        var prViewStdout = RunGh(
            BuildPrViewArguments(repo, prNumber),
            $"`gh pr view {prNumber} --repo {repo} --json {PrViewJsonFields}`");

        GitHubPrCommentsLookupResult partial;
        try
        {
            partial = JsonSerializer.Deserialize<GitHubPrCommentsLookupResult>(prViewStdout)
                ?? throw new InvalidOperationException(
                    $"`gh pr view` returned an empty comments payload for PR {prNumber} in {repo}");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"could not parse `gh pr view` comments JSON for PR {prNumber} in {repo}: {exception.Message}",
                exception);
        }

        // Call B: review threads via GraphQL.
        var graphqlStdout = RunGh(
            BuildGraphqlArguments(repo, prNumber),
            $"`gh api graphql` reviewThreads for PR {prNumber} in {repo}");

        var reviewThreads = ParseGraphqlReviewThreads(graphqlStdout, repo, prNumber);

        return partial with { ReviewThreads = reviewThreads };
    }

    private static string RunGh(IReadOnlyList<string> arguments, string description)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            // G484: decode gh stdout/stderr as UTF-8 regardless of the ambient
            // console code page (Windows cp932) so Japanese payloads stay valid.
            StandardOutputEncoding = GitHubCliProcessEncoding.Utf8NoBom,
            StandardErrorEncoding = GitHubCliProcessEncoding.Utf8NoBom,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        string stdout;
        string stderr;
        int exitCode;

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    $"failed to start `gh` process for {description}");
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
                $"could not invoke `gh` for {description}: {exception.Message}",
                exception);
        }

        if (exitCode != 0)
        {
            var errorBody = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"{description} failed with exit {exitCode}: {errorBody.Trim()}");
        }

        return stdout;
    }

    private static IReadOnlyList<GitHubPrReviewThread> ParseGraphqlReviewThreads(
        string graphqlStdout, string repo, int prNumber)
    {
        try
        {
            using var document = JsonDocument.Parse(graphqlStdout);
            var nodes = document.RootElement
                .GetProperty("data")
                .GetProperty("repository")
                .GetProperty("pullRequest")
                .GetProperty("reviewThreads")
                .GetProperty("nodes");

            var threads = new List<GitHubPrReviewThread>(nodes.GetArrayLength());

            foreach (var node in nodes.EnumerateArray())
            {
                var id = node.TryGetProperty("id", out var idElem)
                    ? (idElem.GetString() ?? string.Empty)
                    : string.Empty;
                var isResolved = node.TryGetProperty("isResolved", out var resolvedElem)
                    && resolvedElem.GetBoolean();

                var commentList = new List<GitHubPrReviewThreadComment>();
                if (node.TryGetProperty("comments", out var commentsElem)
                    && commentsElem.TryGetProperty("nodes", out var commentNodes))
                {
                    foreach (var c in commentNodes.EnumerateArray())
                    {
                        var commentId = c.TryGetProperty("id", out var cId)
                            ? (cId.GetString() ?? string.Empty)
                            : string.Empty;
                        var body = c.TryGetProperty("body", out var bodyElem)
                            ? (bodyElem.GetString() ?? string.Empty)
                            : string.Empty;
                        var author = string.Empty;
                        if (c.TryGetProperty("author", out var authorElem)
                            && authorElem.ValueKind != JsonValueKind.Null
                            && authorElem.TryGetProperty("login", out var loginElem))
                        {
                            author = loginElem.GetString() ?? string.Empty;
                        }

                        commentList.Add(new GitHubPrReviewThreadComment
                        {
                            Id = commentId,
                            Author = author,
                            Body = body,
                        });
                    }
                }

                threads.Add(new GitHubPrReviewThread
                {
                    Id = id,
                    IsResolved = isResolved,
                    Comments = commentList,
                });
            }

            return threads;
        }
        catch (Exception exception) when (
            exception is JsonException
            or KeyNotFoundException
            or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"could not parse `gh api graphql` reviewThreads JSON for PR {prNumber} in {repo}: {exception.Message}",
                exception);
        }
    }
}
