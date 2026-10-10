using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>Pure approval-only policy over already parsed review rows.</summary>
internal static class SoloConductorApprovalGate
{
    public const string DecisionSatisfied = "satisfied";
    public const string DecisionRefused = "refused";

    public const string CauseReviewMissing = "review-missing";
    public const string CauseReviewBlocked = "review-blocked";
    public const string CauseInvalidEvidence = "review-invalid-evidence-unresolved";
    public const string CauseUnscopableEvidence = "review-unscopable-evidence";
    public const string CauseInventoryUnavailable = "review-read-unavailable";
    public const string CauseLocalGateMissing = "cross-runtime-review-missing";
    public const string CauseLocalGateBlocked = "cross-runtime-review-blocked";
    public const string CauseApplicabilityUnresolved = "review-applicability-unresolved";
    public const string CauseHeadRequired = "review-head-required";
    public const string CauseHeadChanged = "review-head-changed";

    public const string RepairUnavailableIdentity = "review-identity-unrecoverable-on-this-pr";

    private static readonly HashSet<string> SubmittedStates = new(StringComparer.Ordinal)
    {
        "COMMENTED",
        "APPROVED",
        "CHANGES_REQUESTED",
    };

    public static SoloConductorApprovalEvaluation Evaluate(
        IReadOnlyList<IndependentReviewRowEvidence> rows,
        string executionUnit,
        string expectedHeadSha,
        CrossRuntimeReviewGateResult? localGate)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (!TryDeduplicate(rows, out var uniqueRows, out var duplicateDetail))
        {
            return Refused(CauseInventoryUnavailable, duplicateDetail);
        }

        var inventoryFailure = uniqueRows.FirstOrDefault(row => row.InventoryFailureDetail is not null);
        if (inventoryFailure is not null)
        {
            return Refused(CauseInventoryUnavailable, inventoryFailure.InventoryFailureDetail!);
        }

        var ordered = uniqueRows
            .Where(row => row.IsNamedIndependent && !row.IsExplicitlyExcludedState && row.HasTrustedRestEnvelope)
            .OrderBy(row => row.SubmittedAt!.Value.ToUniversalTime())
            .ThenBy(row => row.ReviewId!.Value)
            .ToArray();

        var unscopable = uniqueRows
            .Where(row => row.IsNamedIndependent
                && !row.IsExplicitlyExcludedState
                && !row.HasTrustedRestEnvelope)
            .ToArray();
        var loginBlockers = new Dictionary<string, List<ReviewBlocker>>(StringComparer.OrdinalIgnoreCase);
        var runtimeBlockers = new Dictionary<string, List<ReviewBlocker>>(StringComparer.Ordinal);
        var currentApprovals = new List<SoloConductorQualifyingReview>();
        var supersededInvalidIds = new HashSet<long>();

