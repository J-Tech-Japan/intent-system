namespace IntentSystem.Cli.Commands;

/// <summary>
/// G390: pure classifier that distinguishes a high-confidence, writeable
/// same-repo metadata-branch <c>linked_pr</c> linkage repair from advisory-only
/// or unsafe states. In same-repo topology the host metadata lives on a
/// dedicated branch (e.g. <c>intent-metadata</c>) while implementation PRs
/// target a different base (e.g. <c>develop-v2</c>). Recovery must read/write
/// the metadata branch state and validate PR facts against the implementation
/// branch; if a recovery surface looks at the wrong state it can miss
/// deterministic evidence and stop a green, reviewable PR (the observed AIC
/// PR #3639 case).
///
/// The classifier turns the recovery facts into one of:
/// <list type="bullet">
/// <item><description><c>same-repo-metadata-linkage-repair-ready</c> — writeable: the selected PR is in the target repo, uniquely closes the published/linked issue, the execution unit is identified, and only <c>linked_pr</c> is missing.</description></item>
/// <item><description><c>wrong-branch-unsafe</c> — recovery would operate against the implementation branch instead of the configured metadata branch; refuse.</description></item>
/// <item><description><c>advisory-only</c> — same-repo metadata recovery applies but the evidence is ambiguous (multiple candidate PRs / issues / units) or incomplete; no deterministic write.</description></item>
/// <item><description><c>not-applicable</c> — not same-repo metadata topology (the existing single-root recovery lanes apply).</description></item>
/// </list>
///
/// No I/O — the command layer supplies the facts (read from config, queue /
/// publish state, and GitHub), so every branch is unit-testable.
/// </summary>
internal static class SameRepoMetadataLinkageDiagnostics
{
    public static class Classifications
    {
        public const string RepairReady = "same-repo-metadata-linkage-repair-ready";
        public const string WrongBranchUnsafe = "wrong-branch-unsafe";
        public const string AdvisoryOnly = "advisory-only";
        public const string NotApplicable = "not-applicable";
    }

    /// <summary>
    /// Classify a same-repo metadata-branch linkage recovery.
    /// </summary>
    /// <param name="sameRepoMetadataBranchConfigured">Host config declares same-repo topology with a metadata branch distinct from the implementation base.</param>
    /// <param name="recoveryTargetsImplementationBranch">The recovery surface would read/write the implementation branch state instead of the metadata branch (a wrong-branch hazard).</param>
    /// <param name="selectedPrInTargetRepo">The selected PR belongs to the analyzer's target repo.</param>
    /// <param name="prUniquelyClosesLinkedIssue">Exactly one open PR closes the published / linked source issue.</param>
    /// <param name="executionUnitIdentified">A queue item or publish artifact identifies the execution unit.</param>
    /// <param name="onlyLinkedPrMissing">Only <c>linked_pr</c> is absent; <c>linked_issue</c> / publish identity are present.</param>
    public static SameRepoMetadataLinkageResult Classify(
        bool sameRepoMetadataBranchConfigured,
        bool recoveryTargetsImplementationBranch,
        bool selectedPrInTargetRepo,
        bool prUniquelyClosesLinkedIssue,
        bool executionUnitIdentified,
        bool onlyLinkedPrMissing)
    {
        if (!sameRepoMetadataBranchConfigured)
        {
            return new SameRepoMetadataLinkageResult
            {
                Classification = Classifications.NotApplicable,
                Writeable = false,
                RecommendedCommand = string.Empty,
                Diagnostic = "not same-repo metadata topology; the existing single-root publish-recovery / reconcile lanes apply.",
            };
        }

        // A wrong-branch recovery target is unsafe regardless of evidence —
        // reading/writing the implementation branch would miss or corrupt the
        // metadata-branch state.
        if (recoveryTargetsImplementationBranch)
        {
            return new SameRepoMetadataLinkageResult
            {
                Classification = Classifications.WrongBranchUnsafe,
                Writeable = false,
                RecommendedCommand = string.Empty,
                Diagnostic = "recovery target is the implementation branch, not the configured metadata branch; "
                    + "refuse and re-point recovery at the metadata branch/root before retrying.",
            };
        }

        var highConfidence = selectedPrInTargetRepo
            && prUniquelyClosesLinkedIssue
            && executionUnitIdentified
            && onlyLinkedPrMissing;
        if (highConfidence)
        {
            return new SameRepoMetadataLinkageResult
            {
                Classification = Classifications.RepairReady,
                Writeable = true,
                RecommendedCommand =
                    "intent-cli automation publish-recovery --repo <owner/repo> --pr <pr> --write "
                    + "(records linked_pr on the metadata branch, then retry `intent-cli review closeout-plan`).",
                Diagnostic = "selected PR is in the target repo, uniquely closes the linked issue, the execution unit "
                    + "is identified, and only linked_pr is missing — a high-confidence writeable metadata-branch repair.",
            };
        }

        return new SameRepoMetadataLinkageResult
        {
            Classification = Classifications.AdvisoryOnly,
            Writeable = false,
            RecommendedCommand = string.Empty,
            Diagnostic = "same-repo metadata recovery applies but the evidence is ambiguous or incomplete "
                + "(PR/issue/unit match is not unique, or more than linked_pr is missing); no deterministic write — advisory only.",
        };
    }
}

/// <summary>G390: the verdict from <see cref="SameRepoMetadataLinkageDiagnostics.Classify"/>.</summary>
internal sealed record SameRepoMetadataLinkageResult
{
    public required string Classification { get; init; }

    /// <summary>True only for the high-confidence repair-ready case.</summary>
    public required bool Writeable { get; init; }

    /// <summary>The recommended <c>--write</c> command (set only when writeable).</summary>
    public required string RecommendedCommand { get; init; }

    public required string Diagnostic { get; init; }
}
