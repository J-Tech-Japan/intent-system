namespace IntentSystem.Cli.Commands;

/// <summary>Pure composition of already-materialized lifecycle evidence.</summary>
internal static class UnitEvidenceEvaluator
{
    private static readonly (string Step, string[] Facts)[] PhaseFacts =
    [
        ("claim-design", ["design-claim-acquired"]),
        ("bug-chain-or-ruling", ["bug-chain-or-ruling"]),
        ("packet-draft-and-validate", ["packet-current-files", "packet-current-validation", "guide-declaration"]),
        ("seed-and-publish", ["queue-seed", "publication-artifact", "issue-published-run"]),
        ("switch-to-implementation", ["design-claim-release", "implementation-claim-acquired"]),
        ("implement-and-open-pr", ["issue-completion-marker", "host-pr-linkage", "worker-completion-receipt"]),
        ("independent-subagent-review", ["recorded-review", "posted-review"]),
        ("fix-and-delta-review", ["delta-review"]),
        ("exact-head-ci-and-merge", ["observed-ci", "approved-marker", "approval-head-receipt", "pr-merged"]),
        ("closeout-and-release", ["pr-merged-run", "closeout-recorded-run", "architect-knowledge-writeback", "orchestrator-knowledge-writeback", "guide-reachability", "implementation-claim-release"]),
    ];

