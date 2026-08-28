using System.Globalization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G448: Pure analyzer for the unified host-metadata state doctor. Given a
/// snapshot of queue-state items, publish-artifact evidence, and GitHub PRs
/// (open + merged), it reports deterministic, forward-only drift categories
/// with evidence and confidence, and classifies ambiguous cases as unsafe
/// (no repair). It NEVER reads files, mutates GitHub or local state, or
/// launches an AI provider — the command layer wraps it with I/O and the
/// fail-closed <c>--write</c> path.
///
/// Backward-compat invariant: the analyzer only proposes repairs that FILL a
/// missing field or advance a lifecycle state. It never proposes clearing,
/// rewriting, or downgrading existing host data, so applying high-confidence
/// repairs to an old host is always safe and non-destructive.
/// </summary>
internal static class AutomationStateDoctorAnalyzer
{
    public static AutomationStateDoctorAnalysis Analyze(
        string repo,
        IReadOnlyList<StateDoctorQueueItem> queueItems,
        IReadOnlyList<StateDoctorPublishEvidence> publishEvidence,
        IReadOnlyList<StateDoctorPr> pullRequests)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(queueItems);
        ArgumentNullException.ThrowIfNull(publishEvidence);
        ArgumentNullException.ThrowIfNull(pullRequests);

        var findings = new List<AutomationStateDoctorFinding>();
        var unsafeFindings = new List<AutomationStateDoctorUnsafe>();

        // ---- duplicate-queue-item (G746) ------------------------------------
        // Queue-state is an ordered list, but array order is not authority. A
        // duplicate is repairable only when one complete entry strictly
        // dominates every competing entry; otherwise all competing entries are
        // retained in an unsafe finding and the command must not write.
        var indexedQueueItems = queueItems
            .Select((item, index) => item with
            {
                SourceIndex = item.SourceIndex >= 0 ? item.SourceIndex : index,
            })
            .ToArray();
        var duplicateGroups = DuplicateQueueItemRules.Analyze(
            indexedQueueItems
                .Select((item, index) => DuplicateQueueItemRules.FromProjection(item, index))
                .ToArray());
        var duplicateGroupsByUnit = duplicateGroups.ToDictionary(
            group => group.ExecutionUnit,
            StringComparer.Ordinal);

        foreach (var group in duplicateGroups)
        {
            var competingEntries = DuplicateQueueItemRules.FormatCompetingEntries(group);
            if (group.Winner is { } winner)
            {
                findings.Add(new AutomationStateDoctorFinding
                {
                    Category = AutomationStateDoctorCategories.DuplicateQueueItem,
                    ExecutionUnit = group.ExecutionUnit,
                    RepairKind = AutomationStateDoctorRepairKinds.DeduplicateQueueItem,
                    IssueNumber = null,
                    IssueUrl = null,
                    IssueRepo = null,
                    PrNumber = null,
                    PrUrl = null,
                    QueueItemIndex = winner.Index,
                    RemoveQueueItemIndices = group.Entries
                        .Where(entry => entry.Index != winner.Index)
                        .Select(entry => entry.Index)
                        .ToArray(),
                    Confidence = AutomationStateDoctorConfidence.High,
                    Applied = false,
                    Evidence =
                    [
                        $"entry[{winner.Index}] for '{group.ExecutionUnit}' strictly dominates the other duplicate entry or entries without losing competing information",
                        $"state-doctor --write keeps entry[{winner.Index}] and removes only the less informative duplicate entry or entries",
                        $"competing entries:\n{competingEntries}",
                    ],
                    Summary = $"Deduplicate queue-state entries for '{group.ExecutionUnit}' by keeping the strictly more informative entry[{winner.Index}].",
                });
            }
            else
            {
                unsafeFindings.Add(new AutomationStateDoctorUnsafe
                {
                    Kind = AutomationStateDoctorUnsafeKinds.DuplicateQueueItem,
                    ExecutionUnit = group.ExecutionUnit,
                    IssueNumber = null,
                    Reason = $"duplicate queue-state entries for execution unit '{group.ExecutionUnit}' are incomparable or equivalent; unsafe-stop — no mutation is allowed. Competing full entries:\n{competingEntries}",
                    MissingEvidence =
                    [
                        "operator must provide a strictly more informative canonical entry or reconcile the competing fields before retrying state-doctor --write",
                    ],
                    CompetingEntries = group.Entries.Select(entry => entry.FullEntryJson).ToArray(),
                });
            }
        }

