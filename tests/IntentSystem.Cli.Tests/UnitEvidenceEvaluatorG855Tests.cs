using System.Text.Json;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class UnitEvidenceEvaluatorG855Tests
{
    private static readonly string[] FactIds =
    [
        "design-claim-acquired", "bug-chain-or-ruling", "packet-current-files", "packet-current-validation", "guide-declaration",
        "queue-seed", "publication-artifact", "issue-published-run", "design-claim-release", "implementation-claim-acquired",
        "issue-completion-marker", "host-pr-linkage", "worker-completion-receipt", "recorded-review", "posted-review",
        "delta-review", "observed-ci", "approved-marker", "approval-head-receipt", "pr-merged", "pr-merged-run",
        "closeout-recorded-run", "architect-knowledge-writeback", "orchestrator-knowledge-writeback", "guide-reachability",
        "implementation-claim-release",
    ];

    [Fact]
    public void EvaluatorExposesTenFixedPhasesAndAllFourStatesInCounts()
    {
        var report = UnitEvidenceEvaluator.Evaluate(Snapshot(FactIds.Select(id => Fact(id, UnitStatusStates.Done)).ToArray()));

        Assert.Equal(
            ["claim-design", "bug-chain-or-ruling", "packet-draft-and-validate", "seed-and-publish", "switch-to-implementation",
             "implement-and-open-pr", "independent-subagent-review", "fix-and-delta-review", "exact-head-ci-and-merge", "closeout-and-release"],
            report.Steps.Select(step => step.Id));
        Assert.Equal(10, report.Steps.Count);
        Assert.Equal(26, report.Summary.StateCounts[UnitStatusStates.Done]);
        Assert.Equal(0, report.Summary.StateCounts[UnitStatusStates.Missing]);
        Assert.Equal(0, report.Summary.StateCounts[UnitStatusStates.NotApplicable]);
        Assert.Equal(0, report.Summary.StateCounts[UnitStatusStates.Unavailable]);
        Assert.Equal(0, report.Summary.ObservationExitCode);
    }

    [Fact]
    public void AggregationUsesUnavailableThenMissingThenDoneAndAllNotApplicable()
    {
        var facts = FactIds.Select(id => id switch
        {
            "packet-current-validation" => Fact(id, UnitStatusStates.Missing, cause: "packet-gap"),
            "guide-declaration" => Fact(id, UnitStatusStates.Unavailable, unavailableClass: UnitStatusStates.ReadFailure),
            _ => Fact(id, UnitStatusStates.Done),
        }).ToList();
        var report = UnitEvidenceEvaluator.Evaluate(Snapshot(facts));

        Assert.Equal(UnitStatusStates.Unavailable, report.Steps.Single(step => step.Id == "packet-draft-and-validate").State);
        Assert.Equal("packet-current-validation", report.Steps.Single(step => step.Id == "packet-draft-and-validate").Subchecks[1].Id);
        Assert.Equal("guide-declaration", report.Steps.Single(step => step.Id == "packet-draft-and-validate").Subchecks[2].Id);
        Assert.Equal(1, report.Summary.UnavailableClassCounts[UnitStatusStates.ReadFailure]);
        Assert.Equal(1, report.Summary.ObservationExitCode);

        var notApplicable = UnitEvidenceEvaluator.Evaluate(Snapshot(
            FactIds.Select(id => Fact(id, UnitStatusStates.NotApplicable)).ToArray(),
            applicability: UnitStatusStates.NotApplicable,
            cause: "recorded-non-solo-mode"));
        Assert.All(notApplicable.Steps, step => Assert.Equal(UnitStatusStates.NotApplicable, step.State));
        Assert.Equal(26, notApplicable.Summary.StateCounts[UnitStatusStates.NotApplicable]);
        Assert.Equal(0, notApplicable.Summary.ObservationExitCode);
    }

    [Fact]
    public void ProvenanceLimitRemainsUnavailableButDoesNotRequestRetryOrReapproval()
    {
        var provenanceFact = Fact("approval-head-receipt", UnitStatusStates.Unavailable,
            cause: "approval-head-receipt-not-recorded", unavailableClass: UnitStatusStates.ProvenanceLimit,
            repairCommands: ["intent-cli review approve --write"]);
        var report = UnitEvidenceEvaluator.Evaluate(Snapshot([provenanceFact]));
        var result = report.Steps.SelectMany(step => step.Subchecks).Single(fact => fact.Id == "approval-head-receipt");

        Assert.Equal(UnitStatusStates.Unavailable, result.State);
        Assert.Empty(result.RepairCommands);
        Assert.Equal("no-supported-repair-command-in-this-slice", result.RepairUnavailableReason);
        Assert.Equal(1, report.Summary.UnavailableClassCounts[UnitStatusStates.ProvenanceLimit]);
        Assert.Equal(0, report.Summary.ObservationExitCode);
    }

    [Fact]
    public void UnavailableFactsWithoutRepairCommandsReceiveReasonAndProvenanceCommandsAreRemoved()
    {
        var report = UnitEvidenceEvaluator.Evaluate(Snapshot(
        [
            Fact("worker-completion-receipt", UnitStatusStates.Unavailable,
                cause: "worker-completion-receipt-not-recorded", unavailableClass: UnitStatusStates.ProvenanceLimit),
            Fact("posted-review", UnitStatusStates.Unavailable,
                cause: "github-read-failed", unavailableClass: UnitStatusStates.ReadFailure) with
                { RepairUnavailableReason = "source read failed" },
            Fact("delta-review", UnitStatusStates.Unavailable,
                cause: "delta-review-not-supported", unavailableClass: UnitStatusStates.ProvenanceLimit,
                repairCommands: ["intent-cli review cross-runtime record --write"]) with
                { RepairUnavailableReason = "review provenance is not writable here" },
            Fact("queue-seed", UnitStatusStates.Unavailable,
                cause: "queue-read-failed", unavailableClass: UnitStatusStates.ReadFailure) with
                { RepairUnavailableReason = "   " },
            Fact("publication-artifact", UnitStatusStates.Done,
                repairCommands: ["intent-cli issue publish"]) with { RepairUnavailableReason = "done-reason" },
            Fact("issue-published-run", UnitStatusStates.Missing,
                repairCommands: ["intent-cli issue publish"]) with { RepairUnavailableReason = "missing-reason" },
        ]));
        var facts = report.Steps.SelectMany(step => step.Subchecks).ToDictionary(fact => fact.Id, StringComparer.Ordinal);

        Assert.Equal("no-supported-repair-command-in-this-slice", facts["worker-completion-receipt"].RepairUnavailableReason);
        Assert.Equal("source read failed", facts["posted-review"].RepairUnavailableReason);
        Assert.Equal("review provenance is not writable here", facts["delta-review"].RepairUnavailableReason);
        Assert.Equal("no-supported-repair-command-in-this-slice", facts["queue-seed"].RepairUnavailableReason);
        Assert.Empty(facts["worker-completion-receipt"].RepairCommands);
        Assert.Empty(facts["delta-review"].RepairCommands);
        Assert.Equal("done-reason", facts["publication-artifact"].RepairUnavailableReason);
        Assert.Equal("missing-reason", facts["issue-published-run"].RepairUnavailableReason);
        Assert.Equal("done", facts["publication-artifact"].State);
        Assert.Equal("missing", facts["issue-published-run"].State);
        Assert.Equal(1, report.Summary.ObservationExitCode);
    }

    [Fact]
    public void MixedProvenanceAndReadFailureKeepsBothClassesAndExitsNonzero()
    {
        var report = UnitEvidenceEvaluator.Evaluate(Snapshot(
        [
            Fact("worker-completion-receipt", UnitStatusStates.Unavailable,
                cause: "worker-completion-receipt-not-recorded", unavailableClass: UnitStatusStates.ProvenanceLimit),
            Fact("posted-review", UnitStatusStates.Unavailable,
                cause: "github-read-failed", unavailableClass: UnitStatusStates.ReadFailure),
        ]));

        Assert.Equal(1, report.Summary.ObservationExitCode);
        Assert.Equal(2, report.Summary.StateCounts[UnitStatusStates.Unavailable]);
        Assert.Equal(1, report.Summary.UnavailableClassCounts[UnitStatusStates.ProvenanceLimit]);
        Assert.Equal(1, report.Summary.UnavailableClassCounts[UnitStatusStates.ReadFailure]);
    }

    [Fact]
    public void ApplicabilityReadFailureAndInvalidRequestExitNonzero()
    {
        var modeFailure = UnitEvidenceEvaluator.Evaluate(Snapshot([], UnitStatusStates.Unavailable,
            cause: "team-mode-unrecorded", unavailableClass: UnitStatusStates.ApplicabilityUnresolved));
        Assert.Equal(1, modeFailure.Summary.ObservationExitCode);
        Assert.Equal(26, modeFailure.Summary.StateCounts[UnitStatusStates.Unavailable]);
        Assert.Equal(26, modeFailure.Summary.UnavailableClassCounts[UnitStatusStates.ApplicabilityUnresolved]);
        var modeFacts = modeFailure.Steps.SelectMany(step => step.Subchecks).ToArray();
        Assert.Equal(26, modeFacts.Length);
        Assert.All(modeFacts.Where(fact => fact.RepairCommands.Count == 0), fact =>
            Assert.False(string.IsNullOrWhiteSpace(fact.RepairUnavailableReason)));

        var requestFailure = UnitEvidenceEvaluator.Evaluate(Snapshot([], UnitStatusStates.Unavailable,
            cause: "invalid-request", unavailableClass: UnitStatusStates.InvalidRequest));
        Assert.Equal(1, requestFailure.Summary.ObservationExitCode);
        Assert.All(requestFailure.Steps.SelectMany(step => step.Subchecks).Where(fact => fact.RepairCommands.Count == 0), fact =>
            Assert.False(string.IsNullOrWhiteSpace(fact.RepairUnavailableReason)));
    }

    [Fact]
    public void ApplicabilityRefusalPreservesObservedClaimFailureAndReclassifiesOtherFacts()
    {
        var pointer = new UnitStatusEvidencePointer
        {
            Kind = "local-claim-snapshot",
            Path = ".intent-cli/claims/active.json",
            ExecutionUnit = "G855",
            Provenance = "configured local claim snapshot",
        };
        var report = UnitEvidenceEvaluator.Evaluate(Snapshot(
        [
            Fact("design-claim-acquired", UnitStatusStates.Unavailable,
                cause: "local-claim-record-unreadable", unavailableClass: UnitStatusStates.ReadFailure) with { Evidence = [pointer] },
            Fact("worker-completion-receipt", UnitStatusStates.Unavailable,
                cause: "worker-completion-receipt-not-recorded", unavailableClass: UnitStatusStates.ProvenanceLimit),
        ], UnitStatusStates.Unavailable,
            cause: "claim-snapshot-unavailable", unavailableClass: UnitStatusStates.ReadFailure));

        var facts = report.Steps.SelectMany(step => step.Subchecks).ToArray();
        Assert.All(facts.Where(fact => fact.Id == "design-claim-acquired"), fact =>
        {
            Assert.Equal("unavailable", fact.State);
            Assert.Equal("local-claim-record-unreadable", fact.Cause);
            Assert.Equal("read-failure", fact.UnavailableClass);
        });
        Assert.All(facts.Where(fact => fact.Id != "design-claim-acquired"), fact =>
        {
            Assert.Equal("unavailable", fact.State);
            Assert.Equal("claim-snapshot-unavailable", fact.Cause);
            Assert.Equal("read-failure", fact.UnavailableClass);
        });
        Assert.Same(pointer, Assert.Single(facts.Single(fact => fact.Id == "design-claim-acquired").Evidence));
    }

    [Fact]
    public void JsonAndMarkdownRenderTheSamePhaseAndSummaryFacts()
    {
        var report = UnitEvidenceEvaluator.Evaluate(Snapshot(FactIds.Select(id => Fact(id, UnitStatusStates.Missing)).ToArray()));
        using var json = new StringWriter();
        using var markdown = new StringWriter();
        UnitStatusRenderer.Write(json, report, "json");
        UnitStatusRenderer.Write(markdown, report, "markdown");

        using var document = JsonDocument.Parse(json.ToString());
        Assert.Equal("1", document.RootElement.GetProperty("schema_version").GetString());
        Assert.Equal(10, document.RootElement.GetProperty("steps").GetArrayLength());
        Assert.Equal(26, document.RootElement.GetProperty("summary").GetProperty("state_counts").GetProperty("missing").GetInt32());
        foreach (var step in report.Steps)
        {
            Assert.Contains($"### {step.Id}: {step.State}", markdown.ToString(), StringComparison.Ordinal);
            foreach (var fact in step.Subchecks)
                Assert.Contains($"subcheck `{fact.Id}`", markdown.ToString(), StringComparison.Ordinal);
        }
        Assert.Contains("observation_exit_code: 0", markdown.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("ready_to_merge", json.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceDetailFieldsAreRetainedInJsonAndMarkdown()
    {
        var epoch = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        var pointer = new UnitStatusEvidencePointer
        {
            Kind = "claim-record",
            Path = ".intent-cli/claims/example.json",
            RecordId = "takeover",
            ExecutionUnit = "G855",
            ClaimEpochClaimedAt = epoch,
            ClaimOperation = "takeover",
            ClaimDisposition = "takeover",
            ClaimActor = "builder",
            ClaimTeam = "intent-cli-dev",
            DisplacedClaimedAt = DateTimeOffset.Parse("2026-09-30T00:00:00Z"),
            ReviewVerdict = "request-changes",
            ReviewState = "COMMENTED",
            ReviewDisposition = "implementation-review",
            Provenance = "fixture",
        };
        var input = Snapshot([Fact("design-claim-acquired", UnitStatusStates.Done) with { Evidence = [pointer] }]) with
        {
            Reviews =
            [
                new UnitStatusObservedReview
                {
                    Source = "github-pr-review", HeadSha = new string('a', 40), Verdict = "request-changes",
                    ReviewState = "COMMENTED", RecordId = "review-901", Qualification = "implementation-review",
                },
            ],
            Checks =
            [
                new UnitStatusObservedCheck
                {
                    Source = "check-run", Identity = "github-actions/build", RecordId = "check-901",
                    Sha = new string('a', 40), Status = "completed", Conclusion = "success", RunId = 901,
                    Attempt = 1, ReportedRunAttempt = 1, AttemptBasis = "actions-run-initial-attempt",
                },
            ],
        };
        var report = UnitEvidenceEvaluator.Evaluate(input);
        using var json = new StringWriter();
        using var markdown = new StringWriter();
        UnitStatusRenderer.Write(json, report, "json");
        UnitStatusRenderer.Write(markdown, report, "markdown");

        using var parsed = JsonDocument.Parse(json.ToString());
        var evidence = parsed.RootElement.GetProperty("steps").EnumerateArray()
            .SelectMany(step => step.GetProperty("subchecks").EnumerateArray())
            .Single(fact => fact.GetProperty("id").GetString() == "design-claim-acquired")
            .GetProperty("evidence").EnumerateArray().Single();
        Assert.Equal("builder", evidence.GetProperty("claim_actor").GetString());
        Assert.Equal("intent-cli-dev", evidence.GetProperty("claim_team").GetString());
        Assert.Equal("takeover", evidence.GetProperty("claim_operation").GetString());
        Assert.Equal("COMMENTED", evidence.GetProperty("review_state").GetString());
        Assert.Equal("request-changes", evidence.GetProperty("review_verdict").GetString());
        Assert.Equal("implementation-review", evidence.GetProperty("review_disposition").GetString());
        Assert.Contains($"claim_epoch_claimed_at=`{epoch:O}`", markdown.ToString(), StringComparison.Ordinal);
        Assert.Contains("claim_disposition=`takeover`", markdown.ToString(), StringComparison.Ordinal);
        Assert.Contains("claim_actor=`builder`", markdown.ToString(), StringComparison.Ordinal);
        Assert.Contains("claim_team=`intent-cli-dev`", markdown.ToString(), StringComparison.Ordinal);
        Assert.Contains("review_verdict=`request-changes`", markdown.ToString(), StringComparison.Ordinal);
        Assert.Contains("review_state=`COMMENTED`", markdown.ToString(), StringComparison.Ordinal);
        Assert.Contains("review_disposition=`implementation-review`", markdown.ToString(), StringComparison.Ordinal);
        Assert.Contains("record_id=`check-901`", markdown.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewOrderingRetainsRowsAndRejectsOnlySameIdentityTies()
    {
        var at = DateTimeOffset.Parse("2026-10-07T00:00:00Z");
        UnitStatusObservedReview Review(string runtime, string verdict, DateTimeOffset time, string id) => new()
        {
            Source = "local-cross-runtime-record",
            HeadSha = new string('a', 40),
            Verdict = verdict,
            Runtime = runtime,
            Relation = "cross-runtime",
            At = time,
            RecordId = id,
        };

        var tied = new[]
        {
            Review("claude", "request-changes", at, "same-local-record-id"),
            Review("claude", "approve", at, "same-local-record-id"),
        };
        Assert.False(UnitEvidenceEvaluator.TryOrderReviewRows(tied, out var tiedRows, out var conflict));
        Assert.Equal(2, tiedRows.Count);
        Assert.Contains("conflicting dispositions", conflict, StringComparison.Ordinal);

        var ordered = new[]
        {
            Review("claude", "request-changes", at, "record-a"),
            Review("claude", "approve", at.AddMinutes(1), "record-b"),
        };
        Assert.True(UnitEvidenceEvaluator.TryOrderReviewRows(ordered, out var orderedRows, out _));
        Assert.Equal(["request-changes", "approve"], orderedRows.Select(review => review.Verdict));

        var distinctRuntime = new[]
        {
            Review("claude", "request-changes", at, "same-id"),
            Review("codex", "approve", at, "same-id"),
        };
        Assert.True(UnitEvidenceEvaluator.TryOrderReviewRows(distinctRuntime, out var distinctRows, out _));
        Assert.Equal(2, distinctRows.Count);
    }

    private static UnitStatusEvidenceSnapshot Snapshot(
        IReadOnlyList<UnitStatusFact> facts,
        string? applicability = UnitStatusStates.Done,
        string? cause = null,
        string? unavailableClass = null) => new()
    {
        ExecutionUnit = "G855",
        ObservedAt = DateTimeOffset.Parse("2026-10-07T00:00:00Z"),
        Facts = facts,
        ApplicabilityState = applicability,
        ApplicabilityCause = cause,
        UnavailableClass = unavailableClass,
    };

    private static UnitStatusFact Fact(
        string id,
        string state,
        string? cause = null,
        string? unavailableClass = null,
        IReadOnlyList<string>? repairCommands = null) => new()
    {
        Id = id,
        State = state,
        Cause = cause,
        UnavailableClass = unavailableClass,
        RepairCommands = repairCommands ?? [],
    };
}
