using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G319: command-level regression tests for the approved-PR
/// continuation lane. Verifies the end-to-end path — the command layer
/// detects an approved PR from GitHub labels, plumbs it into the
/// analyzer with the correct `is_draft` + merge-state + metadata flags,
/// and the analyzer surfaces it BEFORE the wip-cap-blocked stop.
/// </summary>
public sealed class AutomationHostLoopNextActionCommandTests : IDisposable
{
    public AutomationHostLoopNextActionCommandTests()
    {
        AutomationHostLoopNextActionCommand.CandidateListerFactory = null;
    }

    public void Dispose()
    {
        AutomationHostLoopNextActionCommand.CandidateListerFactory = null;
    }

    [Fact]
    public void Execute_ApprovedPrOpenAndNonDraft_SelectsApprovedContinuation_NotWipCapBlocked()
    {
        // Mirrors SKS PR #571: open, non-draft, carries
        // intent-target + intent-pr-approved; the host loop previously
        // stopped at wip-cap-blocked for the open intent-target.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: new[]
            {
                NewPr(571, isDraft: false, state: "OPEN", labels: new[] { "intent-target", "intent-pr-approved" },
                      closingIssue: 570)
            },
            issues: new[] { NewIssue(570, labels: new[] { "intent-target", "intent-pr-created" }) });

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("approved-pr-merge-closeout", root.GetProperty("classification").GetString());
        Assert.True(root.GetProperty("mutation_allowed").GetBoolean());
        Assert.Contains("--pr 571", root.GetProperty("recommended_command").GetString()!, StringComparison.Ordinal);
        var evidence = root.GetProperty("evidence").EnumerateArray()
            .Select(e => e.GetString()!).ToArray();
        Assert.Contains(evidence, e => e.Contains("#571", StringComparison.Ordinal));
        Assert.Contains(evidence, e => e.Contains("Closes #570", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_ApprovedPrIsDraft_MapsToApprovedPrDraftBlocked()
    {
        // G297: an approved PR that is currently a draft cannot be
        // merged; the analyzer must classify draft-blocked, not the
        // happy path or generic wip-cap-blocked.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: new[]
            {
                NewPr(571, isDraft: true, state: "OPEN", labels: new[] { "intent-target", "intent-pr-approved" })
            },
            issues: Array.Empty<GitHubAutomationIssueCandidate>());

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("approved-pr-draft-blocked", doc.RootElement.GetProperty("classification").GetString());
        Assert.False(doc.RootElement.GetProperty("mutation_allowed").GetBoolean());
    }

    [Fact]
    public void Execute_ApprovedPrMergeStateOverride_MapsToApprovedPrMergeBlocked()
    {
        // The command exposes `--approved-pr-merge-state <state>` so
        // host-loop guidance can pre-fetch `gh pr view --json
        // mergeStateStatus` once and pipe it in. CONFLICTING → blocked.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: new[]
            {
                NewPr(571, isDraft: false, state: "OPEN", labels: new[] { "intent-target", "intent-pr-approved" })
            },
            issues: Array.Empty<GitHubAutomationIssueCandidate>());

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService",
             "--approved-pr-merge-state", "CONFLICTING", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("approved-pr-merge-blocked", doc.RootElement.GetProperty("classification").GetString());
        Assert.False(doc.RootElement.GetProperty("mutation_allowed").GetBoolean());
        Assert.Contains("CONFLICTING", doc.RootElement.GetProperty("summary").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ApprovedPrMetadataBlockedFlag_MapsToApprovedPrMetadataBlocked()
    {
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: new[]
            {
                NewPr(571, isDraft: false, state: "OPEN", labels: new[] { "intent-target", "intent-pr-approved" })
            },
            issues: Array.Empty<GitHubAutomationIssueCandidate>());

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService",
             "--approved-pr-metadata-blocked", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("approved-pr-metadata-blocked", doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_OpenIntentTargetPrWithoutApproved_FallsThroughToWipCapBlocked()
    {
        // Acceptance: when there is open intent-target work but NO
        // approved continuation pending, the existing wip-cap-blocked
        // classification is unchanged.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: new[] { NewIssue(700, labels: new[] { "intent-target" }) });

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("wip-cap-blocked", doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_ApprovedPrWithUpdateInProgress_NotSelectedForContinuation()
    {
        // An approved PR that is also `intent-pr-update-in-progress`
        // means a child worker is repairing it. The continuation lane
        // skips it; the existing wip-cap / wait-for-child lanes handle
        // the in-flight state.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: new[]
            {
                NewPr(571, isDraft: false, state: "OPEN",
                    labels: new[] { "intent-target", "intent-pr-approved", "intent-pr-update-in-progress" })
            },
            issues: Array.Empty<GitHubAutomationIssueCandidate>());

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var classification = doc.RootElement.GetProperty("classification").GetString();
        Assert.NotEqual("approved-pr-merge-closeout", classification);
        // Open intent-target + lease held → wait-for-child.
        Assert.Equal("wait-for-child", classification);
    }

    // --- helpers --------------------------------------------------------------

    private static GitHubAutomationPrCandidate NewPr(
        int number,
        bool isDraft,
        string state,
        IReadOnlyList<string> labels,
        int? closingIssue = null)
    {
        var refs = closingIssue is int issueNumber
            ? new[]
            {
                new GitHubPrClosingIssueReference { Number = issueNumber }
            }
            : Array.Empty<GitHubPrClosingIssueReference>();
        return new GitHubAutomationPrCandidate
        {
            Number = number,
            Title = $"PR {number}",
            Url = $"https://github.com/J-Tech-Japan/SekibanAsAService/pull/{number}",
            Body = "stub",
            CreatedAt = "2026-05-10T00:00:00Z",
            UpdatedAt = "2026-05-10T00:00:00Z",
            Labels = labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
            ClosingIssuesReferences = refs,
            State = state,
            IsDraft = isDraft
        };
    }

    private static GitHubAutomationIssueCandidate NewIssue(int number, IReadOnlyList<string> labels) =>
        new()
        {
            Number = number,
            Title = $"issue {number}",
            Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{number}",
            CreatedAt = "2026-05-10T00:00:00Z",
            Labels = labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
            State = "OPEN"
        };

    private sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        private readonly IReadOnlyList<GitHubAutomationPrCandidate> prs;
        private readonly IReadOnlyList<GitHubAutomationIssueCandidate> issues;

        public FakeLister(
            IReadOnlyList<GitHubAutomationPrCandidate> prs,
            IReadOnlyList<GitHubAutomationIssueCandidate> issues)
        {
            this.prs = prs;
            this.issues = issues;
        }

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => prs;
        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) => issues;
    }

    private static CliContext CreateContext() =>
        new()
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };
}
