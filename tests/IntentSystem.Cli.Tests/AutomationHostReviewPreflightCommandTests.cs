using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class AutomationHostReviewPreflightCommandTests : IDisposable
{
    public AutomationHostReviewPreflightCommandTests()
    {
        AutomationHostReviewPreflightCommand.CandidateListerFactory = null;
        AutomationHostReviewPreflightCommand.NestedProviderLauncher = null;
    }

    public void Dispose()
    {
        AutomationHostReviewPreflightCommand.CandidateListerFactory = null;
        AutomationHostReviewPreflightCommand.NestedProviderLauncher = null;
    }

    [Fact]
    public void Execute_EmptyQueueReturnsNoActionableItem()
    {
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        var lister = new FakeLister();
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewPreflightCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewPreflightResult>(writer.ToString())!;
        Assert.Equal("no-actionable-item", result.Action);
        Assert.Null(result.TargetPr);
        Assert.Empty(result.InFlightPrs);
        Assert.Empty(result.InFlightIssues);
    }

    [Fact]
    public void Execute_PrReadySelectsOldestReviewPr()
    {
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        var lister = new FakeLister
        {
            Prs =
            [
                BuildPr(30, "newer", "https://github.com/J-Tech-Japan/intent-system/pull/30",
                    "2026-05-02T02:00:00Z", ["intent-target"]),
                BuildPr(20, "older", "https://github.com/J-Tech-Japan/intent-system/pull/20",
                    "2026-05-02T01:00:00Z", ["intent-target", "intent-pr-rereview-ready"]),
            ],
        };
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewPreflightCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewPreflightResult>(writer.ToString())!;
        Assert.Equal("review-pr", result.Action);
        Assert.Equal(20, result.TargetPr);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/20", result.TargetPrUrl);
        Assert.Equal([20, 30], result.InFlightPrs);
    }

    [Fact]
    public void Execute_WipIssueBlocksCandidate()
    {
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        var lister = new FakeLister
        {
            Issues =
            [
                BuildIssue(559, "wip", "https://github.com/J-Tech-Japan/intent-system/issues/559",
                    "2026-05-02T01:00:00Z", ["intent-target"]),
            ],
        };
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewPreflightCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--candidate", "G228", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewPreflightResult>(writer.ToString())!;
        Assert.Equal("skip-next-slice-due-to-wip", result.Action);
        Assert.Equal([559], result.InFlightIssues);
        Assert.Equal("G228", result.CandidateExecutionUnit);
    }

    [Fact]
    public void Execute_CandidateReadyWhenNoWip()
    {
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewPreflightCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--candidate", "G228", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewPreflightResult>(writer.ToString())!;
        Assert.Equal("candidate-ready", result.Action);
        Assert.Equal("G228", result.CandidateExecutionUnit);
    }

    [Fact]
    public void Execute_ClarificationRequiredWins()
    {
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => new FakeLister
        {
            Prs = [BuildPr(20, "ready", "https://github.com/J-Tech-Japan/intent-system/pull/20", "2026-05-02T01:00:00Z", ["intent-target"])],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewPreflightCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--clarification-required", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewPreflightResult>(writer.ToString())!;
        Assert.Equal("clarification-required", result.Action);
        Assert.Null(result.TargetPr);
    }

    [Fact]
    public void CommandRouter_RegistersAutomationHostReviewPreflight()
    {
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["automation", "host-review-preflight", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            workspace.Context,
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewPreflightResult>(writer.ToString())!;
        Assert.Equal("no-actionable-item", result.Action);
    }

    private static GitHubAutomationPrCandidate BuildPr(
        int number,
        string title,
        string url,
        string createdAt,
        IReadOnlyList<string> labels) =>
        new()
        {
            Number = number,
            Title = title,
            Url = url,
            CreatedAt = createdAt,
            Labels = labels.Select(label => new GitHubAutomationLabel { Name = label }).ToArray(),
        };

    private static GitHubAutomationIssueCandidate BuildIssue(
        int number,
        string title,
        string url,
        string createdAt,
        IReadOnlyList<string> labels) =>
        new()
        {
            Number = number,
            Title = title,
            Url = url,
            CreatedAt = createdAt,
            Labels = labels.Select(label => new GitHubAutomationLabel { Name = label }).ToArray(),
        };

    private sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> Prs { get; init; } = Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationIssueCandidate> Issues { get; init; } = Array.Empty<GitHubAutomationIssueCandidate>();

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels) => Prs;

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo,
            IReadOnlyCollection<string> requiredLabels) => Issues;
    }

    private sealed class AutomationHostReviewPreflightWorkspace : IDisposable
    {
        public AutomationHostReviewPreflightWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("automation-host-review-preflight-tests-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli"
                    }
                }
            };
        }

        public string RootPath { get; }

        public CliContext Context { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
