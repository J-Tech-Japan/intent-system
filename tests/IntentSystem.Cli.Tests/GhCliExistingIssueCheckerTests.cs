using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G536 round-5 review repair: exercises the REAL <see cref="GhCliExistingIssueChecker"/>
/// production logic end-to-end (pagination loop, truncation guard, exact
/// title/body classification, and body normalization) via its
/// <see cref="GhCliExistingIssueChecker.PageFetcherOverride"/> test seam —
/// only the literal OS process spawn is replaced with canned GraphQL JSON.
/// This is deliberately NOT the same thing as stubbing
/// <see cref="IGitHubExistingIssueChecker"/> at the interface level (as
/// <c>IssuePublishFlowCommandTests</c> does for command-level tests): here
/// the actual pagination algorithm, cursor handling, and normalization code
/// that ships in the checker is what runs.
/// </summary>
public sealed class GhCliExistingIssueCheckerTests
{
    [Fact]
    public void FetchAllCandidates_MultiPagePagination_AccumulatesEveryPageOpenAndClosed()
    {
        var pageCalls = new List<IReadOnlyList<string>>();
        var checker = new GhCliExistingIssueChecker
        {
            PageFetcherOverride = arguments =>
            {
                pageCalls.Add(arguments);
                var cursor = ExtractCursor(arguments);
                return cursor switch
                {
                    null => GraphQlPage(
                        hasNextPage: true,
                        endCursor: "cursor-a",
                        nodes: new[]
                        {
                            (1, "G536 first open issue", "https://github.com/acme/widgets/issues/1", "body one"),
                            (2, "G536 second open issue", "https://github.com/acme/widgets/issues/2", "body two"),
                        }),
                    "cursor-a" => GraphQlPage(
                        hasNextPage: true,
                        endCursor: "cursor-b",
                        nodes: new[]
                        {
                            (3, "G536 a closed issue", "https://github.com/acme/widgets/issues/3", "body three"),
                        }),
                    "cursor-b" => GraphQlPage(
                        hasNextPage: false,
                        endCursor: null,
                        nodes: new[]
                        {
                            (4, "G536 last page issue", "https://github.com/acme/widgets/issues/4", "body four"),
                        }),
                    _ => throw new InvalidOperationException($"unexpected cursor '{cursor}' in test"),
                };
            },
        };

        var all = checker.FetchAllCandidates("acme/widgets", "G536");

        Assert.Equal(3, pageCalls.Count);
        Assert.Equal(new[] { 1, 2, 3, 4 }, all.Select(e => e.Number).ToArray());
        // state=all contract preserved — GraphQL search query never adds a state qualifier.
        Assert.DoesNotContain(pageCalls[0], arg => arg.Contains("state:"));
    }

