using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G344: Unit coverage for the pure host queue-item recovery analyzer.
/// </summary>
public sealed class HostQueueItemRecoveryAnalyzerTests
{
    private const string Repo = "J-Tech-Japan/SekibanAsAService";
    private const string Unit = "SKS-G263";
    private const int IssueNumber = 646;
    private const int PrNumber = 647;

    [Fact]
    public void Analyze_HappyPath_AllFactsAgree_ReturnsRecoverable()
    {
        var candidate = BuildCandidate();

        var result = HostQueueItemRecoveryAnalyzer.Analyze(candidate);

        Assert.Equal(HostQueueItemRecoveryAnalyzer.ResultRecoverable, result.Result);
        Assert.Null(result.ReasonCode);
        Assert.NotNull(result.ProposedQueueItem);
        Assert.Equal(Unit, result.ProposedQueueItem!.ExecutionUnit);
        Assert.Equal(Repo, result.ProposedQueueItem.TargetRepo);
        Assert.Equal(IssueNumber, result.ProposedQueueItem.LinkedIssueNumber);
        Assert.Equal(PrNumber, result.ProposedQueueItem.LinkedPrNumber);
        Assert.NotNull(result.RecommendedCommand);
        Assert.Contains("--repo " + Repo, result.RecommendedCommand!, StringComparison.Ordinal);
        Assert.Contains("--unit " + Unit, result.RecommendedCommand!, StringComparison.Ordinal);
        Assert.Contains("--issue " + IssueNumber, result.RecommendedCommand!, StringComparison.Ordinal);
        Assert.Contains("--pr " + PrNumber, result.RecommendedCommand!, StringComparison.Ordinal);
        Assert.Contains("--write", result.RecommendedCommand!, StringComparison.Ordinal);
        Assert.True(result.Evidence.Count >= 3);
    }

    [Fact]
    public void Analyze_QueueAlreadyBound_ReturnsNotApplicable()
    {
        var candidate = BuildCandidate() with
        {
            ExistingQueueItems = new[]
            {
                new HostQueueItemRecoveryExistingItem
                {
                    ExecutionUnit = Unit,
                    LinkedPrNumber = PrNumber,
                    LinkedIssueNumber = IssueNumber,
                }
            }
        };

        var result = HostQueueItemRecoveryAnalyzer.Analyze(candidate);

        Assert.Equal(HostQueueItemRecoveryAnalyzer.ResultNotApplicable, result.Result);
        Assert.Null(result.ProposedQueueItem);
    }

    [Fact]
    public void Analyze_MissingPacketArtifact_UnsafeStop()
    {
        var candidate = BuildCandidate() with { PacketArtifactExists = false };

        var result = HostQueueItemRecoveryAnalyzer.Analyze(candidate);

        Assert.Equal(HostQueueItemRecoveryAnalyzer.ResultUnsafeStop, result.Result);
        Assert.Equal(HostQueueItemRecoveryAnalyzer.ReasonMissingPacketArtifact, result.ReasonCode);
    }

    [Fact]
    public void Analyze_MissingPublishArtifact_UnsafeStop()
    {
        var candidate = BuildCandidate() with { Publish = null };

        var result = HostQueueItemRecoveryAnalyzer.Analyze(candidate);

        Assert.Equal(HostQueueItemRecoveryAnalyzer.ResultUnsafeStop, result.Result);
        Assert.Equal(HostQueueItemRecoveryAnalyzer.ReasonMissingPublishArtifact, result.ReasonCode);
    }

    [Fact]
    public void Analyze_PacketPublishUnitMismatch_UnsafeStop()
    {
        var candidate = BuildCandidate() with
        {
            Publish = BuildPublishArtifact("OTHER-UNIT", IssueNumber),
        };

        var result = HostQueueItemRecoveryAnalyzer.Analyze(candidate);

        Assert.Equal(HostQueueItemRecoveryAnalyzer.ResultUnsafeStop, result.Result);
        Assert.Equal(HostQueueItemRecoveryAnalyzer.ReasonPacketPublishUnitMismatch, result.ReasonCode);
    }

    [Fact]
    public void Analyze_PublishIssueMismatch_PrClosesDifferentIssue_UnsafeStop()
    {
        var candidate = BuildCandidate() with
        {
            ClosingIssue = BuildClosingIssue(number: 999),
        };

        var result = HostQueueItemRecoveryAnalyzer.Analyze(candidate);

        Assert.Equal(HostQueueItemRecoveryAnalyzer.ResultUnsafeStop, result.Result);
        Assert.Equal(HostQueueItemRecoveryAnalyzer.ReasonPublishIssueMismatch, result.ReasonCode);
    }

