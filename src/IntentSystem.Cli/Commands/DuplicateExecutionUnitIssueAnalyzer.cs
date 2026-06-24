using System.Globalization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G481: Pure analyzer for duplicate execution-unit issues and concurrent host
/// publish fallout. Given the same durable projections the state doctor reads —
/// queue-state items, publish-artifact evidence, GitHub PRs (with closing-issue
/// references), plus optional weaker-tier GitHub issues matched to a unit by
/// body/title — it detects when a SINGLE execution unit resolves to MORE THAN
/// ONE GitHub issue and classifies the state deterministically:
///
/// <list type="bullet">
/// <item><c>duplicate-execution-unit-issue-detected</c> (advisory finding) — a
/// unique canonical issue is resolvable from durable evidence and the
/// non-canonical duplicate(s) carry no active PR; the safe repair is to close
/// the non-canonical issue (offered, never auto-applied).</item>
/// <item><c>pr-closes-noncanonical-issue</c> (unsafe) — a PR closes a
/// non-canonical duplicate issue; fail-closed and classified separately from
/// ordinary missing-linked_pr recovery.</item>
/// <item><c>canonical-issue-mismatch</c> (unsafe) — durable sources disagree on
/// the canonical issue (queue-state vs publish artifact).</item>
/// <item><c>concurrent-host-publish-detected</c> (unsafe) — more than one issue
/// for the unit with no durable source uniquely anchoring the canonical
/// issue.</item>
/// </list>
///
/// Canonical selection prefers durable evidence over live GitHub recency, in
/// priority order: queue-state <c>linked_issue</c>, then packet
/// <c>publish.yaml</c> created issue. Weaker-tier GitHub matches never win the
/// canonical slot. Read-only: never reads files, mutates state, or launches an
/// AI provider. Reuses the state-doctor result types so the command layer can
/// merge these findings directly.
/// </summary>
internal static class DuplicateExecutionUnitIssueAnalyzer
{
    /// <summary>
    /// Analyze duplicate execution-unit issue states. <paramref name="unitGithubIssues"/>
    /// carries weaker-tier candidate issues (GitHub issues matched to a unit by
    /// body/title with no durable backing); pass an empty list when no such
    /// enumeration is available. Returns findings (advisory safe-repair offers)
    /// and unsafe findings (fail-closed classifications) to merge into the
    /// state-doctor analysis.
    /// </summary>
    public static AutomationStateDoctorAnalysis Analyze(
        string repo,
        IReadOnlyList<StateDoctorQueueItem> queueItems,
        IReadOnlyList<StateDoctorPublishEvidence> publishEvidence,
        IReadOnlyList<StateDoctorPr> pullRequests,
        IReadOnlyList<DuplicateUnitGithubIssue>? unitGithubIssues = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(queueItems);
        ArgumentNullException.ThrowIfNull(publishEvidence);
        ArgumentNullException.ThrowIfNull(pullRequests);

        var githubIssues = unitGithubIssues ?? Array.Empty<DuplicateUnitGithubIssue>();

        var findings = new List<AutomationStateDoctorFinding>();
        var unsafeFindings = new List<AutomationStateDoctorUnsafe>();

        // Index PRs by the (same-repo) issue numbers they close.
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

        // Collect every execution unit that appears in any evidence source.
        var units = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var item in queueItems)
        {
            units.Add(item.ExecutionUnit);
        }
        foreach (var evidence in publishEvidence.Where(e => SameRepo(e.IssueRepo, repo)))
        {
            units.Add(evidence.ExecutionUnit);
        }
        foreach (var issue in githubIssues.Where(i => SameRepo(i.IssueRepo, repo)))
        {
            units.Add(issue.ExecutionUnit);
        }

