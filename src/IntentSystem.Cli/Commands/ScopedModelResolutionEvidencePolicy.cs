namespace IntentSystem.Cli.Commands;

internal sealed record ScopedModelResolutionBaselineSelection(
    bool Resolved,
    string Reason,
    ModelResolutionLedgerEntry? Entry,
    int? AppendIndex);

internal sealed record ScopedModelResolutionCandidateSafety(
    bool Permitted,
    ModelResolutionLedgerEntry? BlockingLegacyRefusal,
    ModelResolutionLedgerEntry? BlockingScopedRefusal);

internal sealed record ScopedModelResolutionQueryCandidateSafety(
    bool Permitted,
    string? Reason,
    ScopedModelResolutionCandidateSafety BaselineInvocation,
    ScopedModelResolutionCandidateSafety? OptionalCandidate);

/// <summary>
/// Pure ordering and refusal rules for scoped model-resolution evidence.
/// It does not read or write the ledger and never observes a provider process.
/// </summary>
internal static class ScopedModelResolutionEvidencePolicy
{
    public static ScopedModelResolutionBaselineSelection SelectNewestBaseline(
        IReadOnlyList<ModelResolutionLedgerEntry> entries,
        ScopedModelResolutionScopeKey key)
    {
        var matching = entries
            .Select((entry, index) => (Entry: entry, Index: index))
            .Where(item => item.Entry.Outcome == ModelResolutionLedgerCommand.VerifiedOutcome
                && key.Matches(item.Entry))
            .OrderBy(item => item.Entry.RecordedAt)
            .ThenBy(item => item.Index)
            .ToArray();
        if (matching.Length == 0)
            return new ScopedModelResolutionBaselineSelection(false, "missing-scoped-baseline", null, null);

        // Choose first. Completeness, host, generation and digest are comparison
        // evidence, never selectors that permit falling back to an older row.
        var selected = matching[^1];
        if (key.RequestForm == "informal"
            && HasConflictingLatestInformalMappings(matching, selected.Entry.RecordedAt))
            return new ScopedModelResolutionBaselineSelection(false, "mapping-conflict", selected.Entry, selected.Index);

        return new ScopedModelResolutionBaselineSelection(true, "baseline-selected", selected.Entry, selected.Index);
    }

    public static ScopedModelResolutionCandidateSafety EvaluateCandidateSafety(
        IReadOnlyList<ModelResolutionLedgerEntry> entries,
        ScopedModelResolutionScopeKey key,
        string invocation)
    {
        var ordered = entries.Select((entry, index) => (Entry: entry, Index: index))
            .Where(item => item.Entry.Outcome is ModelResolutionLedgerCommand.VerifiedOutcome
                or ModelResolutionLedgerCommand.RefusedOutcome)
            .OrderBy(item => item.Entry.RecordedAt)
            .ThenBy(item => item.Index)
            .ToArray();

        var legacy = ordered.Where(item => item.Entry.ScopeVersion is null
            && string.Equals(item.Entry.Kind, key.Kind, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Entry.Invocation, invocation, StringComparison.Ordinal))
            .ToArray();
        var scoped = ordered.Where(item => key.Matches(item.Entry)
            && string.Equals(item.Entry.Invocation, invocation, StringComparison.Ordinal))
            .ToArray();

        var legacyBlocking = FindBlockingRefusal(legacy);
        var scopedBlocking = FindBlockingRefusal(scoped);
        return new ScopedModelResolutionCandidateSafety(
            legacyBlocking is null && scopedBlocking is null,
            legacyBlocking,
            scopedBlocking);
    }

    public static ScopedModelResolutionQueryCandidateSafety EvaluateQueryCandidates(
        IReadOnlyList<ModelResolutionLedgerEntry> entries,
        ScopedModelResolutionScopeKey key,
        string baselineInvocation,
        string? optionalCandidate)
    {
        var baseline = EvaluateCandidateSafety(entries, key, baselineInvocation);
        var candidate = optionalCandidate is null
            ? null
            : EvaluateCandidateSafety(entries, key, optionalCandidate);
        var permitted = baseline.Permitted && (candidate?.Permitted ?? true);
        return new ScopedModelResolutionQueryCandidateSafety(
            permitted,
            permitted ? null : "refused-invocation",
            baseline,
            candidate);
    }

    private static bool HasConflictingLatestInformalMappings(
        IReadOnlyList<(ModelResolutionLedgerEntry Entry, int Index)> ordered,
        DateTimeOffset newestAt)
    {
        var mappings = new HashSet<(string Model, string Effort)>();
        foreach (var item in ordered.Where(item => item.Entry.RecordedAt == newestAt))
        {
            var model = item.Entry.ObservedModel;
            var effort = item.Entry.ObservedEffort;
            if (!string.IsNullOrWhiteSpace(model) && !string.IsNullOrWhiteSpace(effort))
                mappings.Add((model, effort));
        }
        return mappings.Count > 1;
    }

    private static ModelResolutionLedgerEntry? FindBlockingRefusal(
        IReadOnlyList<(ModelResolutionLedgerEntry Entry, int Index)> ordered)
    {
        if (ordered.Count == 0)
            return null;
        var newestAt = ordered[^1].Entry.RecordedAt;
        var newestRows = ordered.Where(item => item.Entry.RecordedAt == newestAt).ToArray();
        // A refusal at the newest timestamp wins regardless of append order;
        // only a strictly later applicable verified row clears it.
        return newestRows.LastOrDefault(item => item.Entry.Outcome == ModelResolutionLedgerCommand.RefusedOutcome).Entry;
    }
}
