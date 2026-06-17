namespace IntentSystem.Cli.Commands;

/// <summary>
/// G204: Pure deterministic classifier for
/// <c>intent-cli worker pr-comment-preflight</c>. Given a fetched
/// <see cref="GitHubPrLookupResult"/>, the (optional) source issue lookup
/// payload, the PR comments lookup payload, target repo, and implementation
/// workdir, runs the ten-step first-match-wins precedence and returns a
/// stable <see cref="WorkerPrCommentPreflightResult"/>. Never mutates state,
/// never invokes any external process, never touches GitHub.
/// </summary>
internal static class WorkerPrCommentPreflightAnalyzer
{
    private const int ExcerptLength = 120;

    /// <summary>
    /// Run the ten-step classifier. <paramref name="sourceIssue"/> is the
    /// already-fetched source issue payload (or <c>null</c> if no candidate
    /// was traced or if the caller did not look it up). The analyzer does not
    /// itself perform any lookup — that is the command layer's responsibility.
    /// </summary>
    public static WorkerPrCommentPreflightResult Analyze(
        GitHubPrLookupResult pr,
        GitHubPrCommentsLookupResult comments,
        string repo,
        int prNumber,
        string workdir,
        SourceIssueCandidate? sourceIssueCandidate,
        GitHubIssueLookupResult? sourceIssue)
    {
        ArgumentNullException.ThrowIfNull(pr);
        ArgumentNullException.ThrowIfNull(comments);
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
            return labelsSet.Contains(WorkerPrCommentPreflightConstants.Labels.IntentPrRequestUpdate)
                || labelsSet.Contains(WorkerPrCommentPreflightConstants.Labels.IntentPrUpdateInProgress)
                || labelsSet.Contains(WorkerPrCommentPreflightConstants.Labels.IntentPrApproved);
        }

