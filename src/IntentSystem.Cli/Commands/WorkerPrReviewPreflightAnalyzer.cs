using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G203: Pure deterministic classifier for <c>intent-cli worker pr-review-preflight</c>.
/// Given a fetched <see cref="GitHubPrLookupResult"/>, the (optional) source
/// issue lookup payload, target repo, and implementation workdir, runs the
/// ten-step first-match-wins precedence and returns a stable
/// <see cref="WorkerPrReviewPreflightResult"/>. Never mutates state, never
/// invokes any external process, never touches GitHub.
/// </summary>
internal static class WorkerPrReviewPreflightAnalyzer
{
    private static readonly Regex RepositoryHeaderRegex = new(
        @"Repository:\s*([A-Za-z0-9_.\-]+/[A-Za-z0-9_.\-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ClosesIssueRegex = new(
        @"(?i)(?:closes|fixes|resolves)\s+#(\d+)",
        RegexOptions.Compiled);

    /// <summary>
    /// Trace the source-issue candidate from the PR lookup payload using the
    /// algorithm: prefer <c>closingIssuesReferences</c>, fall back to the
    /// first <c>(?i)(closes|fixes|resolves)\s+#N</c> match in the body. Returns
    /// the issue number and the cross-repo descriptor (if the closing-issues
    /// metadata names a different repo). Returns null when neither produces a
    /// candidate.
    /// </summary>
    public static SourceIssueCandidate? TraceSourceIssue(GitHubPrLookupResult pr, string defaultRepo)
    {
        ArgumentNullException.ThrowIfNull(pr);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultRepo);

        if (pr.ClosingIssuesReferences is { Count: > 0 } refs)
        {
            var first = refs[0];
            if (first.Number > 0)
            {
                string repo = defaultRepo;
                if (first.Repository is { } r
                    && !string.IsNullOrWhiteSpace(r.Name)
                    && r.Owner is { Login: { Length: > 0 } owner })
                {
                    repo = $"{owner}/{r.Name}";
                }
                return new SourceIssueCandidate(first.Number, repo, FromClosingIssuesMetadata: true);
            }
        }

        var body = pr.Body ?? string.Empty;
        var match = ClosesIssueRegex.Match(body);
        if (match.Success
            && int.TryParse(
                match.Groups[1].Value,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var number)
            && number > 0)
        {
            return new SourceIssueCandidate(number, defaultRepo, FromClosingIssuesMetadata: false);
        }