    public static UnitStatusReport Evaluate(UnitStatusEvidenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var supplied = snapshot.Facts.ToDictionary(fact => fact.Id, StringComparer.Ordinal);
        var steps = PhaseFacts.Select(phase => BuildStep(phase.Step, phase.Facts, supplied,
            snapshot.ApplicabilityState, snapshot.ApplicabilityCause, snapshot.ApplicabilityDetail, snapshot.UnavailableClass)).ToArray();
        var allFacts = steps.SelectMany(step => step.Subchecks).ToArray();
        var counts = StateNames.ToDictionary(state => state, state => allFacts.Count(fact => fact.State == state), StringComparer.Ordinal);
        var unavailableClasses = allFacts
            .Where(fact => fact.State == UnitStatusStates.Unavailable && !string.IsNullOrWhiteSpace(fact.UnavailableClass))
            .GroupBy(fact => fact.UnavailableClass!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var exitCode = snapshot.ApplicabilityState == UnitStatusStates.Unavailable
            && snapshot.UnavailableClass != UnitStatusStates.ProvenanceLimit
                ? 1
                : allFacts.Any(fact => fact.State == UnitStatusStates.Unavailable
                    && fact.UnavailableClass != UnitStatusStates.ProvenanceLimit)
                    ? 1
                    : 0;
        return new UnitStatusReport
        {
            ExecutionUnit = snapshot.ExecutionUnit,
            Domain = snapshot.Domain,
            Team = snapshot.Team,
            TeamMode = snapshot.TeamMode,
            ModeBasis = snapshot.ModeBasis,
            Identity = new UnitStatusIdentity
            {
                Repo = snapshot.Repo,
                Issue = snapshot.Issue,
                Pr = snapshot.Pr,
                HeadSha = snapshot.HeadSha,
                Sources = snapshot.IdentitySources,
            },
            Applicability = new UnitStatusApplicability
            {
                State = snapshot.ApplicabilityState ?? UnitStatusStates.Done,
                Cause = snapshot.ApplicabilityCause,
                Detail = snapshot.ApplicabilityDetail,
                UnavailableClass = snapshot.ApplicabilityState == UnitStatusStates.Unavailable
                    ? snapshot.UnavailableClass
                    : null,
                ModeSource = snapshot.ModeBasis,
                ModeEntryPath = snapshot.ModeEntryPath,
                ModeEntryUpdatedAt = snapshot.ModeEntryUpdatedAt,
            },
            Observation = new UnitStatusObservation
            {
                ObservedAt = snapshot.ObservedAt,
                LocalHeadSha = snapshot.LocalHeadSha,
                LocalHeadRef = snapshot.LocalHeadRef,
                ClaimMetadataRef = snapshot.ClaimMetadataRef,
                ClaimMetadataOid = snapshot.ClaimMetadataOid,
                LocalSources = snapshot.LocalSources,
                GitHubSnapshot = new UnitStatusGitHubSnapshot
                {
                    State = snapshot.GitHubSnapshotState ?? "not-observed",
                    Cause = snapshot.GitHubSnapshotCause,
                    Detail = snapshot.GitHubSnapshotDetail,
                    HeadBefore = snapshot.GitHubHeadBefore,
                    HeadAfter = snapshot.GitHubHeadAfter,
                    IssueLabels = snapshot.IssueLabels,
                    PullRequestLabels = snapshot.PullRequestLabels,
                    Checks = snapshot.Checks,
                    Reviews = snapshot.Reviews,
                    Merged = snapshot.PullRequestMerged,
                    MergeCommitSha = snapshot.MergeCommitSha,
                },
                Warnings = snapshot.Warnings,
            },
            Steps = steps,
            Summary = new UnitStatusSummary
            {
                StateCounts = counts,
                UnavailableClassCounts = unavailableClasses,
                ObservationExitCode = exitCode,
            },
        };
    }

    internal static bool TryOrderReviewRows(
        IEnumerable<UnitStatusObservedReview> reviews,
        out IReadOnlyList<UnitStatusObservedReview> ordered,
        out string detail)
    {
        var rows = reviews.ToArray();
        var ambiguous = rows
            .GroupBy(review => (
                review.Source,
                Head: review.HeadSha.ToUpperInvariant(),
                Reviewer: review.Reviewer?.ToUpperInvariant() ?? "",
                Runtime: review.Runtime?.ToUpperInvariant() ?? "",
                Relation: review.Relation?.ToUpperInvariant() ?? ""))
            .SelectMany(group => group.GroupBy(review => (review.At, review.RecordId)))
            .Any(tied => tied.Select(review => (review.Verdict, review.ReviewState, review.Dismissed)).Distinct().Skip(1).Any());

        ordered = rows
            .OrderBy(review => review.Source, StringComparer.Ordinal)
            .ThenBy(review => review.HeadSha, StringComparer.OrdinalIgnoreCase)
            .ThenBy(review => review.Reviewer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(review => review.Runtime, StringComparer.OrdinalIgnoreCase)
            .ThenBy(review => review.Relation, StringComparer.OrdinalIgnoreCase)
            .ThenBy(review => review.At)
            .ThenBy(review => review.RecordId, StringComparer.Ordinal)
            .ToArray();
        detail = ambiguous
            ? "Review rows with the same source, head, reviewer/runtime relation, submission time, and record ID contain conflicting dispositions."
            : "";
        return !ambiguous;
    }

    private static UnitStatusStep BuildStep(
        string id,
        IReadOnlyList<string> factIds,
        IReadOnlyDictionary<string, UnitStatusFact> supplied,
        string? applicabilityState,
        string? applicabilityCause,
        string? applicabilityDetail,
        string? unavailableClass)
    {
        var facts = factIds.Select(factId => supplied.TryGetValue(factId, out var fact)
            ? applicabilityState == UnitStatusStates.Unavailable
                ? IsObservedClaimSnapshotFailure(fact)
                    ? NormalizeFact(fact)
                    : NormalizeFact(fact with
                    {
                        State = UnitStatusStates.Unavailable,
                        Cause = applicabilityCause ?? "applicability-unresolved",
                        Detail = applicabilityDetail ?? "Observation stopped before this evidence was read.",
                        UnavailableClass = unavailableClass ?? UnitStatusStates.ApplicabilityUnresolved,
                    })
                : NormalizeFact(fact)
            : applicabilityState == UnitStatusStates.Unavailable
                ? NormalizeFact(new UnitStatusFact
                {
                    Id = factId,
                    State = UnitStatusStates.Unavailable,
                    Cause = applicabilityCause ?? "applicability-unresolved",
                    Detail = applicabilityDetail ?? "Observation stopped before this evidence was read.",
                    UnavailableClass = unavailableClass ?? UnitStatusStates.ApplicabilityUnresolved,
                })
                : new UnitStatusFact
            {
                Id = factId,
                State = UnitStatusStates.Missing,
                Cause = "evidence-not-observed",
                Detail = "No observation for this subcheck was supplied.",
            }).ToArray();
        var aggregate = Aggregate(facts);
        return new UnitStatusStep
        {
            Id = id,
            State = aggregate.State,
            Cause = aggregate.Fact?.Cause,
            Detail = aggregate.Fact?.Detail,
            Subchecks = facts,
        };
    }

    private static bool IsObservedClaimSnapshotFailure(UnitStatusFact fact) =>
        fact.Id is "design-claim-acquired" or "design-claim-release" or "implementation-claim-acquired" or "implementation-claim-release"
        && fact.State == UnitStatusStates.Unavailable;

    private static UnitStatusFact NormalizeFact(UnitStatusFact fact)
    {
        if (fact.State != UnitStatusStates.Unavailable) return fact;

        var provenanceLimit = fact.UnavailableClass == UnitStatusStates.ProvenanceLimit;
        if (!provenanceLimit && fact.RepairCommands.Count > 0) return fact;
        if (!provenanceLimit && !string.IsNullOrWhiteSpace(fact.RepairUnavailableReason)) return fact;

        return fact with
        {
            RepairCommands = provenanceLimit ? [] : fact.RepairCommands,
            RepairUnavailableReason = string.IsNullOrWhiteSpace(fact.RepairUnavailableReason)
                ? "no-supported-repair-command-in-this-slice"
                : fact.RepairUnavailableReason,
        };
    }

    private static (string State, UnitStatusFact? Fact) Aggregate(IReadOnlyList<UnitStatusFact> facts)
    {
        var unavailable = facts.FirstOrDefault(fact => fact.State == UnitStatusStates.Unavailable);
        if (unavailable is not null) return (UnitStatusStates.Unavailable, unavailable);
        var missing = facts.FirstOrDefault(fact => fact.State == UnitStatusStates.Missing);
        if (missing is not null) return (UnitStatusStates.Missing, missing);
        var applicable = facts.FirstOrDefault(fact => fact.State != UnitStatusStates.NotApplicable);
        return applicable is null
            ? (UnitStatusStates.NotApplicable, facts.FirstOrDefault())
            : (UnitStatusStates.Done, applicable);
    }

    private static readonly string[] StateNames =
    [
        UnitStatusStates.Done,
        UnitStatusStates.Missing,
        UnitStatusStates.NotApplicable,
        UnitStatusStates.Unavailable,
    ];
}