        // Compute the actionable-comments list once up-front. The classifier
        // only inspects the count for step 9, but the list is always emitted
        // so callers can introspect it regardless of classification.
        var actionableComments = ComputeActionableComments(comments);

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
                actionableComments,
                WorkerPrCommentPreflightConstants.Classifications.NonActionable,
                [$"pr is {displayState}"],
                actionable: false,
                WorkerPrCommentPreflightConstants.RecommendedActions.DeclineWithSummary);
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
                actionableComments,
                WorkerPrCommentPreflightConstants.Classifications.NonActionable,
                ["pr is draft and not yet promoted for review"],
                actionable: false,
                WorkerPrCommentPreflightConstants.RecommendedActions.DeclineWithSummary);
        }

        // Step 2: approved-or-merged.
        var hasApprovedLabel = labelsSet.Contains(
            WorkerPrCommentPreflightConstants.Labels.IntentPrApproved);
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
                actionableComments,
                WorkerPrCommentPreflightConstants.Classifications.ApprovedOrMerged,
                reasons,
                actionable: false,
                WorkerPrCommentPreflightConstants.RecommendedActions.NoAction);
        }

        // Step 3: update-in-progress.
        if (labelsSet.Contains(WorkerPrCommentPreflightConstants.Labels.IntentPrUpdateInProgress))
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
                actionableComments,
                WorkerPrCommentPreflightConstants.Classifications.UpdateInProgress,
                ["pr carries intent-pr-update-in-progress label; a worker is iterating on this PR"],
                actionable: false,
                WorkerPrCommentPreflightConstants.RecommendedActions.WaitForWorkerUpdate);
        }

        // Step 4: request-update-pending — only a non-actionable wait when the
        // PR has NO actionable comments. (G392) A request-update PR that ALSO
        // carries actionable review feedback must fall through to the
        // source-issue / source-issue-not-target / host-artifact / repair
        // steps so that `worker next-action` and `worker pr-comment-preflight`
        // share the same child-actionability decision. Previously this step
        // short-circuited EVERY intent-pr-request-update PR to
        // request-update-pending/actionable:false before the comment-content
        // steps ran — which meant `next-action` could select a source-issue-
        // present request-update PR as claimable `pr-comment-fix` even though
        // preflight reported actionable:false for the same PR (the AIC #3648
        // next-action↔preflight contradiction). Gating on
        // actionableComments.Count == 0 keeps the genuine "reviewer set the
        // label but no actionable comment yet" wait while letting a repairable
        // request-update PR reach repair-required/actionable:true at step 9.
        if (labelsSet.Contains(WorkerPrCommentPreflightConstants.Labels.IntentPrRequestUpdate)
            && actionableComments.Count == 0)
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
                actionableComments,
                WorkerPrCommentPreflightConstants.Classifications.RequestUpdatePending,
                ["pr carries intent-pr-request-update label; reviewer requested a worker update"],
                actionable: false,
                WorkerPrCommentPreflightConstants.RecommendedActions.WaitForWorkerUpdate);
        }

        // Step 5: missing-target-label.
        if (!labelsSet.Contains(WorkerPrCommentPreflightConstants.Labels.IntentTarget))
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
                actionableComments,
                WorkerPrCommentPreflightConstants.Classifications.MissingTargetLabel,
                ["pr is missing intent-target label; not marked as an automation target"],
                actionable: false,
                WorkerPrCommentPreflightConstants.RecommendedActions.DeclineWithSummary);
        }

        // Step 6: target-mismatch — reuse the G203 helper so the heuristics
        // (Repository: header, submodules/, parent-host) stay in lock-step.
        var mismatchReasons = WorkerPrReviewPreflightAnalyzer.DetectTargetMismatch(
            body, repo, workdir, sourceIssueCandidate);
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
                actionableComments,
                WorkerPrCommentPreflightConstants.Classifications.TargetMismatch,
                mismatchReasons,
                actionable: false,
                WorkerPrCommentPreflightConstants.RecommendedActions.SwitchRepo);
        }

        // Step 7: source-issue-missing.
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
                actionableComments,
                WorkerPrCommentPreflightConstants.Classifications.SourceIssueMissing,
                ["pr has no linked source issue (no closingIssuesReferences and no Closes/Fixes/Resolves #N reference in body)"],
                actionable: false,
                WorkerPrCommentPreflightConstants.RecommendedActions.DeclineWithSummary);
        }

        // Step 8: source-issue-not-target.
        // Sub-case 8a: PR itself carries intent-pr-created (label-policy violation).
        if (labelsSet.Contains(WorkerPrCommentPreflightConstants.Labels.IntentPrCreated))
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
                actionableComments,
                WorkerPrCommentPreflightConstants.Classifications.SourceIssueNotTarget,
                ["label-policy violation: intent-pr-created found on PR but should be on source issue only"],
                actionable: false,
                WorkerPrCommentPreflightConstants.RecommendedActions.LabelCleanupRequired);
        }

        // Sub-case 8b/8c: source issue exists but lacks required labels.
        if (sourceIssue is not null)
        {
            var missingLabels = new List<string>();
            if (!sourceIssueLabelsSet.Contains(WorkerPrCommentPreflightConstants.Labels.IntentTarget))
            {
                missingLabels.Add("intent-target");
            }
            if (!sourceIssueLabelsSet.Contains(WorkerPrCommentPreflightConstants.Labels.IntentPrCreated))
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
                    actionableComments,
                    WorkerPrCommentPreflightConstants.Classifications.SourceIssueNotTarget,
                    reasons,
                    actionable: false,
                    WorkerPrCommentPreflightConstants.RecommendedActions.LabelCleanupRequired);
            }
        }

        // Step 8.5 (G353 / G476): host-artifact-repair-required — when EVERY
        // actionable comment's REQUESTED EDIT TARGET is a host metadata path
        // (.intent-cli/** or intents/**), the child worker must not attempt to
        // repair them. The host agent must commit/push the artifact fix and
        // re-run review readiness. If even one comment asks to edit
        // implementation code — even while citing a host packet path as
        // evidence (G316 packet-aware review) — the standard repair-required
        // path takes over so the child can still address it. The decision is
        // made by edit target, not by incidental host-path text, so an evidence
        // citation no longer deadlocks an implementation repair (G476).
        if (actionableComments.Count > 0
            && actionableComments.All(c => c.TargetsHostMetadata))
        {
            var hostEditTargets = actionableComments
                .SelectMany(c => c.RequestedEditPaths)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var reasons = new List<string>
            {
                $"all {actionableComments.Count} actionable comment(s) target host metadata paths " +
                "(.intent-cli/** or intents/**); child worker must not edit host artifacts"
            };
            if (hostEditTargets.Length > 0)
            {
                reasons.Add(
                    "requested host edit target(s): " + string.Join(", ", hostEditTargets));
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
                actionableComments,
                WorkerPrCommentPreflightConstants.Classifications.HostArtifactRepairRequired,
                reasons,
                actionable: false,
                WorkerPrCommentPreflightConstants.RecommendedActions.EscalateToHostRepair);
        }

        // Step 9: repair-required.
        if (actionableComments.Count > 0)
        {
            var reasons = new List<string>
            {
                $"pr has {actionableComments.Count} actionable comment(s) requiring repair"
            };
            return Build(
                pr,
                repo,
                prNumber,
                title,
                displayState,
                labels,
                sourceIssueCandidate?.Number,
                sourceIssueLabels,
                actionableComments,
                WorkerPrCommentPreflightConstants.Classifications.RepairRequired,
                reasons,
                actionable: true,
                WorkerPrCommentPreflightConstants.RecommendedActions.RepairPr);
        }

        // Step 10: no-actionable-comments.
        return Build(
            pr,
            repo,
            prNumber,
            title,
            displayState,
            labels,
            sourceIssueCandidate?.Number,
            sourceIssueLabels,
            actionableComments,
            WorkerPrCommentPreflightConstants.Classifications.NoActionableComments,
            Array.Empty<string>(),
            actionable: false,
            WorkerPrCommentPreflightConstants.RecommendedActions.NoAction);
    }

    /// <summary>
    /// G204: Pure filter — produce the actionable-comments list from a raw
    /// comments lookup payload. Bias: overinclude rather than underinclude;
    /// the operator can decline manually. Bot-author and auto-generated-marker
    /// rejection is still applied.
    /// </summary>
    internal static IReadOnlyList<WorkerPrCommentPreflightActionableComment> ComputeActionableComments(
        GitHubPrCommentsLookupResult comments)
    {
        ArgumentNullException.ThrowIfNull(comments);

        var entries = new List<WorkerPrCommentPreflightActionableComment>();

        foreach (var thread in comments.ReviewThreads ?? Array.Empty<GitHubPrReviewThread>())
        {
            if (thread.IsResolved)
            {
                continue;
            }

            var threadComments = thread.Comments ?? Array.Empty<GitHubPrReviewThreadComment>();
            var firstActionable = threadComments
                .FirstOrDefault(c => IsActionableAuthor(c.Author) && IsActionableBody(c.Body));
            if (firstActionable is null)
            {
                continue;
            }

            var targets = ClassifyCommentTargets(firstActionable.Body);
            entries.Add(new WorkerPrCommentPreflightActionableComment
            {
                Id = string.IsNullOrEmpty(thread.Id) ? firstActionable.Id : thread.Id,
                Author = firstActionable.Author,
                Kind = WorkerPrCommentPreflightConstants.CommentKinds.ReviewThread,
                Excerpt = TruncateExcerpt(firstActionable.Body),
                RequestedEditPaths = targets.RequestedEditPaths,
                HostEvidencePaths = targets.HostEvidencePaths,
                TargetsHostMetadata = targets.TargetsHostMetadata
            });
        }

        foreach (var review in comments.Reviews ?? Array.Empty<GitHubPrReview>())
        {
            if (!string.Equals(review.State, "CHANGES_REQUESTED", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!IsActionableAuthor(review.Author))
            {
                continue;
            }
            if (!IsActionableBody(review.Body))
            {
                continue;
            }

            var targets = ClassifyCommentTargets(review.Body);
            entries.Add(new WorkerPrCommentPreflightActionableComment
            {
                Id = review.Id,
                Author = review.Author,
                Kind = WorkerPrCommentPreflightConstants.CommentKinds.Review,
                Excerpt = TruncateExcerpt(review.Body),
                RequestedEditPaths = targets.RequestedEditPaths,
                HostEvidencePaths = targets.HostEvidencePaths,
                TargetsHostMetadata = targets.TargetsHostMetadata
            });
        }

        foreach (var comment in comments.Comments ?? Array.Empty<GitHubPrIssueComment>())
        {
            if (!IsActionableAuthor(comment.Author))
            {
                continue;
            }
            if (!IsActionableBody(comment.Body))
            {
                continue;
            }

            var targets = ClassifyCommentTargets(comment.Body);
            entries.Add(new WorkerPrCommentPreflightActionableComment
            {
                Id = comment.Id,
                Author = comment.Author,
                Kind = WorkerPrCommentPreflightConstants.CommentKinds.IssueComment,
                Excerpt = TruncateExcerpt(comment.Body),
                RequestedEditPaths = targets.RequestedEditPaths,
                HostEvidencePaths = targets.HostEvidencePaths,
                TargetsHostMetadata = targets.TargetsHostMetadata
            });
        }

        return entries;
    }

    private static bool IsActionableAuthor(string? author)
    {
        if (string.IsNullOrWhiteSpace(author))
        {
            return false;
        }
        return !WorkerPrCommentPreflightConstants.NonActionableAuthors.Contains(author);
    }

    private static bool IsActionableBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        foreach (var marker in WorkerPrCommentPreflightConstants.AutoGeneratedMarkers)
        {
            if (body.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// G476: detected path sets for a single comment, computed over the FULL
    /// comment body (never the truncated excerpt).
    /// </summary>
    internal readonly record struct CommentTargetClassification(
        IReadOnlyList<string> RequestedEditPaths,
        IReadOnlyList<string> HostEvidencePaths,
        bool TargetsHostMetadata);

    // Path-like token: one or more slash-separated segments of path characters,
    // e.g. `.intent-cli/issues/E038/packet.yaml`, `scripts/reset-dev-db.ps1`,
    // `intents/intent-cli/clarifications/open.md`. Backticks delimit (they are
    // excluded from the segment character class) but are not required.
    private static readonly System.Text.RegularExpressions.Regex PathTokenRegex =
        new(
            @"(?:[A-Za-z0-9._-]+/)+[A-Za-z0-9._-]+",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    // Evidence cues: when a host metadata path is introduced by one of these
    // phrases it is being cited as packet/review evidence (G316), not as an
    // edit target. Matched against the lowercased text immediately preceding
    // the path occurrence.
    private static readonly string[] EvidenceCues =
    {
        "according to",
        "as per",
        "as documented in",
        "as described in",
        "as noted in",
        "as shown in",
        "as referenced in",
        "documented in",
        "referenced in",
        "cited in",
        "based on",
        "per ",
        "see ",
    };

    private const int EvidenceWindow = 48;

    /// <summary>
    /// G353 / G476: classify the paths a comment references into requested edit
    /// targets vs host-metadata evidence citations, then decide whether the
    /// comment is a host-artifact edit request.
    ///
    /// A path is host metadata when (after trimming a leading <c>./</c>) it
    /// starts with <c>.intent-cli/</c> or <c>intents/</c>. Implementation paths
    /// are always treated as requested edit targets — a reviewer naming an
    /// implementation file is asking for it to change. A host path is treated
    /// as an edit target unless every occurrence is immediately preceded by an
    /// evidence cue, in which case it is recorded as evidence only.
    ///
    /// <c>TargetsHostMetadata</c> is true only when the comment names at least
    /// one edit target and every edit target is a host metadata path. A comment
    /// that cites a host path purely as evidence (no edit target, or an
    /// implementation edit target alongside) is NOT a host-artifact request, so
    /// the child can still repair it (G476). Ordinal comparison is used because
    /// these path prefixes are always lowercase in the intent-system layout and
    /// path separators are case-sensitive on Linux file systems.
    /// </summary>
    internal static CommentTargetClassification ClassifyCommentTargets(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new CommentTargetClassification(
                Array.Empty<string>(), Array.Empty<string>(), false);
        }

        var requestedEditPaths = new List<string>();
        var hostEvidencePaths = new List<string>();
        var seenEdit = new HashSet<string>(StringComparer.Ordinal);
        var seenEvidence = new HashSet<string>(StringComparer.Ordinal);

        foreach (System.Text.RegularExpressions.Match match in PathTokenRegex.Matches(body))
        {
            var token = match.Value;
            var normalized = token.StartsWith("./", StringComparison.Ordinal)
                ? token.Substring(2)
                : token;

            var isHost = normalized.StartsWith(".intent-cli/", StringComparison.Ordinal)
                || normalized.StartsWith("intents/", StringComparison.Ordinal);

            if (!isHost)
            {
                // Implementation path → always a requested edit target.
                if (seenEdit.Add(normalized))
                {
                    requestedEditPaths.Add(normalized);
                }
                continue;
            }

            // Host path: edit target unless introduced by an evidence cue.
            var windowStart = Math.Max(0, match.Index - EvidenceWindow);
            var preceding = body.Substring(windowStart, match.Index - windowStart)
                .ToLowerInvariant();
            var isEvidence = EvidenceCues.Any(cue => preceding.Contains(cue, StringComparison.Ordinal));

            if (isEvidence)
            {
                if (seenEvidence.Add(normalized))
                {
                    hostEvidencePaths.Add(normalized);
                }
            }
            else if (seenEdit.Add(normalized))
            {
                requestedEditPaths.Add(normalized);
            }
        }

        // A path recorded as an edit target in one occurrence wins over an
        // evidence-only occurrence elsewhere: drop it from the evidence list.
        if (hostEvidencePaths.Count > 0 && seenEdit.Count > 0)
        {
            hostEvidencePaths.RemoveAll(p => seenEdit.Contains(p));
        }

        var targetsHostMetadata = requestedEditPaths.Count > 0
            && requestedEditPaths.All(p =>
                p.StartsWith(".intent-cli/", StringComparison.Ordinal)
                || p.StartsWith("intents/", StringComparison.Ordinal));

        return new CommentTargetClassification(
            requestedEditPaths,
            hostEvidencePaths,
            targetsHostMetadata);
    }

    private static string TruncateExcerpt(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return string.Empty;
        }
        return body.Length <= ExcerptLength ? body : body.Substring(0, ExcerptLength);
    }

    private static WorkerPrCommentPreflightResult Build(
        GitHubPrLookupResult pr,
        string repo,
        int prNumber,
        string title,
        string displayState,
        IReadOnlyList<string> labels,
        int? sourceIssueNumber,
        IReadOnlyList<string>? sourceIssueLabels,
        IReadOnlyList<WorkerPrCommentPreflightActionableComment> actionableComments,
        string classification,
        IReadOnlyList<string> reasons,
        bool actionable,
        string recommendedAction)
    {
        var summaryLine =
            $"Worker pr-comment-preflight for {repo}#{prNumber} — classification={classification}, actionable={actionable.ToString().ToLowerInvariant()}.";

        return new WorkerPrCommentPreflightResult
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
            ActionableComments = actionableComments,
            Reasons = reasons,
            RecommendedAction = recommendedAction,
            SummaryLine = summaryLine
        };
    }
}