    [Fact]
    public void Analyze_PrDoesNotCloseIssue_UnsafeStop()
    {
        var candidate = BuildCandidate() with { ClosingIssue = null };

        var result = HostQueueItemRecoveryAnalyzer.Analyze(candidate);

        Assert.Equal(HostQueueItemRecoveryAnalyzer.ResultUnsafeStop, result.Result);
        Assert.Equal(HostQueueItemRecoveryAnalyzer.ReasonPrDoesNotCloseIssue, result.ReasonCode);
    }

    [Fact]
    public void Analyze_RepoMismatch_PacketTargetRepo_UnsafeStop()
    {
        var candidate = BuildCandidate() with { PacketTargetRepo = "other/repo" };

        var result = HostQueueItemRecoveryAnalyzer.Analyze(candidate);

        Assert.Equal(HostQueueItemRecoveryAnalyzer.ResultUnsafeStop, result.Result);
        Assert.Equal(HostQueueItemRecoveryAnalyzer.ReasonRepoMismatch, result.ReasonCode);
    }

    [Fact]
    public void Analyze_RepoMismatch_PublishCreatedIssueUrl_UnsafeStop()
    {
        var candidate = BuildCandidate() with
        {
            Publish = BuildPublishArtifact(Unit, IssueNumber, $"https://github.com/other/repo/issues/{IssueNumber}"),
        };

        var result = HostQueueItemRecoveryAnalyzer.Analyze(candidate);

        Assert.Equal(HostQueueItemRecoveryAnalyzer.ResultUnsafeStop, result.Result);
        Assert.Equal(HostQueueItemRecoveryAnalyzer.ReasonRepoMismatch, result.ReasonCode);
    }

    [Fact]
    public void Analyze_RepoMismatch_ClosingIssueRepo_UnsafeStop()
    {
        var candidate = BuildCandidate() with
        {
            ClosingIssue = BuildClosingIssue(repo: "other/repo"),
        };

        var result = HostQueueItemRecoveryAnalyzer.Analyze(candidate);

        Assert.Equal(HostQueueItemRecoveryAnalyzer.ResultUnsafeStop, result.Result);
        Assert.Equal(HostQueueItemRecoveryAnalyzer.ReasonRepoMismatch, result.ReasonCode);
    }

    [Fact]
    public void Analyze_MultipleMatchingPackets_UnsafeStop()
    {
        var candidate = BuildCandidate() with
        {
            OtherUnitsClaimingSameIssue = new[] { "SKS-OTHER" },
        };

        var result = HostQueueItemRecoveryAnalyzer.Analyze(candidate);

        Assert.Equal(HostQueueItemRecoveryAnalyzer.ResultUnsafeStop, result.Result);
        Assert.Equal(HostQueueItemRecoveryAnalyzer.ReasonMultipleMatchingPackets, result.ReasonCode);
    }

    [Fact]
    public void Analyze_ConflictingQueueItem_DifferentUnitOwnsPr_UnsafeStop()
    {
        var candidate = BuildCandidate() with
        {
            ExistingQueueItems = new[]
            {
                new HostQueueItemRecoveryExistingItem
                {
                    ExecutionUnit = "SKS-OTHER",
                    LinkedPrNumber = PrNumber,
                    LinkedIssueNumber = null,
                }
            }
        };

        var result = HostQueueItemRecoveryAnalyzer.Analyze(candidate);

        Assert.Equal(HostQueueItemRecoveryAnalyzer.ResultUnsafeStop, result.Result);
        Assert.Equal(HostQueueItemRecoveryAnalyzer.ReasonConflictingExistingQueueItem, result.ReasonCode);
    }

    [Fact]
    public void Analyze_ConflictingQueueItem_SameUnitDifferentPr_UnsafeStop()
    {
        var candidate = BuildCandidate() with
        {
            ExistingQueueItems = new[]
            {
                new HostQueueItemRecoveryExistingItem
                {
                    ExecutionUnit = Unit,
                    LinkedPrNumber = 9999,
                    LinkedIssueNumber = IssueNumber,
                }
            }
        };

        var result = HostQueueItemRecoveryAnalyzer.Analyze(candidate);

        Assert.Equal(HostQueueItemRecoveryAnalyzer.ResultUnsafeStop, result.Result);
        Assert.Equal(HostQueueItemRecoveryAnalyzer.ReasonConflictingExistingQueueItem, result.ReasonCode);
    }

