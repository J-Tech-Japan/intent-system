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

        // Step 4: request-update-pending.
        if (labelsSet.Contains(WorkerPrCommentPreflightConstants.Labels.IntentPrRequestUpdate))
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

        // Step 8.5 (G353): host-artifact-repair-required — when EVERY
        // actionable comment targets a host metadata path (.intent-cli/**
        // or intents/**), the child worker must not attempt to repair them.
        // The host agent must commit/push the artifact fix and re-run review
        // readiness. If even one comment targets implementation code the
        // standard repair-required path takes over so the child can still
        // address it.
        if (actionableComments.Count > 0
            && actionableComments.All(c => CommentTargetsHostMetadata(c.Excerpt)))
        {
            var reasons = new List<string>
            {
                $"all {actionableComments.Count} actionable comment(s) target host metadata paths " +
                "(.intent-cli/** or intents/**); child worker must not edit host artifacts"
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

            entries.Add(new WorkerPrCommentPreflightActionableComment
            {
                Id = string.IsNullOrEmpty(thread.Id) ? firstActionable.Id : thread.Id,
                Author = firstActionable.Author,
                Kind = WorkerPrCommentPreflightConstants.CommentKinds.ReviewThread,
                Excerpt = TruncateExcerpt(firstActionable.Body)
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

            entries.Add(new WorkerPrCommentPreflightActionableComment
            {
                Id = review.Id,
                Author = review.Author,
                Kind = WorkerPrCommentPreflightConstants.CommentKinds.Review,
                Excerpt = TruncateExcerpt(review.Body)
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

            entries.Add(new WorkerPrCommentPreflightActionableComment
            {
                Id = comment.Id,
                Author = comment.Author,
                Kind = WorkerPrCommentPreflightConstants.CommentKinds.IssueComment,
                Excerpt = TruncateExcerpt(comment.Body)
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
    /// G353: Returns true when a comment excerpt explicitly references a host
    /// metadata path (<c>.intent-cli/</c> or <c>intents/</c>), indicating the
    /// reviewer is asking to repair a host artifact rather than an
    /// implementation file. The check is ordinal because path separators are
    /// case-sensitive on Linux file systems and these path prefixes are always
    /// lowercase in the intent-system layout.
    /// </summary>
    private static bool CommentTargetsHostMetadata(string excerpt)
    {
        if (string.IsNullOrEmpty(excerpt))
        {
            return false;
        }
        return excerpt.Contains(".intent-cli/", StringComparison.Ordinal)
            || excerpt.Contains("intents/", StringComparison.Ordinal);
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
