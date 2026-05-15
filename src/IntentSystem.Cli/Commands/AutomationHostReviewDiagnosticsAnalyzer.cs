using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G280: Pure analyzer for the host review/next-slice diagnostics surface.
/// Inspects open PRs and intent-target issues in the target repo and
/// classifies why the host loop did not advance, so an operator can tell true
/// idle apart from stale-CLI, stuck review label, missing target, conflicting
/// review-side labels, WIP-cap blockage, and clarification-required.
/// Read-only: never reads files, never mutates GitHub or local state, and
/// never launches an AI provider. The command layer wraps this with I/O and
/// the installed-CLI surface probe.
/// </summary>
internal static class AutomationHostReviewDiagnosticsAnalyzer
{
    private static readonly Regex ClosesIssueRegex = new(
        @"(?i)\b(?:close[sd]?|fix(?:es|ed)?|resolve[sd]?)\s+(?:(?<repo>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+))?#(?<number>\d+)\b",
        RegexOptions.Compiled);

    public static AutomationHostReviewDiagnosticsResult Analyze(
        string repo,
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs,
        IReadOnlyList<GitHubAutomationIssueCandidate> publishedIntentTargetIssues,
        bool clarificationRequired,
        string? candidateExecutionUnit,
        bool staleClarificationMetadata = false,
        IReadOnlyList<string>? reconcileUnsafeStopKinds = null,
        int reconcileHighConfidenceRepairsAvailable = 0,
        bool allowWipCapOverride = false,
        bool? prDraft = null,
        int publishRecoveryHighConfidenceRepairsAvailable = 0,
        bool workspaceSafeDirty = false,
        int closeoutDriftRepairsAvailable = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(openPrs);
        ArgumentNullException.ThrowIfNull(publishedIntentTargetIssues);

        // G286: surface stale clarification metadata as a non-fatal warning on
        // every classification so the host loop can re-stamp the file later
        // without flipping the terminal class.
        var warnings = staleClarificationMetadata
            ? new List<string> { "stale-clarification-metadata" }
            : new List<string>();

        var unsafeStopKinds = reconcileUnsafeStopKinds ?? Array.Empty<string>();

        var details = new List<AutomationHostReviewDiagnosticsDetail>();

        var publishedIssueNumbers = publishedIntentTargetIssues
            .Where(issue => LabelNames(issue.Labels).Contains(WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal))
            .Select(issue => issue.Number)
            .ToHashSet();

        AutomationHostReviewDiagnosticsDetail? requestUpdateConflictPr = null;
        AutomationHostReviewDiagnosticsDetail? stuckReviewingPr = null;
        AutomationHostReviewDiagnosticsDetail? missingTargetPr = null;
        AutomationHostReviewDiagnosticsDetail? actionableReviewPr = null;

        foreach (var pr in openPrs)
        {
            var labels = LabelNames(pr.Labels);
            var hasReviewing = labels.Contains(WorkerNextActionConstants.Labels.IntentPrReviewing, StringComparer.Ordinal);
            var hasRequestUpdate = labels.Contains(WorkerNextActionConstants.Labels.IntentPrRequestUpdate, StringComparer.Ordinal);
            var hasRereviewReady = labels.Contains(WorkerNextActionConstants.Labels.IntentPrRereviewReady, StringComparer.Ordinal);
            var hasUpdateInProgress = labels.Contains(WorkerNextActionConstants.Labels.IntentPrUpdateInProgress, StringComparer.Ordinal);
            var hasApproved = labels.Contains(WorkerNextActionConstants.Labels.IntentPrApproved, StringComparer.Ordinal);
            var hasIntentTarget = labels.Contains(WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal);

            // Conflict: both request-update and rereview-ready (or update-in-progress + rereview-ready)
            if (hasRequestUpdate && hasRereviewReady && requestUpdateConflictPr is null)
            {
                requestUpdateConflictPr = new AutomationHostReviewDiagnosticsDetail
                {
                    Kind = AutomationHostReviewDiagnosticsClassifications.RequestUpdateRereviewConflict,
                    TargetKind = GhCliGitHubLabelMutator.Kinds.Pr,
                    TargetNumber = pr.Number,
                    TargetUrl = pr.Url,
                    Description = $"PR #{pr.Number} carries both 'intent-pr-request-update' and 'intent-pr-rereview-ready'; the workflow cannot pick a transition until one is removed.",
                };
            }

            // Stuck reviewing: intent-pr-reviewing without an exit transition label
            if (hasReviewing && !hasRequestUpdate && !hasUpdateInProgress && !hasRereviewReady && !hasApproved && stuckReviewingPr is null)
            {
                stuckReviewingPr = new AutomationHostReviewDiagnosticsDetail
                {
                    Kind = AutomationHostReviewDiagnosticsClassifications.StuckReviewing,
                    TargetKind = GhCliGitHubLabelMutator.Kinds.Pr,
                    TargetNumber = pr.Number,
                    TargetUrl = pr.Url,
                    Description = $"PR #{pr.Number} carries 'intent-pr-reviewing' with no exit-transition label. The previous review session may have crashed before completing approve/request-update.",
                };
            }

            // Missing target on a PR that closes a published intent-target issue
            if (!hasIntentTarget)
            {
                var linkedNumbers = ExtractLinkedIssueNumbers(repo, pr);
                var closedPublished = linkedNumbers.FirstOrDefault(publishedIssueNumbers.Contains);
                if (closedPublished > 0 && missingTargetPr is null)
                {
                    missingTargetPr = new AutomationHostReviewDiagnosticsDetail
                    {
                        Kind = AutomationHostReviewDiagnosticsClassifications.MissingTargetOnPr,
                        TargetKind = GhCliGitHubLabelMutator.Kinds.Pr,
                        TargetNumber = pr.Number,
                        TargetUrl = pr.Url,
                        Description = $"PR #{pr.Number} closes published intent-target issue #{closedPublished} but lacks 'intent-target'; host review preflight will skip it.",
                    };
                }
            }

            // Actionable review PR: intent-target without blocking labels
            if (hasIntentTarget && !hasRequestUpdate && !hasUpdateInProgress && !hasApproved && actionableReviewPr is null)
            {
                actionableReviewPr = new AutomationHostReviewDiagnosticsDetail
                {
                    Kind = AutomationHostReviewDiagnosticsClassifications.ReviewPrActionable,
                    TargetKind = GhCliGitHubLabelMutator.Kinds.Pr,
                    TargetNumber = pr.Number,
                    TargetUrl = pr.Url,
                    Description = $"PR #{pr.Number} carries 'intent-target' with no blocking review-side label; host review preflight should pick it up.",
                };
            }
        }

        // G297: draft-merge-blocked. When the host loop tells us the
        // selected (or actionable) review PR is draft, GitHub will reject
        // the merge with "Pull Request is still a draft". We must surface
        // this as a deterministic terminal class so the host loop drops the
        // review lease and stops short of approval/closeout/next-slice
        // publish. This precedes the other PR-state classifications because
        // a draft PR cannot be approved regardless of its review-side
        // labels.
        if (prDraft == true)
        {
            // Prefer the actionable-review PR; fall back to any
            // intent-pr-reviewing PR; finally any open PR (so the
            // diagnostic can still classify when no PR carries
            // intent-target labels yet — e.g. before review-start).
            var draftPr = actionableReviewPr
                ?? stuckReviewingPr
                ?? FindFirstDraftCandidate(openPrs);

            details.Add(new AutomationHostReviewDiagnosticsDetail
            {
                Kind = AutomationHostReviewDiagnosticsClassifications.DraftMergeBlocked,
                TargetKind = draftPr?.TargetKind ?? GhCliGitHubLabelMutator.Kinds.Pr,
                TargetNumber = draftPr?.TargetNumber,
                TargetUrl = draftPr?.TargetUrl,
                Description = draftPr is null
                    ? $"--pr-draft true was passed; no open PR could be matched to a target. Stop the host loop until the operator names the draft PR."
                    : $"PR #{draftPr.TargetNumber} is still draft; host approval/merge/closeout must NOT proceed (G297). Drop the review lease via pr-transition --transition review-release and surface the gap.",
            });
            return Build(
                repo,
                AutomationHostReviewDiagnosticsClassifications.DraftMergeBlocked,
                draftPr is null
                    ? "Selected review PR is reported as draft (G297). Host approval/merge/closeout cannot proceed; release the review lease and surface the gap to the implementer/operator."
                    : $"PR #{draftPr.TargetNumber} is still draft (G297). Host approval/merge/closeout cannot proceed; release the review lease and surface the gap to the implementer/operator.",
                recommendedNextCommand: draftPr?.TargetNumber is { } number
                    ? $"intent-cli automation pr-transition --transition review-release --repo {repo} --pr {number} --write --format json"
                    : null,
                clarification: null,
                details,
                warnings);
        }

        // Precedence: stale-host-cli is handled by the command layer because it
        // depends on the surface probe; here we cover the analyzer-side cases
        // in the order the operator should care about.
        if (requestUpdateConflictPr is not null)
        {
            details.Add(requestUpdateConflictPr);
            return Build(
                repo,
                AutomationHostReviewDiagnosticsClassifications.RequestUpdateRereviewConflict,
                $"PR #{requestUpdateConflictPr.TargetNumber} carries conflicting review-side labels (request-update + rereview-ready). Resolve before the host loop can pick a transition.",
                recommendedNextCommand: null,
                clarification: new AutomationHostReviewDiagnosticsClarification
                {
                    Background = $"PR #{requestUpdateConflictPr.TargetNumber} carries both 'intent-pr-request-update' and 'intent-pr-rereview-ready', so the host workflow has no canonical next transition.",
                    Question = "Which state is the operator's intent for this PR?",
                    Options =
                    [
                        $"intent-pr-rereview-ready (treat the previous repair as ready for re-review): operator removes intent-pr-request-update, then the host loop runs review-start.",
                        $"intent-pr-request-update (treat the previous repair as still incomplete): operator removes intent-pr-rereview-ready and leaves request-update; child loop will re-claim with worker claim --kind pr."
                    ],
                },
                details,
                warnings);
        }

        // G286: reconcile-reported unsafe stops dominate every other terminal
        // classification (except request-update-rereview-conflict, which is a
        // PR-label conflict the operator must resolve first). The host loop
        // must not guess past ambiguous metadata.
        if (unsafeStopKinds.Count > 0)
        {
            details.Add(new AutomationHostReviewDiagnosticsDetail
            {
                Kind = AutomationHostReviewDiagnosticsClassifications.UnsafeMetadata,
                TargetKind = null,
                TargetNumber = null,
                TargetUrl = null,
                Description = $"reconcile reported unsafe_stops: {string.Join(", ", unsafeStopKinds)}",
            });
            return Build(
                repo,
                AutomationHostReviewDiagnosticsClassifications.UnsafeMetadata,
                $"Reconcile surfaced {unsafeStopKinds.Count} unsafe stop(s) ({string.Join(", ", unsafeStopKinds)}); the host loop must stop with structured clarification rather than guess past ambiguous parent state.",
                recommendedNextCommand: $"intent-cli automation reconcile --lane host-review --repo {repo} --format json",
                clarification: null,
                details,
                warnings);
        }

        // G355: workspace-safe-dirty is a deterministic repair when host-sync-preflight
        // returns dirty-unrelated-submodule (G352). The workspace-guard stash lane can
        // stash the unrelated paths and the wake proceeds cleanly. Surface this before
        // the review/WIP checks so the host loop knows to apply the workspace-guard
        // repair rather than aborting the wake.
        if (workspaceSafeDirty)
        {
            details.Add(new AutomationHostReviewDiagnosticsDetail
            {
                Kind = SafeRepairCategories.WorkspaceSafeDirty,
                TargetKind = null,
                TargetNumber = null,
                TargetUrl = null,
                Description = "host-sync-preflight reported dirty-unrelated-submodule; workspace-guard stash lane is the deterministic repair.",
            });
            return Build(
                repo,
                AutomationHostReviewDiagnosticsClassifications.RepairedAndRetry,
                "Host working tree has an unrelated dirty submodule or safe dirty path. Run `automation workspace-guard --mode begin --write` to stash it before the wake body, then `--mode end --write` after the push lands.",
                recommendedNextCommand: $"intent-cli automation workspace-guard --mode begin --write --format json",
                clarification: null,
                details,
                warnings,
                safeRepairCategory: SafeRepairCategories.WorkspaceSafeDirty);
        }

        if (stuckReviewingPr is not null)
        {
            details.Add(stuckReviewingPr);
            return Build(
                repo,
                AutomationHostReviewDiagnosticsClassifications.StuckReviewing,
                $"PR #{stuckReviewingPr.TargetNumber} has a stale review lease (intent-pr-reviewing with no active review). Release the lease with `pr-transition --transition review-release` and retry review selection on the next wake.",
                recommendedNextCommand: $"intent-cli automation pr-transition --transition review-release --repo {repo} --pr {stuckReviewingPr.TargetNumber} --write --format json",
                clarification: null,
                details,
                warnings,
                safeRepairCategory: SafeRepairCategories.StaleReviewLease);
        }

        if (missingTargetPr is not null)
        {
            details.Add(missingTargetPr);
            return Build(
                repo,
                AutomationHostReviewDiagnosticsClassifications.MissingTargetOnPr,
                $"PR #{missingTargetPr.TargetNumber} links a published intent-target issue but lacks 'intent-target'; host review preflight will skip it until reconcile applies the label.",
                recommendedNextCommand: $"intent-cli automation reconcile --lane host-review --repo {repo} --write --format json",
                clarification: null,
                details,
                warnings);
        }

        if (clarificationRequired)
        {
            return Build(
                repo,
                AutomationHostReviewDiagnosticsClassifications.ClarificationRequired,
                "Host review preflight was told a clarification is required; resolve the source clarification before resuming the host loop.",
                recommendedNextCommand: null,
                clarification: null,
                details,
                warnings);
        }

        // G289: defensively exclude closed issues / merged-or-closed PRs from
        // WIP, even when callers (e.g. fakes in tests, or future paths that
        // bypass `--state open`) pass them through. A historically-labeled
        // closed issue must NOT keep the host loop wip-cap-blocked.
        var inFlightIssues = publishedIntentTargetIssues
            .Where(issue => IsOpenState(issue.State)
                && LabelNames(issue.Labels).Contains(WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal))
            .Select(issue => issue.Number)
            .OrderBy(n => n)
            .ToArray();
        var inFlightPrs = openPrs
            .Where(pr => IsOpenState(pr.State)
                && LabelNames(pr.Labels).Contains(WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal))
            .Select(pr => pr.Number)
            .OrderBy(n => n)
            .ToArray();

        if (actionableReviewPr is not null)
        {
            details.Add(actionableReviewPr);
            return Build(
                repo,
                AutomationHostReviewDiagnosticsClassifications.ReviewPrActionable,
                $"PR #{actionableReviewPr.TargetNumber} is eligible for host review; preflight should not have returned no-actionable.",
                recommendedNextCommand: $"intent-cli automation host-review-preflight --repo {repo} --format json",
                clarification: null,
                details,
                warnings);
        }

        if (inFlightPrs.Length > 0 || inFlightIssues.Length > 0)
        {
            // G288: when the operator explicitly opts in (--allow-wip-cap-override)
            // AND a complete candidate is present, route to issue-publish-ready
            // and surface a `wip-cap-overridden` warning (plus a detail listing
            // the in-flight items that were bypassed) so the override is
            // auditable. Without the flag the default WIP cap is unchanged.
            if (allowWipCapOverride && !string.IsNullOrWhiteSpace(candidateExecutionUnit))
            {
                warnings.Add("wip-cap-overridden");
                details.Add(new AutomationHostReviewDiagnosticsDetail
                {
                    Kind = AutomationHostReviewDiagnosticsClassifications.WipCapBlocked,
                    TargetKind = null,
                    TargetNumber = null,
                    TargetUrl = null,
                    Description = $"WIP cap bypassed by --allow-wip-cap-override; in-flight intent-target items at override time: PRs={string.Join(",", inFlightPrs)} issues={string.Join(",", inFlightIssues)}",
                });
                details.Add(new AutomationHostReviewDiagnosticsDetail
                {
                    Kind = AutomationHostReviewDiagnosticsClassifications.IssuePublishReady,
                    TargetKind = null,
                    TargetNumber = null,
                    TargetUrl = null,
                    Description = $"next-slice candidate provided: {candidateExecutionUnit}",
                });
                return Build(
                    repo,
                    AutomationHostReviewDiagnosticsClassifications.IssuePublishReady,
                    $"WIP cap bypassed by operator override; next-slice candidate {candidateExecutionUnit} is ready to publish exactly one prepared issue.",
                    recommendedNextCommand:
                        $"intent-cli packet draft --execution-unit {candidateExecutionUnit} --target-repo {repo} --format json"
                        + $" && intent-cli issue publish-flow {candidateExecutionUnit} --repo {repo} --write --format json"
                        + $" && intent-cli automation issue-publish --repo {repo} --issue <new-issue-number> --write --format json",
                    clarification: null,
                    details,
                    warnings,
                    safeRepairCategory: SafeRepairCategories.IssuePublishGap);
            }

            details.Add(new AutomationHostReviewDiagnosticsDetail
            {
                Kind = AutomationHostReviewDiagnosticsClassifications.WipCapBlocked,
                TargetKind = null,
                TargetNumber = null,
                TargetUrl = null,
                Description = $"in-flight intent-target items: PRs={string.Join(",", inFlightPrs)} issues={string.Join(",", inFlightIssues)}",
            });
            return Build(
                repo,
                AutomationHostReviewDiagnosticsClassifications.WipCapBlocked,
                "WIP cap blocks new next-slice publication; an open intent-target issue or PR is still in flight.",
                recommendedNextCommand: null,
                clarification: null,
                details,
                warnings);
        }

        if (!string.IsNullOrWhiteSpace(candidateExecutionUnit))
        {
            // G286: a complete queued candidate with no review/WIP/clarification
            // blocker is the deterministic publish path. Surface
            // `issue-publish-ready` and a recommended chain that walks the
            // operator straight through `packet draft` → `issue publish-flow
            // --write` → `automation issue-publish --write`.
            details.Add(new AutomationHostReviewDiagnosticsDetail
            {
                Kind = AutomationHostReviewDiagnosticsClassifications.IssuePublishReady,
                TargetKind = null,
                TargetNumber = null,
                TargetUrl = null,
                Description = $"next-slice candidate provided: {candidateExecutionUnit}",
            });
            return Build(
                repo,
                AutomationHostReviewDiagnosticsClassifications.IssuePublishReady,
                $"No host review work and no WIP; next-slice candidate {candidateExecutionUnit} is ready to publish per the host loop's pre-approval gate.",
                recommendedNextCommand:
                    $"intent-cli packet draft --execution-unit {candidateExecutionUnit} --target-repo {repo} --format json"
                    + $" && intent-cli issue publish-flow {candidateExecutionUnit} --repo {repo} --write --format json"
                    + $" && intent-cli automation issue-publish --repo {repo} --issue <new-issue-number> --write --format json",
                clarification: null,
                details,
                warnings,
                safeRepairCategory: SafeRepairCategories.IssuePublishGap);
        }

        // G313: when publish-recovery reports an unapplied high-confidence
        // repair, that lane is the first-class recovery for missing
        // `linked_pr` host-metadata blockers — surface it BEFORE the
        // generic `repaired-and-retry` branch so the host loop runs
        // publish-recovery rather than generic reconcile when both
        // signals are present (publish-recovery's evidence is stronger
        // because it derives from `.intent-cli/issues/<unit>/publish.yaml`
        // converging on a single execution unit / source issue).
        if (publishRecoveryHighConfidenceRepairsAvailable > 0)
        {
            details.Add(new AutomationHostReviewDiagnosticsDetail
            {
                Kind = AutomationHostReviewDiagnosticsClassifications.PublishRecoveryReady,
                TargetKind = null,
                TargetNumber = null,
                TargetUrl = null,
                Description = $"publish-recovery reports {publishRecoveryHighConfidenceRepairsAvailable} unapplied high-confidence repair(s).",
            });
            return Build(
                repo,
                AutomationHostReviewDiagnosticsClassifications.PublishRecoveryReady,
                $"Publish-recovery has {publishRecoveryHighConfidenceRepairsAvailable} unapplied high-confidence repair(s) backed by `.intent-cli/issues/<unit>/publish.yaml`; apply them with `automation publish-recovery --write` and retry the wake before falling back to generic reconcile.",
                recommendedNextCommand: $"intent-cli automation publish-recovery --repo {repo} --write --format json",
                clarification: null,
                details,
                warnings,
                safeRepairCategory: SafeRepairCategories.ReviewLinkageGap);
        }

        // G286: when reconcile has unapplied high-confidence repairs and no
        // other terminal classification fits, surface `repaired-and-retry` so
        // the host loop knows to apply the safe repair and retry the wake
        // rather than reporting a misleading `true-idle`.
        if (reconcileHighConfidenceRepairsAvailable > 0)
        {
            details.Add(new AutomationHostReviewDiagnosticsDetail
            {
                Kind = AutomationHostReviewDiagnosticsClassifications.RepairedAndRetry,
                TargetKind = null,
                TargetNumber = null,
                TargetUrl = null,
                Description = $"reconcile reports {reconcileHighConfidenceRepairsAvailable} unapplied high-confidence repair(s).",
            });
            return Build(
                repo,
                AutomationHostReviewDiagnosticsClassifications.RepairedAndRetry,
                $"Reconcile has {reconcileHighConfidenceRepairsAvailable} unapplied high-confidence repair(s); apply them and retry the host wake before reporting idle.",
                recommendedNextCommand: $"intent-cli automation reconcile --lane host-review --repo {repo} --write --format json",
                clarification: null,
                details,
                warnings,
                safeRepairCategory: SafeRepairCategories.HostArtifactRepair);
        }

        // G356: when closeout-drift-check reports unapplied repairs, surface
        // `closeout-drift-repair` before `true-idle` so the host loop records
        // the missing closeout deterministically rather than declaring idle
        // while a queue item remains un-completed for an already-merged PR.
        if (closeoutDriftRepairsAvailable > 0)
        {
            details.Add(new AutomationHostReviewDiagnosticsDetail
            {
                Kind = AutomationHostReviewDiagnosticsClassifications.CloseoutDriftRepair,
                TargetKind = null,
                TargetNumber = null,
                TargetUrl = null,
                Description = $"closeout-drift-check reports {closeoutDriftRepairsAvailable} queue item(s) whose linked PR is merged but whose state is not Completed.",
            });
            return Build(
                repo,
                AutomationHostReviewDiagnosticsClassifications.CloseoutDriftRepair,
                $"Closeout drift detected: {closeoutDriftRepairsAvailable} queue item(s) are not Completed despite their linked PR being merged. Apply the repair with `automation closeout-drift-check --write`, commit/push durable state, then retry the wake.",
                recommendedNextCommand: $"intent-cli automation closeout-drift-check --repo {repo} --write --format json",
                clarification: null,
                details,
                warnings,
                safeRepairCategory: SafeRepairCategories.CloseoutDriftRepair);
        }

        return Build(
            repo,
            AutomationHostReviewDiagnosticsClassifications.TrueIdle,
            "No host review PR, no in-flight intent-target item, and no next-slice candidate. The host loop is correctly idle.",
            recommendedNextCommand: null,
            clarification: null,
            details,
            warnings);
    }