        return null;
    }

    /// <summary>
    /// Run the ten-step classifier. <paramref name="sourceIssue"/> is the
    /// already-fetched source issue payload (or <c>null</c> if no candidate
    /// was traced or if the caller did not look it up). The analyzer does not
    /// itself perform the lookup — that is the command layer's responsibility.
    /// </summary>
    public static WorkerPrReviewPreflightResult Analyze(
        GitHubPrLookupResult pr,
        string repo,
        int prNumber,
        string workdir,
        SourceIssueCandidate? sourceIssueCandidate,
        GitHubIssueLookupResult? sourceIssue)
    {
        ArgumentNullException.ThrowIfNull(pr);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(workdir);

        var labels = pr.Labels?
            .Select(label => label.Name ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray()
            ?? Array.Empty<string>();
        var labelsSet = new HashSet<string>(labels, StringComparer.Ordinal);

        var sourceIssueLabels = sourceIssue?.Labels?
            .Select(label => label.Name ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        var sourceIssueLabelsSet = new HashSet<string>(
            sourceIssueLabels ?? Array.Empty<string>(),
            StringComparer.Ordinal);

        var rawState = string.IsNullOrWhiteSpace(pr.State) ? "unknown" : pr.State;
        var stateNormalized = rawState.ToLowerInvariant();

        // Surface the merged signal in the displayed pr_state. gh emits
        // state="MERGED" already when a PR is merged, but defensively also
        // honor merged==true even if state somehow says "closed".
        string displayState;
        if (pr.Merged || string.Equals(stateNormalized, "merged", StringComparison.Ordinal))
        {
            displayState = "merged";
        }
        else if (string.Equals(stateNormalized, "open", StringComparison.Ordinal))
        {
            displayState = "open";
        }
        else if (string.Equals(stateNormalized, "closed", StringComparison.Ordinal) || pr.Closed)
        {
            displayState = "closed";
        }
        else
        {
            displayState = stateNormalized;
        }

        var title = pr.Title ?? string.Empty;
        var body = pr.Body ?? string.Empty;

        bool HasAnyReviewLabel()
        {
            return labelsSet.Contains(WorkerPrReviewPreflightConstants.Labels.IntentPrReviewing)
                || labelsSet.Contains(WorkerPrReviewPreflightConstants.Labels.IntentPrRequestUpdate)
                || labelsSet.Contains(WorkerPrReviewPreflightConstants.Labels.IntentPrUpdateInProgress)
                || labelsSet.Contains(WorkerPrReviewPreflightConstants.Labels.IntentPrApproved);
        }

        // Step 1: non-actionable — closed AND not merged, OR fresh draft.
        var isClosedNotMerged =
            (pr.Closed || string.Equals(stateNormalized, "closed", StringComparison.Ordinal))
            && !pr.Merged
            && !string.Equals(stateNormalized, "merged", StringComparison.Ordinal);
        if (isClosedNotMerged)
        {
            return Build(
                pr,
                repo,
                prNumber,
                title,
                displayState,
                labels,
                sourceIssueCandidate?.Number,
                sourceIssueLabels,
                WorkerPrReviewPreflightConstants.Classifications.NonActionable,
                [$"pr is {displayState}"],
                actionable: false,
                WorkerPrReviewPreflightConstants.RecommendedActions.DeclineWithSummary);
        }

        if (pr.IsDraft && !HasAnyReviewLabel())
        {
            return Build(
                pr,
                repo,
                prNumber,
                title,
                displayState,
                labels,
                sourceIssueCandidate?.Number,
                sourceIssueLabels,
                WorkerPrReviewPreflightConstants.Classifications.NonActionable,
                ["pr is draft and not yet promoted for review"],
                actionable: false,
                WorkerPrReviewPreflightConstants.RecommendedActions.DeclineWithSummary);
        }

        // Step 2: approved-or-merged.
        var hasApprovedLabel = labelsSet.Contains(
            WorkerPrReviewPreflightConstants.Labels.IntentPrApproved);
        if (hasApprovedLabel || pr.Merged)
        {
            var reasons = new List<string>();
            if (pr.Merged)
            {
                reasons.Add("pr is already merged");
            }
            if (hasApprovedLabel)
            {
                reasons.Add("pr carries intent-pr-approved label");
            }
            return Build(
                pr,
                repo,
                prNumber,
                title,
                displayState,
                labels,
                sourceIssueCandidate?.Number,
                sourceIssueLabels,
                WorkerPrReviewPreflightConstants.Classifications.ApprovedOrMerged,
                reasons,
                actionable: false,
                WorkerPrReviewPreflightConstants.RecommendedActions.NoAction);
        }

        // Step 3: update-in-progress.
        if (labelsSet.Contains(WorkerPrReviewPreflightConstants.Labels.IntentPrUpdateInProgress))
        {
            return Build(
                pr,
                repo,
                prNumber,
                title,
                displayState,
                labels,
                sourceIssueCandidate?.Number,
                sourceIssueLabels,
                WorkerPrReviewPreflightConstants.Classifications.UpdateInProgress,
                ["pr carries intent-pr-update-in-progress label; a worker is iterating on this PR"],
                actionable: false,
                WorkerPrReviewPreflightConstants.RecommendedActions.WaitForWorkerUpdate);
        }

        // Step 4: request-update-pending.
        if (labelsSet.Contains(WorkerPrReviewPreflightConstants.Labels.IntentPrRequestUpdate))
        {
            return Build(
                pr,
                repo,
                prNumber,
                title,
                displayState,
                labels,
                sourceIssueCandidate?.Number,
                sourceIssueLabels,
                WorkerPrReviewPreflightConstants.Classifications.RequestUpdatePending,
                ["pr carries intent-pr-request-update label; reviewer requested a worker update"],
                actionable: false,
                WorkerPrReviewPreflightConstants.RecommendedActions.WaitForWorkerUpdate);
        }

        // Step 5: already-reviewing.
        if (labelsSet.Contains(WorkerPrReviewPreflightConstants.Labels.IntentPrReviewing))
        {
            return Build(
                pr,
                repo,
                prNumber,
                title,
                displayState,
                labels,
                sourceIssueCandidate?.Number,
                sourceIssueLabels,
                WorkerPrReviewPreflightConstants.Classifications.AlreadyReviewing,
                ["pr carries intent-pr-reviewing label; another reviewer is already on it"],
                actionable: false,
                WorkerPrReviewPreflightConstants.RecommendedActions.NoAction);
        }

        // Step 6: missing-target-label.
        if (!labelsSet.Contains(WorkerPrReviewPreflightConstants.Labels.IntentTarget))
        {
            return Build(
                pr,
                repo,
                prNumber,
                title,
                displayState,
                labels,
                sourceIssueCandidate?.Number,
                sourceIssueLabels,
                WorkerPrReviewPreflightConstants.Classifications.MissingTargetLabel,
                ["pr is missing intent-target label; not marked as an automation target"],
                actionable: false,
                WorkerPrReviewPreflightConstants.RecommendedActions.DeclineWithSummary);
        }

        // Step 7: target-mismatch — derived from PR body and (if known) source-issue repo.
        var mismatchReasons = DetectTargetMismatch(body, repo, workdir, sourceIssueCandidate);
        if (mismatchReasons.Count > 0)
        {
            return Build(
                pr,
                repo,
                prNumber,
                title,
                displayState,
                labels,
                sourceIssueCandidate?.Number,
                sourceIssueLabels,
                WorkerPrReviewPreflightConstants.Classifications.TargetMismatch,
                mismatchReasons,
                actionable: false,
                WorkerPrReviewPreflightConstants.RecommendedActions.SwitchRepo);
        }

        // Step 8: source-issue-missing.
        if (sourceIssueCandidate is null)
        {
            return Build(
                pr,
                repo,
                prNumber,
                title,
                displayState,
                labels,
                null,
                sourceIssueLabels,
                WorkerPrReviewPreflightConstants.Classifications.SourceIssueMissing,
                ["pr has no linked source issue (no closingIssuesReferences and no Closes/Fixes/Resolves #N reference in body)"],
                actionable: false,
                WorkerPrReviewPreflightConstants.RecommendedActions.DeclineWithSummary);
        }

        // Step 9: source-issue-not-target.
        // Sub-case 9a: PR itself carries intent-pr-created (label-policy violation).
        if (labelsSet.Contains(WorkerPrReviewPreflightConstants.Labels.IntentPrCreated))
        {
            return Build(
                pr,
                repo,
                prNumber,
                title,
                displayState,
                labels,
                sourceIssueCandidate?.Number,
                sourceIssueLabels,
                WorkerPrReviewPreflightConstants.Classifications.SourceIssueNotTarget,
                ["label-policy violation: intent-pr-created found on PR but should be on source issue only"],
                actionable: false,
                WorkerPrReviewPreflightConstants.RecommendedActions.LabelCleanupRequired);
        }

        // Sub-case 9b/9c: source issue exists but lacks required labels.
        if (sourceIssue is not null)
        {
            var missingLabels = new List<string>();
            if (!sourceIssueLabelsSet.Contains(WorkerPrReviewPreflightConstants.Labels.IntentTarget))
            {
                missingLabels.Add("intent-target");
            }
            if (!sourceIssueLabelsSet.Contains(WorkerPrReviewPreflightConstants.Labels.IntentPrCreated))
            {
                missingLabels.Add("intent-pr-created");
            }
            if (missingLabels.Count > 0)
            {
                var reasons = new List<string>();
                foreach (var label in missingLabels)
                {
                    reasons.Add(
                        $"source issue #{sourceIssueCandidate.Value.Number} is missing {label} label");
                }
                return Build(
                    pr,
                    repo,
                    prNumber,
                    title,
                    displayState,
                    labels,
                    sourceIssueCandidate?.Number,
                    sourceIssueLabels,
                    WorkerPrReviewPreflightConstants.Classifications.SourceIssueNotTarget,
                    reasons,
                    actionable: false,
                    WorkerPrReviewPreflightConstants.RecommendedActions.LabelCleanupRequired);
            }
        }

        // Step 10: ready-to-review.
        return Build(
            pr,
            repo,
            prNumber,
            title,
            displayState,
            labels,
            sourceIssueCandidate?.Number,
            sourceIssueLabels,
            WorkerPrReviewPreflightConstants.Classifications.ReadyToReview,
            Array.Empty<string>(),
            actionable: true,
            WorkerPrReviewPreflightConstants.RecommendedActions.Review);
    }

    /// <summary>
    /// G204 reuse: the same target-mismatch heuristics (Repository: header,
    /// submodules/, parent-host) are needed by the pr-comment-preflight
    /// analyzer. Promoted from <c>private</c> to <c>internal static</c> so the
    /// G204 surface can call it without duplicating the logic. Pure: no I/O,
    /// no external process.
    /// </summary>
    internal static IReadOnlyList<string> DetectTargetMismatch(
        string body,
        string repo,
        string workdir,
        SourceIssueCandidate? sourceIssueCandidate)
    {
        var reasons = new List<string>();

        // Heuristic 0: closing-issues metadata names a different repo.
        if (sourceIssueCandidate is { } candidate
            && candidate.FromClosingIssuesMetadata
            && !string.Equals(candidate.Repo, repo, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add(
                $"closingIssuesReferences names {candidate.Repo} which does not match --repo {repo}");
        }

        if (string.IsNullOrEmpty(body))
        {
            return reasons;
        }

        // Heuristic 1: explicit "Repository: <owner/repo>" header in body.
        var repoMatch = RepositoryHeaderRegex.Match(body);
        if (repoMatch.Success)
        {
            var declared = repoMatch.Groups[1].Value;
            if (!string.Equals(declared, repo, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add(
                    $"pr body declares Repository: {declared} which does not match --repo {repo}");
            }
        }

        // Heuristic 2: body mentions submodules/ while workdir does not.
        if (body.Contains("submodules/", StringComparison.OrdinalIgnoreCase))
        {
            var workdirNormalized = workdir.Replace('\\', '/');
            if (workdirNormalized.IndexOf("submodules/", StringComparison.OrdinalIgnoreCase) < 0)
            {
                reasons.Add(
                    "pr body references a submodules/ path but --workdir does not point inside a submodules/ tree");
            }
        }

        // Heuristic 3: body mentions parent-host / MyIntentHost.
        var mentionsParentHost = body.Contains("parent-host", StringComparison.OrdinalIgnoreCase)
            || body.Contains("MyIntentHost", StringComparison.Ordinal);
        if (mentionsParentHost)
        {
            reasons.Add(
                $"pr body references parent-host/MyIntentHost execution path which conflicts with child --repo {repo}");
        }

        return reasons;
    }

    private static WorkerPrReviewPreflightResult Build(
        GitHubPrLookupResult pr,
        string repo,
        int prNumber,
        string title,
        string displayState,
        IReadOnlyList<string> labels,
        int? sourceIssueNumber,
        IReadOnlyList<string>? sourceIssueLabels,
        string classification,
        IReadOnlyList<string> reasons,
        bool actionable,
        string recommendedAction)
    {
        var summaryLine =
            $"Worker pr-review-preflight for {repo}#{prNumber} — classification={classification}, actionable={actionable.ToString().ToLowerInvariant()}.";

        return new WorkerPrReviewPreflightResult
        {
            Actionable = actionable,
            Classification = classification,
            Pr = prNumber,
            Repo = repo,
            Title = title,
            PrState = displayState,
            IsDraft = pr.IsDraft,
            Labels = labels,
            SourceIssue = sourceIssueNumber,
            SourceIssueLabels = sourceIssueLabels,
            Reasons = reasons,
            RecommendedAction = recommendedAction,
            SummaryLine = summaryLine
        };
    }
}

/// <summary>
/// G203: Source-issue trace candidate produced by
/// <see cref="WorkerPrReviewPreflightAnalyzer.TraceSourceIssue"/>. The
/// <see cref="FromClosingIssuesMetadata"/> flag distinguishes the metadata
/// path from the regex fallback so the cross-repo heuristic in
/// target-mismatch detection only triggers when the repo descriptor came
/// from gh's structured payload.
/// </summary>
internal readonly record struct SourceIssueCandidate(
    int Number,
    string Repo,
    bool FromClosingIssuesMetadata);