        foreach (var row in ordered)
        {
            var body = row.BodyEvidence;
            if (body is null || !body.NamedIndependent)
            {
                continue;
            }

            var login = row.ReviewerLogin!;
            var state = row.State;
            if (!IsCoherentSubmittedEvidence(row, body))
            {
                var isLegacyProvenance = !body.IsStructured
                    && !body.Conflicting
                    && !body.ApprovalEvidenceConflicting
                    && !body.HasStructuredMetadata
                    && !body.HasCitationAssertion
                    && !body.ExplicitRequestChangesAssertion
                    && (state is "COMMENTED" or "APPROVED")
                    && row.Classification == IndependentReviewEvidenceKind.Provenance;
                if (!isLegacyProvenance)
                {
                    AddLoginBlocker(loginBlockers, login, Blocker(row, "invalid-evidence", login));
                }

                continue;
            }

            // A coherent foreign-unit or design review has proved its own
            // identity and attachment. It cannot block this implementation.
            if (!string.Equals(body.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                || !string.Equals(body.Kind, CrossRuntimeReviewRecord.KindImplementation, StringComparison.Ordinal))
            {
                continue;
            }

            var bodyHead = body.HeadSha!;
            var isCanonical = body.CitedRecord is not null
                && row.CitedRecordValidatedForExpected;
            var isBlocking = state == "CHANGES_REQUESTED"
                || string.Equals(body.Verdict, CrossRuntimeReviewVerdict.RequestChanges, StringComparison.Ordinal);
            if (isBlocking)
            {
                if (isCanonical)
                {
                    AddRuntimeBlocker(runtimeBlockers, body.Runtime!, Blocker(row, "request-changes", body.Runtime!));
                }
                else
                {
                    AddLoginBlocker(loginBlockers, login, Blocker(row, "request-changes", login));
                }

                continue;
            }

            if (state is not ("COMMENTED" or "APPROVED")
                || !string.Equals(body.Verdict, CrossRuntimeReviewVerdict.Approve, StringComparison.Ordinal)
                || !string.Equals(bodyHead, expectedHeadSha, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var approval = new SoloConductorQualifyingReview
            {
                ReviewId = row.ReviewId!.Value,
                Url = row.ReviewUrl,
                Login = login,
                Runtime = isCanonical ? body.Runtime : null,
                Relation = isCanonical ? body.Relation : null,
                CitedRecordPath = isCanonical ? row.ObserverReview?.CitedRecordPath : null,
            };
            currentApprovals.Add(approval);
            ClearLoginBlockers(loginBlockers, login, row, supersededInvalidIds);
            if (isCanonical)
            {
                ClearRuntimeBlockers(runtimeBlockers, body.Runtime!, row);
            }
        }

        if (unscopable.Length > 0)
        {
            var ids = unscopable.Select(row => (long?)row.ReviewId).ToArray();
            var detail = "A named independent submitted review is missing a trustworthy REST login, positive numeric review ID, or valid submitted_at timestamp; unrelated approvals cannot safely supersede it."
                + $" Unscopable review IDs: {FormatIds(ids)}.";
            return new SoloConductorApprovalEvaluation
            {
                Decision = DecisionRefused,
                Cause = CauseUnscopableEvidence,
                Detail = detail,
                QualifyingReviews = SortApprovals(currentApprovals),
                Obligations = [],
                UnscopableReviewIds = ids,
                SupersededInvalidReviewIds = SortIds(supersededInvalidIds),
                RepairUnavailableReason = RepairUnavailableIdentity,
            };
        }

        var obligations = BuildObligations(loginBlockers, runtimeBlockers);
        var activeInvalid = obligations.Any(item => item.Kind == "invalid-evidence");
        if (activeInvalid)
        {
            return Refused(
                CauseInvalidEvidence,
                "Trusted but invalid independent review evidence remains unresolved. " + FormatObligations(obligations),
                currentApprovals,
                obligations,
                supersededInvalidIds);
        }

        if (obligations.Count > 0)
        {
            return Refused(
                CauseReviewBlocked,
                "An independent implementation review blocks approval or requires the same identity to re-review the current head. " + FormatObligations(obligations),
                currentApprovals,
                obligations,
                supersededInvalidIds);
        }

        if (localGate is not null && localGate.Decision != CrossRuntimeReviewGate.DecisionSatisfied)
        {
            var cause = localGate.Decision == CrossRuntimeReviewGate.DecisionBlocked
                ? CauseLocalGateBlocked
                : CauseLocalGateMissing;
            var detail = localGate.Reasons.Count == 0
                ? "The existing G834 local review gate is not satisfied."
                : string.Join(" ", localGate.Reasons.Select(reason => $"[{reason.Cause}] {reason.Detail}"));
            return Refused(cause, detail, currentApprovals, obligations, supersededInvalidIds);
        }

        if (localGate is null)
        {
            if (currentApprovals.Count == 0)
            {
                return Refused(
                    CauseReviewMissing,
                    "No submitted, structured independent implementation approve is posted on the expected current head.",
                    currentApprovals,
                    obligations,
                    supersededInvalidIds);
            }
        }
        else
        {
            var missingRelations = MissingLocalRelations(localGate, currentApprovals);
            if (missingRelations.Count > 0)
            {
                return Refused(
                    CauseReviewMissing,
                    "The existing G834 gate is satisfied, but a currently deciding canonical posted approve is missing for relation(s): "
                        + string.Join(", ", missingRelations)
                        + ". Post the rendered canonical review body for the deciding local record.",
                    currentApprovals,
                    obligations,
                    supersededInvalidIds,
                    missingRelations);
            }
        }

        return new SoloConductorApprovalEvaluation
        {
            Decision = DecisionSatisfied,
            QualifyingReviews = SortApprovals(currentApprovals),
            Obligations = [],
            UnscopableReviewIds = [],
            SupersededInvalidReviewIds = SortIds(supersededInvalidIds),
        };
    }

    private static bool TryDeduplicate(
        IReadOnlyList<IndependentReviewRowEvidence> rows,
        out IReadOnlyList<IndependentReviewRowEvidence> unique,
        out string detail)
    {
        var byId = new Dictionary<long, IndependentReviewRowEvidence>();
        var output = new List<IndependentReviewRowEvidence>();
        foreach (var row in rows)
        {
            if (row.ReviewId is not > 0)
            {
                output.Add(row);
                continue;
            }

            if (!byId.TryGetValue(row.ReviewId.Value, out var existing))
            {
                byId.Add(row.ReviewId.Value, row);
                output.Add(row);
                continue;
            }

            if (!string.Equals(existing.RawPayload, row.RawPayload, StringComparison.Ordinal))
            {
                unique = [];
                detail = $"GitHub review ID {row.ReviewId.Value} was returned with contradictory payloads; the complete review inventory is unavailable.";
                return false;
            }
        }

        unique = output;
        detail = "";
        return true;
    }

    private static bool IsCoherentSubmittedEvidence(IndependentReviewRowEvidence row, UnitStatusReviewBodyParse body)
    {
        if (!row.StateFieldValid
            || !SubmittedStates.Contains(row.State ?? string.Empty)
            || !row.CommitIdFieldValid
            || !row.ReviewUrlFieldValid
            || !body.IsStructured
            || body.Conflicting
            || body.ApprovalEvidenceConflicting
            || body.Kind is not (CrossRuntimeReviewRecord.KindImplementation or CrossRuntimeReviewRecord.KindDesign))
        {
            return false;
        }

        var bodyHead = body.HeadSha;
        if (bodyHead is null
            || !CrossRuntimeReviewPaths.IsFullHeadSha(bodyHead)
            || !string.Equals(bodyHead, row.CommitId, StringComparison.OrdinalIgnoreCase)
            || body.Verdict is not (CrossRuntimeReviewVerdict.Approve or CrossRuntimeReviewVerdict.RequestChanges))
        {
            return false;
        }

        var relationValid = body.Relation is null
            || CrossRuntimeReviewRuntimes.IsSupported(body.Runtime)
            && CrossRuntimeReviewRuntimes.IsSupported(body.ConductorRuntime)
            && (body.Relation == CrossRuntimeReviewRecord.RelationSameRuntime
                ? string.Equals(body.Runtime, body.ConductorRuntime, StringComparison.Ordinal)
                : body.Relation == CrossRuntimeReviewRecord.RelationCrossRuntime
                && !string.Equals(body.Runtime, body.ConductorRuntime, StringComparison.Ordinal));
        var citationValid = body.Relation is null
            ? !body.HasCitationAssertion && body.CitedRecord is null
            : body.HasCitationAssertion
            && body.CitedRecord is not null
            && row.CitedRecordValidatedForDeclaredUnit;
        return relationValid && citationValid;
    }

    private static void AddLoginBlocker(
        IDictionary<string, List<ReviewBlocker>> blockers,
        string login,
        ReviewBlocker blocker)
    {
        if (!blockers.TryGetValue(login, out var list)) blockers[login] = list = [];
        list.Add(blocker);
    }

    private static void AddRuntimeBlocker(
        IDictionary<string, List<ReviewBlocker>> blockers,
        string runtime,
        ReviewBlocker blocker)
    {
        if (!blockers.TryGetValue(runtime, out var list)) blockers[runtime] = list = [];
        list.Add(blocker);
    }

    private static void ClearLoginBlockers(
        IDictionary<string, List<ReviewBlocker>> blockers,
        string login,
        IndependentReviewRowEvidence approval,
        ISet<long> supersededInvalidIds)
    {
        if (!blockers.TryGetValue(login, out var existing)) return;
        foreach (var blocker in existing.Where(blocker => IsStrictlyLater(approval, blocker)))
        {
            if (blocker.Kind == "invalid-evidence" && blocker.ReviewId is > 0) supersededInvalidIds.Add(blocker.ReviewId.Value);
        }
        blockers.Remove(login);
    }

    private static void ClearRuntimeBlockers(
        IDictionary<string, List<ReviewBlocker>> blockers,
        string runtime,
        IndependentReviewRowEvidence approval)
    {
        if (!blockers.TryGetValue(runtime, out var existing)) return;
        if (existing.All(blocker => IsStrictlyLater(approval, blocker))) blockers.Remove(runtime);
    }

    private static bool IsStrictlyLater(IndependentReviewRowEvidence current, ReviewBlocker prior)
    {
        var time = current.SubmittedAt!.Value.ToUniversalTime().CompareTo(prior.SubmittedAt.ToUniversalTime());
        return time > 0 || time == 0 && current.ReviewId!.Value > prior.ReviewIdOrder;
    }

    private static ReviewBlocker Blocker(IndependentReviewRowEvidence row, string kind, string identity) => new(
        kind,
        identity,
        row.ReviewId,
        row.ReviewUrl,
        row.SubmittedAt!.Value,
        row.ReviewId!.Value);

    private static IReadOnlyList<SoloConductorReviewObligation> BuildObligations(
        IReadOnlyDictionary<string, List<ReviewBlocker>> loginBlockers,
        IReadOnlyDictionary<string, List<ReviewBlocker>> runtimeBlockers)
    {
        var output = new List<SoloConductorReviewObligation>();
        foreach (var (login, blockers) in loginBlockers.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            output.Add(new SoloConductorReviewObligation
            {
                Kind = blockers.Any(blocker => blocker.Kind == "invalid-evidence") ? "invalid-evidence" : "request-changes",
                IdentityKind = "github-login",
                Identity = login,
                ReviewIds = blockers.Select(blocker => blocker.ReviewId).Distinct().Order().ToArray(),
                Recovery = $"GitHub login '{login}' must perform a fresh independent re-review on the current head and post a valid approve; retain the earlier review.",
            });
        }
        foreach (var (runtime, blockers) in runtimeBlockers.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            output.Add(new SoloConductorReviewObligation
            {
                Kind = "runtime-request-changes",
                IdentityKind = "runtime",
                Identity = runtime,
                ReviewIds = blockers.Select(blocker => blocker.ReviewId).Distinct().Order().ToArray(),
                Recovery = $"Runtime '{runtime}' must perform a fresh canonical review on the current head and post a valid approve; its local record must also decide as approve.",
            });
        }
        return output;
    }

    private static IReadOnlyList<string> MissingLocalRelations(
        CrossRuntimeReviewGateResult gate,
        IReadOnlyList<SoloConductorQualifyingReview> approvals)
    {
        var relationSlots = gate.Records
            .Where(entry => entry.Status == CrossRuntimeReviewGate.StatusDeciding
                && entry.Verdict == CrossRuntimeReviewVerdict.Approve
                && entry.Relation is CrossRuntimeReviewRecord.RelationSameRuntime or CrossRuntimeReviewRecord.RelationCrossRuntime)
            .GroupBy(entry => entry.Relation, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.File).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

        var missing = new List<string>();
        foreach (var relation in new[] { CrossRuntimeReviewRecord.RelationSameRuntime, CrossRuntimeReviewRecord.RelationCrossRuntime })
        {
            if (!relationSlots.TryGetValue(relation, out var files)
                || !approvals.Any(approval => approval.Relation == relation
                    && approval.CitedRecordPath is not null
                    && files.Contains(approval.CitedRecordPath)))
            {
                missing.Add(relation);
            }
        }
        return missing;
    }

    private static SoloConductorApprovalEvaluation Refused(
        string cause,
        string detail,
        IReadOnlyList<SoloConductorQualifyingReview>? approvals = null,
        IReadOnlyList<SoloConductorReviewObligation>? obligations = null,
        IEnumerable<long>? supersededInvalidIds = null,
        IReadOnlyList<string>? missingRelations = null) => new()
        {
            Decision = DecisionRefused,
            Cause = cause,
            Detail = detail,
            QualifyingReviews = SortApprovals(approvals ?? []),
            Obligations = obligations ?? [],
            UnscopableReviewIds = [],
            SupersededInvalidReviewIds = SortIds(supersededInvalidIds ?? []),
            MissingRelations = missingRelations,
        };

    private static IReadOnlyList<SoloConductorQualifyingReview> SortApprovals(IEnumerable<SoloConductorQualifyingReview> approvals) =>
        approvals.OrderBy(item => item.ReviewId).ToArray();

    private static IReadOnlyList<long> SortIds(IEnumerable<long> values) => values.Distinct().Order().ToArray();

    private static string FormatObligations(IReadOnlyList<SoloConductorReviewObligation> obligations) =>
        string.Join(" ", obligations.Select(item => $"{item.IdentityKind} '{item.Identity}' must repair review(s) {FormatIds(item.ReviewIds)}."));

    private static string FormatIds(IEnumerable<long?> ids) =>
        string.Join(", ", ids.Select(id => id?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"));

    private sealed record ReviewBlocker(
        string Kind,
        string Identity,
        long? ReviewId,
        string? Url,
        DateTimeOffset SubmittedAt,
        long ReviewIdOrder);
}

internal sealed record SoloConductorApprovalEvaluation
{
    public required string Decision { get; init; }
    public string? Cause { get; init; }
    public string? Detail { get; init; }
    public IReadOnlyList<SoloConductorQualifyingReview> QualifyingReviews { get; init; } = [];
    public IReadOnlyList<SoloConductorReviewObligation> Obligations { get; init; } = [];
    public IReadOnlyList<long?> UnscopableReviewIds { get; init; } = [];
    public IReadOnlyList<long> SupersededInvalidReviewIds { get; init; } = [];
    public string? RepairUnavailableReason { get; init; }

    /// <summary>
    /// Typed G861 evidence for the only approved-transition exception: local
    /// G834 is satisfied, but a deciding canonical posted approve is missing
    /// for one or both existing review relations. This remains private policy
    /// data and is never included in the ordinary G856 result wire format.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string>? MissingRelations { get; init; }
}

internal sealed record SoloConductorQualifyingReview
{
    [JsonPropertyName("review_id")]
    public required long ReviewId { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("login")]
    public required string Login { get; init; }

    [JsonPropertyName("runtime")]
    public string? Runtime { get; init; }

    [JsonPropertyName("relation")]
    public string? Relation { get; init; }

    [JsonPropertyName("cited_record_path")]
    public string? CitedRecordPath { get; init; }
}

internal sealed record SoloConductorReviewObligation
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("identity_kind")]
    public required string IdentityKind { get; init; }

    [JsonPropertyName("identity")]
    public required string Identity { get; init; }

    [JsonPropertyName("review_ids")]
    public IReadOnlyList<long?> ReviewIds { get; init; } = [];

    [JsonPropertyName("recovery")]
    public required string Recovery { get; init; }
}