    private static AutomationHostReviewDiagnosticsResult Build(
        string repo,
        string classification,
        string summary,
        string? recommendedNextCommand,
        AutomationHostReviewDiagnosticsClarification? clarification,
        IReadOnlyList<AutomationHostReviewDiagnosticsDetail> details,
        IReadOnlyList<string> warnings,
        string? safeRepairCategory = null) =>
        new()
        {
            Repo = repo,
            Classification = classification,
            Summary = summary,
            ReadOnly = true,
            RecommendedNextCommand = recommendedNextCommand,
            StructuredClarification = clarification,
            Details = details,
            Warnings = warnings,
            // G355: safe_repair_available is true only when a high-confidence
            // deterministic repair is declared by diagnostics — never for
            // unsafe-metadata, clarification-required, or true-idle paths.
            SafeRepairAvailable = safeRepairCategory is not null,
            SafeRepairCategory = safeRepairCategory,
        };

    private static IReadOnlyList<int> ExtractLinkedIssueNumbers(string repo, GitHubAutomationPrCandidate pr)
    {
        var numbers = new List<int>();

        foreach (var reference in pr.ClosingIssuesReferences)
        {
            if (reference.Number <= 0)
            {
                continue;
            }

            var referenceRepo = repo;
            if (reference.Repository is { Name.Length: > 0, Owner.Login.Length: > 0 } repository)
            {
                referenceRepo = $"{repository.Owner.Login}/{repository.Name}";
            }

            if (string.Equals(referenceRepo, repo, StringComparison.OrdinalIgnoreCase)
                && !numbers.Contains(reference.Number))
            {
                numbers.Add(reference.Number);
            }
        }

        foreach (Match match in ClosesIssueRegex.Matches(pr.Body ?? string.Empty))
        {
            var linkedRepo = match.Groups["repo"].Value;
            if (!string.IsNullOrWhiteSpace(linkedRepo)
                && !string.Equals(linkedRepo, repo, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(
                    match.Groups["number"].Value,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var issueNumber)
                && !numbers.Contains(issueNumber))
            {
                numbers.Add(issueNumber);
            }
        }

        return numbers;
    }

    /// <summary>
    /// G289: returns true when a candidate's <c>state</c> field indicates the
    /// item is still active. The empty / unset state is treated as open for
    /// backward compatibility with callers that pre-date the field. Closed
    /// or merged states (case-insensitive) explicitly drop the candidate
    /// from WIP detection.
    /// </summary>
    private static AutomationHostReviewDiagnosticsDetail? FindFirstDraftCandidate(
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs)
    {
        // G297: pick the first open PR that carries intent-target or
        // intent-pr-reviewing as the draft target so the diagnostic has a
        // concrete `target_number` for the recommended `review-release`
        // command. Ordering matches the analyzer's actionable-review pass
        // above (intent-target first, then intent-pr-reviewing).
        foreach (var pr in openPrs)
        {
            var labels = LabelNames(pr.Labels);
            if (labels.Contains(WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal)
                || labels.Contains(WorkerNextActionConstants.Labels.IntentPrReviewing, StringComparer.Ordinal))
            {
                return new AutomationHostReviewDiagnosticsDetail
                {
                    Kind = AutomationHostReviewDiagnosticsClassifications.DraftMergeBlocked,
                    TargetKind = GhCliGitHubLabelMutator.Kinds.Pr,
                    TargetNumber = pr.Number,
                    TargetUrl = pr.Url,
                    Description = $"PR #{pr.Number} is still draft.",
                };
            }
        }

        return null;
    }

    private static bool IsOpenState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return true;
        }

        return !string.Equals(state, "CLOSED", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(state, "MERGED", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyCollection<string> LabelNames(IReadOnlyList<GitHubAutomationLabel>? labels)
    {
        if (labels is null || labels.Count == 0)
        {
            return Array.Empty<string>();
        }

        return labels
            .Select(label => label.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToArray();
    }
}
