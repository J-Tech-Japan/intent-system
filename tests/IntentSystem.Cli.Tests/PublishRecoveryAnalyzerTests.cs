using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class PublishRecoveryAnalyzerTests
{
    [Fact]
    public void Analyze_PublishArtifactWithUniqueClosingPr_ProducesHighConfidenceRepair()
    {
        var artifact = BuildPublishArtifact("G300", createdIssueNumber: 703,
            createdIssueUrl: "https://github.com/J-Tech-Japan/intent-system/issues/703");
        var candidate = NewCandidate("G300", artifact);
        var pr = BuildPr(706, "G300 implement", body: "Closes #703");

        var result = PublishRecoveryAnalyzer.Analyze(
            "J-Tech-Japan/intent-system",
            new[] { candidate },
            new[] { pr });

        Assert.Single(result.SafeRepairs);
        Assert.Empty(result.UnsafeStops);
        var repair = result.SafeRepairs[0];
        Assert.Equal("G300", repair.ExecutionUnit);
        Assert.Equal(703, repair.LinkedIssueNumber);
        Assert.Equal(706, repair.LinkedPrNumber);
        Assert.Equal("J-Tech-Japan/intent-system", repair.LinkedIssueRepo);
        Assert.Equal("high", repair.Confidence);
        Assert.Contains(repair.Evidence, e => e.Contains("created_issue_number = 703", StringComparison.Ordinal));
        Assert.Contains(repair.Evidence, e => e.Contains("PR #706", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_MultipleClosingPrs_ReturnsUnsafeStop_NoRepair()
    {
        var artifact = BuildPublishArtifact("G300", createdIssueNumber: 703,
            createdIssueUrl: "https://github.com/J-Tech-Japan/intent-system/issues/703");
        var candidate = NewCandidate("G300", artifact);
        var pr706 = BuildPr(706, "first", body: "Closes #703");
        var pr707 = BuildPr(707, "second", body: "Closes #703");

        var result = PublishRecoveryAnalyzer.Analyze(
            "J-Tech-Japan/intent-system",
            new[] { candidate },
            new[] { pr706, pr707 });

        Assert.Empty(result.SafeRepairs);
        Assert.Single(result.UnsafeStops);
        Assert.Equal(PublishRecoveryAnalyzer.UnsafeMultipleClosingPrs, result.UnsafeStops[0].Kind);
        Assert.Contains("#706", result.UnsafeStops[0].Reason, StringComparison.Ordinal);
        Assert.Contains("#707", result.UnsafeStops[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_NoClosingPr_ReturnsUnsafeStop()
    {
        var artifact = BuildPublishArtifact("G300", createdIssueNumber: 703,
            createdIssueUrl: "https://github.com/J-Tech-Japan/intent-system/issues/703");
        var candidate = NewCandidate("G300", artifact);
        var unrelatedPr = BuildPr(999, "Unrelated", body: "Closes #555");

        var result = PublishRecoveryAnalyzer.Analyze(
            "J-Tech-Japan/intent-system",
            new[] { candidate },
            new[] { unrelatedPr });

        Assert.Empty(result.SafeRepairs);
        Assert.Single(result.UnsafeStops);
        Assert.Equal(PublishRecoveryAnalyzer.UnsafeNoClosingPr, result.UnsafeStops[0].Kind);
    }

    [Fact]
    public void Analyze_MissingPublishArtifact_ReturnsUnsafeStop()
    {
        var candidate = NewCandidate("G300", publishArtifact: null);
        var pr = BuildPr(706, "G300", body: "Closes #703");

        var result = PublishRecoveryAnalyzer.Analyze(
            "J-Tech-Japan/intent-system",
            new[] { candidate },
            new[] { pr });

        Assert.Empty(result.SafeRepairs);
        Assert.Single(result.UnsafeStops);
        Assert.Equal(PublishRecoveryAnalyzer.UnsafeMissingPublishArtifact, result.UnsafeStops[0].Kind);
    }

    [Fact]
    public void Analyze_PublishArtifactWithoutCreatedIssue_ReturnsUnsafeStop()
    {
        var artifact = BuildPublishArtifact("G300", createdIssueNumber: null, createdIssueUrl: null);
        var candidate = NewCandidate("G300", artifact);
        var pr = BuildPr(706, "G300", body: "Closes #703");

        var result = PublishRecoveryAnalyzer.Analyze(
            "J-Tech-Japan/intent-system",
            new[] { candidate },
            new[] { pr });

        Assert.Empty(result.SafeRepairs);
        Assert.Single(result.UnsafeStops);
        Assert.Equal(PublishRecoveryAnalyzer.UnsafeMissingCreatedIssue, result.UnsafeStops[0].Kind);
    }

    [Fact]
    public void Analyze_PublishArtifactUrlForDifferentRepo_ReturnsRepoMismatchUnsafeStop()
    {
        var artifact = BuildPublishArtifact("G300", createdIssueNumber: 703,
            createdIssueUrl: "https://github.com/SomeoneElse/different-repo/issues/703");
        var candidate = NewCandidate("G300", artifact);
        var pr = BuildPr(706, "G300", body: "Closes #703");

        var result = PublishRecoveryAnalyzer.Analyze(
            "J-Tech-Japan/intent-system",
            new[] { candidate },
            new[] { pr });

        Assert.Empty(result.SafeRepairs);
        Assert.Single(result.UnsafeStops);
        Assert.Equal(PublishRecoveryAnalyzer.UnsafeRepoMismatch, result.UnsafeStops[0].Kind);
    }

    [Fact]
    public void Analyze_AlreadyLinkedItem_NoRepairOrStop_NoRegression()
    {
        // Already-linked item is out of G303 scope; the existing G284 lane
        // handles linked_issue → linked_pr. The analyzer must not propose
        // anything for this row.
        var artifact = BuildPublishArtifact("G300", createdIssueNumber: 703,
            createdIssueUrl: "https://github.com/J-Tech-Japan/intent-system/issues/703");
        var candidate = new PublishRecoveryCandidate
        {
            ExecutionUnit = "G300",
            LinkedIssueRepo = "J-Tech-Japan/intent-system",
            LinkedIssueNumber = 703,
            LinkedPrUrl = "https://github.com/J-Tech-Japan/intent-system/pull/706",
            PublishArtifact = artifact,
            PublishArtifactExpectedPath = ".intent-cli/issues/G300/publish.yaml"
        };
        var pr = BuildPr(706, "G300", body: "Closes #703");

        var result = PublishRecoveryAnalyzer.Analyze(
            "J-Tech-Japan/intent-system",
            new[] { candidate },
            new[] { pr });

        Assert.Empty(result.SafeRepairs);
        Assert.Empty(result.UnsafeStops);
    }

    [Fact]
    public void Analyze_AcceptsRepoPrefixedClosesKeyword()
    {
        var artifact = BuildPublishArtifact("G300", createdIssueNumber: 703,
            createdIssueUrl: "https://github.com/J-Tech-Japan/intent-system/issues/703");
        var candidate = NewCandidate("G300", artifact);
        var pr = BuildPr(706, "G300", body: "Closes J-Tech-Japan/intent-system#703");

        var result = PublishRecoveryAnalyzer.Analyze(
            "J-Tech-Japan/intent-system",
            new[] { candidate },
            new[] { pr });

        Assert.Single(result.SafeRepairs);
        Assert.Equal(706, result.SafeRepairs[0].LinkedPrNumber);
    }

    private static PublishRecoveryCandidate NewCandidate(string executionUnit, IssuePublishArtifact? publishArtifact) =>
        new()
        {
            ExecutionUnit = executionUnit,
            LinkedIssueRepo = null,
            LinkedIssueNumber = null,
            LinkedPrUrl = null,
            PublishArtifact = publishArtifact,
            PublishArtifactExpectedPath = $".intent-cli/issues/{executionUnit}/publish.yaml"
        };

    private static IssuePublishArtifact BuildPublishArtifact(string executionUnit, int? createdIssueNumber, string? createdIssueUrl) =>
        new()
        {
            ExecutionUnit = executionUnit,
            PublishStatus = "published",
            PacketPath = $".intent-cli/issues/{executionUnit}/packet.yaml",
            IssueBodyPath = $".intent-cli/issues/{executionUnit}/github-body.md",
            CreatedIssueNumber = createdIssueNumber,
            CreatedIssueUrl = createdIssueUrl,
            PublishedLabelName = "intent-target"
        };

    private static GitHubAutomationPrCandidate BuildPr(int number, string title, string body) =>
        new()
        {
            Number = number,
            Title = title,
            Url = $"https://github.com/J-Tech-Japan/intent-system/pull/{number}",
            Body = body,
            CreatedAt = "2026-05-08T00:00:00Z",
            UpdatedAt = "2026-05-08T00:00:00Z",
            Labels = Array.Empty<GitHubAutomationLabel>(),
            State = "OPEN"
        };
}