        // Analyze only one canonical entry for a dominated group. Ambiguous
        // groups are omitted from ordinary drift checks because they already
        // have a fail-closed duplicate finding.
        var queueItemsForAnalysis = indexedQueueItems
            .Where(item => !duplicateGroupsByUnit.TryGetValue(item.ExecutionUnit, out var group)
                || group.Winner?.Index == item.SourceIndex)
            .ToArray();

        // Index PRs by the issue numbers they close (only same-repo numbers are
        // supplied by the command projection). A given issue may be closed by
        // more than one PR — that is the ambiguous case.
        var prsByClosingIssue = new Dictionary<int, List<StateDoctorPr>>();
        foreach (var pr in pullRequests)
        {
            foreach (var issueNumber in pr.ClosingIssueNumbers.Distinct())
            {
                if (!prsByClosingIssue.TryGetValue(issueNumber, out var list))
                {
                    list = new List<StateDoctorPr>();
                    prsByClosingIssue[issueNumber] = list;
                }
                if (list.All(existing => existing.Number != pr.Number))
                {
                    list.Add(pr);
                }
            }
        }

        // ---- duplicate-issue-evidence (unsafe, fail-closed) ----------------
        // More than one queue item that resolves to the SAME source issue, or
        // more than one publish artifact recording the same created issue, is
        // ambiguous: the doctor cannot decide which row owns the linkage.
        var ambiguousIssueNumbers = new HashSet<int>();

        var queueItemsByIssue = queueItems
            .Where(item => item.LinkedIssueNumber is int && SameRepo(item.LinkedIssueRepo, repo))
            .GroupBy(item => item.LinkedIssueNumber!.Value);
        foreach (var group in queueItemsByIssue)
        {
            var units = group.Select(g => g.ExecutionUnit).Distinct().ToArray();
            if (units.Length > 1)
            {
                ambiguousIssueNumbers.Add(group.Key);
                unsafeFindings.Add(new AutomationStateDoctorUnsafe
                {
                    Kind = AutomationStateDoctorUnsafeKinds.DuplicateIssueEvidence,
                    ExecutionUnit = null,
                    IssueNumber = group.Key,
                    Reason = $"{units.Length.ToString(CultureInfo.InvariantCulture)} queue items reference source issue #{group.Key.ToString(CultureInfo.InvariantCulture)} ({string.Join(", ", units.Select(u => "'" + u + "'"))}); cannot deterministically pick the owning row.",
                    MissingEvidence =
                    [
                        "operator must dedupe queue-state so exactly one item owns the source issue before the doctor can repair its linkage",
                    ],
                });
            }
        }

        var publishByIssue = publishEvidence
            .Where(evidence => SameRepo(evidence.IssueRepo, repo))
            .GroupBy(evidence => evidence.IssueNumber);
        foreach (var group in publishByIssue)
        {
            var units = group.Select(g => g.ExecutionUnit).Distinct().ToArray();
            if (units.Length > 1)
            {
                ambiguousIssueNumbers.Add(group.Key);
                unsafeFindings.Add(new AutomationStateDoctorUnsafe
                {
                    Kind = AutomationStateDoctorUnsafeKinds.AmbiguousPublishEvidence,
                    ExecutionUnit = null,
                    IssueNumber = group.Key,
                    Reason = $"{units.Length.ToString(CultureInfo.InvariantCulture)} publish artifacts record created issue #{group.Key.ToString(CultureInfo.InvariantCulture)} for different execution units ({string.Join(", ", units.Select(u => "'" + u + "'"))}); duplicate publish evidence is ambiguous.",
                    MissingEvidence =
                    [
                        "operator must remove the duplicate publish artifact before the doctor can fill linked_issue",
                    ],
                });
            }
        }

        // Index publish evidence by execution unit (after dedup, one issue per unit).
        var publishByUnit = publishEvidence
            .Where(evidence => SameRepo(evidence.IssueRepo, repo))
            .GroupBy(evidence => evidence.ExecutionUnit, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);