        foreach (var unit in units)
        {
            var queueIssue = queueItems
                .Where(item => string.Equals(item.ExecutionUnit, unit, StringComparison.Ordinal)
                    && item.LinkedIssueNumber is int
                    && SameRepo(item.LinkedIssueRepo, repo))
                .Select(item => item.LinkedIssueNumber!.Value)
                .Distinct()
                .ToArray();

            var publishIssues = publishEvidence
                .Where(e => string.Equals(e.ExecutionUnit, unit, StringComparison.Ordinal)
                    && SameRepo(e.IssueRepo, repo))
                .Select(e => e.IssueNumber)
                .Distinct()
                .ToArray();

            var weakIssues = githubIssues
                .Where(i => string.Equals(i.ExecutionUnit, unit, StringComparison.Ordinal)
                    && SameRepo(i.IssueRepo, repo))
                .Select(i => i.IssueNumber)
                .Distinct()
                .ToArray();

            var allCandidates = queueIssue
                .Concat(publishIssues)
                .Concat(weakIssues)
                .Distinct()
                .OrderBy(n => n)
                .ToArray();

            // Not a duplicate state: zero or one issue for the unit. Single-issue
            // linkage repairs belong to the existing state-doctor checks.
            if (allCandidates.Length < 2)
            {
                continue;
            }

            // If the durable queue-state record itself is internally ambiguous
            // (two different linked_issue values for the same unit) we cannot
            // anchor a canonical — concurrent publish, fail-closed.
            if (queueIssue.Length > 1)
            {
                unsafeFindings.Add(ConcurrentPublish(unit, allCandidates,
                    $"queue-state records {queueIssue.Length.ToString(CultureInfo.InvariantCulture)} different linked_issue values for execution unit '{unit}'"));
                continue;
            }

            // Canonical selection: durable queue-state wins; otherwise a single
            // publish artifact anchors it. Weaker GitHub matches never win.
            int? canonical;
            string canonicalSource;
            if (queueIssue.Length == 1)
            {
                canonical = queueIssue[0];
                canonicalSource = "queue-state linked_issue";
            }
            else if (publishIssues.Length == 1)
            {
                canonical = publishIssues[0];
                canonicalSource = "packet publish.yaml created issue";
            }
            else
            {
                canonical = null;
                canonicalSource = string.Empty;
            }

            // No durable anchor among ≥2 issues → concurrent/duplicate publish.
            if (canonical is not int canonicalIssue)
            {
                unsafeFindings.Add(ConcurrentPublish(unit, allCandidates,
                    $"{publishIssues.Length.ToString(CultureInfo.InvariantCulture)} publish artifacts record different created issues for execution unit '{unit}' and no queue-state linked_issue anchors the canonical issue"));
                continue;
            }

            var duplicates = allCandidates.Where(n => n != canonicalIssue).ToArray();

            // A PR that closes a NON-canonical duplicate issue is the highest
            // priority hazard (the Zero4Racer case): classify it separately.
            var noncanonicalClosedByPr = duplicates
                .Where(prsByClosingIssue.ContainsKey)
                .ToArray();
            if (noncanonicalClosedByPr.Length > 0)
            {
                var dupNumber = noncanonicalClosedByPr[0];
                var closingPr = prsByClosingIssue[dupNumber][0];
                unsafeFindings.Add(new AutomationStateDoctorUnsafe
                {
                    Kind = AutomationStateDoctorUnsafeKinds.PrClosesNoncanonicalIssue,
                    ExecutionUnit = unit,
                    IssueNumber = dupNumber,
                    Reason = $"PR #{closingPr.Number.ToString(CultureInfo.InvariantCulture)} closes non-canonical duplicate issue #{dupNumber.ToString(CultureInfo.InvariantCulture)} for execution unit '{unit}', but the canonical issue is #{canonicalIssue.ToString(CultureInfo.InvariantCulture)} ({canonicalSource}).",
                    MissingEvidence =
                    [
                        $"operator must reconcile the duplicate publish: confirm whether the implementation on PR #{closingPr.Number.ToString(CultureInfo.InvariantCulture)} belongs to canonical issue #{canonicalIssue.ToString(CultureInfo.InvariantCulture)} before any close/reopen or linkage change",
                        "do not auto-edit the PR body or reopen/close issues during the race (G481 out-of-scope)",
                    ],
                });
                continue;
            }

            // Durable sources disagree (queue-state vs publish artifact name
            // different issues) → canonical mismatch, fail-closed.
            var durableConflict = queueIssue.Length == 1
                && publishIssues.Any(n => n != canonicalIssue);
            if (durableConflict)
            {
                var publishIssue = publishIssues.First(n => n != canonicalIssue);
                unsafeFindings.Add(new AutomationStateDoctorUnsafe
                {
                    Kind = AutomationStateDoctorUnsafeKinds.CanonicalIssueMismatch,
                    ExecutionUnit = unit,
                    IssueNumber = canonicalIssue,
                    Reason = $"durable sources disagree for execution unit '{unit}': queue-state linked_issue = #{canonicalIssue.ToString(CultureInfo.InvariantCulture)} but a publish artifact records created issue #{publishIssue.ToString(CultureInfo.InvariantCulture)}.",
                    MissingEvidence =
                    [
                        "operator must confirm the canonical issue and remove or correct the conflicting durable record before any linkage repair",
                    ],
                });
                continue;
            }

            // Canonical is uniquely anchored and the non-canonical duplicate(s)
            // carry no active PR: safe repair offered (close the duplicate). This
            // is advisory only — closing a GitHub issue is outside the doctor's
            // forward-only queue-state write path, so it is never auto-applied.
            findings.Add(new AutomationStateDoctorFinding
            {
                Category = AutomationStateDoctorCategories.DuplicateExecutionUnitIssue,
                ExecutionUnit = unit,
                RepairKind = AutomationStateDoctorRepairKinds.None,
                IssueNumber = canonicalIssue,
                IssueUrl = null,
                IssueRepo = repo,
                PrNumber = null,
                PrUrl = null,
                Confidence = AutomationStateDoctorConfidence.Advisory,
                Applied = false,
                Evidence =
                [
                    $"canonical issue for '{unit}' is #{canonicalIssue.ToString(CultureInfo.InvariantCulture)} ({canonicalSource})",
                    $"non-canonical duplicate issue(s) {string.Join(", ", duplicates.Select(n => "#" + n.ToString(CultureInfo.InvariantCulture)))} have no active closing PR",
                ],
                Summary = $"Duplicate issues for '{unit}': canonical #{canonicalIssue.ToString(CultureInfo.InvariantCulture)}; safe repair is to close non-canonical {string.Join(", ", duplicates.Select(n => "#" + n.ToString(CultureInfo.InvariantCulture)))} (operator action — not auto-applied).",
            });
        }