    [Fact]
    public void FetchAllCandidates_HasNextPageTrueWithNoEndCursor_ThrowsRatherThanStoppingSilently()
    {
        var checker = new GhCliExistingIssueChecker
        {
            PageFetcherOverride = _ => GraphQlPage(hasNextPage: true, endCursor: null, nodes: Array.Empty<(int, string, string, string)>()),
        };

        var exception = Assert.Throws<InvalidOperationException>(() => checker.FetchAllCandidates("acme/widgets", "G536"));
        Assert.Contains("hasNextPage=true", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FetchAllCandidates_NeverTerminates_FailsLoudInsteadOfSilentlyTruncating()
    {
        var callCount = 0;
        var checker = new GhCliExistingIssueChecker
        {
            PageFetcherOverride = _ =>
            {
                callCount++;
                return GraphQlPage(hasNextPage: true, endCursor: $"cursor-{callCount}", nodes: Array.Empty<(int, string, string, string)>());
            },
        };

        var exception = Assert.Throws<InvalidOperationException>(() => checker.FetchAllCandidates("acme/widgets", "G536"));
        Assert.Contains("refusing to silently truncate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindExistingIssue_EndToEnd_UniqueExactMatchAcrossTwoPages_ClassifiesUnique()
    {
        var checker = new GhCliExistingIssueChecker
        {
            PageFetcherOverride = arguments => ExtractCursor(arguments) switch
            {
                null => GraphQlPage(
                    hasNextPage: true,
                    endCursor: "cursor-a",
                    nodes: new[] { (10, "G536 unrelated issue", "https://github.com/acme/widgets/issues/10", "unrelated body") }),
                "cursor-a" => GraphQlPage(
                    hasNextPage: false,
                    endCursor: null,
                    nodes: new[] { (11, "G536 Canonical Title", "https://github.com/acme/widgets/issues/11", "the exact body") }),
                _ => throw new InvalidOperationException("unexpected cursor"),
            },
        };

        var result = checker.FindExistingIssue("acme/widgets", "G536", "G536 Canonical Title", "the exact body");

        Assert.Equal(GitHubExistingIssueClassification.Unique, result.Classification);
        Assert.Equal(11, result.IssueNumber);
        Assert.Equal("https://github.com/acme/widgets/issues/11", result.IssueUrl);
    }

    [Theory]
    [InlineData("the exact body", "the exact body", true)] // byte-for-byte identical
    [InlineData("the exact body\n", "the exact body", true)] // GitHub's single trailing newline convention
    [InlineData("the exact body\r\n", "the exact body\n", true)] // CRLF vs LF
    [InlineData(" the exact body", "the exact body", false)] // leading space drift must NOT match
    [InlineData("the  exact body", "the exact body", false)] // inner space drift must NOT match
    [InlineData("the exact body ", "the exact body", false)] // trailing (non-newline) space drift must NOT match
    [InlineData("the exact body\n\n", "the exact body", false)] // more than one trailing newline is a real difference
    public void ClassifyCandidates_BodyNormalization_OnlyLineEndingAndSingleTrailingNewlineAreEquivalent(
        string candidateBody, string expectedBody, bool shouldMatch)
    {
        var candidates = new[]
        {
            new GhCliExistingIssueChecker.GhIssueListEntry(1, "G536 Canonical Title", "https://github.com/acme/widgets/issues/1", candidateBody),
        };

        var result = GhCliExistingIssueChecker.ClassifyCandidates(candidates, "G536 Canonical Title", expectedBody);

        Assert.Equal(
            shouldMatch ? GitHubExistingIssueClassification.Unique : GitHubExistingIssueClassification.None,
            result.Classification);
    }

    [Fact]
    public void ClassifyCandidates_SimilarTitlePrefix_NeverMatchesExactTitle()
    {
        var candidates = new[]
        {
            new GhCliExistingIssueChecker.GhIssueListEntry(1, "G53 unrelated but similarly prefixed issue", "https://github.com/acme/widgets/issues/1", "body"),
        };

        var result = GhCliExistingIssueChecker.ClassifyCandidates(candidates, "G536 Canonical Title", "body");

        Assert.Equal(GitHubExistingIssueClassification.None, result.Classification);
    }

    [Fact]
    public void ClassifyCandidates_TwoExactTitleAndBodyMatches_ClassifiesMultiple()
    {
        var candidates = new[]
        {
            new GhCliExistingIssueChecker.GhIssueListEntry(1, "G536 Canonical Title", "https://github.com/acme/widgets/issues/1", "the exact body"),
            new GhCliExistingIssueChecker.GhIssueListEntry(2, "G536 Canonical Title", "https://github.com/acme/widgets/issues/2", "the exact body"),
        };

        var result = GhCliExistingIssueChecker.ClassifyCandidates(candidates, "G536 Canonical Title", "the exact body");

        Assert.Equal(GitHubExistingIssueClassification.Multiple, result.Classification);
    }

    private static string? ExtractCursor(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (arguments[index].StartsWith("cursor=", StringComparison.Ordinal))
            {
                return arguments[index]["cursor=".Length..];
            }
        }
        return null;
    }

    private static string GraphQlPage(bool hasNextPage, string? endCursor, IReadOnlyList<(int Number, string Title, string Url, string Body)> nodes)
    {
        var nodesJson = string.Join(",", nodes.Select(n => System.Text.Json.JsonSerializer.Serialize(new
        {
            number = n.Number,
            title = n.Title,
            url = n.Url,
            body = n.Body,
        })));
        var endCursorJson = endCursor is null ? "null" : System.Text.Json.JsonSerializer.Serialize(endCursor);
        var hasNextPageJson = hasNextPage ? "true" : "false";
        return "{\"data\":{\"search\":{\"pageInfo\":{\"hasNextPage\":" + hasNextPageJson
            + ",\"endCursor\":" + endCursorJson + "},\"nodes\":[" + nodesJson + "]}}}";
    }
}
