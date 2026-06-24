using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G481: coverage for the duplicate execution-unit issue / concurrent host
/// publish analyzer. Exercises the four required scenarios — duplicate publish
/// with no durable anchor, canonical durable linkage with a safe-repair offer,
/// non-canonical PR closing reference, and durable-source canonical mismatch —
/// plus the no-duplicate baseline and the durable-over-recency canonical rule.
/// </summary>
public sealed class DuplicateExecutionUnitIssueAnalyzerTests
{
    private const string Repo = "J-Tech-Japan/intent-system";

    [Fact]
    public void Analyze_SingleIssuePerUnit_NoDuplicateFindings()
    {
        var queue = new[] { Queue("G479", linkedIssue: 1059) };
        var publish = new[] { Publish("G479", 1059) };

        var analysis = DuplicateExecutionUnitIssueAnalyzer.Analyze(
            Repo, queue, publish, Array.Empty<StateDoctorPr>());

        Assert.Empty(analysis.Findings);
        Assert.Empty(analysis.UnsafeFindings);
    }

    [Fact]
    public void Analyze_QueueAndPublishDisagree_ClassifiesCanonicalIssueMismatch()
    {
        // Durable queue-state says #100 is canonical; a publish artifact records
        // a different created issue #101. Fail-closed: never overwrite one
        // durable record with another.
        var queue = new[] { Queue("Z4R-G479", linkedIssue: 100) };
        var publish = new[] { Publish("Z4R-G479", 101) };

        var analysis = DuplicateExecutionUnitIssueAnalyzer.Analyze(
            Repo, queue, publish, Array.Empty<StateDoctorPr>());

        Assert.Empty(analysis.Findings);
        var unsafeFinding = Assert.Single(analysis.UnsafeFindings);
        Assert.Equal(AutomationStateDoctorUnsafeKinds.CanonicalIssueMismatch, unsafeFinding.Kind);
        Assert.Equal("Z4R-G479", unsafeFinding.ExecutionUnit);
        Assert.Equal(100, unsafeFinding.IssueNumber); // canonical = durable queue-state
    }

