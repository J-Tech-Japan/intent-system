namespace IntentSystem.Cli.Tests;

public sealed class GhRunSubmitPublisherTests
{
    [Fact]
    public void CreateDraftPullRequest_GivenTargetRepoBranchTitleAndBody_CreatesDraftPrAndReturnsUrl()
    {
        var runner = new FakeRunner();
        var publisher = new GhRunSubmitPublisher(runner);

        var linkedPr = publisher.CreateDraftPullRequest(
            "J-Tech-Japan/intent-system",
            "issue-56-g14",
            "[G14] Run Start Command",
            "Linked issue");

        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/58", linkedPr);
        Assert.Equal(
            [
                "pr",
                "create",
                "--draft",
                "--base",
                "main",
                "--head",
                "issue-56-g14",
                "--title",
                "[G14] Run Start Command",
                "--body-file",
                runner.BodyFilePath,
                "--repo",
                "J-Tech-Japan/intent-system"
            ],
            runner.Arguments);
        Assert.Equal("Linked issue", runner.BodyContents);
    }

    private sealed class FakeRunner : IGitHubCommandRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public string BodyFilePath { get; private set; } = string.Empty;

        public string BodyContents { get; private set; } = string.Empty;

        public GitHubCommandResult Run(IReadOnlyList<string> arguments)
        {
            Arguments = arguments.ToArray();
            BodyFilePath = Arguments[10];
            BodyContents = File.ReadAllText(BodyFilePath);

            return new GitHubCommandResult
            {
                ExitCode = 0,
                StdOut = "https://github.com/J-Tech-Japan/intent-system/pull/58" + Environment.NewLine,
                StdErr = string.Empty
            };
        }
    }
}
