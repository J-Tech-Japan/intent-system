using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G374: tests for the host-side <c>review collect-signals</c> analyzer
/// and command — pending detection by label + marker, the
/// already-handled skip (regression guard against reprocessing), the
/// labelled-but-unmarked degenerate case, and the end-to-end command
/// path over fake lister + gateway seams.
/// </summary>
public sealed class ReviewCollectSignalsCommandTests : IDisposable
{
    public ReviewCollectSignalsCommandTests()
    {
        Reset();
    }

    public void Dispose()
    {
        Reset();
    }

    private static void Reset()
    {
        ReviewCollectSignalsCommand.ListerFactory = null;
        ReviewCollectSignalsCommand.GatewayFactory = null;
    }

    private static GitHubSignalComment SignalComment(string kind, string target, int number, string createdAt, string url)
        => new()
        {
            Body = WorkerSignalContract.BuildCommentBody(kind, target, number, "details"),
            CreatedAt = createdAt,
            Url = url,
        };

    [Fact]
    public void Analyze_PendingItem_WithMarker_ParsesKind()
    {
        var candidates = new[]
        {
            new SignalCandidateInput
            {
                Target = "issue",
                Number = 851,
                Title = "G374",
                Url = "https://github.com/o/r/issues/851",
                Labels = new[] { "intent-signal-sent" },
                Comments = new[] { SignalComment("blocker", "issue", 851, "2026-05-20T07:00:00Z", "c1") },
            },
        };

        var result = ReviewCollectSignalsAnalyzer.Analyze("o/r", candidates);

        var pending = Assert.Single(result.PendingSignals);
        Assert.Equal("blocker", pending.SignalKind);
        Assert.Equal("issue", pending.Target);
        Assert.Equal(851, pending.Number);
        Assert.Equal("c1", pending.CommentRef);
        Assert.Equal(0, result.HandledSkippedCount);
        Assert.Equal(0, result.UnmarkedCount);
    }

    [Fact]
    public void Analyze_HandledItem_IsSkipped_NotReprocessed()
    {
        // Regression guard: an item already converged to intent-signal-handled
        // (pending marker cleared) must never reappear as pending.
        var candidates = new[]
        {
            new SignalCandidateInput
            {
                Target = "issue",
                Number = 851,
                Labels = new[] { "intent-signal-handled" },
                Comments = new[] { SignalComment("blocker", "issue", 851, "2026-05-20T07:00:00Z", "c1") },
            },
            new SignalCandidateInput
            {
                Target = "pr",
                Number = 860,
                Labels = new[] { "intent-signal-sent" },
                Comments = new[] { SignalComment("follow-up", "pr", 860, "2026-05-20T08:00:00Z", "c2") },
            },
        };

        var result = ReviewCollectSignalsAnalyzer.Analyze("o/r", candidates);

        Assert.Equal(1, result.PendingCount);
        Assert.Equal(1, result.HandledSkippedCount);
        Assert.Equal("follow-up", result.PendingSignals[0].SignalKind);
    }

    [Fact]
    public void Analyze_LatestMarkerWins_WhenMultipleSignalComments()
    {
        var candidates = new[]
        {
            new SignalCandidateInput
            {
                Target = "pr",
                Number = 860,
                Labels = new[] { "intent-signal-sent" },
                Comments = new[]
                {
                    SignalComment("follow-up", "pr", 860, "2026-05-20T07:00:00Z", "old"),
                    SignalComment("scope-warning", "pr", 860, "2026-05-20T09:00:00Z", "new"),
                },
            },
        };

        var result = ReviewCollectSignalsAnalyzer.Analyze("o/r", candidates);

        var pending = Assert.Single(result.PendingSignals);
        Assert.Equal("scope-warning", pending.SignalKind);
        Assert.Equal("new", pending.CommentRef);
    }