    [Fact]
    public void Analyze_PrClosesNonCanonicalDuplicate_ClassifiedSeparately()
    {
        // Canonical is the durable queue-state issue #100; a stray duplicate
        // GitHub issue #101 (no durable backing) is closed by an implementation
        // PR. This must be classified as pr-closes-noncanonical-issue, NOT as
        // ordinary missing-linked_pr recovery.
        var queue = new[] { Queue("Z4R-G479", linkedIssue: 100) };
        var github = new[] { Github("Z4R-G479", 101) };
        var prs = new[] { Pr(205, merged: false, closes: 101) };

        var analysis = DuplicateExecutionUnitIssueAnalyzer.Analyze(
            Repo, queue, Array.Empty<StateDoctorPublishEvidence>(), prs, github);

        Assert.Empty(analysis.Findings);
        var unsafeFinding = Assert.Single(analysis.UnsafeFindings);
        Assert.Equal(AutomationStateDoctorUnsafeKinds.PrClosesNoncanonicalIssue, unsafeFinding.Kind);
        Assert.Equal(101, unsafeFinding.IssueNumber);
        Assert.Contains("#205", unsafeFinding.Reason, StringComparison.Ordinal);
        Assert.Contains("canonical issue is #100", unsafeFinding.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_TwoPublishArtifactsNoQueueAnchor_ClassifiesConcurrentHostPublish()
    {
        // Two publish artifacts for the same unit record different created issues
        // and there is no queue-state linked_issue to anchor the canonical. This
        // is concurrent/duplicate host publish — fail-closed, no winner by recency.
        var publish = new[]
        {
            Publish("Z4R-G479", 100),
            Publish("Z4R-G479", 101),
        };

        var analysis = DuplicateExecutionUnitIssueAnalyzer.Analyze(
            Repo, Array.Empty<StateDoctorQueueItem>(), publish, Array.Empty<StateDoctorPr>());

        Assert.Empty(analysis.Findings);
        var unsafeFinding = Assert.Single(analysis.UnsafeFindings);
        Assert.Equal(AutomationStateDoctorUnsafeKinds.ConcurrentHostPublishDetected, unsafeFinding.Kind);
        Assert.Equal("Z4R-G479", unsafeFinding.ExecutionUnit);
        Assert.Contains("do not pick a winner by recency", unsafeFinding.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_UniqueCanonicalDuplicateWithoutPr_OffersSafeRepairAdvisoryOnly()
    {
        // Durable queue-state uniquely names canonical #100; a stray duplicate
        // GitHub issue #101 exists with no closing PR. Safe repair (close the
        // non-canonical) is OFFERED as an advisory finding — never auto-applied.
        var queue = new[] { Queue("Z4R-G479", linkedIssue: 100) };
        var github = new[] { Github("Z4R-G479", 101) };

        var analysis = DuplicateExecutionUnitIssueAnalyzer.Analyze(
            Repo, queue, Array.Empty<StateDoctorPublishEvidence>(), Array.Empty<StateDoctorPr>(), github);

        Assert.Empty(analysis.UnsafeFindings);
        var finding = Assert.Single(analysis.Findings);
        Assert.Equal(AutomationStateDoctorCategories.DuplicateExecutionUnitIssue, finding.Category);
        Assert.Equal(AutomationStateDoctorConfidence.Advisory, finding.Confidence);
        Assert.Equal(AutomationStateDoctorRepairKinds.None, finding.RepairKind);
        Assert.False(finding.Applied);
        Assert.Equal(100, finding.IssueNumber); // canonical
        Assert.Contains("#101", finding.Summary, StringComparison.Ordinal); // duplicate to close
    }

    [Fact]
    public void Analyze_CanonicalPrefersDurableQueueStateOverWeakerGithubMatch()
    {
        // Queue-state (durable) names #100; publish agrees on #100; a weaker
        // GitHub body/title match surfaces #101. Canonical stays #100 — durable
        // evidence wins over live GitHub recency.
        var queue = new[] { Queue("Z4R-G479", linkedIssue: 100) };
        var publish = new[] { Publish("Z4R-G479", 100) };
        var github = new[] { Github("Z4R-G479", 101) };

        var analysis = DuplicateExecutionUnitIssueAnalyzer.Analyze(
            Repo, queue, publish, Array.Empty<StateDoctorPr>(), github);

        var finding = Assert.Single(analysis.Findings);
        Assert.Equal(AutomationStateDoctorCategories.DuplicateExecutionUnitIssue, finding.Category);
        Assert.Equal(100, finding.IssueNumber);
        Assert.Contains(finding.Evidence, e => e.Contains("queue-state linked_issue", StringComparison.Ordinal));
    }

    private static StateDoctorQueueItem Queue(string unit, int? linkedIssue, bool completed = false) =>
        new()
        {
            ExecutionUnit = unit,
            LinkedIssueRepo = linkedIssue is null ? null : Repo,
            LinkedIssueNumber = linkedIssue,
            LinkedIssueUrl = linkedIssue is null ? null : $"https://github.com/{Repo}/issues/{linkedIssue}",
            LinkedPrUrl = null,
            Completed = completed,
        };

    private static StateDoctorPublishEvidence Publish(string unit, int issueNumber) =>
        new()
        {
            ExecutionUnit = unit,
            IssueRepo = Repo,
            IssueNumber = issueNumber,
            IssueUrl = $"https://github.com/{Repo}/issues/{issueNumber}",
        };

    private static DuplicateUnitGithubIssue Github(string unit, int issueNumber) =>
        new()
        {
            ExecutionUnit = unit,
            IssueRepo = Repo,
            IssueNumber = issueNumber,
        };

    private static StateDoctorPr Pr(int number, bool merged, params int[] closes) =>
        new()
        {
            Number = number,
            Url = $"https://github.com/{Repo}/pull/{number}",
            Merged = merged,
            ClosingIssueNumbers = closes,
        };
}
