using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// The single G855 process/API boundary. It accepts only fixed, read-only Git
/// and REST request forms, and immediately projects claim DTOs into plain data.
/// </summary>
internal sealed class UnitStatusReadAdapter : IUnitStatusSnapshotReader
{
    private const int PageSize = 100;
    private const int MaximumPages = 20;
    private readonly IGitHubCommandRunner githubRunner;
    private readonly IGitRemoteCommandRunner gitRunner;

    internal UnitStatusReadAdapter(IGitHubCommandRunner? githubRunner = null, IGitRemoteCommandRunner? gitRunner = null)
    {
        this.githubRunner = githubRunner ?? new GhCommandRunner();
        this.gitRunner = gitRunner ?? new GitRemoteCommandRunner();
    }

    public UnitStatusClaimSnapshot ReadClaimSnapshot(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out var unitError))
        {
            return ClaimFailure("unit-invalid", unitError, UnitStatusStates.InvalidRequest);
        }

        string claimPath;
        string historyDirectory;
        string metadataRef;
        var projectConfig = context.Config.Project;
        if (string.IsNullOrWhiteSpace(projectConfig.MetadataSourceBranch)
            && string.IsNullOrWhiteSpace(projectConfig.MetadataBranch)
            && string.IsNullOrWhiteSpace(projectConfig.MetadataWriteBranch))
        {
            return ClaimFailure(
                "local-claim-ref-unavailable",
                "local-claim-ref-unavailable: no configured local metadata branch is available; this reader does not infer or fetch a canonical claim branch.",
                UnitStatusStates.ProvenanceLimit);
        }

        try
        {
            (claimPath, historyDirectory, metadataRef) = ClaimCommand.DescribeLocalReadPaths(
                context,
                $"execution-unit:{executionUnit}");
        }
        catch (InvalidOperationException exception)
        {
            return ClaimFailure("local-claim-ref-unavailable", exception.Message, UnitStatusStates.ReadFailure);
        }
        catch (ArgumentException exception)
        {
            return ClaimFailure("local-claim-scope-invalid", exception.Message, UnitStatusStates.InvalidRequest);
        }

        var localHeadSha = ReadGitText(context.RepoRoot, ["rev-parse", "--verify", "HEAD"], out _);
        var localHeadRef = ReadGitText(context.RepoRoot, ["rev-parse", "--abbrev-ref", "HEAD"], out _);
        if (string.Equals(localHeadRef, "HEAD", StringComparison.Ordinal)) localHeadRef = null;

        var oid = ReadGitText(
            context.RepoRoot,
            ["rev-parse", "--verify", $"{metadataRef}^{{commit}}"],
            out var oidError,
            metadataRef: metadataRef);
        if (oid is null || !IsObjectId(oid))
        {
            return ClaimFailure(
                "local-claim-ref-unavailable",
                oidError ?? $"Configured local tracking ref '{metadataRef}' did not resolve to a commit.",
                UnitStatusStates.ReadFailure,
                metadataRef,
                localHeadSha,
                localHeadRef);
        }

        var tree = ReadGitText(
            context.RepoRoot,
            ["ls-tree", "-r", "-z", "--name-only", oid, "--", claimPath, historyDirectory],
            out var treeError,
            snapshotOid: oid,
            claimPath: claimPath,
            historyDirectory: historyDirectory);
        if (tree is null)
        {
            return ClaimFailure(
                "local-claim-tree-unreadable",
                treeError ?? "The configured local claim snapshot tree could not be read.",
                UnitStatusStates.ReadFailure,
                metadataRef,
                localHeadSha,
                localHeadRef,
                oid);
        }

        var paths = tree.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (paths.Any(path => !IsAllowedClaimPath(path, claimPath, historyDirectory)))
        {
            return ClaimFailure(
                "local-claim-tree-invalid",
                "The local claim snapshot listed a path outside the exact claim and direct history-file paths.",
                UnitStatusStates.ReadFailure,
                metadataRef,
                localHeadSha,
                localHeadRef,
                oid);
        }

        UnitStatusClaimRecordFact? active = null;
        var history = new List<UnitStatusClaimRecordFact>();
        if (paths.Distinct(StringComparer.Ordinal).Count() != paths.Length)
        {
            return ClaimFailure(
                "local-claim-tree-invalid",
                "The local claim snapshot tree listed a duplicate record path.",
                UnitStatusStates.ReadFailure,
                metadataRef,
                localHeadSha,
                localHeadRef,
                oid);
        }

        foreach (var path in paths)
        {
            var show = ReadGitText(
                context.RepoRoot,
                ["show", $"{oid}:{path}"],
                out var showError,
                snapshotOid: oid,
                claimPath: claimPath,
                historyDirectory: historyDirectory,
                expectedPath: path);
            if (show is null)
            {
                return ClaimFailure(
                    "local-claim-record-unreadable",
                    showError ?? $"The local claim snapshot record '{path}' could not be read.",
                    UnitStatusStates.ReadFailure,
                    metadataRef,
                    localHeadSha,
                    localHeadRef,
                    oid);
            }

            try
            {
                if (string.Equals(path, claimPath, StringComparison.Ordinal))
                {
                    var record = JsonSerializer.Deserialize<ClaimRecord>(show)
                        ?? throw new InvalidOperationException("claim record deserialized to null.");
                    if (!string.Equals(record.Scope, $"execution-unit:{executionUnit}", StringComparison.Ordinal)
                        || string.IsNullOrWhiteSpace(record.Actor)
                        || string.IsNullOrWhiteSpace(record.Team)
                        || record.ClaimedAt == default)
                    {
                        throw new InvalidOperationException("claim record has missing or mismatched identity fields.");
                    }

                    active = new UnitStatusClaimRecordFact
                    {
                        Path = path,
                        Scope = record.Scope,
                        Actor = record.Actor,
                        Team = record.Team,
                        ClaimedAt = record.ClaimedAt,
                    };
                }
                else
                {
                    var record = JsonSerializer.Deserialize<ClaimHistoryRecord>(show)
                        ?? throw new InvalidOperationException("claim history record deserialized to null.");
                    if (!string.Equals(record.Scope, $"execution-unit:{executionUnit}", StringComparison.Ordinal)
                        || string.IsNullOrWhiteSpace(record.Actor)
                        || string.IsNullOrWhiteSpace(record.Team)
                        || record.Operation is not ("release" or "takeover")
                        || record.RecordedAt == default
                        || string.IsNullOrWhiteSpace(record.DisplacedHolder)
                        || string.IsNullOrWhiteSpace(record.DisplacedTeam)
                        || record.DisplacedClaimedAt == default)
                    {
                        throw new InvalidOperationException("claim history record has missing or mismatched identity fields.");
                    }

                    history.Add(new UnitStatusClaimRecordFact
                    {
                        Path = path,
                        Scope = record.Scope,
                        Actor = record.Actor,
                        Team = record.Team,
                        Operation = record.Operation,
                        RecordedAt = record.RecordedAt,
                        DisplacedHolder = record.DisplacedHolder,
                        DisplacedTeam = record.DisplacedTeam,
                        DisplacedClaimedAt = record.DisplacedClaimedAt,
                    });
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
            {
                return ClaimFailure(
                    "local-claim-record-invalid",
                    $"The local claim snapshot record '{path}' is invalid: {exception.Message}",
                    UnitStatusStates.ReadFailure,
                    metadataRef,
                    localHeadSha,
                    localHeadRef,
                    oid);
            }
        }

        return new UnitStatusClaimSnapshot
        {
            State = "completed",
            MetadataRef = metadataRef,
            MetadataOid = oid,
            LocalHeadSha = localHeadSha,
            LocalHeadRef = localHeadRef,
            ActiveClaim = active,
            History = history.OrderBy(item => item.RecordedAt).ThenBy(item => item.Path, StringComparer.Ordinal).ToArray(),
        };
    }

    public UnitStatusRemoteSnapshot ObserveGitHub(
        CliContext context,
        string repo,
        int issue,
        int pullRequest,
        string executionUnit,
        string domain,
        string team)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsSafeRepo(repo) || issue <= 0 || pullRequest <= 0)
        {
            return RemoteFailure("github-request-invalid", "Repository or issue/PR identity is invalid.", UnitStatusStates.InvalidRequest);
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var snapshot = ObserveGitHubAttempt(context, repo, issue, pullRequest, executionUnit, domain, team);
            if (!string.IsNullOrWhiteSpace(snapshot.HeadBefore)
                && string.Equals(snapshot.HeadBefore, snapshot.HeadAfter, StringComparison.OrdinalIgnoreCase))
            {
                return snapshot;
            }

            if (snapshot.HeadBefore is null || snapshot.HeadAfter is null)
            {
                return snapshot;
            }

            if (attempt == 1)
            {
                var facts = FailureFacts(
                    ["issue-completion-marker", "posted-review", "delta-review", "observed-ci", "approved-marker", "approval-head-receipt", "pr-merged"],
                    "snapshot-head-changed",
                    "The pull-request head changed during both bounded snapshot attempts.",
                    UnitStatusStates.IdentityConflict);
                return snapshot with
                {
                    State = UnitStatusStates.Unavailable,
                    Cause = "snapshot-head-changed",
                    Detail = "The pull-request head changed during both bounded snapshot attempts.",
                    Facts = facts,
                };
            }
        }

        return RemoteFailure("snapshot-unavailable", "GitHub snapshot could not be completed.", UnitStatusStates.ReadFailure);
    }

    public UnitStatusRemoteSnapshot ObserveGitHubIssue(CliContext context, string repo, int issue, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsSafeRepo(repo) || issue <= 0)
        {
            var invalid = UnavailableFact("issue-completion-marker", "github-request-invalid",
                "Repository or issue identity is invalid.", UnitStatusStates.InvalidRequest);
            return new UnitStatusRemoteSnapshot
            {
                State = UnitStatusStates.Unavailable,
                Cause = invalid.Cause,
                Detail = invalid.Detail,
                Facts = [invalid],
            };
        }

        var url = $"https://github.com/{repo}/issues/{issue}";
        if (!TryReadJson(context, ["api", "--method", "GET", $"repos/{repo}/issues/{issue}"], out var document, out var failure))
        {
            var unavailable = UnavailableFact("issue-completion-marker", failure.Cause, failure.Detail, UnitStatusStates.ReadFailure)
                with { Evidence = [GitHubEvidence("issue-labels", url, executionUnit, repo, null, null)] };
            return new UnitStatusRemoteSnapshot
            {
                State = UnitStatusStates.Unavailable,
                Cause = failure.Cause,
                Detail = failure.Detail,
                Facts = [unavailable],
                Warnings = [failure.Detail],
            };
        }

        using (document)
        {
            if (!TryReadNumber(document!.RootElement, "number", out var observedIssue)
                || observedIssue != issue
                || !TryReadLabels(document.RootElement, out var labels))
            {
                var malformed = UnavailableFact("issue-completion-marker", "github-api-error",
                    "Issue response is missing its expected number or labels.", UnitStatusStates.ReadFailure)
                    with { Evidence = [GitHubEvidence("issue-labels", url, executionUnit, repo, null, null)] };
                return new UnitStatusRemoteSnapshot
                {
                    State = UnitStatusStates.Unavailable,
                    Cause = malformed.Cause,
                    Detail = malformed.Detail,
                    Facts = [malformed],
                    Warnings = [malformed.Detail!],
                };
            }

            var fact = labels.Contains("intent-pr-created", StringComparer.Ordinal)
                ? new UnitStatusFact
                {
                    Id = "issue-completion-marker",
                    State = UnitStatusStates.Done,
                    Cause = "intent-pr-created-observed",
                    Detail = "The successful issue response includes label 'intent-pr-created'.",
                    Evidence = [GitHubEvidence("issue-label", url, executionUnit, repo, null, null)],
                }
                : new UnitStatusFact
                {
                    Id = "issue-completion-marker",
                    State = UnitStatusStates.Missing,
                    Cause = "issue-pr-created-marker-absent",
                    Detail = "The successful issue response does not include label 'intent-pr-created'.",
                    Evidence = [GitHubEvidence("issue-labels", url, executionUnit, repo, null, null)],
                };
            return new UnitStatusRemoteSnapshot
            {
                State = "completed",
                Detail = "The issue labels were read successfully; no PR identity is linked yet.",
                Facts = [fact],
                IssueLabels = labels,
                Warnings = labels.Contains("intent-pr-created", StringComparer.Ordinal)
                    ? []
                    : ["The source issue has no intent-pr-created marker."],
            };
        }
    }

    private UnitStatusRemoteSnapshot ObserveGitHubAttempt(
        CliContext context,
        string repo,
        int issue,
        int pullRequest,
        string executionUnit,
        string domain,
        string team)
    {
        var facts = new List<UnitStatusFact>();
        var warnings = new List<string>();
        IReadOnlyList<string> observedIssueLabels = [];
        if (!TryReadPullRequest(context, repo, pullRequest, out var before, out var pullFailure))
        {
            return RemoteFailure(pullFailure.Cause, pullFailure.Detail, UnitStatusStates.ReadFailure);
        }

        var headBefore = before.HeadSha;
        facts.Add(LabelFact(
            "approved-marker",
            before.ObservedLabels,
            "intent-pr-approved",
            $"https://github.com/{repo}/pull/{pullRequest}",
            "approval-marker-absent",
            "pull-request-label",
            executionUnit,
            repo,
            pullRequest,
            headBefore));
        var hasApprovalMarker = before.ObservedLabels.Contains("intent-pr-approved", StringComparer.Ordinal);
        facts.Add(hasApprovalMarker
            ? new UnitStatusFact
            {
                Id = "approval-head-receipt",
                State = UnitStatusStates.Unavailable,
                Cause = "approval-head-receipt-not-recorded",
                Detail = "The current approval marker has no supported durable receipt bound to this exact PR head.",
                UnavailableClass = UnitStatusStates.ProvenanceLimit,
                RepairUnavailableReason = "no-supported-historical-receipt-writer",
                Evidence = [GitHubEvidence("pull-request-label", $"https://github.com/{repo}/pull/{pullRequest}", executionUnit, repo, pullRequest, headBefore)],
            }
            : new UnitStatusFact
            {
                Id = "approval-head-receipt",
                State = UnitStatusStates.Missing,
                Cause = "approval-marker-absent",
                Detail = "The approved marker is absent and no exact-head receipt was observed.",
            });

        var issueUrl = $"https://github.com/{repo}/issues/{issue}";
        if (TryReadJson(context, ["api", "--method", "GET", $"repos/{repo}/issues/{issue}"], out var issueDoc, out var issueFailure))
        {
            using (issueDoc)
            {
                if (TryReadNumber(issueDoc!.RootElement, "number", out var observedIssue)
                    && observedIssue == issue
                    && TryReadLabels(issueDoc.RootElement, out var issueLabels))
                {
                    observedIssueLabels = issueLabels;
                    var marker = LabelFact("issue-completion-marker", issueLabels, "intent-pr-created", issueUrl,
                        "issue-pr-created-marker-absent", "issue-label", executionUnit, repo, pullRequest, headBefore);
                    facts.Add(marker);
                    warnings.AddRange(issueLabels.Contains("intent-pr-created", StringComparer.Ordinal)
                        ? []
                        : ["The source issue has no intent-pr-created marker."]);
                }
                else
                {
                    issueFailure = new UnitStatusReadFailure("github-api-error", "Issue response is missing its expected number or labels.");
                    facts.Add(UnavailableFact("issue-completion-marker", issueFailure.Cause, issueFailure.Detail, UnitStatusStates.ReadFailure));
                }
            }
        }
        else
        {
            facts.Add(UnavailableFact("issue-completion-marker", issueFailure.Cause, issueFailure.Detail, UnitStatusStates.ReadFailure));
        }

        var reviewReadOk = TryReadPages(
            context,
            $"repos/{repo}/pulls/{pullRequest}/reviews",
            "",
            out var reviewItems,
            out var reviewFailure);
        var reviewRows = new List<UnitStatusObservedReview>();
        var reviewIdentityFailure = false;
        var reviewNamedUnstructured = false;
        var reviewReadFailure = false;
        var reviewFailureDetail = "";
        var localReviews = CrossRuntimeReviewStore.Read(context.RepoRoot, repo, pullRequest);
        if (reviewReadOk)
        {
            foreach (var item in reviewItems)
            {
                if (!TryReadReview(item, executionUnit, domain, team, repo, pullRequest, localReviews,
                    out var review, out var failureKind, out var parseDetail))
                {
                    reviewIdentityFailure |= failureKind == ReviewParseFailure.IdentityConflict;
                    reviewNamedUnstructured |= failureKind == ReviewParseFailure.ProvenanceLimit;
                    reviewReadFailure |= failureKind == ReviewParseFailure.Malformed;
                    if (failureKind == ReviewParseFailure.Malformed) reviewFailureDetail = parseDetail;
                    continue;
                }

                reviewRows.Add(review!);
                if (review!.Dismissed)
                {
                    warnings.Add($"GitHub review {review.RecordId ?? "(unknown)"} is dismissed and does not satisfy current-head review.");
                }
            }
        }

        if (!TryOrderReviewRows(reviewRows, out var orderedReviewRows, out var reviewOrderingError))
        {
            reviewIdentityFailure = true;
            reviewFailureDetail = reviewOrderingError;
        }
        reviewRows = orderedReviewRows.ToList();

        var reviewInventoryReadable = reviewReadOk && !reviewReadFailure;
        facts.Add(reviewInventoryReadable && reviewIdentityFailure
            ? UnavailableFact("posted-review", "review-identity-conflict", "A structured named review has mismatched or invalid unit/head/kind/cited-record identity.", UnitStatusStates.IdentityConflict)
            : reviewReadOk && reviewReadFailure
                ? UnavailableFact("posted-review", "github-review-invalid", reviewFailureDetail, UnitStatusStates.ReadFailure)
            : reviewReadOk
            ? ReviewFact(reviewRows, headBefore, reviewNamedUnstructured, $"https://github.com/{repo}/pull/{pullRequest}")
            : UnavailableFact("posted-review", reviewFailure.Cause, reviewFailure.Detail, UnitStatusStates.ReadFailure));

        var checks = new List<UnitStatusObservedCheck>();
        var ciFailures = new List<UnitStatusReadFailure>();
        if (TryReadPages(context, $"repos/{repo}/commits/{headBefore}/check-runs", "check_runs", out var checkItems, out var checksFailure))
        {
            var runAttempts = new Dictionary<long, int>();
            var runFailures = new HashSet<long>();
            foreach (var item in checkItems)
            {
                if (!TryReadCheckRun(item, repo, headBefore, out var check, out var runId, out var parseError))
                {
                    ciFailures.Add(new UnitStatusReadFailure("github-api-error", parseError));
                    continue;
                }

                if (runId is { } id)
                {
                    if (!runAttempts.ContainsKey(id) && !runFailures.Contains(id))
                    {
                        if (!TryReadActionRun(context, repo, id, headBefore, out var attemptValue, out var actionFailure))
                        {
                            runFailures.Add(id);
                            ciFailures.Add(actionFailure!);
                        }
                        else
                        {
                            runAttempts[id] = attemptValue!.Value;
                        }
                    }
                    if (runAttempts.TryGetValue(id, out var knownAttempt))
                    {
                        check = check! with
                        {
                            RunId = id,
                            Attempt = knownAttempt == 1 ? 1 : null,
                            AttemptBasis = knownAttempt == 1
                                ? "actions-run-initial-attempt"
                            : "actions-attempt-unattributed",
                            ReportedRunAttempt = knownAttempt,
                        };
                    }
                }
                checks.Add(check!);
            }
        }
        else
        {
            ciFailures.Add(checksFailure);
        }

        if (TryReadPages(context, $"repos/{repo}/commits/{headBefore}/statuses", "", out var statusItems, out var statusesFailure))
        {
            foreach (var item in statusItems)
            {
                if (!TryReadCommitStatus(item, headBefore, out var check, out var parseError))
                {
                    ciFailures.Add(new UnitStatusReadFailure("github-api-error", parseError));
                    continue;
                }
                checks.Add(check!);
            }
        }
        else
        {
            ciFailures.Add(statusesFailure);
        }

        facts.Add(ciFailures.Count > 0
            ? UnavailableFact("observed-ci", ciFailures[0].Cause, ciFailures[0].Detail, UnitStatusStates.ReadFailure)
            : CiFact(checks, headBefore, repo, pullRequest));

        if (!TryReadPullRequest(context, repo, pullRequest, out var after, out var afterFailure))
        {
            return new UnitStatusRemoteSnapshot
            {
                State = UnitStatusStates.Unavailable,
                Cause = afterFailure.Cause,
                Detail = afterFailure.Detail,
                HeadBefore = headBefore,
                HeadAfter = null,
                HeadSha = headBefore,
                Facts = FailureFacts(
                    ["issue-completion-marker", "posted-review", "delta-review", "observed-ci", "approved-marker", "approval-head-receipt", "pr-merged"],
                    afterFailure.Cause,
                    "The PR head could not be re-read after collecting GitHub evidence: " + afterFailure.Detail,
                    UnitStatusStates.ReadFailure),
                Reviews = reviewRows,
                Checks = checks,
                IssueLabels = observedIssueLabels,
                PullRequestLabels = before.ObservedLabels,
                Warnings = warnings,
            };
        }

        var headAfter = after.HeadSha;
        facts.Add(after.Merged
            ? new UnitStatusFact
            {
                Id = "pr-merged",
                State = UnitStatusStates.Done,
                Cause = "pull-request-merged",
                Detail = "The pull-request endpoint reports merged=true.",
                Evidence = [GitHubEvidence("pull-request-merge", $"https://github.com/{repo}/pull/{pullRequest}", executionUnit, repo, pullRequest, headBefore)],
            }
            : new UnitStatusFact
            {
                Id = "pr-merged",
                State = UnitStatusStates.Missing,
                Cause = "pull-request-not-merged",
                Detail = "The pull-request endpoint reports merged=false.",
                Evidence = [GitHubEvidence("pull-request", $"https://github.com/{repo}/pull/{pullRequest}", executionUnit, repo, pullRequest, headBefore)],
            });

        var deltaFailure = reviewReadFailure
            ? new UnitStatusReadFailure("github-review-invalid", reviewFailureDetail)
            : reviewFailure;
        var recordedReviewRows = localReviews.Records
            .Where(item => item.Record.ExecutionUnit == executionUnit && item.Record.Domain == domain && item.Record.Team == team
                && item.Record.Kind == CrossRuntimeReviewRecord.KindImplementation)
            .Select(item => new UnitStatusObservedReview
            {
                Source = "local-cross-runtime-record",
                HeadSha = item.Record.HeadSha,
                Verdict = item.Record.Verdict,
                Runtime = item.Record.Runtime,
                Relation = item.Record.Relation,
                At = item.Record.RecordedAt,
                RecordId = item.RelativePath,
                CitedRecordPath = item.Record.RawVerdictFile,
            });
        var deltaReviewRows = reviewRows.Concat(recordedReviewRows).ToArray();
        var deltaOrderingValid = TryOrderReviewRows(deltaReviewRows, out var orderedDeltaReviewRows, out var deltaOrderingError);
        var deltaIdentityConflict = reviewIdentityFailure || !deltaOrderingValid;
        var deltaLocalReadFailure = localReviews.Unreadable.Count > 0;
        var deltaReviewFailure = deltaIdentityConflict
            ? new UnitStatusReadFailure(
                "review-identity-conflict",
                !deltaOrderingValid ? deltaOrderingError : "Review history contains invalid identity.")
            : deltaLocalReadFailure
                ? new UnitStatusReadFailure("review-history-unavailable", "One or more local review records could not be read or validated.")
            : reviewNamedUnstructured
                ? new UnitStatusReadFailure("legacy-review-identity-unrecorded", "A named independent review lacks exact unit/head identity.")
                : deltaFailure;
        var deltaUnavailableClass = deltaIdentityConflict
            ? UnitStatusStates.IdentityConflict
            : deltaLocalReadFailure || !reviewInventoryReadable
                ? UnitStatusStates.ReadFailure
            : reviewNamedUnstructured ? UnitStatusStates.ProvenanceLimit : UnitStatusStates.ReadFailure;
        facts.Add(BuildDeltaFact(orderedDeltaReviewRows, headBefore, executionUnit, repo, pullRequest,
            reviewInventoryReadable && !reviewIdentityFailure && !reviewNamedUnstructured && !deltaLocalReadFailure && deltaOrderingValid,
            deltaReviewFailure,
            deltaUnavailableClass));
        var stableHead = string.Equals(headBefore, headAfter, StringComparison.OrdinalIgnoreCase);
        var blockingReadFailure = facts.FirstOrDefault(fact => fact.State == UnitStatusStates.Unavailable
            && fact.UnavailableClass != UnitStatusStates.ProvenanceLimit);
        var snapshotUnavailable = !stableHead || blockingReadFailure is not null;
        return new UnitStatusRemoteSnapshot
        {
            State = snapshotUnavailable ? UnitStatusStates.Unavailable : "completed",
            Cause = !stableHead ? "snapshot-head-changed" : blockingReadFailure?.Cause,
            Detail = !stableHead
                ? "The PR head changed during this snapshot attempt."
                : blockingReadFailure is not null
                    ? "One or more bounded GitHub reads failed; successful local and remote observations remain visible. " + blockingReadFailure.Detail
                    : "Read-only PR, review, and observed-check inventory was bracketed by matching head snapshots.",
            HeadBefore = headBefore,
            HeadAfter = headAfter,
            HeadSha = headAfter,
            MergeCommitSha = after.MergeCommitSha,
            Merged = after.Merged,
            IssueLabels = observedIssueLabels,
            PullRequestLabels = before.ObservedLabels,
            Reviews = reviewRows,
            Checks = checks,
            Facts = facts,
            Warnings = warnings,
        };
    }

    private bool TryReadPullRequest(CliContext context, string repo, int pullRequest, out UnitStatusPullRequestRead pull, out UnitStatusReadFailure failure)
    {
        pull = default!;
        if (!TryReadJson(context, ["api", "--method", "GET", $"repos/{repo}/pulls/{pullRequest}"], out var document, out failure))
        {
            return false;
        }

        using (document)
        {
            var root = document!.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryReadNumber(root, "number", out var number)
                || number != pullRequest
                || !root.TryGetProperty("head", out var head)
                || !TryReadString(head, "sha", out var sha)
                || !IsSha(sha)
                || !TryReadBool(root, "merged", out var merged)
                || !TryReadLabels(root, out var labels))
            {
                failure = new UnitStatusReadFailure("github-api-error", "Pull-request response is missing its expected identity, head, merge state, or labels.");
                return false;
            }

            _ = TryReadNullableString(root, "merge_commit_sha", out var mergeCommit);
            pull = new UnitStatusPullRequestRead(sha, merged, mergeCommit, labels);
            failure = default!;
            return true;
        }
    }

    internal bool TryReadJson(
        CliContext context,
        IReadOnlyList<string> arguments,
        out JsonDocument? document,
        out UnitStatusReadFailure failure)
    {
        document = null;
        if (!IsAllowedReadOnlyRequest(true, arguments))
        {
            failure = new UnitStatusReadFailure(
                "github-request-invalid",
                "The request is outside the fixed read-only GitHub REST endpoint and pagination allowlist.");
            return false;
        }

        var result = githubRunner.Run(arguments);
        if (result.ExitCode != 0)
        {
            var code = Regex.Match(result.StdErr ?? string.Empty, @"\b(401|403|404|429|5\d\d)\b", RegexOptions.CultureInvariant);
            failure = new UnitStatusReadFailure(
                "github-read-failed",
                code.Success ? $"GitHub read failed with HTTP {code.Value}." : $"GitHub read failed with exit code {result.ExitCode}.");
            return false;
        }

        try
        {
            document = JsonDocument.Parse(result.StdOut);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("message", out _)
                && !document.RootElement.TryGetProperty("number", out _)
                && !document.RootElement.TryGetProperty("head", out _))
            {
                document.Dispose();
                document = null;
                failure = new UnitStatusReadFailure("github-api-error", "GitHub returned an error object for a successful GET request.");
                return false;
            }
        }
        catch (JsonException)
        {
            failure = new UnitStatusReadFailure("github-json-invalid", "GitHub GET response was not valid JSON.");
            return false;
        }

        failure = default!;
        return true;
    }

    private bool TryReadPages(
        CliContext context,
        string endpoint,
        string arrayProperty,
        out IReadOnlyList<JsonElement> items,
        out UnitStatusReadFailure failure)
    {
        var collected = new List<JsonElement>();
        for (var page = 1; page <= MaximumPages; page++)
        {
            var separator = endpoint.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            var filter = endpoint.EndsWith("/check-runs", StringComparison.Ordinal) ? "&filter=all" : "";
            var path = $"{endpoint}{separator}per_page={PageSize}&page={page.ToString(CultureInfo.InvariantCulture)}{filter}";
            if (!TryReadJson(context, ["api", "--method", "GET", path], out var document, out failure))
            {
                items = collected;
                return false;
            }

            using (document)
            {
                var root = document!.RootElement;
                JsonElement array;
                int? total = null;
                if (arrayProperty.Length == 0)
                {
                    array = root;
                }
                else if (root.ValueKind == JsonValueKind.Object
                         && root.TryGetProperty(arrayProperty, out array)
                         && root.TryGetProperty("total_count", out var totalCount)
                         && totalCount.TryGetInt32(out var parsedTotal))
                {
                    total = parsedTotal;
                }
                else
                {
                    items = collected;
                    failure = new UnitStatusReadFailure("github-api-error", $"GitHub page is missing its '{arrayProperty}' array or total_count.");
                    return false;
                }

                if (array.ValueKind != JsonValueKind.Array)
                {
                    items = collected;
                    failure = new UnitStatusReadFailure("github-api-error", "GitHub paginated response is not an array.");
                    return false;
                }

                var pageItems = array.EnumerateArray().Select(item => item.Clone()).ToArray();
                collected.AddRange(pageItems);
                if (total is { } expected && (expected < 0 || collected.Count > expected))
                {
                    items = collected;
                    failure = new UnitStatusReadFailure("github-api-error", "GitHub page count exceeds its reported total_count.");
                    return false;
                }

                var complete = total is { } expectedCount
                    ? collected.Count >= expectedCount
                    : pageItems.Length < PageSize;
                if (complete)
                {
                    items = collected;
                    failure = default!;
                    return true;
                }

                if (page == MaximumPages)
                {
                    items = collected;
                    failure = new UnitStatusReadFailure("pagination-bound-exhausted", "GitHub pagination exceeded the 20-page observation bound.");
                    return false;
                }
            }
        }

        items = collected;
        failure = new UnitStatusReadFailure("pagination-bound-exhausted", "GitHub pagination exceeded the 20-page observation bound.");
        return false;
    }

    private bool TryReadActionRun(
        CliContext context,
        string repo,
        long runId,
        string expectedHead,
        out int? attempt,
        out UnitStatusReadFailure? failure)
    {
        attempt = null;
        if (!TryReadJson(context, ["api", "--method", "GET", $"repos/{repo}/actions/runs/{runId.ToString(CultureInfo.InvariantCulture)}"], out var document, out var readFailure))
        {
            failure = readFailure;
            return false;
        }

        using (document)
        {
            var root = document!.RootElement;
            if (!TryReadLong(root, "id", out var returnedId)
                || returnedId != runId
                || !TryReadString(root, "head_sha", out var sha)
                || !string.Equals(sha, expectedHead, StringComparison.OrdinalIgnoreCase)
                || !TryReadInt(root, "run_attempt", out var attemptNumber)
                || attemptNumber <= 0)
            {
                failure = new UnitStatusReadFailure("github-api-error", "Actions run response is missing its expected ID, head SHA, or attempt.");
                return false;
            }

            attempt = attemptNumber;
        }

        failure = null;
        return true;
    }

    private bool TryReadReview(
        JsonElement item,
        string unit,
        string domain,
        string team,
        string repo,
        int pullRequest,
        CrossRuntimeReviewReadResult localReviews,
        out UnitStatusObservedReview? review,
        out ReviewParseFailure failureKind,
        out string failureDetail)
    {
        review = null;
        failureKind = ReviewParseFailure.None;
        failureDetail = "";
        if (!TryReadLong(item, "id", out var id)
            || !TryReadString(item, "state", out var state)
            || !TryReadNullableString(item, "commit_id", out var commitId)
            || !TryReadNullableString(item, "body", out var body))
        {
            failureKind = ReviewParseFailure.Malformed;
            failureDetail = "Pull-request review response is missing id, state, commit_id, or body fields.";
            return false;
        }

        if (state is not ("PENDING" or "COMMENTED" or "APPROVED" or "CHANGES_REQUESTED" or "DISMISSED"))
        {
            failureKind = ReviewParseFailure.Malformed;
            failureDetail = "Pull-request review response has an unknown state.";
            return false;
        }

        var reviewUrl = TryReadNullableString(item, "html_url", out var htmlUrl) ? htmlUrl : null;
        var reviewer = item.TryGetProperty("user", out var user) && TryReadString(user, "login", out var login) ? login : null;
        var submittedAt = TryReadNullableDateTime(item, "submitted_at", out var at) ? at : null;
        if (string.IsNullOrWhiteSpace(body)) return false;

        var parsed = ParseReviewBody(body);
        if (!parsed.NamedIndependent)
        {
            return false;
        }
        if (parsed.Conflicting)
        {
            failureKind = ReviewParseFailure.IdentityConflict;
            failureDetail = "The structured review body contains conflicting repeated identity fields.";
            return false;
        }
        if (!parsed.IsStructured)
        {
            failureKind = ReviewParseFailure.ProvenanceLimit;
            return false;
        }

        if (!string.Equals(parsed.ExecutionUnit, unit, StringComparison.Ordinal)
            || !string.Equals(parsed.Kind, CrossRuntimeReviewRecord.KindImplementation, StringComparison.Ordinal))
        {
            review = new UnitStatusObservedReview
            {
                Source = "github-pr-review",
                HeadSha = parsed.HeadSha!,
                Verdict = parsed.Verdict!,
                ReviewState = state,
                Reviewer = reviewer,
                Runtime = parsed.Runtime,
                Relation = parsed.Relation,
                At = submittedAt,
                RecordId = id.ToString(CultureInfo.InvariantCulture),
                Url = reviewUrl,
                Qualification = !string.Equals(parsed.ExecutionUnit, unit, StringComparison.Ordinal)
                    ? "nonqualifying-other-unit-review"
                    : "nonqualifying-design-review",
                Dismissed = string.Equals(state, "DISMISSED", StringComparison.OrdinalIgnoreCase),
            };
            return true;
        }

        var dismissed = string.Equals(state, "DISMISSED", StringComparison.OrdinalIgnoreCase);
        var valid = parsed.HeadSha is not null
            && IsObjectId(parsed.HeadSha)
            && string.Equals(parsed.HeadSha, commitId, StringComparison.OrdinalIgnoreCase)
            && parsed.Verdict is CrossRuntimeReviewVerdict.Approve or CrossRuntimeReviewVerdict.RequestChanges
            && (parsed.Relation is null
                || CrossRuntimeReviewRuntimes.IsSupported(parsed.Runtime)
                && CrossRuntimeReviewRuntimes.IsSupported(parsed.ConductorRuntime)
                && (parsed.Relation == "same-runtime"
                    ? string.Equals(parsed.Runtime, parsed.ConductorRuntime, StringComparison.Ordinal)
                    : !string.Equals(parsed.Runtime, parsed.ConductorRuntime, StringComparison.Ordinal)));
        if (!valid)
        {
            failureKind = ReviewParseFailure.IdentityConflict;
            return false;
        }

        string? citedRecordPath = null;
        if (parsed.CitedRecord is { } citedRecord
            && !TryValidateCitedReviewRecord(localReviews, citedRecord, unit, domain, team, repo, pullRequest, parsed, out citedRecordPath))
        {
            failureKind = ReviewParseFailure.IdentityConflict;
            return false;
        }
        review = new UnitStatusObservedReview
        {
            Source = "github-pr-review",
            HeadSha = parsed.HeadSha!,
            Verdict = parsed.Verdict!,
            ReviewState = state,
            Reviewer = reviewer,
            Runtime = parsed.Runtime,
            Relation = parsed.Relation,
            At = submittedAt,
            RecordId = id.ToString(CultureInfo.InvariantCulture),
            CitedRecordPath = citedRecordPath,
            Url = reviewUrl,
            Qualification = "implementation-review",
            Dismissed = dismissed,
        };
        return true;
    }

    private static bool TryOrderReviewRows(
        IEnumerable<UnitStatusObservedReview> reviews,
        out IReadOnlyList<UnitStatusObservedReview> ordered,
        out string detail)
    {
        var rows = reviews.ToArray();
        var ambiguous = rows
            .GroupBy(review => (
                review.Source,
                Head: review.HeadSha.ToUpperInvariant(),
                Reviewer: review.Reviewer?.ToUpperInvariant() ?? "",
                Runtime: review.Runtime?.ToUpperInvariant() ?? "",
                Relation: review.Relation?.ToUpperInvariant() ?? ""))
            .SelectMany(group => group.GroupBy(review => (review.At, review.RecordId)))
            .Any(tied => tied.Select(review => (review.Verdict, review.ReviewState, review.Dismissed)).Distinct().Skip(1).Any());

        ordered = rows
            .OrderBy(review => review.Source, StringComparer.Ordinal)
            .ThenBy(review => review.HeadSha, StringComparer.OrdinalIgnoreCase)
            .ThenBy(review => review.Reviewer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(review => review.Runtime, StringComparer.OrdinalIgnoreCase)
            .ThenBy(review => review.Relation, StringComparer.OrdinalIgnoreCase)
            .ThenBy(review => review.At)
            .ThenBy(review => review.RecordId, StringComparer.Ordinal)
            .ToArray();
        detail = ambiguous
            ? "Review rows with the same source, head, reviewer/runtime relation, submission time, and record ID contain conflicting dispositions."
            : "";
        return !ambiguous;
    }

    private static bool TryValidateCitedReviewRecord(
        CrossRuntimeReviewReadResult localReviews,
        string citedRecord,
        string unit,
        string domain,
        string team,
        string repo,
        int pullRequest,
        UnitStatusReviewBodyParse parsed,
        out string? recordPath)
    {
        recordPath = null;
        if (localReviews.Unreadable.Any(item => string.Equals(item.RelativePath, citedRecord, StringComparison.Ordinal))) return false;
        var stored = localReviews.Records.FirstOrDefault(item => string.Equals(item.RelativePath, citedRecord, StringComparison.Ordinal));
        if (stored is null) return false;
        var record = stored.Record;
        if (!string.Equals(record.Repo, repo, StringComparison.OrdinalIgnoreCase)
            || record.Pr != pullRequest
            || !string.Equals(record.ExecutionUnit, unit, StringComparison.Ordinal)
            || !string.Equals(record.Domain, domain, StringComparison.Ordinal)
            || !string.Equals(record.Team, team, StringComparison.Ordinal)
            || !string.Equals(record.Kind, CrossRuntimeReviewRecord.KindImplementation, StringComparison.Ordinal)
            || !string.Equals(record.HeadSha, parsed.HeadSha, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(record.Runtime, parsed.Runtime, StringComparison.Ordinal)
            || !string.Equals(record.ConductorRuntime, parsed.ConductorRuntime, StringComparison.Ordinal)
            || !string.Equals(record.Verdict, parsed.Verdict, StringComparison.Ordinal))
        {
            return false;
        }

        recordPath = stored.RelativePath;
        return true;
    }

    private static UnitStatusReviewBodyParse ParseReviewBody(string body)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? heading = null;
        string? citedRecord = null;
        var conflicting = false;
        foreach (var rawLine in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal)) heading = line[3..].Trim();
            if (line.StartsWith("Recorded as `", StringComparison.Ordinal)
                && line.Contains("` by `intent-cli review cross-runtime record`", StringComparison.Ordinal))
            {
                var close = line.IndexOf('`', "Recorded as `".Length);
                if (close > "Recorded as `".Length) citedRecord = line["Recorded as `".Length..close];
            }

            if (line.StartsWith("- ", StringComparison.Ordinal)) line = line[2..].Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim().ToLowerInvariant() switch
            {
                "head sha" => "head_sha",
                "execution unit" => "execution_unit",
                "conductor runtime" => "conductor_runtime",
                "runtime version" => "runtime_version",
                var other => other,
            };
            var value = line[(colon + 1)..].Trim().Trim('`');
            if (fields.TryGetValue(key, out var previous) && !string.Equals(previous, value, StringComparison.Ordinal))
            {
                conflicting = true;
            }
            else
            {
                fields[key] = value;
            }
        }

        var relation = heading?.StartsWith("Cross-runtime review:", StringComparison.Ordinal) == true
            ? "cross-runtime"
            : heading?.StartsWith("Independent same-runtime subagent review:", StringComparison.Ordinal) == true
                ? "same-runtime"
                : null;
        var headingVerdict = relation is null || heading is null ? null : heading[(heading.IndexOf(':') + 1)..].Trim();
        var reviewer = fields.GetValueOrDefault("reviewer");
        var namedIndependent = relation is not null
            || string.Equals(reviewer, "independent same-runtime subagent review", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reviewer, "independent subagent review", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reviewer, "cross-runtime review", StringComparison.OrdinalIgnoreCase);
        var verdict = fields.GetValueOrDefault("verdict");
        var canonicalRelationMatchesReviewer = relation switch
        {
            "cross-runtime" => string.Equals(reviewer, "cross-runtime review", StringComparison.OrdinalIgnoreCase),
            "same-runtime" => string.Equals(reviewer, "independent same-runtime subagent review", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
        var structured = !conflicting
            && namedIndependent
            && canonicalRelationMatchesReviewer
            && !string.IsNullOrWhiteSpace(fields.GetValueOrDefault("execution_unit"))
            && !string.IsNullOrWhiteSpace(fields.GetValueOrDefault("head_sha"))
            && !string.IsNullOrWhiteSpace(fields.GetValueOrDefault("kind"))
            && !string.IsNullOrWhiteSpace(verdict)
            && (relation is null || string.Equals(headingVerdict, verdict, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(fields.GetValueOrDefault("runtime"))
                && !string.IsNullOrWhiteSpace(fields.GetValueOrDefault("conductor_runtime")));
        return new UnitStatusReviewBodyParse(
            structured,
            namedIndependent,
            relation,
            fields.GetValueOrDefault("execution_unit"),
            fields.GetValueOrDefault("head_sha"),
            fields.GetValueOrDefault("kind"),
            verdict,
            fields.GetValueOrDefault("runtime"),
            fields.GetValueOrDefault("conductor_runtime"),
            citedRecord,
            conflicting);
    }

    private bool TryReadCheckRun(JsonElement item, string expectedRepo, string expectedHead, out UnitStatusObservedCheck? check, out long? runId, out string error)
    {
        check = null;
        runId = null;
        if (!TryReadLong(item, "id", out var id)
            || !TryReadString(item, "name", out var name)
            || !TryReadString(item, "status", out var status)
            || !TryReadNullableString(item, "conclusion", out var conclusion)
            || !TryReadString(item, "head_sha", out var sha)
            || !item.TryGetProperty("app", out var app)
            || !TryReadString(app, "slug", out var appSlug))
        {
            error = "Check-run response is missing id, name, app, status, conclusion, or head_sha.";
            return false;
        }

        if (!string.Equals(sha, expectedHead, StringComparison.OrdinalIgnoreCase))
        {
            error = "Check-run head_sha differs from the observed pull-request head.";
            return false;
        }

        string? detailsUrl = TryReadNullableString(item, "details_url", out var readDetails) ? readDetails : null;
        if (string.Equals(appSlug, "github-actions", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryGetActionsRunId(detailsUrl, expectedRepo, out var actionsRunId))
            {
                error = "GitHub Actions check-run details URL does not identify a run in the observed repository.";
                return false;
            }
            runId = actionsRunId;
        }

        check = new UnitStatusObservedCheck
        {
            Source = "check-run",
            Identity = $"{appSlug}/{name}",
            RecordId = id.ToString(CultureInfo.InvariantCulture),
            Sha = sha,
            Status = status,
            Conclusion = conclusion,
            RunId = runId,
            AttemptBasis = runId is null ? "not-actions" : null,
            Url = detailsUrl,
        };
        error = string.Empty;
        return true;
    }

    private static bool TryGetActionsRunId(string? detailsUrl, string expectedRepo, out long runId)
    {
        runId = 0;
        if (!Uri.TryCreate(detailsUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var match = Regex.Match(uri.AbsolutePath, @"^/(?<repo>[^/]+/[^/]+)/actions/runs/(?<id>[0-9]+)(?:/|$)", RegexOptions.CultureInvariant);
        return match.Success
            && string.Equals(match.Groups["repo"].Value, expectedRepo, StringComparison.OrdinalIgnoreCase)
            && long.TryParse(match.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out runId);
    }

    private static bool TryReadCommitStatus(JsonElement item, string expectedHead, out UnitStatusObservedCheck? check, out string error)
    {
        check = null;
        if (!TryReadLong(item, "id", out var id)
            || !TryReadString(item, "context", out var context)
            || !TryReadString(item, "state", out var state))
        {
            error = "Commit-status response is missing id, context, or state.";
            return false;
        }

        if (item.TryGetProperty("sha", out var shaValue)
            && (shaValue.ValueKind != JsonValueKind.String
                || !string.Equals(shaValue.GetString(), expectedHead, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Optional commit-status sha differs from the exact commit referenced by the successful request.";
            return false;
        }

        _ = TryReadNullableString(item, "target_url", out var url);
        check = new UnitStatusObservedCheck
        {
            Source = "commit-status",
            Identity = context,
            RecordId = id.ToString(CultureInfo.InvariantCulture),
            // The list-statuses endpoint has no per-row sha field; the exact commit
            // object in the validated request path supplies this identity.
            Sha = expectedHead,
            Status = state,
            AttemptBasis = "not-actions",
            Url = url,
        };
        error = string.Empty;
        return true;
    }

    internal string? ReadGitText(
        string repoRoot,
        IReadOnlyList<string> arguments,
        out string? error,
        string? metadataRef = null,
        string? snapshotOid = null,
        string? claimPath = null,
        string? historyDirectory = null,
        string? expectedPath = null)
    {
        if (!IsAllowedReadOnlyRequest(
                false,
                arguments,
                metadataRef,
                snapshotOid,
                claimPath,
                historyDirectory,
                expectedPath))
        {
            error = "Git request is outside the fixed local snapshot forms, object ID, or contained-path allowlist.";
            return null;
        }

        var result = gitRunner.Run(repoRoot, arguments);
        if (result.ExitCode != 0 || result.TimedOut)
        {
            error = $"Local git read failed with exit code {result.ExitCode}.";
            return null;
        }

        error = null;
        return result.StdOut.Trim();
    }

    private static bool IsAllowedReadOnlyRequest(
        bool github,
        IReadOnlyList<string> arguments,
        string? metadataRef = null,
        string? snapshotOid = null,
        string? claimPath = null,
        string? historyDirectory = null,
        string? expectedPath = null) => github
        ? IsAllowedGitHubRequest(arguments)
        : IsAllowedGitRequest(arguments, metadataRef, snapshotOid, claimPath, historyDirectory, expectedPath);

    private static bool IsAllowedGitHubRequest(IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 4
            || arguments[0] != "api"
            || arguments[1] != "--method"
            || arguments[2] != "GET")
        {
            return false;
        }

        var endpoint = arguments[3];
        var queryIndex = endpoint.IndexOf('?', StringComparison.Ordinal);
        var path = queryIndex < 0 ? endpoint : endpoint[..queryIndex];
        var parts = path.Split('/');
        if (parts.Length < 5
            || parts[0] != "repos"
            || !IsSafeRepositorySegment(parts[1])
            || !IsSafeRepositorySegment(parts[2]))
        {
            return false;
        }

        if (queryIndex >= 0)
        {
            var checkRunsEndpoint = parts.Length == 6
                && parts[3] == "commits"
                && IsObjectId(parts[4])
                && parts[5] == "check-runs";
            if (endpoint.IndexOf('?', queryIndex + 1) >= 0
                || !IsAllowedPaginationQuery(endpoint[(queryIndex + 1)..], checkRunsEndpoint))
            {
                return false;
            }

            return parts.Length == 6
                && ((parts[3] == "pulls" && IsPositiveNumber(parts[4]) && parts[5] == "reviews")
                    || (parts[3] == "commits" && IsObjectId(parts[4]) && parts[5] is ("check-runs" or "statuses")));
        }

        return (parts.Length == 5
                && parts[3] is ("pulls" or "issues")
                && IsPositiveNumber(parts[4]))
            || (parts.Length == 6
                && parts[3] == "actions"
                && parts[4] == "runs"
                && IsPositiveNumber(parts[5]));
    }

    private static bool IsAllowedPaginationQuery(string query, bool checkRunsEndpoint)
    {
        const string prefix = "per_page=100&page=";
        if (!query.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var suffixIndex = query.IndexOf('&', prefix.Length);
        var pageText = suffixIndex < 0 ? query[prefix.Length..] : query[prefix.Length..suffixIndex];
        var suffix = suffixIndex < 0 ? "" : query[suffixIndex..];
        if (!string.Equals(suffix, checkRunsEndpoint ? "&filter=all" : "", StringComparison.Ordinal)) return false;
        return int.TryParse(pageText, NumberStyles.None, CultureInfo.InvariantCulture, out var page)
            && page is >= 1 and <= MaximumPages
            && string.Equals(page.ToString(CultureInfo.InvariantCulture), pageText, StringComparison.Ordinal);
    }

    private static bool IsAllowedGitRequest(
        IReadOnlyList<string> arguments,
        string? metadataRef,
        string? snapshotOid,
        string? claimPath,
        string? historyDirectory,
        string? expectedPath)
    {
        if (arguments.Count == 3
            && arguments[0] == "rev-parse"
            && arguments[1] == "--verify"
            && arguments[2] == "HEAD")
        {
            return metadataRef is null && snapshotOid is null && claimPath is null && historyDirectory is null && expectedPath is null;
        }

        if (arguments.Count == 3
            && arguments[0] == "rev-parse"
            && arguments[1] == "--abbrev-ref"
            && arguments[2] == "HEAD")
        {
            return metadataRef is null && snapshotOid is null && claimPath is null && historyDirectory is null && expectedPath is null;
        }

        if (arguments.Count == 3
            && arguments[0] == "rev-parse"
            && arguments[1] == "--verify"
            && metadataRef is not null
            && IsSafeMetadataRef(metadataRef)
            && string.Equals(arguments[2], $"{metadataRef}^{{commit}}", StringComparison.Ordinal))
        {
            return snapshotOid is null && claimPath is null && historyDirectory is null && expectedPath is null;
        }

        if (arguments.Count == 8
            && arguments[0] == "ls-tree"
            && arguments[1] == "-r"
            && arguments[2] == "-z"
            && arguments[3] == "--name-only"
            && IsObjectId(arguments[4])
            && snapshotOid is not null
            && string.Equals(arguments[4], snapshotOid, StringComparison.OrdinalIgnoreCase)
            && arguments[5] == "--"
            && claimPath is not null
            && historyDirectory is not null
            && IsCanonicalClaimPath(claimPath)
            && IsCanonicalHistoryDirectory(historyDirectory, claimPath)
            && IsSafeRepoRelativePath(claimPath)
            && IsSafeRepoRelativePath(historyDirectory)
            && string.Equals(arguments[6], claimPath, StringComparison.Ordinal)
            && string.Equals(arguments[7], historyDirectory, StringComparison.Ordinal))
        {
            return metadataRef is null && expectedPath is null;
        }

        if (arguments.Count == 2
            && arguments[0] == "show"
            && snapshotOid is not null
            && IsObjectId(snapshotOid)
            && claimPath is not null
            && historyDirectory is not null
            && expectedPath is not null
            && IsCanonicalClaimPath(claimPath)
            && IsCanonicalHistoryDirectory(historyDirectory, claimPath)
            && IsSafeRepoRelativePath(claimPath)
            && IsSafeRepoRelativePath(historyDirectory)
            && IsAllowedClaimPath(expectedPath, claimPath, historyDirectory)
            && string.Equals(arguments[1], $"{snapshotOid}:{expectedPath}", StringComparison.Ordinal))
        {
            return metadataRef is null;
        }

        return false;
    }

    private static bool IsSafeRepositorySegment(string value) =>
        value.Length is > 0 and <= 100
        && char.IsAsciiLetterOrDigit(value[0])
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static bool IsPositiveNumber(string value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
        && number > 0
        && string.Equals(number.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal);

    private static bool IsSafeMetadataRef(string value)
    {
        const string prefix = "refs/remotes/origin/";
        if (!value.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var branch = value[prefix.Length..];
        if (branch.Length == 0
            || branch.StartsWith("/", StringComparison.Ordinal)
            || branch.EndsWith("/", StringComparison.Ordinal)
            || branch.EndsWith(".", StringComparison.Ordinal)
            || branch.Contains("..", StringComparison.Ordinal)
            || branch.Contains("//", StringComparison.Ordinal)
            || branch.Contains("@{", StringComparison.Ordinal)
            || branch.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)
                || character is '~' or '^' or ':' or '?' or '*' or '[' or '\\'))
        {
            return false;
        }

        return branch.Split('/').All(segment => segment is not ("" or "." or ".."));
    }

    private static bool IsSafeRepoRelativePath(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && !Path.IsPathRooted(value)
        && !value.Contains('\\')
        && !value.Contains(':')
        && value.Split('/').All(segment => segment is not ("" or "." or ".."));

    private static bool IsCanonicalClaimPath(string path)
    {
        const string prefix = ".intent-cli/claims/";
        const string suffix = ".json";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)
            || !path.EndsWith(suffix, StringComparison.Ordinal)) return false;
        var digest = path[prefix.Length..^suffix.Length];
        return digest.Length == 64
            && digest.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static bool IsCanonicalHistoryDirectory(string path, string claimPath) =>
        string.Equals(
            path,
            $".intent-cli/claims/history/{Path.GetFileNameWithoutExtension(claimPath)}",
            StringComparison.Ordinal);

    private static bool IsAllowedClaimPath(string path, string claimPath, string historyDirectory)
    {
        if (!IsSafeRepoRelativePath(path)
            || !IsCanonicalClaimPath(claimPath)
            || !IsCanonicalHistoryDirectory(historyDirectory, claimPath)
            || !IsSafeRepoRelativePath(claimPath)
            || !IsSafeRepoRelativePath(historyDirectory)) return false;
        if (string.Equals(path, claimPath, StringComparison.Ordinal)) return true;
        var prefix = historyDirectory + "/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var fileName = path[prefix.Length..];
        return fileName.Length > 5
            && fileName.EndsWith(".json", StringComparison.Ordinal)
            && fileName.IndexOf('/') < 0
            && fileName.IndexOf('\\') < 0;
    }

    private static UnitStatusClaimSnapshot ClaimFailure(
        string cause,
        string detail,
        string unavailableClass,
        string? metadataRef = null,
        string? localHeadSha = null,
        string? localHeadRef = null,
        string? metadataOid = null) => new()
    {
        State = UnitStatusStates.Unavailable,
        Cause = cause,
        Detail = detail,
        MetadataRef = metadataRef,
        MetadataOid = metadataOid,
        LocalHeadSha = localHeadSha,
        LocalHeadRef = localHeadRef,
        UnavailableClass = unavailableClass,
        Warnings = [detail],
    };

    private static UnitStatusRemoteSnapshot RemoteFailure(string cause, string detail, string unavailableClass) => new()
    {
        State = UnitStatusStates.Unavailable,
        Cause = cause,
        Detail = detail,
        Facts = FailureFacts(
            ["issue-completion-marker", "posted-review", "delta-review", "observed-ci", "approved-marker", "approval-head-receipt", "pr-merged"],
            cause,
            detail,
            unavailableClass),
        Warnings = [detail],
    };

    private static IReadOnlyList<UnitStatusFact> FailureFacts(
        IReadOnlyList<string> ids,
        string cause,
        string detail,
        string unavailableClass) => ids.Select(id => UnavailableFact(id, cause, detail, unavailableClass)).ToArray();

    private static UnitStatusFact UnavailableFact(string id, string cause, string detail, string unavailableClass) => new()
    {
        Id = id,
        State = UnitStatusStates.Unavailable,
        Cause = cause,
        Detail = detail,
        UnavailableClass = unavailableClass,
    };

    private static UnitStatusFact LabelFact(
        string id,
        IReadOnlyList<string> labels,
        string expected,
        string url,
        string absentCause,
        string evidenceKind,
        string executionUnit,
        string repo,
        int pullRequest,
        string head) => labels.Contains(expected, StringComparer.Ordinal)
        ? new UnitStatusFact
        {
            Id = id,
            State = UnitStatusStates.Done,
            Cause = $"{expected}-observed",
            Detail = $"The current endpoint response includes label '{expected}'.",
            Evidence = [GitHubEvidence(evidenceKind, url, executionUnit, repo, pullRequest, head)],
        }
        : new UnitStatusFact
        {
            Id = id,
            State = UnitStatusStates.Missing,
            Cause = absentCause,
            Detail = $"The successful current label response does not include '{expected}'.",
            Evidence = [GitHubEvidence(evidenceKind + "s", url, executionUnit, repo, pullRequest, head)],
        };

    private static UnitStatusFact ReviewFact(
        IReadOnlyList<UnitStatusObservedReview> reviews,
        string currentHead,
        bool namedUnstructured,
        string url)
    {
        if (reviews.Any(review => IsSubmittedReview(review) && IsImplementationReview(review)
            && string.Equals(review.HeadSha, currentHead, StringComparison.OrdinalIgnoreCase)))
        {
            return new UnitStatusFact
            {
                Id = "posted-review",
                State = UnitStatusStates.Done,
                Cause = "posted-review-current-head",
                Detail = "At least one structured independent review is attached to the observed PR head; verdict and GitHub review state remain separate.",
                Evidence = reviews.Where(review => IsSubmittedReview(review) && IsImplementationReview(review)
                    && string.Equals(review.HeadSha, currentHead, StringComparison.OrdinalIgnoreCase)).Select(review => new UnitStatusEvidencePointer
                {
                    Kind = "github-pr-review",
                    Url = review.Url,
                    RecordId = review.RecordId,
                    HeadSha = review.HeadSha,
                    RecordedAt = review.At,
                    ReviewVerdict = review.Verdict,
                    ReviewState = review.ReviewState,
                    ReviewDisposition = review.Qualification,
                    Provenance = "github-rest-review",
                }).ToArray(),
            };
        }

        if (namedUnstructured)
        {
            return new UnitStatusFact
            {
                Id = "posted-review",
                State = UnitStatusStates.Unavailable,
                Cause = "legacy-review-identity-unrecorded",
                Detail = "A named review lacks the structured unit, kind, head, verdict, or matching commit identity needed for attribution.",
                UnavailableClass = UnitStatusStates.ProvenanceLimit,
                RepairUnavailableReason = "no-supported-historical-review-identity-writer",
                Evidence = [GitHubEvidence("github-pr-review", url, null, null, null, currentHead)],
            };
        }

        return new UnitStatusFact
        {
            Id = "posted-review",
            State = UnitStatusStates.Missing,
            Cause = "posted-review-not-recorded-current-head",
            Detail = "The pull-request review list contains no submitted, non-dismissed structured independent review bound to this head.",
            Evidence = reviews.Select(review => new UnitStatusEvidencePointer
            {
                Kind = "github-pr-review",
                Url = review.Url,
                RecordId = review.RecordId,
                HeadSha = review.HeadSha,
                RecordedAt = review.At,
                ReviewVerdict = review.Verdict,
                ReviewState = review.ReviewState,
                ReviewDisposition = review.Qualification,
                Provenance = "github-rest-review",
            }).ToArray(),
        };
    }

    private static bool IsSubmittedReview(UnitStatusObservedReview review) =>
        !review.Dismissed
        && (review.Source != "github-pr-review"
            || review.ReviewState is "COMMENTED" or "APPROVED" or "CHANGES_REQUESTED");

    private static bool IsImplementationReview(UnitStatusObservedReview review) =>
        review.Qualification is null or "implementation-review";

    private static UnitStatusFact BuildDeltaFact(
        IReadOnlyList<UnitStatusObservedReview> reviews,
        string currentHead,
        string executionUnit,
        string repo,
        int pullRequest,
        bool readSucceeded,
        UnitStatusReadFailure reviewFailure,
        string unavailableClass)
    {
        if (!readSucceeded)
        {
            return UnavailableFact("delta-review", reviewFailure.Cause, reviewFailure.Detail, unavailableClass);
        }

        var currentHeadReviews = reviews
            .Where(review => IsSubmittedReview(review) && IsImplementationReview(review)
                && string.Equals(review.HeadSha, currentHead, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (currentHeadReviews.Length > 0)
        {
            return new UnitStatusFact
            {
                Id = "delta-review",
                State = UnitStatusStates.Done,
                Cause = "delta-review-current-head",
                Detail = "A non-dismissed structured review exists for the current PR head.",
                Evidence = currentHeadReviews.Select(review => new UnitStatusEvidencePointer
                {
                    Kind = review.Source,
                    Path = review.Source == "local-cross-runtime-record" ? review.RecordId : null,
                    Url = review.Url,
                    RecordId = review.RecordId,
                    ExecutionUnit = executionUnit,
                    Repo = repo,
                    Pr = pullRequest,
                    HeadSha = review.HeadSha,
                    RecordedAt = review.At,
                    ReviewVerdict = review.Verdict,
                    ReviewState = review.ReviewState,
                    ReviewDisposition = review.Qualification ?? (review.Source == "local-cross-runtime-record" ? "local-record-not-github" : null),
                    Provenance = review.Source == "local-cross-runtime-record"
                        ? "digest-validated-local-cross-runtime-review-record"
                        : "github-rest-review",
                }).ToArray(),
            };
        }

        return new UnitStatusFact
        {
            Id = "delta-review",
            State = UnitStatusStates.Missing,
            Cause = "delta-review-missing",
            Detail = "No submitted, non-dismissed structured review is bound to the current head; pending and earlier-head reviews remain visible but do not satisfy the delta observation.",
            Evidence = reviews.Where(review => review.Source == "github-pr-review" && IsImplementationReview(review)
                    && string.Equals(review.ReviewState, "PENDING", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(review.HeadSha, currentHead, StringComparison.OrdinalIgnoreCase))
                .Select(review => new UnitStatusEvidencePointer
                {
                    Kind = review.Source,
                    Url = review.Url,
                    RecordId = review.RecordId,
                    ExecutionUnit = executionUnit,
                    Repo = repo,
                    Pr = pullRequest,
                    HeadSha = review.HeadSha,
                    RecordedAt = review.At,
                    ReviewVerdict = review.Verdict,
                    ReviewState = review.ReviewState,
                    ReviewDisposition = review.Qualification,
                    Provenance = "github-rest-pending-review-not-submitted",
                }).ToArray(),
        };
    }

    internal static UnitStatusFact CiFact(IReadOnlyList<UnitStatusObservedCheck> checks, string currentHead, string repo, int pullRequest)
    {
        if (checks.Count == 0)
        {
            return new UnitStatusFact
            {
                Id = "observed-ci",
                State = UnitStatusStates.Missing,
                Cause = "observed-ci-empty",
                Detail = "Both completed exact-head check-run and commit-status collections were successfully read and empty.",
            };
        }

        var invalid = checks.Any(check => !string.Equals(check.Sha, currentHead, StringComparison.OrdinalIgnoreCase));
        if (invalid)
        {
            return UnavailableFact(
                "observed-ci",
                "github-check-head-conflict",
                "At least one observed check has a SHA different from the bracketed PR head.",
                UnitStatusStates.IdentityConflict);
        }

        var actionsByContext = checks.Where(check => check.Source == "check-run" && check.RunId is not null)
            .GroupBy(check => check.Identity, StringComparer.Ordinal);
        var ambiguousActionContext = actionsByContext.FirstOrDefault(group => group.Select(check => check.RunId).Distinct().Count() > 1);
        if (ambiguousActionContext is not null)
        {
            return UnavailableFact("observed-ci", "github-check-run-identity-conflict",
                "Multiple GitHub Actions run ids report the same exact-head check context, and the snapshot cannot establish which run supersedes the other.",
                UnitStatusStates.IdentityConflict) with
            {
                Evidence = ambiguousActionContext.Select(check => new UnitStatusEvidencePointer
                {
                    Kind = check.Source,
                    Url = check.Url ?? $"https://github.com/{repo}/pull/{pullRequest}",
                    RecordId = check.RecordId,
                    HeadSha = check.Sha,
                    Provenance = "github-rest-observed-check",
                }).ToArray(),
            };
        }

        var unattributedActions = checks.Where(check => check.Source == "check-run"
            && check.RunId is not null && check.Attempt is null).ToArray();
        if (unattributedActions.Length > 0)
        {
            return UnavailableFact("observed-ci", "github-check-attempt-unattributed",
                "GitHub reports an Actions run after its initial attempt, but the observed check-run rows cannot be linked to specific attempts by the permitted read surface. The run ID and current run attempt are retained as provenance without assigning an attempt to any check.",
                UnitStatusStates.ProvenanceLimit) with
            {
                Evidence = unattributedActions.Select(ToCheckEvidence).ToArray(),
            };
        }

        var groups = checks.GroupBy(check => check.Source == "check-run" && check.RunId is { } runId
            ? $"{check.Source}\0{check.Identity}\0{runId.ToString(CultureInfo.InvariantCulture)}"
            : $"{check.Source}\0{check.Identity}", StringComparer.Ordinal);
        var authoritative = new List<UnitStatusObservedCheck>();
        foreach (var group in groups)
        {
            if (group.Key.StartsWith("check-run\0", StringComparison.Ordinal) && group.Any(check => check.RunId is not null))
            {
                var maxAttempt = group.Max(check => check.Attempt ?? 0);
                var latestAttempt = group.Where(check => (check.Attempt ?? 0) == maxAttempt).ToArray();
                if (latestAttempt.Select(Disposition).Distinct(StringComparer.Ordinal).Count() > 1)
                {
                    return UnavailableFact("observed-ci", "github-check-attempt-identity-conflict",
                        "One Actions run has conflicting dispositions for the same check context and attempt.", UnitStatusStates.IdentityConflict)
                        with { Evidence = latestAttempt.Select(ToCheckEvidence).ToArray() };
                }
                authoritative.Add(latestAttempt[0]);
            }
            else if (group.Key.StartsWith("check-run\0", StringComparison.Ordinal)
                && group.Select(Disposition).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                return UnavailableFact("observed-ci", "github-check-identity-conflict",
                    "An external check context has conflicting dispositions without Actions attempt identity.", UnitStatusStates.IdentityConflict)
                    with { Evidence = group.Select(ToCheckEvidence).ToArray() };
            }
            else if (group.Key.StartsWith("commit-status\0", StringComparison.Ordinal))
            {
                authoritative.Add(group.OrderByDescending(check =>
                    long.TryParse(check.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0).First());
            }
            else
            {
                authoritative.Add(group.First());
            }
        }
        var successful = authoritative.All(check =>
            string.Equals(check.Source, "commit-status", StringComparison.Ordinal)
                ? string.Equals(check.Status, "success", StringComparison.OrdinalIgnoreCase)
                : string.Equals(check.Status, "completed", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(check.Conclusion, "success", StringComparison.OrdinalIgnoreCase));
        return new UnitStatusFact
        {
            Id = "observed-ci",
            State = successful ? UnitStatusStates.Done : UnitStatusStates.Missing,
            Cause = successful ? "observed-ci-success" : "observed-ci-not-successful",
            Detail = successful
                ? $"All {authoritative.Count} authoritative entries in the observed exact-head check inventory succeeded; branch protection and required checks were not evaluated."
                : "The observed exact-head inventory contains pending, failed, skipped, neutral, or otherwise non-success entries.",
            Evidence = authoritative.Select(ToCheckEvidence).ToArray(),
        };

        string Disposition(UnitStatusObservedCheck check) => string.Join("\0", check.Status, check.Conclusion ?? string.Empty);
        UnitStatusEvidencePointer ToCheckEvidence(UnitStatusObservedCheck check) => new()
            {
                Kind = check.Source,
                Url = check.Url ?? $"https://github.com/{repo}/pull/{pullRequest}",
                RecordId = check.RecordId,
                HeadSha = check.Sha,
                Provenance = "github-rest-observed-check",
            };
    }

    private static UnitStatusEvidencePointer GitHubEvidence(
        string kind,
        string url,
        string? unit,
        string? repo,
        int? pullRequest,
        string? head) => new()
    {
        Kind = kind,
        Url = url,
        ExecutionUnit = unit,
        Repo = repo,
        Pr = pullRequest,
        HeadSha = head,
        Provenance = "github-rest",
    };

    private static bool TryReadLabels(JsonElement root, out IReadOnlyList<string> labels)
    {
        labels = [];
        if (!root.TryGetProperty("labels", out var raw) || raw.ValueKind != JsonValueKind.Array) return false;
        var values = new List<string>();
        foreach (var label in raw.EnumerateArray())
        {
            if (!TryReadString(label, "name", out var name)) return false;
            values.Add(name);
        }
        labels = values;
        return true;
    }

    private static bool TryReadString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var raw)
            && raw.ValueKind == JsonValueKind.String
            && raw.GetString() is { } text
            && !string.IsNullOrWhiteSpace(text)
            && Assign(text, out value);
    }

    private static bool TryReadNullableString(JsonElement root, string name, out string? value)
    {
        value = null;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var raw)) return false;
        if (raw.ValueKind == JsonValueKind.Null) return true;
        if (raw.ValueKind != JsonValueKind.String) return false;
        value = raw.GetString();
        return true;
    }

    private static bool TryReadBool(JsonElement root, string name, out bool value)
    {
        value = false;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var raw)
            && (raw.ValueKind == JsonValueKind.True || raw.ValueKind == JsonValueKind.False)
            && Assign(raw.GetBoolean(), out value);
    }

    private static bool TryReadNumber(JsonElement root, string name, out int value) => TryReadInt(root, name, out value);

    private static bool TryReadInt(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var raw)
            && raw.TryGetInt32(out value);
    }

    private static bool TryReadLong(JsonElement root, string name, out long value)
    {
        value = 0;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var raw)
            && raw.TryGetInt64(out value);
    }

    private static bool TryReadNullableDateTime(JsonElement root, string name, out DateTimeOffset? value)
    {
        value = null;
        if (!TryReadNullableString(root, name, out var raw)) return false;
        if (raw is null) return true;
        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)) return false;
        value = parsed;
        return true;
    }

    private static bool IsSafeRepo(string value)
    {
        var parts = value.Split('/');
        return parts.Length == 2
            && parts.All(part => part.Length > 0 && part.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            && parts.All(part => !part.Contains("..", StringComparison.Ordinal));
    }

    private static bool IsObjectId(string value) => value.Length is 40 or 64 && value.All(Uri.IsHexDigit);

    private static bool IsSha(string value) => value.Length == 40 && value.All(Uri.IsHexDigit);

    private static bool Assign<T>(T value, out T target)
    {
        target = value;
        return true;
    }

    private enum ReviewParseFailure
    {
        None,
        Malformed,
        ProvenanceLimit,
        IdentityConflict,
    }

}
