using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class AutomationStalledWorkCommandTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    public AutomationStalledWorkCommandTests()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = () => FixedNow;
    }

    public void Dispose()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = null;
    }

    [Fact]
    public void Execute_EmptyPipeline_ReturnsStalledFalseAndNoItems()
    {
        using var workspace = new StalledWorkWorkspace();
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("stalled").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void Execute_PublishedNotDelegated_FiresForOpenIntentTargetIssueWithNoClaim()
    {
        using var workspace = new StalledWorkWorkspace();
        var issue = BuildIssue(1147, "G523: Add automation stalled-work surface", FixedNow.AddHours(-26),
            "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.True(doc.RootElement.GetProperty("stalled").GetBoolean());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindPublishedNotDelegated, item.GetProperty("kind").GetString());
        Assert.Equal("G523", item.GetProperty("execution_unit").GetString());
        Assert.Equal(1147, item.GetProperty("issue").GetProperty("number").GetInt32());
        Assert.Equal(1560, item.GetProperty("age_minutes").GetInt32());
        Assert.Contains("worker claim", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
        Assert.Contains("--number 1147", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_PublishedNotDelegated_ExcludesIssueAlreadyClaimed()
    {
        using var workspace = new StalledWorkWorkspace();
        var claimedIssue = BuildIssue(1148, "G524: Something else", FixedNow.AddHours(-26),
            "intent-target", "intent-issue-in-progress");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [claimedIssue]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void Execute_PrCreatedNotReviewing_FiresWhenIssueCarriesPrCreatedAndPrLacksReviewStart()
    {
        using var workspace = new StalledWorkWorkspace();
        var issue = BuildIssue(1143, "G521: Document agmsg Codex monitor", FixedNow.AddDays(-2), "intent-pr-created");
        var pr = BuildPr(1144, "G521: Document agmsg Codex monitor", FixedNow.AddHours(-1.5),
            state: "OPEN", closingIssueNumber: 1143);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindPrCreatedNotReviewing, item.GetProperty("kind").GetString());
        Assert.Equal("G521", item.GetProperty("execution_unit").GetString());
        Assert.Equal(1143, item.GetProperty("issue").GetProperty("number").GetInt32());
        Assert.Equal(1144, item.GetProperty("pr").GetProperty("number").GetInt32());
        Assert.Equal(90, item.GetProperty("age_minutes").GetInt32());
        Assert.Contains("--transition review-start", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_PrCreatedNotReviewing_ExcludesPrAlreadyReviewing()
    {
        using var workspace = new StalledWorkWorkspace();
        var issue = BuildIssue(1143, "G521: Document agmsg Codex monitor", FixedNow.AddDays(-2), "intent-pr-created");
        var pr = BuildPr(1144, "G521: Document agmsg Codex monitor", FixedNow.AddHours(-1.5),
            state: "OPEN", closingIssueNumber: 1143, extraLabels: ["intent-pr-reviewing"]);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void Execute_MergedNotClosedOut_FiresWhenQueueItemNotCompleted()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteQueueState(BuildQueueStateJson("G500", QueueItemState.Review,
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/1200",
            linkedIssueNumber: 1199));
        var mergedPr = BuildPr(1200, "G500: Some merged change", FixedNow.AddHours(-3), state: "MERGED");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(mergedPrs: [mergedPr]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindMergedNotClosedOut, item.GetProperty("kind").GetString());
        Assert.Equal("G500", item.GetProperty("execution_unit").GetString());
        Assert.Equal(1200, item.GetProperty("pr").GetProperty("number").GetInt32());
        Assert.Equal(180, item.GetProperty("age_minutes").GetInt32());
        Assert.Contains("closeout pr", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
        Assert.Contains("--pr 1200", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MergedNotClosedOut_ExcludesCompletedQueueItem()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteQueueState(BuildQueueStateJson("G500", QueueItemState.Completed,
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/1200",
            linkedIssueNumber: 1199));
        var mergedPr = BuildPr(1200, "G500: Some merged change", FixedNow.AddHours(-3), state: "MERGED");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(mergedPrs: [mergedPr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void Execute_MergedNotClosedOut_MissingQueueStateSurfacesWarning_DoesNotFail()
    {
        using var workspace = new StalledWorkWorkspace();
        // No queue-state.json written at all.
        var mergedPr = BuildPr(1200, "G500: Some merged change", FixedNow.AddHours(-3), state: "MERGED");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(mergedPrs: [mergedPr]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.True(doc.RootElement.GetProperty("warnings").GetArrayLength() > 0);
    }

    [Fact]
    public void Execute_StaleMinutesFilter_ExcludesItemsYoungerThanThreshold()
    {
        using var workspace = new StalledWorkWorkspace();
        var youngIssue = BuildIssue(1150, "G525: A brand new issue", FixedNow.AddMinutes(-10), "intent-target");
        var oldIssue = BuildIssue(1151, "G526: A stale issue", FixedNow.AddHours(-26), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [youngIssue, oldIssue]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--stale-minutes", "60", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("G526", item.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_DomainBindingRegex_ExcludesOtherDomainIssue()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteBindings("intent-cli", "^G[0-9]+$");
        var ourIssue = BuildIssue(1147, "G523: Ours", FixedNow.AddHours(-26), "intent-target");
        var otherDomainIssue = BuildIssue(9999, "SKS-G512: Not ours", FixedNow.AddHours(-26), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [ourIssue, otherDomainIssue]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("G523", item.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_MissingDomainBindings_DoesNotFilter_AndSurfacesWarning()
    {
        using var workspace = new StalledWorkWorkspace();
        // No bindings.md written for "intent-cli" — regex cannot be resolved.
        var issue = BuildIssue(9999, "SKS-G512: From a different domain naming convention", FixedNow.AddHours(-26), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(1, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.True(doc.RootElement.GetProperty("warnings").GetArrayLength() > 0);
    }

    [Fact]
    public void Execute_RequiresDomainFlag()
    {
        using var workspace = new StalledWorkWorkspace();
        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--domain", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RequiresRepoFlag()
    {
        using var workspace = new StalledWorkWorkspace();
        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverMutatesGitHubQueueStateOrRunsLog()
    {
        // Read-only guarantee: even with a full one-of-each fixture, the
        // command must never touch queue-state.json / runs.jsonl / any
        // GitHub write path (the fake lister has no write methods at all,
        // so this test additionally proves the command never needs one).
        using var workspace = new StalledWorkWorkspace();
        var queueStateJson = BuildQueueStateJson("G500", QueueItemState.Review,
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/1200",
            linkedIssueNumber: 1199);
        workspace.WriteQueueState(queueStateJson);
        var publishedIssue = BuildIssue(1147, "G523: Ours", FixedNow.AddHours(-26), "intent-target");
        var prCreatedIssue = BuildIssue(1143, "G521: Document agmsg", FixedNow.AddDays(-2), "intent-pr-created");
        var reviewPr = BuildPr(1144, "G521: Document agmsg", FixedNow.AddHours(-1.5), state: "OPEN", closingIssueNumber: 1143);
        var mergedPr = BuildPr(1200, "G500: Some merged change", FixedNow.AddHours(-3), state: "MERGED");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(
            issues: [publishedIssue, prCreatedIssue],
            prs: [reviewPr],
            mergedPrs: [mergedPr]);

        var runsPath = Path.Combine(workspace.RootPath, ".intent-cli", "runs.jsonl");
        var queueStatePath = workspace.Context.GetQueueStatePath();
        var queueStateBefore = File.ReadAllText(queueStatePath);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.False(File.Exists(runsPath), "stalled-work must never append a runs.jsonl event");
        Assert.Equal(queueStateBefore, File.ReadAllText(queueStatePath));
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

    private static GitHubAutomationPrCandidate BuildPr(
        int number,
        string title,
        DateTimeOffset createdAt,
        string state,
        int? closingIssueNumber = null,
        string[]? extraLabels = null) => new()
        {
            Number = number,
            Title = title,
            Url = $"https://github.com/J-Tech-Japan/intent-system/pull/{number}",
            CreatedAt = createdAt.ToString("O"),
            UpdatedAt = createdAt.ToString("O"),
            State = state,
            IsDraft = false,
            Labels = (extraLabels ?? Array.Empty<string>()).Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
            ClosingIssuesReferences = closingIssueNumber is int n
                ? new[]
                {
                    new GitHubPrClosingIssueReference
                    {
                        Number = n,
                        Repository = new GitHubPrClosingIssueRepository
                        {
                            Name = "intent-system",
                            Owner = new GitHubPrClosingIssueRepositoryOwner { Login = "J-Tech-Japan" },
                        },
                    },
                }
                : Array.Empty<GitHubPrClosingIssueReference>(),
        };

    private static string BuildQueueStateJson(string executionUnit, QueueItemState state, string linkedPr, int linkedIssueNumber)
    {
        var queueState = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = FixedNow,
            Items = new[]
            {
                new QueueItem
                {
                    ExecutionUnit = executionUnit,
                    Title = $"{executionUnit} title",
                    State = state,
                    Dependencies = Array.Empty<string>(),
                    BlockedBy = Array.Empty<string>(),
                    ClarificationReturnPath = string.Empty,
                    PacketPaths = new PacketPaths
                    {
                        Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml",
                        Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                        ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = linkedIssueNumber,
                        Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{linkedIssueNumber}",
                    },
                    LinkedPr = linkedPr,
                    WorkerRole = "Claude",
                    ReviewRole = "Codex",
                    Priority = "normal",
                },
            },
        };
        return QueueStateSerializer.Serialize(queueState);
    }

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

    private sealed class StalledWorkWorkspace : IDisposable
    {
        public StalledWorkWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("stalled-work-tests-").FullName;
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

        public void WriteQueueState(string json) => File.WriteAllText(Context.GetQueueStatePath(), json);

        public void WriteBindings(string domain, string executionUnitRegex)
        {
            var dir = Path.Combine(RootPath, "intents", domain, "automation");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "bindings.md"),
                $"---\nexecution_unit_regex: '{executionUnitRegex}'\n---\n");
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
