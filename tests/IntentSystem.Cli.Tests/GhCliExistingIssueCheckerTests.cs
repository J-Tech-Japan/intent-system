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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FetchAllCandidates_HasNextPageTrueWithEmptyOrWhitespaceEndCursor_ThrowsRatherThanFetchingAgain(string emptyCursor)
    {
        // G536 round-7 review repair: an empty/whitespace-only endCursor
        // is just as much a "missing cursor" as null — a bare `?? throw`
        // only rejected null, letting an empty string be sent back as
        // `cursor=` on the next request and recorded as if it were real.
        var pageCalls = 0;
        var checker = new GhCliExistingIssueChecker
        {
            PageFetcherOverride = _ =>
            {
                pageCalls++;
                return GraphQlPage(hasNextPage: true, endCursor: emptyCursor, nodes: Array.Empty<(int, string, string, string)>());
            },
        };

        var exception = Assert.Throws<InvalidOperationException>(() => checker.FetchAllCandidates("acme/widgets", "G536"));
        Assert.Contains("hasNextPage=true", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, pageCalls); // never re-fetched with the bad cursor
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("123")]
    [InlineData("\"just a string\"")]
    public void FetchAllCandidates_NullOrWrongTypeEnvelope_FailsLoudNotNullReferenceException(string malformedJson)
    {
        var checker = new GhCliExistingIssueChecker { PageFetcherOverride = _ => malformedJson };

        // Must be an intentional diagnostic, never an incidental NRE.
        Assert.Throws<InvalidOperationException>(() => checker.FetchAllCandidates("acme/widgets", "G536"));
    }

    [Fact]
    public void FetchAllCandidates_EmptyOutput_FailsLoudAsUnparseableJson()
    {
        var checker = new GhCliExistingIssueChecker { PageFetcherOverride = _ => string.Empty };

        var exception = Assert.Throws<InvalidOperationException>(() => checker.FetchAllCandidates("acme/widgets", "G536"));
        Assert.Contains("unparseable JSON", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FetchAllCandidates_NullPageInfo_FailsLoudNotNullReferenceException()
    {
        var checker = new GhCliExistingIssueChecker
        {
            PageFetcherOverride = _ => "{\"data\":{\"search\":{\"pageInfo\":null,\"nodes\":[]}}}",
        };

        var exception = Assert.Throws<InvalidOperationException>(() => checker.FetchAllCandidates("acme/widgets", "G536"));
        Assert.Contains("no pageInfo", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FetchAllCandidates_NullNodesArray_FailsLoudNotNullReferenceException()
    {
        var checker = new GhCliExistingIssueChecker
        {
            PageFetcherOverride = _ => "{\"data\":{\"search\":{\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null},\"nodes\":null}}}",
        };

        var exception = Assert.Throws<InvalidOperationException>(() => checker.FetchAllCandidates("acme/widgets", "G536"));
        Assert.Contains("no nodes array", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FetchAllCandidates_NullNodeWithinNodesArray_FailsLoudNotNullReferenceException()
    {
        var checker = new GhCliExistingIssueChecker
        {
            PageFetcherOverride = _ => "{\"data\":{\"search\":{\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null},\"nodes\":[null]}}}",
        };

        var exception = Assert.Throws<InvalidOperationException>(() => checker.FetchAllCandidates("acme/widgets", "G536"));
        Assert.Contains("null node", exception.Message, StringComparison.Ordinal);
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

    // ─── G536 round-6 tests ─────────────────────────────────────────────────

    [Fact]
    public void FetchAllCandidates_PinsLiteralProductionSearchQuery_IsIssueIncludedNoStateFilter()
    {
        IReadOnlyList<string>? capturedArguments = null;
        var checker = new GhCliExistingIssueChecker
        {
            PageFetcherOverride = arguments =>
            {
                capturedArguments = arguments;
                return GraphQlPage(hasNextPage: false, endCursor: null, nodes: Array.Empty<(int, string, string, string)>());
            },
        };

        checker.FetchAllCandidates("acme/widgets", "G536");

        Assert.NotNull(capturedArguments);
        Assert.Contains("searchQuery=repo:acme/widgets G536 in:title is:issue", capturedArguments!);
        Assert.DoesNotContain(capturedArguments!, arg => arg.Contains("state:", StringComparison.Ordinal));
    }

    [Fact]
    public void FetchAllCandidates_GraphQlErrorsPresentAlongsidePartialData_FailsLoudRatherThanAcceptingPartialData()
    {
        var checker = new GhCliExistingIssueChecker
        {
            PageFetcherOverride = _ => GraphQlPageWithErrors(
                hasNextPage: false,
                endCursor: null,
                nodes: new[] { (1, "G536 partial candidate", "https://github.com/acme/widgets/issues/1", "body") },
                errorMessages: new[] { "Something went wrong while executing your query" }),
        };

        var exception = Assert.Throws<InvalidOperationException>(() => checker.FetchAllCandidates("acme/widgets", "G536"));
        Assert.Contains("Something went wrong", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FetchAllCandidates_RepeatedCursor_FailsLoudInsteadOfLoopingUntilSafetyCap()
    {
        var pageCalls = 0;
        var checker = new GhCliExistingIssueChecker
        {
            PageFetcherOverride = arguments =>
            {
                pageCalls++;
                var cursor = ExtractCursor(arguments);
                // Page 1 (no cursor) -> "cursor-a". Page 2 (cursor-a) also
                // reports "cursor-a" as its OWN endCursor — a server bug
                // that would otherwise loop forever.
                return GraphQlPage(hasNextPage: true, endCursor: "cursor-a", nodes: Array.Empty<(int, string, string, string)>());
            },
        };

        var exception = Assert.Throws<InvalidOperationException>(() => checker.FetchAllCandidates("acme/widgets", "G536"));
        Assert.Contains("repeated cursor", exception.Message, StringComparison.OrdinalIgnoreCase);
        // Fails on the second occurrence, long before the 50-page safety cap.
        Assert.Equal(2, pageCalls);
    }

    [Fact]
    public void FetchAllCandidates_PageFetcherThrows_PropagatesFailureWithoutSwallowing()
    {
        var checker = new GhCliExistingIssueChecker
        {
            PageFetcherOverride = _ => throw new InvalidOperationException("gh api graphql exit 1: authentication required"),
        };

        var exception = Assert.Throws<InvalidOperationException>(() => checker.FetchAllCandidates("acme/widgets", "G536"));
        Assert.Contains("authentication required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FetchAllCandidates_MalformedJson_FailsLoud()
    {
        var checker = new GhCliExistingIssueChecker
        {
            PageFetcherOverride = _ => "not json at all",
        };

        var exception = Assert.Throws<InvalidOperationException>(() => checker.FetchAllCandidates("acme/widgets", "G536"));
        Assert.Contains("unparseable JSON", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FetchAllCandidates_NodeWithNullBody_FailsLoudRatherThanAccumulating()
    {
        var checker = new GhCliExistingIssueChecker
        {
            PageFetcherOverride = _ => "{\"data\":{\"search\":{\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null},"
                + "\"nodes\":[{\"number\":1,\"title\":\"G536 issue\",\"url\":\"https://github.com/acme/widgets/issues/1\",\"body\":null}]}}}",
        };

        var exception = Assert.Throws<InvalidOperationException>(() => checker.FetchAllCandidates("acme/widgets", "G536"));
        Assert.Contains("null body", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateCandidate_HappyPath_ReturnsSameEntry()
    {
        var entry = new GhCliExistingIssueChecker.GhIssueListEntry(7, "G536 Title", "https://github.com/acme/widgets/issues/7", "body");

        var validated = GhCliExistingIssueChecker.ValidateCandidate(entry, "acme/widgets");

        Assert.Equal(entry, validated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateCandidate_NonPositiveNumber_ThrowsFailLoud(int number)
    {
        var entry = new GhCliExistingIssueChecker.GhIssueListEntry(number, "G536 Title", $"https://github.com/acme/widgets/issues/{number}", "body");

        var exception = Assert.Throws<InvalidOperationException>(() => GhCliExistingIssueChecker.ValidateCandidate(entry, "acme/widgets"));
        Assert.Contains("non-positive issue number", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ValidateCandidate_NullOrEmptyTitle_ThrowsFailLoud(string? title)
    {
        var entry = new GhCliExistingIssueChecker.GhIssueListEntry(1, title!, "https://github.com/acme/widgets/issues/1", "body");

        var exception = Assert.Throws<InvalidOperationException>(() => GhCliExistingIssueChecker.ValidateCandidate(entry, "acme/widgets"));
        Assert.Contains("null/empty title", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateCandidate_NullBody_ThrowsFailLoud()
    {
        var entry = new GhCliExistingIssueChecker.GhIssueListEntry(1, "G536 Title", "https://github.com/acme/widgets/issues/1", null);

        var exception = Assert.Throws<InvalidOperationException>(() => GhCliExistingIssueChecker.ValidateCandidate(entry, "acme/widgets"));
        Assert.Contains("null body", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://github.com/other-org/other-repo/issues/1")] // different repo entirely
    [InlineData("https://github.com/acme/widgets/issues/2")] // number mismatch
    [InlineData("http://github.com/acme/widgets/issues/1")] // wrong scheme
    [InlineData(null)]
    public void ValidateCandidate_UrlNotExactCanonicalForRequestedRepo_ThrowsFailLoud(string? url)
    {
        var entry = new GhCliExistingIssueChecker.GhIssueListEntry(1, "G536 Title", url!, "body");

        var exception = Assert.Throws<InvalidOperationException>(() => GhCliExistingIssueChecker.ValidateCandidate(entry, "acme/widgets"));
        Assert.Contains("expected exactly", exception.Message, StringComparison.Ordinal);
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

    private static string GraphQlPage(bool hasNextPage, string? endCursor, IReadOnlyList<(int Number, string Title, string Url, string Body)> nodes) =>
        GraphQlPageWithErrors(hasNextPage, endCursor, nodes, errorMessages: null);

    private static string GraphQlPageWithErrors(
        bool hasNextPage,
        string? endCursor,
        IReadOnlyList<(int Number, string Title, string Url, string Body)> nodes,
        IReadOnlyList<string>? errorMessages)
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
        var dataJson = "{\"search\":{\"pageInfo\":{\"hasNextPage\":" + hasNextPageJson
            + ",\"endCursor\":" + endCursorJson + "},\"nodes\":[" + nodesJson + "]}}";

        if (errorMessages is null || errorMessages.Count == 0)
        {
            return "{\"data\":" + dataJson + "}";
        }

        var errorsJson = string.Join(",", errorMessages.Select(message =>
            "{\"message\":" + System.Text.Json.JsonSerializer.Serialize(message) + "}"));
        return "{\"data\":" + dataJson + ",\"errors\":[" + errorsJson + "]}";
    }
}