    [Fact]
    public void Analyze_LabelledButUnmarked_CountsAsUnmarkedWithWarning()
    {
        var candidates = new[]
        {
            new SignalCandidateInput
            {
                Target = "issue",
                Number = 851,
                Labels = new[] { "intent-signal-sent" },
                Comments = new[] { new GitHubSignalComment { Body = "ordinary comment", CreatedAt = "2026-05-20T07:00:00Z", Url = "c1" } },
            },
        };

        var result = ReviewCollectSignalsAnalyzer.Analyze("o/r", candidates);

        Assert.Empty(result.PendingSignals);
        Assert.Equal(1, result.UnmarkedCount);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Execute_CollectsAcrossIssuesAndPrs_ViaFakes()
    {
        using var workspace = new SignalWorkspace();
        var lister = new FakeLister
        {
            Issues = new[]
            {
                new GitHubAutomationIssueCandidate
                {
                    Number = 851,
                    Title = "G374",
                    Url = "u851",
                    Labels = new[] { new GitHubAutomationLabel { Name = "intent-signal-sent" } },
                },
            },
            Prs = new[]
            {
                new GitHubAutomationPrCandidate
                {
                    Number = 860,
                    Title = "fix",
                    Url = "u860",
                    Labels = new[] { new GitHubAutomationLabel { Name = "intent-signal-sent" } },
                },
            },
        };
        var gateway = new FakeGateway
        {
            CommentsByTarget = new Dictionary<string, IReadOnlyList<GitHubSignalComment>>(StringComparer.Ordinal)
            {
                ["issue#851"] = new[] { SignalComment("blocker", "issue", 851, "2026-05-20T07:00:00Z", "ic") },
                ["pr#860"] = new[] { SignalComment("follow-up", "pr", 860, "2026-05-20T08:00:00Z", "pc") },
            },
        };
        ReviewCollectSignalsCommand.ListerFactory = () => lister;
        ReviewCollectSignalsCommand.GatewayFactory = () => gateway;

        using var writer = new StringWriter();
        var exit = ReviewCollectSignalsCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--format", "json" },
            writer);

        Assert.Equal(0, exit);
        var result = JsonSerializer.Deserialize<ReviewCollectSignalsResult>(writer.ToString())!;
        Assert.Equal(2, result.PendingCount);
        Assert.Contains(result.PendingSignals, s => s.Target == "issue" && s.Number == 851 && s.SignalKind == "blocker");
        Assert.Contains(result.PendingSignals, s => s.Target == "pr" && s.Number == 860 && s.SignalKind == "follow-up");
    }

    [Fact]
    public void Execute_RequiresRepo()
    {
        using var workspace = new SignalWorkspace();
        using var writer = new StringWriter();
        var exit = ReviewCollectSignalsCommand.Execute(workspace.Context, Array.Empty<string>(), writer);
        Assert.Equal(1, exit);
        Assert.Contains("--repo", writer.ToString(), StringComparison.Ordinal);
    }

    internal sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationIssueCandidate> Issues { get; init; } = Array.Empty<GitHubAutomationIssueCandidate>();
        public IReadOnlyList<GitHubAutomationPrCandidate> Prs { get; init; } = Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => Prs;
        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) => Issues;
    }

    internal sealed class FakeGateway : IGitHubSignalGateway
    {
        public IReadOnlyDictionary<string, IReadOnlyList<GitHubSignalComment>> CommentsByTarget { get; init; }
            = new Dictionary<string, IReadOnlyList<GitHubSignalComment>>(StringComparer.Ordinal);

        public string PostComment(string repo, string kind, int number, string body) =>
            throw new NotSupportedException("collect-signals must not post comments");

        public IReadOnlyList<GitHubSignalComment> ListComments(string repo, string kind, int number) =>
            CommentsByTarget.TryGetValue($"{kind}#{number}", out var comments)
                ? comments
                : Array.Empty<GitHubSignalComment>();
    }

    private sealed class SignalWorkspace : IDisposable
    {
        public SignalWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("collect-signals-tests-").FullName;
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" },
                },
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
