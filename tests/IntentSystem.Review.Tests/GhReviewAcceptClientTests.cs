namespace IntentSystem.Review.Tests;

public sealed class GhReviewAcceptClientTests
{
    [Fact]
    public void MergePullRequest_GivenLinkedPr_MergesViaGhApiAndReturnsMergedSha()
    {
        var runner = new FakeRunner(
        [
            new ExpectedCall(
                [
                    "api",
                    "repos/tomohisa/toy-calc-sample/pulls/4/merge",
                    "--method",
                    "PUT",
                    "-f",
                    "merge_method=merge"
                ],
                new ReviewCommandResult
                {
                    ExitCode = 0,
                    StdOut = """{"sha":"abc123"}""",
                    StdErr = string.Empty
                })
        ]);
        var client = new GhReviewAcceptClient(runner);

        var mergedSha = client.MergePullRequest("https://github.com/tomohisa/toy-calc-sample/pull/4");

        Assert.Equal("abc123", mergedSha);
        Assert.Equal(
            [
                "api",
                "repos/tomohisa/toy-calc-sample/pulls/4/merge",
                "--method",
                "PUT",
                "-f",
                "merge_method=merge"
            ],
            runner.Arguments);
    }

    [Fact]
    public void MarkPullRequestReady_GivenLinkedPr_MarksReadyViaGhApi()
    {
        var runner = new FakeRunner(
        [
            new ExpectedCall(
                [
                    "api",
                    "repos/tomohisa/toy-calc-sample/pulls/4/ready_for_review",
                    "--method",
                    "POST"
                ],
                new ReviewCommandResult
                {
                    ExitCode = 0,
                    StdOut = "{}",
                    StdErr = string.Empty
                })
        ]);
        var client = new GhReviewAcceptClient(runner);

        client.MarkPullRequestReady("https://github.com/tomohisa/toy-calc-sample/pull/4");

        Assert.Single(runner.Calls);
    }

    [Fact]
    public void MarkPullRequestReady_GivenReadyForReviewApi404_FallsBackToGhPrReady()
    {
        var runner = new FakeRunner(
        [
            new ExpectedCall(
                [
                    "api",
                    "repos/tomohisa/toy-calc-sample/pulls/4/ready_for_review",
                    "--method",
                    "POST"
                ],
                new ReviewCommandResult
                {
                    ExitCode = 1,
                    StdOut = string.Empty,
                    StdErr = "gh: Not Found (HTTP 404)"
                }),
            new ExpectedCall(
                [
                    "pr",
                    "ready",
                    "4",
                    "--repo",
                    "tomohisa/toy-calc-sample"
                ],
                new ReviewCommandResult
                {
                    ExitCode = 0,
                    StdOut = string.Empty,
                    StdErr = string.Empty
                })
        ]);
        var client = new GhReviewAcceptClient(runner);

        client.MarkPullRequestReady("https://github.com/tomohisa/toy-calc-sample/pull/4");

        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public void CloseIssue_GivenLinkedIssue_ClosesViaGhApi()
    {
        var runner = new FakeRunner(
        [
            new ExpectedCall(
                [
                    "api",
                    "repos/tomohisa/toy-calc-sample/issues/3",
                    "--method",
                    "PATCH",
                    "-f",
                    "state=closed"
                ],
                new ReviewCommandResult
                {
                    ExitCode = 0,
                    StdOut = """{"state":"closed"}""",
                    StdErr = string.Empty
                })
        ]);
        var client = new GhReviewAcceptClient(runner);

        client.CloseIssue("https://github.com/tomohisa/toy-calc-sample/issues/3");

        Assert.Equal(
            [
                "api",
                "repos/tomohisa/toy-calc-sample/issues/3",
                "--method",
                "PATCH",
                "-f",
                "state=closed"
            ],
            runner.Arguments);
    }

    [Fact]
    public void Parse_GivenInvalidIssueUrl_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => GitHubIssueRef.Parse("https://github.com/J-Tech-Japan/intent-system/pull/51"));

        Assert.Contains("issue URL shape", exception.Message, StringComparison.Ordinal);
    }

    private sealed record ExpectedCall(IReadOnlyList<string> Arguments, ReviewCommandResult Result);

    private sealed class FakeRunner(IReadOnlyList<ExpectedCall> expectedCalls) : IReviewCommandRunner
    {
        private readonly Queue<ExpectedCall> expectedCalls = new(expectedCalls);

        public List<IReadOnlyList<string>> Calls { get; } = [];

        public IReadOnlyList<string> Arguments => Calls.LastOrDefault() ?? [];

        public ReviewCommandResult Run(IReadOnlyList<string> arguments)
        {
            Calls.Add(arguments.ToArray());

            Assert.NotEmpty(expectedCalls);
            var expected = expectedCalls.Dequeue();
            Assert.Equal(expected.Arguments, arguments);

            return expected.Result;
        }
    }
}
