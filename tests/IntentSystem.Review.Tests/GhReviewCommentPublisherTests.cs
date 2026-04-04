namespace IntentSystem.Review.Tests;

public sealed class GhReviewCommentPublisherTests
{
    [Fact]
    public void PostComment_GivenLinkedPrAndBody_PostsViaGhApiAndReturnsHtmlUrl()
    {
        var runner = new FakeRunner();
        var publisher = new GhReviewCommentPublisher(runner);

        var commentRef = publisher.PostComment(
            "https://github.com/J-Tech-Japan/intent-system/pull/46",
            "repair in place");

        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/46#issuecomment-1", commentRef);
        Assert.Equal(
            "repos/J-Tech-Japan/intent-system/issues/46/comments",
            runner.Arguments[1]);
        Assert.Equal("POST", runner.Arguments[3]);
        Assert.Contains("\"body\":\"repair in place\"", runner.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_GivenInvalidPullRequestUrl_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => GitHubPullRequestRef.Parse("https://github.com/J-Tech-Japan/intent-system/issues/46"));

        Assert.Contains("pull request URL shape", exception.Message, StringComparison.Ordinal);
    }

    private sealed class FakeRunner : IReviewCommandRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public string PayloadJson { get; private set; } = string.Empty;

        public ReviewCommandResult Run(IReadOnlyList<string> arguments)
        {
            Arguments = arguments.ToArray();
            var payloadIndex = Arguments
                .Select((argument, index) => (argument, index))
                .First(item => string.Equals(item.argument, "--input", StringComparison.Ordinal))
                .index + 1;
            PayloadJson = File.ReadAllText(Arguments[payloadIndex]);

            return new ReviewCommandResult
            {
                ExitCode = 0,
                StdOut = """{"html_url":"https://github.com/J-Tech-Japan/intent-system/pull/46#issuecomment-1"}""",
                StdErr = string.Empty
            };
        }
    }
}
