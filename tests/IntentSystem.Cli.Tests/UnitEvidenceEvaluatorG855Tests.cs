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
    public void ApplicabilityReadFailureAndInvalidRequestExitNonzero()
    {
        var modeFailure = UnitEvidenceEvaluator.Evaluate(Snapshot([], UnitStatusStates.Unavailable,
            cause: "team-mode-unrecorded", unavailableClass: UnitStatusStates.ApplicabilityUnresolved));
        Assert.Equal(1, modeFailure.Summary.ObservationExitCode);
        Assert.Equal(1, modeFailure.Summary.UnavailableClassCounts[UnitStatusStates.ApplicabilityUnresolved]);

        var requestFailure = UnitEvidenceEvaluator.Evaluate(Snapshot([], UnitStatusStates.Unavailable,
            cause: "invalid-request", unavailableClass: UnitStatusStates.InvalidRequest));
        Assert.Equal(1, requestFailure.Summary.ObservationExitCode);
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
