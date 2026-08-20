namespace IntentSystem.Cli.Commands;

/// <summary>
/// G211: Pure deterministic mapper for <c>intent-cli worker claim</c>.
/// Given the kind and the issue/PR's current label set, computes the
/// claim transition (add / remove) plus any stale-selection errors and
/// policy warnings.
///
/// Pure: no I/O, no Process.Start, no GitHub network, no provider
/// launch. The command layer reads current labels via the mutator
/// seam, calls this analyzer, and only then optionally applies the
/// transition (dry-run by default).
/// </summary>
internal static class WorkerClaimAnalyzer
{
    /// <summary>
    /// Plan the claim transition for a given target. Returns a
    /// <see cref="ClaimDecision"/> describing the proposed label edits;
    /// the caller decides whether to apply (write mode) or just emit
    /// (dry-run mode).
    /// </summary>
    public static ClaimDecision Analyze(
        string kind,
        IReadOnlyList<string> currentLabels,
        ClaimOwnershipVerification? claimVerification = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(currentLabels);

        if (string.Equals(kind, GhCliGitHubLabelMutator.Kinds.Issue, StringComparison.Ordinal))
        {
            return AnalyzeIssue(currentLabels, claimVerification);
        }
        if (string.Equals(kind, GhCliGitHubLabelMutator.Kinds.Pr, StringComparison.Ordinal))
        {
            return AnalyzePr(currentLabels);
        }

        // Unknown kind — surface as stale-selection refusal so the
        // command exits non-zero without mutating anything.
        return new ClaimDecision
        {
            Proceed = false,
            AddLabels = Array.Empty<string>(),
            RemoveLabels = Array.Empty<string>(),
            Errors = new[] { $"unrecognized kind '{kind}'." },
            Warnings = Array.Empty<string>(),
            Summary = $"Refusing to claim: unrecognized kind '{kind}'.",
        };
    }

    private static ClaimDecision AnalyzeIssue(
        IReadOnlyList<string> currentLabels,
        ClaimOwnershipVerification? claimVerification)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var hasTarget = currentLabels.Contains(
            WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal);
        var hasInProgress = currentLabels.Contains(
            WorkerNextActionConstants.Labels.IntentIssueInProgress, StringComparer.Ordinal);
        var hasPrCreated = currentLabels.Contains(
            WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal);
        var staleInProgress = hasInProgress && IsUnheldClaim(claimVerification);

        if (claimVerification is not null
            && IsActiveOrUnavailableClaim(claimVerification))
        {
            errors.Add(
                $"{WorkerClaimCompleteConstants.ErrorCodes.ClaimRegistryRefused}: {claimVerification.Detail} active or unavailable claim evidence remains an ownership stop.");
        }

        if (!hasTarget)
        {
            errors.Add(
                $"{WorkerClaimCompleteConstants.ErrorCodes.MissingTarget}: issue does not carry '{WorkerNextActionConstants.Labels.IntentTarget}'.");
        }
        if (hasInProgress && !staleInProgress)
        {
            errors.Add(
                $"{WorkerClaimCompleteConstants.ErrorCodes.AlreadyInProgress}: issue already carries '{WorkerNextActionConstants.Labels.IntentIssueInProgress}'.");
        }
        if (hasPrCreated)
        {
            errors.Add(
                $"{WorkerClaimCompleteConstants.ErrorCodes.AlreadyCompleted}: issue already carries '{WorkerNextActionConstants.Labels.IntentPrCreated}'.");
        }

        if (staleInProgress)
        {
            warnings.Add(
                $"claim registry is authoritative for {claimVerification!.Scope}; the '{WorkerNextActionConstants.Labels.IntentIssueInProgress}' label is stale shadow state and no label mutation is required.");
        }

