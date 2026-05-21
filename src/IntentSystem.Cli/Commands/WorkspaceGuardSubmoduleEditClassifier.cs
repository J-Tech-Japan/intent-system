namespace IntentSystem.Cli.Commands;

/// <summary>
/// G384: pure classifier that lets a host review loop converge when a
/// submodule has an INTERNAL uncommitted edit that is provably redundant
/// with the selected PR head, while still hard-stopping on unique or
/// unproven operator work. The pre-G384 behavior repeated the same
/// `dirty-unrelated-submodule` operator-cleanup stop every wake even when
/// the local edit was byte-identical to the change already pushed to the
/// PR head.
///
/// Safety invariants:
/// - Internal submodule edits are more dangerous than gitlink-only
///   changes, so they are only auto-clearable when the local diff
///   fingerprint MATCHES the selected PR head / remote fingerprint AND no
///   unique local content is present. Anything unique or unproven stays a
///   protected hard-stop — intent-cli never silently discards operator work.
/// - A failing required CI check is an implementation blocker that must
///   stay visible even when the workspace edit is redundant-safe; see
///   <see cref="ResolvePrimaryBlocker"/>.
/// - Repeated wakes over the same unchanged (path, diff, PR, PR head) are
///   deduplicated via <see cref="IsDuplicateReport"/> so the loop reports a
///   stable status instead of re-emitting a long operator stop each time.
///
/// No I/O, no git, no GitHub — the command layer supplies the fingerprints.
/// </summary>
internal static class WorkspaceGuardSubmoduleEditClassifier
{
    public static class Classifications
    {
        /// <summary>The dirty path is not an internal submodule edit; this lane does not apply.</summary>
        public const string NotInSubmoduleLane = "not-in-submodule-lane";

        /// <summary>Internal submodule edit whose diff is identical to the selected PR head — safe to clear.</summary>
        public const string RedundantInSubmoduleEdit = "redundant-in-submodule-edit";

        /// <summary>Internal submodule edit with unique or unproven content — hard-stop, never auto-cleared.</summary>
        public const string ProtectedOperatorWork = "protected-operator-work";
    }

    public static class Blockers
    {
        public const string ProtectedOperatorWork = "protected-operator-work";
        public const string RequiredCiFailing = "required-ci-failing";
        public const string None = "none";
    }

    /// <summary>
    /// Classify an internal-submodule dirty edit.
    /// </summary>
    /// <param name="isInternalSubmoduleEdit">The dirty path is an internal (working-tree) edit inside a submodule, not a gitlink-only change.</param>
    /// <param name="submodulePath">The dirty path (used in the dedupe fingerprint + repair command).</param>
    /// <param name="localDiffFingerprint">A fingerprint (e.g. blob hash) of the local working-tree edit.</param>
    /// <param name="prHeadFingerprint">The fingerprint of the same path on the selected PR head / remote commit.</param>
    /// <param name="hasUniqueLocalContent">True when the local edit contains content not present on the PR head — protects operator work.</param>
    /// <param name="selectedPr">The selected PR number (or null) — part of the dedupe key.</param>
    public static WorkspaceGuardSubmoduleEditDecision Classify(
        bool isInternalSubmoduleEdit,
        string submodulePath,
        string localDiffFingerprint,
        string prHeadFingerprint,
        bool hasUniqueLocalContent,
        int? selectedPr)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(submodulePath);
        var local = (localDiffFingerprint ?? string.Empty).Trim();
        var head = (prHeadFingerprint ?? string.Empty).Trim();
        var prText = selectedPr is { } pr ? pr.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none";
        var dedupeFingerprint = $"{submodulePath}|local={local}|pr={prText}|head={head}";

        if (!isInternalSubmoduleEdit)
        {
            return new WorkspaceGuardSubmoduleEditDecision
            {
                Classification = Classifications.NotInSubmoduleLane,
                SafeRepairAvailable = false,
                RecommendedRepair = null,
                DedupeFingerprint = dedupeFingerprint,
                Reason = "Not an internal submodule working-tree edit; this lane does not apply (use the existing gitlink / safe-stash handling).",
            };
        }

