namespace IntentSystem.Review.Tests;

public sealed class GhReviewAcceptClientTests
{
    [Fact]
    public void MergePullRequest_GivenLinkedPr_MergesViaGhApiAndReturnsMergedSha()
    {
        var runner = new FakeRunner(
            """{"sha":"abc123"}""",
            string.Empty);
        var client = new GhReviewAcceptClient(runner);

        var mergedSha = client.MergePullRequest("https://github.com/J-Tech-Japan/intent-system/pull/46");

        Assert.Equal("abc123", mergedSha);
        Assert.Equal(
            [
                "api",
                "repos/J-Tech-Japan/intent-system/pulls/46/merge",
                "--method",
                "PUT",
                "-f",
                "merge_method=merge"
            ],
            runner.Arguments);
    }

    [Fact]
    public void CloseIssue_GivenLinkedIssue_ClosesViaGhApi()
    {
        var runner = new FakeRunner("""{"state":"closed"}""", string.Empty);
        var client = new GhReviewAcceptClient(runner);

        client.CloseIssue("https://github.com/J-Tech-Japan/intent-system/issues/51");

        Assert.Equal(
            [
                "api",
                "repos/J-Tech-Japan/intent-system/issues/51",
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

    private sealed class FakeRunner(string stdOut, string stdErr, int exitCode = 0) : IReviewCommandRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public ReviewCommandResult Run(IReadOnlyList<string> arguments)
        {
            Arguments = arguments.ToArray();

            return new ReviewCommandResult
            {
                ExitCode = exitCode,
                StdOut = stdOut,
                StdErr = stdErr
            };
        }
    }
}