        var proceed = errors.Count == 0;
        return new ClaimDecision
        {
            Proceed = proceed,
            AddLabels = proceed
                ? staleInProgress
                    ? Array.Empty<string>()
                    : new[] { WorkerNextActionConstants.Labels.IntentIssueInProgress }
                : Array.Empty<string>(),
            RemoveLabels = Array.Empty<string>(),
            Errors = errors,
            Warnings = warnings,
            Summary = proceed
                ? staleInProgress
                    ? $"Claim registry reports {claimVerification!.Scope} unheld; the existing '{WorkerNextActionConstants.Labels.IntentIssueInProgress}' label is stale shadow state, so worker claim proceeds without relabeling."
                    : $"Would add '{WorkerNextActionConstants.Labels.IntentIssueInProgress}' to claim the issue."
                : "Refusing to claim issue: stale or ineligible target.",
        };
    }

    private static ClaimDecision AnalyzePr(IReadOnlyList<string> currentLabels)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var hasTarget = currentLabels.Contains(
            WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal);
        var hasRequestUpdate = currentLabels.Contains(
            WorkerNextActionConstants.Labels.IntentPrRequestUpdate, StringComparer.Ordinal);
        var hasUpdateInProgress = currentLabels.Contains(
            WorkerNextActionConstants.Labels.IntentPrUpdateInProgress, StringComparer.Ordinal);
        var hasRereviewReady = currentLabels.Contains(
            WorkerNextActionConstants.Labels.IntentPrRereviewReady, StringComparer.Ordinal);
        var hasMisplacedPrCreated = currentLabels.Contains(
            WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal);

        if (!hasTarget)
        {
            errors.Add(
                $"{WorkerClaimCompleteConstants.ErrorCodes.MissingTarget}: PR does not carry '{WorkerNextActionConstants.Labels.IntentTarget}'.");
        }
        if (!hasRequestUpdate)
        {
            errors.Add(
                $"{WorkerClaimCompleteConstants.ErrorCodes.MissingRepairRequested}: PR does not carry '{WorkerNextActionConstants.Labels.IntentPrRequestUpdate}'.");
        }
        if (hasUpdateInProgress)
        {
            errors.Add(
                $"{WorkerClaimCompleteConstants.ErrorCodes.AlreadyInProgress}: PR already carries '{WorkerNextActionConstants.Labels.IntentPrUpdateInProgress}'.");
        }
        if (hasRereviewReady)
        {
            errors.Add(
                $"{WorkerClaimCompleteConstants.ErrorCodes.AlreadyRereviewReady}: PR already carries '{WorkerNextActionConstants.Labels.IntentPrRereviewReady}'.");
        }
        if (hasMisplacedPrCreated)
        {
            warnings.Add(
                $"label policy: PR carries misplaced '{WorkerNextActionConstants.Labels.IntentPrCreated}' which belongs on the source issue, not the PR.");
        }

        var proceed = errors.Count == 0;
        return new ClaimDecision
        {
            Proceed = proceed,
            AddLabels = proceed
                ? new[] { WorkerNextActionConstants.Labels.IntentPrUpdateInProgress }
                : Array.Empty<string>(),
            RemoveLabels = Array.Empty<string>(),
            Errors = errors,
            Warnings = warnings,
            Summary = proceed
                ? $"Would add '{WorkerNextActionConstants.Labels.IntentPrUpdateInProgress}' to claim the PR for repair."
                : "Refusing to claim PR: stale or ineligible target.",
        };
    }

    private static bool IsUnheldClaim(ClaimOwnershipVerification? claim) =>
        claim is not null
        && claim.StoreConfigured
        && (claim.Status == ClaimOwnershipVerification.StatusUnheld
            || claim.Status == ClaimOwnershipVerification.StatusUnheldAvailable);

    private static bool IsActiveOrUnavailableClaim(ClaimOwnershipVerification claim) =>
        claim.StoreConfigured
        && (claim.Status == ClaimOwnershipVerification.StatusOwned
            || claim.Status == ClaimOwnershipVerification.StatusHeldByOtherTeam
            || claim.Status == ClaimOwnershipVerification.StatusTeamRequired
            || claim.Status == ClaimOwnershipVerification.StatusCanonicalUnavailable
            || claim.Status == ClaimOwnershipVerification.StatusInvalid);

    /// <summary>
    /// G211: Pure data record returned by
    /// <see cref="WorkerClaimAnalyzer.Analyze"/>. The command layer
    /// projects this into <see cref="WorkerClaimResult"/>.
    /// </summary>
    internal sealed record ClaimDecision
    {
        public required bool Proceed { get; init; }
        public required IReadOnlyList<string> AddLabels { get; init; }
        public required IReadOnlyList<string> RemoveLabels { get; init; }
        public required IReadOnlyList<string> Errors { get; init; }
        public required IReadOnlyList<string> Warnings { get; init; }
        public required string Summary { get; init; }
    }
}