        // Unique local content is never auto-cleared — protect operator work.
        if (hasUniqueLocalContent)
        {
            return new WorkspaceGuardSubmoduleEditDecision
            {
                Classification = Classifications.ProtectedOperatorWork,
                SafeRepairAvailable = false,
                RecommendedRepair = null,
                DedupeFingerprint = dedupeFingerprint,
                Reason = "Internal submodule edit has unique local content; refuse auto-repair and surface a protected-operator-work blocker (intent-cli never silently discards operator work).",
            };
        }

        // Redundant only when the local fingerprint provably matches the PR
        // head fingerprint (both non-empty). Anything else is unproven →
        // hard-stop.
        if (local.Length > 0 && head.Length > 0
            && string.Equals(local, head, StringComparison.Ordinal))
        {
            return new WorkspaceGuardSubmoduleEditDecision
            {
                Classification = Classifications.RedundantInSubmoduleEdit,
                SafeRepairAvailable = true,
                RecommendedRepair = $"git -C {submodulePath} checkout -- . (clears the redundant edit; it is byte-identical to the selected PR head, so no unique content is lost)",
                DedupeFingerprint = dedupeFingerprint,
                Reason = "Internal submodule edit is byte-identical to the selected PR head (matching diff fingerprints) with no unique local content; it is redundant/stale and can be cleared safely so the wake proceeds.",
            };
        }

        return new WorkspaceGuardSubmoduleEditDecision
        {
            Classification = Classifications.ProtectedOperatorWork,
            SafeRepairAvailable = false,
            RecommendedRepair = null,
            DedupeFingerprint = dedupeFingerprint,
            Reason = "Internal submodule edit redundancy is unproven (missing or non-matching fingerprints); refuse auto-repair and keep the protected-operator-work hard-stop until redundancy is proven against the PR head.",
        };
    }

    /// <summary>
    /// G384: deduplicate repeated wake reports — true when the prior and
    /// current dedupe fingerprints are identical (same path, local diff,
    /// selected PR, and PR head), so diagnostics can emit a stable status
    /// instead of re-emitting a long operator stop each wake.
    /// </summary>
    public static bool IsDuplicateReport(string? priorDedupeFingerprint, string currentDedupeFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDedupeFingerprint);
        return !string.IsNullOrWhiteSpace(priorDedupeFingerprint)
            && string.Equals(priorDedupeFingerprint.Trim(), currentDedupeFingerprint, StringComparison.Ordinal);
    }

    /// <summary>
    /// G384: a failing required CI check must remain visible as the
    /// implementation blocker even when the workspace edit is
    /// redundant-safe. When the workspace is a protected hard-stop, that is
    /// the primary blocker (operator must resolve it first), but the CI
    /// status is still reported separately. Otherwise (redundant-safe or
    /// not-in-lane) a failing required CI is the primary, implementation-
    /// actionable blocker; with no CI failure the wake may proceed.
    /// </summary>
    public static string ResolvePrimaryBlocker(bool requiredCiFailing, string workspaceClassification)
    {
        if (string.Equals(workspaceClassification, Classifications.ProtectedOperatorWork, StringComparison.Ordinal))
        {
            return Blockers.ProtectedOperatorWork;
        }

        return requiredCiFailing ? Blockers.RequiredCiFailing : Blockers.None;
    }
}

/// <summary>G384: the verdict from <see cref="WorkspaceGuardSubmoduleEditClassifier.Classify"/>.</summary>
internal sealed record WorkspaceGuardSubmoduleEditDecision
{
    public required string Classification { get; init; }
    public required bool SafeRepairAvailable { get; init; }
    public string? RecommendedRepair { get; init; }
    public required string DedupeFingerprint { get; init; }
    public required string Reason { get; init; }
}
