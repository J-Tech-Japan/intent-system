using IntentSystem.Cli.Commands;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G331: tests for the pure <see cref="MigrateHostStateAnalyzer"/>.
/// Covers the match / missing-linkage / ambiguous / other-repo
/// classification rules and the idempotency semantic.
/// </summary>
public sealed class MigrateHostStateAnalyzerTests
{
    [Fact]
    public void Analyze_GivenLinkedIssueRepoMatchingTarget_IncludesItemAsMatch()
    {
        var plan = MigrateHostStateAnalyzer.Analyze(new MigrateHostStateInputs
        {
            Domain = "intent-cli",
            TargetRepo = "J-Tech-Japan/intent-system",
            Role = MigrateHostStateAnalyzer.RoleReviewRuntime,
            LegacyQueueState = NewQueueState(
                Item("G331", linkedIssue: ("J-Tech-Japan/intent-system", 765), linkedPr: null)),
            LegacyRuns = Array.Empty<RunEvent>(),
            ExistingScopedRuns = Array.Empty<RunEvent>()
        });

        Assert.Single(plan.MatchingItems);
        Assert.Equal("G331", plan.MatchingItems[0].ExecutionUnit);
        Assert.Single(plan.ItemsToAdd);
        Assert.False(plan.AlreadyMigrated);
    }

