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
                ? fact with
                {
                    State = UnitStatusStates.Unavailable,
                    Cause = applicabilityCause ?? "applicability-unresolved",
                    Detail = applicabilityDetail ?? "Observation stopped before this evidence was read.",
                    UnavailableClass = unavailableClass ?? UnitStatusStates.ApplicabilityUnresolved,
                }
                : NormalizeFact(fact)
            : applicabilityState == UnitStatusStates.Unavailable
                ? new UnitStatusFact
                {
                    Id = factId,
                    State = UnitStatusStates.Unavailable,
                    Cause = applicabilityCause ?? "applicability-unresolved",
                    Detail = applicabilityDetail ?? "Observation stopped before this evidence was read.",
                    UnavailableClass = unavailableClass ?? UnitStatusStates.ApplicabilityUnresolved,
                }
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

    private static UnitStatusFact NormalizeFact(UnitStatusFact fact)
    {
        if (fact.State != UnitStatusStates.Unavailable
            || fact.UnavailableClass != UnitStatusStates.ProvenanceLimit
            || fact.RepairCommands.Count == 0)
        {
            return fact;
        }

        return fact with
        {
            RepairCommands = [],
            RepairUnavailableReason = fact.RepairUnavailableReason ?? "no-supported-repair-command-in-this-slice",
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
