using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GhQueueDispatchPublisherTests
{
    [Fact]
    public void CreateIssue_GivenTargetRepoTitleAndBody_PostsViaGhApiAndReturnsLinkedIssue()
    {
        var runner = new FakeRunner();
        var publisher = new GhQueueDispatchPublisher(runner);

        var linkedIssue = publisher.CreateIssue(
            "J-Tech-Japan/intent-system",
            "[G13] Queue Dispatch Command",
            "# Goal");

        Assert.Equal(
            new LinkedIssue
            {
                Repo = "J-Tech-Japan/intent-system",
                Number = 53,
                Url = "https://github.com/J-Tech-Japan/intent-system/issues/53"
            },
            linkedIssue);
        Assert.Equal(
            "repos/J-Tech-Japan/intent-system/issues",
            runner.Arguments[1]);
        Assert.Contains("\"title\":\"[G13] Queue Dispatch Command\"", runner.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("\"body\":\"# Goal\"", runner.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateIssue_GivenInvalidTargetRepo_ThrowsInvalidOperationException()
    {
        var publisher = new GhQueueDispatchPublisher(new FakeRunner());

        var exception = Assert.Throws<InvalidOperationException>(
            () => publisher.CreateIssue("intent-system", "title", "body"));

        Assert.Contains("owner/repo shape", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetIssueLabels_GivenTargetRepoAndIssueNumber_ReadsLabelsViaGhApi()
    {
        var runner = new FakeRunner
        {
            ResponseStdOut = """[{"name":"intent-target"},{"name":"bug"}]"""
        };
        var publisher = new GhQueueDispatchPublisher(runner);

        var labels = publisher.GetIssueLabels("J-Tech-Japan/intent-system", 53);

        Assert.Equal(["intent-target", "bug"], labels);
        Assert.Equal(
            "repos/J-Tech-Japan/intent-system/issues/53/labels",
            runner.Arguments[1]);
        Assert.Equal("GET", runner.Arguments[3]);
        Assert.Empty(runner.PayloadJson);
    }

    private sealed class FakeRunner : IGitHubCommandRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public string PayloadJson { get; private set; } = string.Empty;

        public string ResponseStdOut { get; init; } =
            """{"number":53,"html_url":"https://github.com/J-Tech-Japan/intent-system/issues/53"}""";

        public GitHubCommandResult Run(IReadOnlyList<string> arguments)
        {
            Arguments = arguments.ToArray();
            var payloadItem = Arguments
                .Select((argument, index) => (argument, index))
                .FirstOrDefault(item => string.Equals(item.argument, "--input", StringComparison.Ordinal));
            if (payloadItem.argument is not null)
            {
                PayloadJson = File.ReadAllText(Arguments[payloadItem.index + 1]);
            }

            return new GitHubCommandResult
            {
                ExitCode = 0,
                StdOut = ResponseStdOut,
                StdErr = string.Empty
            };
        }
    }
}