    [Fact]
    public void Analyze_GivenLinkedPrPointingAtTarget_IncludesItemAsMatch()
    {
        var plan = MigrateHostStateAnalyzer.Analyze(new MigrateHostStateInputs
        {
            Domain = "intent-cli",
            TargetRepo = "J-Tech-Japan/intent-system",
            Role = MigrateHostStateAnalyzer.RoleReviewRuntime,
            LegacyQueueState = NewQueueState(
                Item("G331", linkedIssue: null,
                    linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/766")),
            LegacyRuns = Array.Empty<RunEvent>(),
            ExistingScopedRuns = Array.Empty<RunEvent>()
        });

        Assert.Single(plan.MatchingItems);
        Assert.Equal("G331", plan.MatchingItems[0].ExecutionUnit);
    }

    [Fact]
    public void Analyze_GivenItemForOtherRepo_ExcludesItem()
    {
        var plan = MigrateHostStateAnalyzer.Analyze(new MigrateHostStateInputs
        {
            Domain = "sekiban-as-a-service",
            TargetRepo = "J-Tech-Japan/SekibanAsAService",
            Role = MigrateHostStateAnalyzer.RoleReviewRuntime,
            LegacyQueueState = NewQueueState(
                Item("G331", linkedIssue: ("J-Tech-Japan/intent-system", 765), linkedPr: null)),
            LegacyRuns = Array.Empty<RunEvent>(),
            ExistingScopedRuns = Array.Empty<RunEvent>()
        });

        Assert.Empty(plan.MatchingItems);
        Assert.Empty(plan.Ambiguities);
        Assert.Empty(plan.MissingGitHubLinkage);
    }

    [Fact]
    public void Analyze_GivenNoLinkedIssueAndNoLinkedPr_FlagsMissingLinkage()
    {
        var plan = MigrateHostStateAnalyzer.Analyze(new MigrateHostStateInputs
        {
            Domain = "intent-cli",
            TargetRepo = "J-Tech-Japan/intent-system",
            Role = MigrateHostStateAnalyzer.RoleReviewRuntime,
            LegacyQueueState = NewQueueState(
                Item("G331", linkedIssue: null, linkedPr: null)),
            LegacyRuns = Array.Empty<RunEvent>(),
            ExistingScopedRuns = Array.Empty<RunEvent>()
        });

        Assert.Empty(plan.MatchingItems);
        var gap = Assert.Single(plan.MissingGitHubLinkage);
        Assert.Contains("G331", gap, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_GivenConflictingLinkedIssueAndLinkedPr_FlagsAmbiguous()
    {
        // linked_issue points at another repo, linked_pr points at the
        // target. The analyzer refuses to migrate without operator
        // disambiguation.
        var plan = MigrateHostStateAnalyzer.Analyze(new MigrateHostStateInputs
        {
            Domain = "intent-cli",
            TargetRepo = "J-Tech-Japan/intent-system",
            Role = MigrateHostStateAnalyzer.RoleReviewRuntime,
            LegacyQueueState = NewQueueState(
                Item("G331",
                    linkedIssue: ("J-Tech-Japan/Sekiban", 1),
                    linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/766")),
            LegacyRuns = Array.Empty<RunEvent>(),
            ExistingScopedRuns = Array.Empty<RunEvent>()
        });

        Assert.Empty(plan.MatchingItems);
        var amb = Assert.Single(plan.Ambiguities);
        Assert.Contains("G331", amb, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_GivenMatchingRunForMatchingItem_IncludesRun()
    {
        var plan = MigrateHostStateAnalyzer.Analyze(new MigrateHostStateInputs
        {
            Domain = "intent-cli",
            TargetRepo = "J-Tech-Japan/intent-system",
            Role = MigrateHostStateAnalyzer.RoleReviewRuntime,
            LegacyQueueState = NewQueueState(
                Item("G331", linkedIssue: ("J-Tech-Japan/intent-system", 765), linkedPr: null)),
            LegacyRuns = new[]
            {
                Run("G331", "pr-merged", repo: "J-Tech-Japan/intent-system")
            },
            ExistingScopedRuns = Array.Empty<RunEvent>()
        });

        Assert.Single(plan.MatchingRuns);
        Assert.Single(plan.RunsToAdd);
    }

    [Fact]
    public void Analyze_GivenRunWithRepoButNoMatchingItem_FlagsUnresolved()
    {
        var plan = MigrateHostStateAnalyzer.Analyze(new MigrateHostStateInputs
        {
            Domain = "intent-cli",
            TargetRepo = "J-Tech-Japan/intent-system",
            Role = MigrateHostStateAnalyzer.RoleReviewRuntime,
            LegacyQueueState = NewQueueState(), // no items
            LegacyRuns = new[]
            {
                Run("DELETED", "pr-merged", repo: "J-Tech-Japan/intent-system")
            },
            ExistingScopedRuns = Array.Empty<RunEvent>()
        });

        Assert.Empty(plan.MatchingRuns);
        var gap = Assert.Single(plan.UnresolvedLegacyRecords);
        Assert.Contains("DELETED", gap, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_GivenItemAlreadyInScopedState_ReportsAlreadyMigrated()
    {
        // Idempotency: matching item is already in scoped state →
        // ItemsToAdd is empty AND AlreadyMigrated is true.
        var item = Item("G331", linkedIssue: ("J-Tech-Japan/intent-system", 765), linkedPr: null);
        var plan = MigrateHostStateAnalyzer.Analyze(new MigrateHostStateInputs
        {
            Domain = "intent-cli",
            TargetRepo = "J-Tech-Japan/intent-system",
            Role = MigrateHostStateAnalyzer.RoleReviewRuntime,
            LegacyQueueState = NewQueueState(item),
            LegacyRuns = Array.Empty<RunEvent>(),
            ExistingScopedQueueState = NewQueueState(item),
            ExistingScopedRuns = Array.Empty<RunEvent>()
        });

        Assert.Single(plan.MatchingItems);
        Assert.Empty(plan.ItemsToAdd);
        Assert.True(plan.AlreadyMigrated);
    }

    [Fact]
    public void Analyze_GivenRunAlreadyInScopedState_OmitsFromRunsToAdd()
    {
        var run = Run("G331", "pr-merged", repo: "J-Tech-Japan/intent-system");
        var plan = MigrateHostStateAnalyzer.Analyze(new MigrateHostStateInputs
        {
            Domain = "intent-cli",
            TargetRepo = "J-Tech-Japan/intent-system",
            Role = MigrateHostStateAnalyzer.RoleReviewRuntime,
            LegacyQueueState = NewQueueState(
                Item("G331", linkedIssue: ("J-Tech-Japan/intent-system", 765), linkedPr: null)),
            LegacyRuns = new[] { run },
            ExistingScopedRuns = new[] { run }
        });

        Assert.Single(plan.MatchingRuns);
        Assert.Empty(plan.RunsToAdd);
    }

    private static QueueItem Item(
        string executionUnit,
        (string Repo, int Number)? linkedIssue,
        string? linkedPr) =>
        new()
        {
            ExecutionUnit = executionUnit,
            Title = executionUnit,
            State = QueueItemState.Review,
            Dependencies = Array.Empty<string>(),
            BlockedBy = Array.Empty<string>(),
            ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
            PacketPaths = new PacketPaths
            {
                Implementation = "a",
                ReviewContext = "b",
                Yaml = "c"
            },
            LinkedIssue = linkedIssue is null
                ? null
                : new LinkedIssue
                {
                    Repo = linkedIssue.Value.Repo,
                    Number = linkedIssue.Value.Number,
                    Url = $"https://github.com/{linkedIssue.Value.Repo}/issues/{linkedIssue.Value.Number}"
                },
            LinkedPr = linkedPr,
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "normal"
        };

    private static RunEvent Run(string executionUnit, string @event, string? repo) =>
        new()
        {
            Ts = DateTimeOffset.Parse("2026-05-11T00:00:00Z"),
            ExecutionUnit = executionUnit,
            Event = @event,
            By = "intent-cli closeout pr",
            Repo = repo
        };

    private static QueueState NewQueueState(params QueueItem[] items) =>
        new()
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-05-11T00:00:00Z"),
            Items = items
        };
}
