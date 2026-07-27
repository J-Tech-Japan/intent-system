using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G526: focused coverage for <c>automation heartbeat</c> — the read-only
/// wrapper around <see cref="AutomationStalledWorkCommand.Analyze"/> that
/// additionally emits a ready-to-send reconcile message body when stale.
/// </summary>
[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class AutomationHeartbeatCommandTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    public AutomationHeartbeatCommandTests()
    {
        // AutomationHeartbeatCommand has no seams of its own — it wraps
        // AutomationStalledWorkCommand.Analyze, so tests drive that
        // command's static factories directly.
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = () => FixedNow;
    }

    public void Dispose()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = null;
    }

    [Fact]
    public void Execute_HealthyPipeline_ReturnsStaleFalseAndNoMessageBody()
    {
        using var workspace = new HeartbeatWorkspace();
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHeartbeatCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("stale").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("message_body").ValueKind);
    }

    [Fact]
    public void Execute_StalePublishedNotDelegated_ReturnsStaleTrueWithMessageBodyNamingItemAndCommand()
    {
        using var workspace = new HeartbeatWorkspace();
        workspace.WritePacketDomain("G523", "intent-cli");
        var issue = BuildIssue(1147, "G523: Add automation stalled-work surface", FixedNow.AddHours(-26), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationHeartbeatCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--stale-minutes", "45", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.True(doc.RootElement.GetProperty("stale").GetBoolean());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("G523", item.GetProperty("execution_unit").GetString());

        var messageBody = doc.RootElement.GetProperty("message_body").GetString()!;
        Assert.Contains("WAKE (heartbeat)", messageBody, StringComparison.Ordinal);
        Assert.Contains("G523", messageBody, StringComparison.Ordinal);
        Assert.Contains("issue #1147", messageBody, StringComparison.Ordinal);
        Assert.Contains("worker claim", messageBody, StringComparison.Ordinal);
        Assert.Contains("--number 1147", messageBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MultipleStaleItems_MessageBodyNamesEachOne()
    {
        using var workspace = new HeartbeatWorkspace();
        workspace.WritePacketDomain("G523", "intent-cli");
        workspace.WritePacketDomain("G524", "intent-cli");
        var first = BuildIssue(1147, "G523: Add automation stalled-work surface", FixedNow.AddHours(-26), "intent-target");
        var second = BuildIssue(1149, "G524: Orchestrator wake contract", FixedNow.AddHours(-30), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [first, second]);

        using var writer = new StringWriter();
        AutomationHeartbeatCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(2, doc.RootElement.GetProperty("items").GetArrayLength());
        var messageBody = doc.RootElement.GetProperty("message_body").GetString()!;
        Assert.Contains("2 pending transition(s)", messageBody, StringComparison.Ordinal);
        Assert.Contains("G523", messageBody, StringComparison.Ordinal);
        Assert.Contains("G524", messageBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ItemBelowDefaultThreshold_StaysHealthy()
    {
        // Default stale-minutes is 45; an item aged 44 minutes must not trip.
        using var workspace = new HeartbeatWorkspace();
        workspace.WritePacketDomain("G523", "intent-cli");
        var issue = BuildIssue(1147, "G523: Add automation stalled-work surface", FixedNow.AddMinutes(-44), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        AutomationHeartbeatCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("stale").GetBoolean());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("message_body").ValueKind);
    }

    [Fact]
    public void Execute_ItemAboveDefaultThreshold_TripsWithoutExplicitStaleMinutes()
    {
        using var workspace = new HeartbeatWorkspace();
        workspace.WritePacketDomain("G523", "intent-cli");
        var issue = BuildIssue(1147, "G523: Add automation stalled-work surface", FixedNow.AddMinutes(-46), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        AutomationHeartbeatCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.True(doc.RootElement.GetProperty("stale").GetBoolean());
        Assert.Equal(45, doc.RootElement.GetProperty("stale_minutes_threshold").GetInt32());
    }

    [Fact]
    public void Execute_ValidRunWithStaleItems_NeverCreatesQueueStateOrRunsLog()
    {
        // The read-only guarantee: even a normal run that finds stale work
        // (the case most likely to tempt a "helpfully" mutating side effect)
        // never touches local durable state — there is no writer seam at
        // all in this command, only readers.
        using var workspace = new HeartbeatWorkspace();
        workspace.WritePacketDomain("G523", "intent-cli");
        var issue = BuildIssue(1147, "G523: Add automation stalled-work surface", FixedNow.AddHours(-26), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationHeartbeatCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(workspace.Context.GetQueueStatePath()));
        Assert.False(File.Exists(workspace.Context.GetRunLogPath()));
    }

    [Fact]
    public void Execute_HasNoWriteFlag_UnrecognizedArgumentsFailClosed()
    {
        // There is no "--write" (or any other mutation-enabling) flag
        // parsed by this command at all — the read-only guarantee is
        // structural, not merely a default. An unrecognized argument fails
        // closed rather than being silently ignored.
        using var workspace = new HeartbeatWorkspace();
        using var writer = new StringWriter();
        var exitCode = AutomationHeartbeatCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingDomain_FailsWithUsage()
    {
        using var workspace = new HeartbeatWorkspace();
        using var writer = new StringWriter();
        var exitCode = AutomationHeartbeatCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--domain", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingRepo_FailsWithUsage()
    {
        using var workspace = new HeartbeatWorkspace();
        using var writer = new StringWriter();
        var exitCode = AutomationHeartbeatCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Help_PrintsUsageAndExitsZero()
    {
        using var workspace = new HeartbeatWorkspace();
        using var writer = new StringWriter();
        var exitCode = AutomationHeartbeatCommand.Execute(workspace.Context, ["--help"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("automation heartbeat", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MarkdownFormat_RendersWarningsAndMessageBodySections()
    {
        using var workspace = new HeartbeatWorkspace();
        workspace.WritePacketDomain("G523", "intent-cli");
        var issue = BuildIssue(1147, "G523: Add automation stalled-work surface", FixedNow.AddHours(-26), "intent-target");
        var mergedPr = BuildPr(9001, FixedNow.AddDays(-1));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], mergedPrs: [mergedPr]);

        using var writer = new StringWriter();
        var exitCode = AutomationHeartbeatCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# automation heartbeat", output, StringComparison.Ordinal);
        Assert.Contains("## Warnings", output, StringComparison.Ordinal);
        Assert.Contains("## message_body (ready to send)", output, StringComparison.Ordinal);
        Assert.Contains("WAKE (heartbeat)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RepairPendingItem_MessageBodyFramesAsFyiNotRecommended()
    {
        // G533: an informational kind (repair-pending here) must render as
        // "FYI: ..." prose in message_body, never "recommended: `<command>`"
        // — a reader (human or orchestrator) must never mistake "no
        // transition needed" for an actionable next command.
        using var workspace = new HeartbeatWorkspace();
        workspace.WritePacketDomain("G521", "intent-cli");
        var issue = BuildIssue(1143, "G521: Document agmsg Codex monitor", FixedNow.AddDays(-2), "intent-pr-created");
        // G546: kept INSIDE --repair-silent-minutes (default 180m) so this
        // fixture keeps pinning the G533 informational framing; the past-
        // threshold promotion to the actionable repair-stalled kind has its
        // own heartbeat fixture in AutomationStalledWorkCommandTests.
        var pr = BuildOpenPrClosingIssue(1750, FixedNow.AddHours(-1), 1143, "intent-pr-request-update");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);

        using var writer = new StringWriter();
        var exitCode = AutomationHeartbeatCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--stale-minutes", "0", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var messageBody = doc.RootElement.GetProperty("message_body").GetString();
        Assert.NotNull(messageBody);
        Assert.Contains("repair-pending", messageBody, StringComparison.Ordinal);
        Assert.Contains("FYI:", messageBody, StringComparison.Ordinal);
        Assert.DoesNotContain("recommended: `", messageBody, StringComparison.Ordinal);
        Assert.Contains("informational note(s)", messageBody, StringComparison.Ordinal);
        Assert.Contains("0 pending transition(s)", messageBody, StringComparison.Ordinal);
    }

    private static GitHubAutomationIssueCandidate BuildIssue(
        int number, string title, DateTimeOffset createdAt, params string[] labels) => new()
        {
            Number = number,
            Title = title,
            Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{number}",
            CreatedAt = createdAt.ToString("O"),
            State = "OPEN",
            Labels = labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
        };

    /// <summary>Merged PR with no matching queue-state entry — used only to
    /// exercise the "queue-state not found" warning path in markdown output.</summary>
    private static GitHubAutomationPrCandidate BuildPr(int number, DateTimeOffset createdAt) => new()
    {
        Number = number,
        Title = "Some merged PR",
        Url = $"https://github.com/J-Tech-Japan/intent-system/pull/{number}",
        CreatedAt = createdAt.ToString("O"),
        UpdatedAt = createdAt.ToString("O"),
        State = "MERGED",
        IsDraft = false,
    };

    /// <summary>G533: an OPEN PR closing a given issue, carrying arbitrary
    /// extra labels — used for repair-pending/rereview-pending message_body
    /// framing fixtures.</summary>
    private static GitHubAutomationPrCandidate BuildOpenPrClosingIssue(
        int number, DateTimeOffset createdAt, int closingIssueNumber, params string[] extraLabels) => new()
    {
        Number = number,
        Title = "Some open PR",
        Url = $"https://github.com/J-Tech-Japan/intent-system/pull/{number}",
        CreatedAt = createdAt.ToString("O"),
        UpdatedAt = createdAt.ToString("O"),
        State = "OPEN",
        IsDraft = false,
        Labels = extraLabels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
        ClosingIssuesReferences = new[]
        {
            new GitHubPrClosingIssueReference
            {
                Number = closingIssueNumber,
                Repository = new GitHubPrClosingIssueRepository
                {
                    Name = "intent-system",
                    Owner = new GitHubPrClosingIssueRepositoryOwner { Login = "J-Tech-Japan" },
                },
            },
        },
    };

    private sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        private readonly IReadOnlyList<GitHubAutomationIssueCandidate> issues;
        private readonly IReadOnlyList<GitHubAutomationPrCandidate> prs;
        private readonly IReadOnlyList<GitHubAutomationPrCandidate> mergedPrs;

        public FakeLister(
            IReadOnlyList<GitHubAutomationIssueCandidate>? issues = null,
            IReadOnlyList<GitHubAutomationPrCandidate>? prs = null,
            IReadOnlyList<GitHubAutomationPrCandidate>? mergedPrs = null)
        {
            this.issues = issues ?? Array.Empty<GitHubAutomationIssueCandidate>();
            this.prs = prs ?? Array.Empty<GitHubAutomationPrCandidate>();
            this.mergedPrs = mergedPrs ?? Array.Empty<GitHubAutomationPrCandidate>();
        }

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => prs;

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) => issues;

        public IReadOnlyList<GitHubAutomationPrCandidate> ListMergedPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => mergedPrs;
    }

    private sealed class HeartbeatWorkspace : IDisposable
    {
        public HeartbeatWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("heartbeat-tests-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            };
        }

        public string RootPath { get; }

        public CliContext Context { get; }

        public void WritePacketDomain(string executionUnit, string domain)
        {
            var dir = Path.Combine(RootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "packet.yaml"), $"domain: {domain}\n");
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