        return new AutomationStateDoctorAnalysis(findings, unsafeFindings);
    }

    private static AutomationStateDoctorUnsafe ConcurrentPublish(
        string unit,
        IReadOnlyList<int> candidates,
        string reason) =>
        new()
        {
            Kind = AutomationStateDoctorUnsafeKinds.ConcurrentHostPublishDetected,
            ExecutionUnit = unit,
            IssueNumber = null,
            Reason = $"{reason}: candidate issues {string.Join(", ", candidates.Select(n => "#" + n.ToString(CultureInfo.InvariantCulture)))}. Fail-closed — do not pick a winner by recency or continue competing publishers.",
            MissingEvidence =
            [
                "operator must designate the canonical issue from durable evidence (queue-state linked_issue / publish.yaml / runs.jsonl issue-created) before any repair",
            ],
        };

    private static bool SameRepo(string? candidate, string repo) =>
        !string.IsNullOrWhiteSpace(candidate)
        && string.Equals(candidate, repo, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// G481: weaker-tier candidate issue — a GitHub issue matched to an execution
/// unit by body/title with no durable (queue-state / publish.yaml) backing.
/// Never wins the canonical slot; used to detect stray duplicate issues.
/// </summary>
internal sealed record DuplicateUnitGithubIssue
{
    public required string ExecutionUnit { get; init; }
    public required string IssueRepo { get; init; }
    public required int IssueNumber { get; init; }
}