        foreach (var queueItem in queueItemsForAnalysis)
        {
            var item = queueItem;
            var hasLinkedIssue = item.LinkedIssueNumber is int && SameRepo(item.LinkedIssueRepo, repo);
            var hasLinkedPr = !string.IsNullOrWhiteSpace(item.LinkedPrUrl);

            // ---- missing-linked-issue (high: fill from publish artifact) ----
            if (!hasLinkedIssue
                && publishByUnit.TryGetValue(item.ExecutionUnit, out var evidenceForUnit))
            {
                var distinctIssues = evidenceForUnit
                    .Select(e => e.IssueNumber)
                    .Distinct()
                    .ToArray();
                if (distinctIssues.Length == 1 && !ambiguousIssueNumbers.Contains(distinctIssues[0]))
                {
                    var evidence = evidenceForUnit[0];
                    findings.Add(new AutomationStateDoctorFinding
                    {
                        Category = AutomationStateDoctorCategories.MissingLinkedIssue,
                        ExecutionUnit = item.ExecutionUnit,
                        RepairKind = AutomationStateDoctorRepairKinds.SetQueueLinkedIssue,
                        IssueNumber = evidence.IssueNumber,
                        IssueUrl = evidence.IssueUrl,
                        IssueRepo = evidence.IssueRepo,
                        PrNumber = null,
                        PrUrl = null,
                        QueueItemIndex = item.SourceIndex,
                        Confidence = AutomationStateDoctorConfidence.High,
                        Applied = false,
                        Evidence =
                        [
                            $"publish artifact for '{item.ExecutionUnit}' records created_issue_number = #{evidence.IssueNumber.ToString(CultureInfo.InvariantCulture)} in {evidence.IssueRepo}",
                            $"queue item '{item.ExecutionUnit}' has linked_issue = null",
                        ],
                        Summary = $"Fill linked_issue for '{item.ExecutionUnit}' from publish artifact (#{evidence.IssueNumber.ToString(CultureInfo.InvariantCulture)}).",
                    });

                    // The unit now (post-repair) resolves to this issue; continue
                    // so missing-linked-pr / merged checks can also key off it.
                    hasLinkedIssue = true;
                    item = item with
                    {
                        LinkedIssueRepo = evidence.IssueRepo,
                        LinkedIssueNumber = evidence.IssueNumber,
                        LinkedIssueUrl = evidence.IssueUrl,
                    };
                }
            }

            if (!hasLinkedIssue || item.LinkedIssueNumber is not int issueNo)
            {
                continue;
            }

            if (ambiguousIssueNumbers.Contains(issueNo))
            {
                continue; // already surfaced as unsafe; do not also propose a repair
            }

            var closingPrs = prsByClosingIssue.TryGetValue(issueNo, out var prs)
                ? prs
                : new List<StateDoctorPr>();

            // ---- missing-linked-pr -----------------------------------------
            if (!hasLinkedPr)
            {
                if (closingPrs.Count == 1)
                {
                    var pr = closingPrs[0];
                    findings.Add(new AutomationStateDoctorFinding
                    {
                        Category = AutomationStateDoctorCategories.MissingLinkedPr,
                        ExecutionUnit = item.ExecutionUnit,
                        RepairKind = AutomationStateDoctorRepairKinds.SetQueueLinkedPr,
                        IssueNumber = issueNo,
                        IssueUrl = item.LinkedIssueUrl,
                        IssueRepo = item.LinkedIssueRepo,
                        PrNumber = pr.Number,
                        PrUrl = pr.Url,
                        QueueItemIndex = item.SourceIndex,
                        Confidence = AutomationStateDoctorConfidence.High,
                        Applied = false,
                        Evidence =
                        [
                            $"PR #{pr.Number.ToString(CultureInfo.InvariantCulture)} uniquely closes source issue #{issueNo.ToString(CultureInfo.InvariantCulture)}",
                            $"queue item '{item.ExecutionUnit}' has linked_pr = null",
                        ],
                        Summary = $"Fill linked_pr for '{item.ExecutionUnit}' (source issue #{issueNo.ToString(CultureInfo.InvariantCulture)}) with PR #{pr.Number.ToString(CultureInfo.InvariantCulture)}.",
                    });
                    hasLinkedPr = true;
                }
                else if (closingPrs.Count > 1)
                {
                    unsafeFindings.Add(new AutomationStateDoctorUnsafe
                    {
                        Kind = AutomationStateDoctorUnsafeKinds.AmbiguousPrLinkage,
                        ExecutionUnit = item.ExecutionUnit,
                        IssueNumber = issueNo,
                        Reason = $"{closingPrs.Count.ToString(CultureInfo.InvariantCulture)} PRs close source issue #{issueNo.ToString(CultureInfo.InvariantCulture)} ({string.Join(", ", closingPrs.Select(p => "#" + p.Number.ToString(CultureInfo.InvariantCulture)))}); cannot deterministically pick linked_pr.",
                        MissingEvidence =
                        [
                            "operator must close or retarget the duplicate PRs so exactly one closes the issue",
                        ],
                    });
                    continue;
                }
            }

            // ---- merged-pr-not-completed -----------------------------------
            if (!item.Completed)
            {
                var mergedClosingPrs = closingPrs.Where(pr => pr.Merged).ToArray();
                if (mergedClosingPrs.Length == 1)
                {
                    var pr = mergedClosingPrs[0];
                    findings.Add(new AutomationStateDoctorFinding
                    {
                        Category = AutomationStateDoctorCategories.MergedPrNotCompleted,
                        ExecutionUnit = item.ExecutionUnit,
                        RepairKind = AutomationStateDoctorRepairKinds.MarkQueueCompleted,
                        IssueNumber = issueNo,
                        IssueUrl = item.LinkedIssueUrl,
                        IssueRepo = item.LinkedIssueRepo,
                        PrNumber = pr.Number,
                        PrUrl = pr.Url,
                        QueueItemIndex = item.SourceIndex,
                        Confidence = AutomationStateDoctorConfidence.High,
                        Applied = false,
                        Evidence =
                        [
                            $"PR #{pr.Number.ToString(CultureInfo.InvariantCulture)} is MERGED and closes source issue #{issueNo.ToString(CultureInfo.InvariantCulture)}",
                            $"queue item '{item.ExecutionUnit}' state is not completed",
                        ],
                        Summary = $"Advance queue item '{item.ExecutionUnit}' to completed (merged PR #{pr.Number.ToString(CultureInfo.InvariantCulture)} closed source issue #{issueNo.ToString(CultureInfo.InvariantCulture)}).",
                    });
                }
                else if (mergedClosingPrs.Length > 1)
                {
                    unsafeFindings.Add(new AutomationStateDoctorUnsafe
                    {
                        Kind = AutomationStateDoctorUnsafeKinds.AmbiguousPrLinkage,
                        ExecutionUnit = item.ExecutionUnit,
                        IssueNumber = issueNo,
                        Reason = $"{mergedClosingPrs.Length.ToString(CultureInfo.InvariantCulture)} merged PRs close source issue #{issueNo.ToString(CultureInfo.InvariantCulture)}; cannot deterministically confirm completion provenance.",
                        MissingEvidence =
                        [
                            "operator must confirm which merged PR represents the completed work",
                        ],
                    });
                }
            }
        }

        // G481: duplicate execution-unit issue / concurrent host publish
        // detection runs on the same durable projections. Its findings (advisory
        // safe-repair offers) and unsafe findings (fail-closed classifications)
        // merge into this analysis. The weaker-tier GitHub-issue enumeration is
        // not yet projected by the command, so an empty list is passed; the
        // queue-state-vs-publish mismatch and PR-closes-non-canonical hazards
        // are fully covered by the durable evidence already supplied.
        var duplicateAnalysis = DuplicateExecutionUnitIssueAnalyzer.Analyze(
            repo,
            queueItemsForAnalysis,
            publishEvidence,
            pullRequests);
        findings.AddRange(duplicateAnalysis.Findings);
        unsafeFindings.AddRange(duplicateAnalysis.UnsafeFindings);

        return new AutomationStateDoctorAnalysis(findings, unsafeFindings);
    }

    private static bool SameRepo(string? candidate, string repo) =>
        !string.IsNullOrWhiteSpace(candidate)
        && string.Equals(candidate, repo, StringComparison.OrdinalIgnoreCase);
}
