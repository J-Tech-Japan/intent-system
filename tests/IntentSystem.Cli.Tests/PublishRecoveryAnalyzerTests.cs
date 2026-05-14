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

    // --- G315: queue-linked-issue → closing-PR lane -----------------------------

    [Fact]
    public void Analyze_LinkedIssuePresentNoPr_UniqueClosingPr_ProducesG315HighConfidenceRepair()
    {
        // Mirrors the SKS-G219 incident: queue already has linked_issue=#558,
        // linked_pr=null; PR #559 closes #558 deterministically. The G315 lane
        // should classify this as a high-confidence repair and preserve the
        // existing linked_issue URL.
        var candidate = NewLinkedIssueCandidate(
            executionUnit: "SKS-G219",
            linkedIssueRepo: "J-Tech-Japan/SekibanAsAService",
            linkedIssueNumber: 558,
            linkedIssueUrl: "https://github.com/J-Tech-Japan/SekibanAsAService/issues/558");
        var pr = BuildPr(559, "SKS-G219 implement", body: "Closes #558");

        var result = PublishRecoveryAnalyzer.Analyze(
            "J-Tech-Japan/SekibanAsAService",
            new[] { candidate },
            new[] { pr });

        Assert.Single(result.SafeRepairs);
        Assert.Empty(result.UnsafeStops);
        var repair = result.SafeRepairs[0];
        Assert.Equal(PublishRecoveryAnalyzer.RepairTypeLinkedIssueClosingPr, repair.Type);
        Assert.Equal("SKS-G219", repair.ExecutionUnit);
        Assert.Equal(558, repair.LinkedIssueNumber);
        Assert.Equal(559, repair.LinkedPrNumber);
        Assert.Equal("J-Tech-Japan/SekibanAsAService", repair.LinkedIssueRepo);
        Assert.Equal("https://github.com/J-Tech-Japan/SekibanAsAService/issues/558", repair.LinkedIssueUrl);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/559", repair.LinkedPrUrl); // BuildPr URL host
        Assert.Equal("high", repair.Confidence);
        Assert.Contains(repair.Evidence, e => e.Contains("linked_issue #558", StringComparison.Ordinal));
        Assert.Contains(repair.Evidence, e => e.Contains("PR #559", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_LinkedIssue_UsesStructuredClosingIssuesReferences()
    {
        // GitHub's `closingIssuesReferences` graph data is the primary signal
        // — even if the body has no Closes/Fixes text, the structured
        // reference must drive the match.
        var candidate = NewLinkedIssueCandidate(
            executionUnit: "G300",
            linkedIssueRepo: "J-Tech-Japan/intent-system",
            linkedIssueNumber: 703,
            linkedIssueUrl: null);
        var pr = new GitHubAutomationPrCandidate
        {
            Number = 706,
            Title = "G300 implement (no closes keyword in body)",
            Url = "https://github.com/J-Tech-Japan/intent-system/pull/706",
            Body = "PR description with no closing keyword.",
            CreatedAt = "2026-05-08T00:00:00Z",
            UpdatedAt = "2026-05-08T00:00:00Z",
            Labels = Array.Empty<GitHubAutomationLabel>(),
            State = "OPEN",
            ClosingIssuesReferences = new[]
            {
                new GitHubPrClosingIssueReference
                {
                    Number = 703,
                    Repository = new GitHubPrClosingIssueRepository
                    {
                        Name = "intent-system",
                        Owner = new GitHubPrClosingIssueRepositoryOwner { Login = "J-Tech-Japan" }
                    }
                }
            }
        };

        var result = PublishRecoveryAnalyzer.Analyze(
            "J-Tech-Japan/intent-system",
            new[] { candidate },
            new[] { pr });

        Assert.Single(result.SafeRepairs);
        Assert.Equal(706, result.SafeRepairs[0].LinkedPrNumber);
        // The repair must synthesize a linked_issue URL when the queue row
        // didn't carry one.
        Assert.Equal(
            "https://github.com/J-Tech-Japan/intent-system/issues/703",
            result.SafeRepairs[0].LinkedIssueUrl);
    }

    [Fact]
    public void Analyze_LinkedIssue_NoClosingPr_PointsToG311()
    {
        var candidate = NewLinkedIssueCandidate(
            executionUnit: "SKS-G219",
            linkedIssueRepo: "J-Tech-Japan/SekibanAsAService",
            linkedIssueNumber: 558,
            linkedIssueUrl: null);
        var unrelatedPr = BuildPr(700, "Unrelated", body: "Closes #999");

        var result = PublishRecoveryAnalyzer.Analyze(
            "J-Tech-Japan/SekibanAsAService",
            new[] { candidate },
            new[] { unrelatedPr });

        Assert.Empty(result.SafeRepairs);
        Assert.Single(result.UnsafeStops);
        var stop = result.UnsafeStops[0];
        Assert.Equal(PublishRecoveryAnalyzer.UnsafeNoClosingPrForLinkedIssue, stop.Kind);
        // G311 owns the PR-body closing-reference repair; the stop reason
        // must point operators in that direction rather than guessing.
        Assert.Contains("G311", stop.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_LinkedIssue_RepoMismatch_ReturnsUnsafeStop()
    {
        var candidate = NewLinkedIssueCandidate(
            executionUnit: "SKS-G219",
            linkedIssueRepo: "SomeoneElse/different-repo",
            linkedIssueNumber: 558,
            linkedIssueUrl: null);
        var pr = BuildPr(559, "SKS-G219", body: "Closes #558");

        var result = PublishRecoveryAnalyzer.Analyze(
            "J-Tech-Japan/SekibanAsAService",
            new[] { candidate },
            new[] { pr });

        Assert.Empty(result.SafeRepairs);
        Assert.Single(result.UnsafeStops);
        Assert.Equal(PublishRecoveryAnalyzer.UnsafeLinkedIssueRepoMismatch, result.UnsafeStops[0].Kind);
    }

    [Fact]
    public void Analyze_LinkedIssue_MultipleClosingPrs_ReturnsUnsafeStop()
    {
        var candidate = NewLinkedIssueCandidate(
            executionUnit: "SKS-G219",
            linkedIssueRepo: "J-Tech-Japan/SekibanAsAService",
            linkedIssueNumber: 558,
            linkedIssueUrl: null);
        var pr559 = BuildPr(559, "first", body: "Closes #558");
        var pr560 = BuildPr(560, "second", body: "Fixes #558");

        var result = PublishRecoveryAnalyzer.Analyze(
            "J-Tech-Japan/SekibanAsAService",
            new[] { candidate },
            new[] { pr559, pr560 });

        Assert.Empty(result.SafeRepairs);
        Assert.Single(result.UnsafeStops);
        Assert.Equal(PublishRecoveryAnalyzer.UnsafeMultipleClosingPrsForLinkedIssue, result.UnsafeStops[0].Kind);
        Assert.Contains("#559", result.UnsafeStops[0].Reason, StringComparison.Ordinal);
        Assert.Contains("#560", result.UnsafeStops[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_LinkedIssue_MultipleQueueItemsForSameIssue_BothAreUnsafe()
    {
        // Two queue items both reference linked_issue #558 — neither can
        // claim the unique closing PR deterministically.
        var candidate1 = NewLinkedIssueCandidate(
            executionUnit: "SKS-G219",
            linkedIssueRepo: "J-Tech-Japan/SekibanAsAService",
            linkedIssueNumber: 558,
            linkedIssueUrl: null);
        var candidate2 = NewLinkedIssueCandidate(
            executionUnit: "SKS-G220",
            linkedIssueRepo: "J-Tech-Japan/SekibanAsAService",
            linkedIssueNumber: 558,
            linkedIssueUrl: null);
        var pr = BuildPr(559, "shared", body: "Closes #558");

        var result = PublishRecoveryAnalyzer.Analyze(
            "J-Tech-Japan/SekibanAsAService",
            new[] { candidate1, candidate2 },
            new[] { pr });

        Assert.Empty(result.SafeRepairs);
        Assert.Equal(2, result.UnsafeStops.Count);
        Assert.All(result.UnsafeStops, stop =>
            Assert.Equal(PublishRecoveryAnalyzer.UnsafeMultipleQueueItemsForLinkedIssue, stop.Kind));
    }

    [Fact]
    public void Analyze_LinkedIssueAlreadyHasLinkedPr_NoRepair()
    {
        // End-to-end linked: nothing for any lane to do.
        var candidate = new PublishRecoveryCandidate
        {
            ExecutionUnit = "SKS-G219",
            LinkedIssueRepo = "J-Tech-Japan/SekibanAsAService",
            LinkedIssueNumber = 558,
            LinkedIssueUrl = "https://github.com/J-Tech-Japan/SekibanAsAService/issues/558",
            LinkedPrUrl = "https://github.com/J-Tech-Japan/SekibanAsAService/pull/559",
            PublishArtifact = null,
            PublishArtifactExpectedPath = ".intent-cli/issues/SKS-G219/publish.yaml"
        };
        var pr = BuildPr(559, "SKS-G219", body: "Closes #558");

        var result = PublishRecoveryAnalyzer.Analyze(
            "J-Tech-Japan/SekibanAsAService",
            new[] { candidate },
            new[] { pr });

        Assert.Empty(result.SafeRepairs);
        Assert.Empty(result.UnsafeStops);
    }

    // ----- G351: AnalyzeScopedToPr -----

    [Fact]
    public void AnalyzeScopedToPr_G351_G346Fixture_LinkedIssuePresent_ProducesSingleRepair()
    {
        // G351 AC fixture: queue item G346 has linked_issue=#795, linked_pr=null.
        // PR #796 closes #795. Scoped recovery must return exactly one
        // high-confidence repair and no unrelated unsafe stops.
        var candidate = NewLinkedIssueCandidate("G346", "J-Tech-Japan/intent-system", 795,
            "https://github.com/J-Tech-Japan/intent-system/issues/795");
        var unrelated = NewLinkedIssueCandidate("G999", "J-Tech-Japan/intent-system", 888,
            "https://github.com/J-Tech-Japan/intent-system/issues/888");
        var pr796 = BuildPr(796, "G346 base branch", "Closes #795");
        var pr900 = BuildPr(900, "G999 unrelated", "Closes #888");

        var result = PublishRecoveryAnalyzer.AnalyzeScopedToPr(
            "J-Tech-Japan/intent-system",
            new[] { candidate, unrelated },
            new[] { pr796, pr900 },
            prNumber: 796);

        Assert.Single(result.SafeRepairs);
        Assert.Empty(result.UnsafeStops);
        var repair = result.SafeRepairs[0];
        Assert.Equal("G346", repair.ExecutionUnit);
        Assert.Equal(795, repair.LinkedIssueNumber);
        Assert.Equal(796, repair.LinkedPrNumber);
        Assert.Equal(PublishRecoveryAnalyzer.RepairTypeLinkedIssueClosingPr, repair.Type);
        Assert.Equal("high", repair.Confidence);
    }

    [Fact]
    public void AnalyzeScopedToPr_G351_PrNotInOpenList_ReturnsEmpty()
    {
        // G351: when the selected PR is not in the open PR list (already
        // merged/closed), the scoped analysis returns empty — no-op.
        var candidate = NewLinkedIssueCandidate("G346", "J-Tech-Japan/intent-system", 795, null);
        var pr797 = BuildPr(797, "other PR", "Closes #799");

        var result = PublishRecoveryAnalyzer.AnalyzeScopedToPr(
            "J-Tech-Japan/intent-system",
            new[] { candidate },
            new[] { pr797 },
            prNumber: 796);

        Assert.Empty(result.SafeRepairs);
        Assert.Empty(result.UnsafeStops);
    }

    [Fact]
    public void AnalyzeScopedToPr_G351_PrHasNoClosingReference_ProducesScopedUnsafeStop()
    {
        // G351: when the selected PR has no Closes/Fixes/Resolves reference,
        // the scoped analysis produces a single concise unsafe stop — not a
        // flood of unrelated stops.
        var candidate = NewLinkedIssueCandidate("G346", "J-Tech-Japan/intent-system", 795, null);
        var pr796 = BuildPr(796, "G346 PR", "no closing reference here");

        var result = PublishRecoveryAnalyzer.AnalyzeScopedToPr(
            "J-Tech-Japan/intent-system",
            new[] { candidate },
            new[] { pr796 },
            prNumber: 796);

        Assert.Empty(result.SafeRepairs);
        Assert.Single(result.UnsafeStops);
        Assert.Equal(PublishRecoveryAnalyzer.UnsafeNoClosingPrForLinkedIssue, result.UnsafeStops[0].Kind);
        Assert.Contains("796", result.UnsafeStops[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeScopedToPr_G351_NoCandidateMatchesClosingIssue_ReturnsEmpty()
    {
        // G351: when the selected PR closes an issue that no queue candidate
        // has as its linked_issue, the scoped analysis returns empty — the PR
        // may be for an untracked issue.
        var candidate = NewLinkedIssueCandidate("G346", "J-Tech-Japan/intent-system", 795, null);
        var pr796 = BuildPr(796, "untracked issue PR", "Closes #999");

        var result = PublishRecoveryAnalyzer.AnalyzeScopedToPr(
            "J-Tech-Japan/intent-system",
            new[] { candidate },
            new[] { pr796 },
            prNumber: 796);

        Assert.Empty(result.SafeRepairs);
        Assert.Empty(result.UnsafeStops);
    }

    [Fact]
    public void AnalyzeScopedToPr_G351_UnrelatedQueueItemsNotIncluded()
    {
        // G351 verification: when multiple queue items have missing linked_pr,
        // a scoped call for PR #796 / issue #795 only surfaces that one item —
        // G999/888 is untouched even though it also has missing linked_pr.
        var g346 = NewLinkedIssueCandidate("G346", "J-Tech-Japan/intent-system", 795, null);
        var g999 = NewLinkedIssueCandidate("G999", "J-Tech-Japan/intent-system", 888, null);
        var pr796 = BuildPr(796, "G346", "Closes #795");
        var pr900 = BuildPr(900, "G999", "Closes #888");

        var result = PublishRecoveryAnalyzer.AnalyzeScopedToPr(
            "J-Tech-Japan/intent-system",
            new[] { g346, g999 },
            new[] { pr796, pr900 },
            prNumber: 796);

        // Only G346 is in scope; G999 must not appear in repairs or stops.
        Assert.All(result.SafeRepairs, r => Assert.Equal("G346", r.ExecutionUnit));
        Assert.All(result.UnsafeStops, s => Assert.NotEqual("G999", s.ExecutionUnit));
    }

    [Fact]
    public void AnalyzeScopedToPr_G351_IssueMismatch_DoesNotRepair()
    {
        // G351 negative test: queue item G346 has linked_issue=#795 but
        // PR #796 closes #888 (wrong issue). No candidate matches, result is empty.
        var candidate = NewLinkedIssueCandidate("G346", "J-Tech-Japan/intent-system", 795, null);
        var pr796 = BuildPr(796, "mismatch", "Closes #888");

        var result = PublishRecoveryAnalyzer.AnalyzeScopedToPr(
            "J-Tech-Japan/intent-system",
            new[] { candidate },
            new[] { pr796 },
            prNumber: 796);

        Assert.Empty(result.SafeRepairs);
        // No stop either — the PR just doesn't match any in-scope candidate.
        Assert.Empty(result.UnsafeStops);
    }

    private static PublishRecoveryCandidate NewLinkedIssueCandidate(
        string executionUnit,
        string linkedIssueRepo,
        int linkedIssueNumber,
        string? linkedIssueUrl) =>
        new()
        {
            ExecutionUnit = executionUnit,
            LinkedIssueRepo = linkedIssueRepo,
            LinkedIssueNumber = linkedIssueNumber,
            LinkedIssueUrl = linkedIssueUrl,
            LinkedPrUrl = null,
            PublishArtifact = null,
            PublishArtifactExpectedPath = $".intent-cli/issues/{executionUnit}/publish.yaml"
        };

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
