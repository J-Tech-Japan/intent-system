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

    private sealed class FakeRunner : IGitHubCommandRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public string PayloadJson { get; private set; } = string.Empty;

        public GitHubCommandResult Run(IReadOnlyList<string> arguments)
        {
            Arguments = arguments.ToArray();
            var payloadIndex = Arguments
                .Select((argument, index) => (argument, index))
                .First(item => string.Equals(item.argument, "--input", StringComparison.Ordinal))
                .index + 1;
            PayloadJson = File.ReadAllText(Arguments[payloadIndex]);

            return new GitHubCommandResult
            {
                ExitCode = 0,
                StdOut = """{"number":53,"html_url":"https://github.com/J-Tech-Japan/intent-system/issues/53"}""",
                StdErr = string.Empty
            };
        }
    }
}
