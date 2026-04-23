namespace IntentSystem.Cli.Tests;

public sealed class GhRunSubmitPublisherTests
{
    [Fact]
    public void CreateDraftPullRequest_GivenTargetRepoBranchTitleAndBody_CreatesDraftPrAndReturnsUrl()
    {
        var runner = new ScriptedRunner(
            new GitHubCommandResult
            {
                ExitCode = 0,
                StdOut = "https://github.com/J-Tech-Japan/intent-system/pull/58" + Environment.NewLine,
                StdErr = string.Empty
            });
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
            runner.Calls.Single());
        Assert.Equal("Linked issue", runner.BodyContents);
    }

    [Fact]
    public void CreateDraftPullRequest_GivenExistingMatchingPr_ReusesExistingPrUrl()
    {
        var runner = new ScriptedRunner(
            new GitHubCommandResult
            {
                ExitCode = 1,
                StdOut = string.Empty,
                StdErr = "GraphQL: A pull request already exists for J-Tech-Japan:issue-18-toy-calc-v0-08."
            },
            new GitHubCommandResult
            {
                ExitCode = 0,
                StdOut = """
                [
                  {
                    "url": "https://github.com/J-Tech-Japan/intent-system/pull/19",
                    "headRefName": "issue-18-toy-calc-v0-08",
                    "baseRefName": "main"
                  }
                ]
                """,
                StdErr = string.Empty
            });
        var publisher = new GhRunSubmitPublisher(runner);

        var linkedPr = publisher.CreateDraftPullRequest(
            "J-Tech-Japan/intent-system",
            "issue-18-toy-calc-v0-08",
            "[G158] Make Run Submit Reuse Existing Pull Requests",
            "Linked issue");

        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/19", linkedPr);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Single(runner.Calls, call => call.Take(2).SequenceEqual(["pr", "create"]));
        Assert.Equal(
            [
                "pr",
                "list",
                "--repo",
                "J-Tech-Japan/intent-system",
                "--head",
                "issue-18-toy-calc-v0-08",
                "--base",
                "main",
                "--state",
                "open",
                "--json",
                "url,headRefName,baseRefName"
            ],
            runner.Calls[1]);
    }

    [Fact]
    public void CreateDraftPullRequest_GivenExistingPrErrorWithoutMatchingPr_ThrowsOriginalCreateError()
    {
        var runner = new ScriptedRunner(
            new GitHubCommandResult
            {
                ExitCode = 1,
                StdOut = string.Empty,
                StdErr = "GraphQL: A pull request already exists for J-Tech-Japan:issue-18-toy-calc-v0-08."
            },
            new GitHubCommandResult
            {
                ExitCode = 0,
                StdOut = """
                [
                  {
                    "url": "https://github.com/J-Tech-Japan/intent-system/pull/19",
                    "headRefName": "different-branch",
                    "baseRefName": "main"
                  }
                ]
                """,
                StdErr = string.Empty
            });
        var publisher = new GhRunSubmitPublisher(runner);

        var exception = Assert.Throws<InvalidOperationException>(
            () => publisher.CreateDraftPullRequest(
                "J-Tech-Japan/intent-system",
                "issue-18-toy-calc-v0-08",
                "[G158] Make Run Submit Reuse Existing Pull Requests",
                "Linked issue"));

        Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, runner.Calls.Count);
    }

    private sealed class ScriptedRunner(params GitHubCommandResult[] results) : IGitHubCommandRunner
    {
        private readonly Queue<GitHubCommandResult> results = new(results);

        public List<IReadOnlyList<string>> Calls { get; } = [];

        public string BodyFilePath { get; private set; } = string.Empty;

        public string BodyContents { get; private set; } = string.Empty;

        public GitHubCommandResult Run(IReadOnlyList<string> arguments)
        {
            Calls.Add(arguments.ToArray());
            if (arguments.Count > 10 && arguments.Take(2).SequenceEqual(["pr", "create"]))
            {
                BodyFilePath = arguments[10];
                BodyContents = File.ReadAllText(BodyFilePath);
            }

            Assert.NotEmpty(results);
            return results.Dequeue();
        }
    }
}