    [Fact]
    public void Analyze_ClosedIssue_WithoutIntentPrCreated_UnsafeStop()
    {
        var candidate = BuildCandidate() with
        {
            ClosingIssue = BuildClosingIssue(state: "CLOSED", labels: new[] { "intent-target" }),
        };

        var result = HostQueueItemRecoveryAnalyzer.Analyze(candidate);

        Assert.Equal(HostQueueItemRecoveryAnalyzer.ResultUnsafeStop, result.Result);
        Assert.Equal(HostQueueItemRecoveryAnalyzer.ReasonClosedIssue, result.ReasonCode);
    }

    [Fact]
    public void Analyze_ClosedIssue_WithIntentPrCreated_StillRecoverable()
    {
        // Issue may be closed by the merge automation but if it still
        // carries intent-pr-created the closeout has progressed past
        // publish — host queue-item recovery should still apply.
        var candidate = BuildCandidate() with
        {
            ClosingIssue = BuildClosingIssue(
                state: "CLOSED",
                labels: new[] { "intent-target", "intent-pr-created" }),
        };

        var result = HostQueueItemRecoveryAnalyzer.Analyze(candidate);

        Assert.Equal(HostQueueItemRecoveryAnalyzer.ResultRecoverable, result.Result);
    }

    [Fact]
    public void Analyze_ClosedPr_UnsafeStop()
    {
        var candidate = BuildCandidate() with { PrState = "MERGED" };

        var result = HostQueueItemRecoveryAnalyzer.Analyze(candidate);

        Assert.Equal(HostQueueItemRecoveryAnalyzer.ResultUnsafeStop, result.Result);
        Assert.Equal(HostQueueItemRecoveryAnalyzer.ReasonClosedPr, result.ReasonCode);
    }

    [Fact]
    public void Analyze_RejectsDraftPrAsUnsafeStop()
    {
        // Every other field is the happy path, but PrIsDraft = true must
        // prevent the analyzer from advancing into a recoverable result —
        // host queue-item recovery is review-safe only when the PR is
        // non-draft.
        var candidate = BuildCandidate() with { PrIsDraft = true };

        var result = HostQueueItemRecoveryAnalyzer.Analyze(candidate);

        Assert.Equal(HostQueueItemRecoveryAnalyzer.ResultUnsafeStop, result.Result);
        Assert.Equal(HostQueueItemRecoveryAnalyzer.ReasonPrIsDraft, result.ReasonCode);
        Assert.Equal("pr-is-draft", result.ReasonCode);
        Assert.Null(result.ProposedQueueItem);
    }

    [Fact]
    public void Analyze_BlankRepo_Throws()
    {
        var candidate = BuildCandidate() with { TargetRepo = "   " };

        Assert.Throws<ArgumentException>(() => HostQueueItemRecoveryAnalyzer.Analyze(candidate));
    }

    private static HostQueueItemRecoveryCandidate BuildCandidate() =>
        new()
        {
            TargetRepo = Repo,
            ExecutionUnit = Unit,
            PacketArtifactExists = true,
            PacketTargetRepo = Repo,
            Publish = BuildPublishArtifact(Unit, IssueNumber),
            PrNumber = PrNumber,
            PrUrl = $"https://github.com/{Repo}/pull/{PrNumber}",
            PrRepo = Repo,
            PrState = "OPEN",
            PrIsDraft = false,
            ClosingIssue = BuildClosingIssue(),
            ExistingQueueItems = Array.Empty<HostQueueItemRecoveryExistingItem>(),
            OtherUnitsClaimingSameIssue = Array.Empty<string>(),
        };

    private static IssuePublishArtifact BuildPublishArtifact(string unit, int issueNumber, string? createdIssueUrl = null) =>
        new()
        {
            ExecutionUnit = unit,
            PublishStatus = "issue-created",
            PacketPath = $".intent-cli/issues/{unit}/packet.yaml",
            IssueBodyPath = $".intent-cli/issues/{unit}/github-body.md",
            CreatedIssueNumber = issueNumber,
            CreatedIssueUrl = createdIssueUrl ?? $"https://github.com/{Repo}/issues/{issueNumber}",
            PublishedLabelName = "intent-target",
        };

    private static HostQueueItemRecoveryClosingIssue BuildClosingIssue(
        int number = IssueNumber,
        string? repo = null,
        string state = "OPEN",
        IReadOnlyList<string>? labels = null) =>
        new()
        {
            Number = number,
            Repo = repo,
            State = state,
            Labels = labels ?? new[] { "intent-target", "intent-pr-created" },
            Url = $"https://github.com/{Repo}/issues/{number}",
        };
}
