using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class SoloConductorReviewReaderTests
{
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G856";
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Head = "1111111111111111111111111111111111111111";

    [Fact]
    public void BuildArguments_UsesOnlyFixedReadOnlyEndpointAndPage()
    {
        Assert.Equal(
            ["api", "--method", "GET", "repos/J-Tech-Japan/intent-system/pulls/1870/reviews?per_page=100&page=2"],
            SoloConductorReviewReader.BuildArguments(Repo, 1870, 2));
    }

    [Fact]
    public void InvalidRequest_ExecutesNoRunner()
    {
        var runner = new ScriptedRunner();
        var reader = new SoloConductorReviewReader(runner);

        var result = reader.Read(Request(repo: "../owner/repo", pr: 0));

        Assert.False(result.Complete);
        Assert.Equal("review-request-invalid", result.Cause);
        Assert.Empty(runner.Arguments);
    }

    [Fact]
    public void FullPageContinuesAndShortSecondPageCompletesInventory()
    {
        var runner = new ScriptedRunner(Page(100), Page(1, ReviewRow(101, "Ordinary comment")));
        var result = new SoloConductorReviewReader(runner).Read(Request());

        Assert.True(result.Complete, result.Detail);
        Assert.Equal(101, result.Rows.Count);
        Assert.Equal(2, runner.Arguments.Count);
        Assert.EndsWith("?per_page=100&page=1", runner.Arguments[0][^1], StringComparison.Ordinal);
        Assert.EndsWith("?per_page=100&page=2", runner.Arguments[1][^1], StringComparison.Ordinal);
    }

    [Fact]
    public void FailedLaterPageRefusesInsteadOfReturningPartialInventory()
    {
        var runner = new ScriptedRunner(
            Page(100),
            new GitHubCommandResult { ExitCode = 1, StdOut = "", StdErr = "HTTP 403: rate limit" });

        var result = new SoloConductorReviewReader(runner).Read(Request());

        Assert.False(result.Complete);
        Assert.Equal(SoloConductorReviewReader.CauseReadUnavailable, result.Cause);
        Assert.Empty(result.Rows);
        Assert.Contains("rate limit", result.Detail, StringComparison.Ordinal);
        Assert.Equal(2, runner.Arguments.Count);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    public void MalformedPageRefuses(string payload)
    {
        var runner = new ScriptedRunner(new GitHubCommandResult { ExitCode = 0, StdOut = payload, StdErr = "" });

        var result = new SoloConductorReviewReader(runner).Read(Request());

        Assert.False(result.Complete);
        Assert.Equal(SoloConductorReviewReader.CauseReadUnavailable, result.Cause);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void FullPageAtBoundRefusesTruncation()
    {
        var page = Page(100);
        var runner = new ScriptedRunner(Enumerable.Repeat(page, SoloConductorReviewReader.MaximumPages).ToArray());

        var result = new SoloConductorReviewReader(runner).Read(Request());

        Assert.False(result.Complete);
        Assert.Equal(SoloConductorReviewReader.CauseReadUnavailable, result.Cause);
        Assert.Empty(result.Rows);
        Assert.Equal(SoloConductorReviewReader.MaximumPages, runner.Arguments.Count);
        Assert.Contains("truncated inventory", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void NonObjectRowIsRetainedForEvaluatorInventoryFailure()
    {
        var runner = new ScriptedRunner(new GitHubCommandResult { ExitCode = 0, StdOut = "[null]", StdErr = "" });

        var result = new SoloConductorReviewReader(runner).Read(Request());

        Assert.True(result.Complete, result.Detail);
        Assert.Equal("Pull-request review response row is not a JSON object.", Assert.Single(result.Rows).InventoryFailureDetail);
    }

    private static SoloConductorReviewReadRequest Request(string repo = Repo, int pr = 1870) => new()
    {
        Repo = repo,
        PullRequest = pr,
        ExecutionUnit = Unit,
        Domain = Domain,
        Team = Team,
        LocalReviews = new CrossRuntimeReviewReadResult([], []),
    };

    private static GitHubCommandResult Page(int count, object? last = null)
    {
        var items = Enumerable.Range(1, count)
            .Select(index => (object?)ReviewRow(index, "Ordinary comment"))
            .ToArray();
        if (last is not null && items.Length > 0) items[^1] = last;
        return new GitHubCommandResult { ExitCode = 0, StdOut = JsonSerializer.Serialize(items), StdErr = "" };
    }

    private static object ReviewRow(int id, string body) => new
    {
        id,
        state = "COMMENTED",
        commit_id = Head,
        body,
        html_url = $"https://github.com/{Repo}/pull/1870#pullrequestreview-{id}",
        user = new { login = "reviewer" },
        submitted_at = "2026-10-08T12:00:00Z",
    };

    private sealed class ScriptedRunner(params GitHubCommandResult[] results) : IGitHubCommandRunner
    {
        private int index;

        public List<IReadOnlyList<string>> Arguments { get; } = [];

        public GitHubCommandResult Run(IReadOnlyList<string> arguments)
        {
            Arguments.Add(arguments.ToArray());
            if (index >= results.Length) throw new InvalidOperationException("unexpected extra GitHub read");
            return results[index++];
        }
    }
}
