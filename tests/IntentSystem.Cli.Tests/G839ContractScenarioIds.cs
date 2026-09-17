namespace IntentSystem.Cli.Tests;

/// <summary>
/// G839: mechanical scenario-id sets derived from the standalone child issue contract
/// (section 4 byte identity and AC7/AC9/AC10 coverage). Harness arrays must equal these sets.
/// </summary>
internal static class G839ContractScenarioIds
{
    internal static readonly string[] PrTransitionNonRefusalVariants =
    [
        "review-start",
        "request-update",
        "review-release",
        "approved-ungated",
        "approved-undeclared",
        "approved-satisfied",
    ];

    internal static readonly string[] PrTransitionFailureVariants =
    [
        "failure-may-have-applied",
        "failure-known-unapplied",
    ];

    internal static readonly string[] PrTransitionRefusalTextScenarios =
    [
        "head-required",
        "head-stale",
        "head-unreadable",
        "team-unresolved",
        "missing",
        "blocked",
        "rereview-missing",
        "record-unreadable",
    ];

    internal static readonly string[] RecoveryDryRunRefusalCauses =
    [
        "runs-log-unreadable",
        "issue-unavailable",
        "issue-not-open",
        "target-absent",
        "queue-item-missing",
        "queue-item-ambiguous",
        "unit-mismatch",
        "repo-mismatch",
        "pr-linkage-missing",
        "pr-state-unavailable",
        "pr-open",
        "pr-merged",
        "open-closing-pr",
        "open-pr-list-unavailable",
        "claim-held",
        "claim-unavailable",
        "in-progress-present",
        "host-state-missing",
        "started-ambiguous",
    ];

    internal static readonly string[] RecoveryWriteRefusalCauses = ["label-changed"];

    internal static readonly string[] RecoveryWriteFailureCauses =
    [
        "recovered-event-append-failed",
        "label-readback-unconfirmed",
        "label-readback-failed",
        "label-removal-failed",
        "started-event-append-failed",
        "superseded-event-append-failed",
    ];

    internal static string[] BuildPrTransitionNonRefusalScenarioIds()
    {
        var ids = new List<string> { "pr-transition-help", "pr-transition-parse-error" };

        foreach (var variant in PrTransitionNonRefusalVariants)
        {
            foreach (var mode in new[] { "dry-run", "write" })
            {
                foreach (var format in new[] { "json", "text" })
                {
                    ids.Add($"pr-transition-{variant}-{mode}-{format}");
                }
            }
        }

        foreach (var failure in PrTransitionFailureVariants)
        {
            foreach (var format in new[] { "json", "text" })
            {
                ids.Add($"pr-transition-{failure}-write-{format}");
            }
        }

        return ids.ToArray();
    }

    internal static string[] BuildPrTransitionRefusalTextScenarioIds() =>
        PrTransitionRefusalTextScenarios
            .Select(scenario => $"pr-transition-refusal-{scenario}-text")
            .ToArray();

    internal static string[] BuildRecoveryScenarioIds()
    {
        var ids = new List<string>
        {
            "recovery-help",
            "recovery-parse-error",
            "recovery-ruling-missing-markdown",
            "recovery-proceed-dry-run-json",
            "recovery-proceed-dry-run-markdown",
            "recovery-already-recovered-json",
            "recovery-already-recovered-markdown",
            "recovery-recovered-write-json",
            "recovery-recovered-write-markdown",
            "recovery-event-completed-write-json",
            "recovery-event-completed-write-markdown",
            "recovery-recovery-completed-write-json",
            "recovery-recovery-completed-write-markdown",
            "recovery-closed-then-label-removed-write-json",
            "recovery-closed-then-label-removed-write-markdown",
        };

        foreach (var cause in RecoveryDryRunRefusalCauses)
        {
            ids.Add($"recovery-{cause}-refusal-json");
            ids.Add($"recovery-{cause}-refusal-markdown");
        }

        foreach (var cause in RecoveryWriteRefusalCauses)
        {
            ids.Add($"recovery-{cause}-refusal-write-json");
            ids.Add($"recovery-{cause}-refusal-write-markdown");
        }

        foreach (var cause in RecoveryWriteFailureCauses)
        {
            ids.Add($"recovery-{cause}-write-json");
            ids.Add($"recovery-{cause}-write-markdown");
        }

        return ids.ToArray();
    }
}
