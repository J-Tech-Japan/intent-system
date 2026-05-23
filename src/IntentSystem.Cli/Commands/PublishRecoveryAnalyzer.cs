using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G303 / G315: pure analyzer for two host-review metadata recovery lanes.
///
/// Lane A (G303): the publish-artifact-backed lane covers queued execution
/// units where BOTH <c>linked_issue</c> and <c>linked_pr</c> are null but
/// the host repo's <c>.intent-cli/issues/&lt;execution-unit&gt;/publish.yaml</c>
/// recorded a created issue number, AND exactly one open PR uniquely
/// closes that issue.
///
/// Lane B (G315): the queue-state-backed lane covers queued execution
/// units where <c>linked_issue</c> is already populated (durable host
/// source-of-truth) but <c>linked_pr</c> is null, AND exactly one open PR
/// closes that linked issue. Publish-artifact evidence is not required
/// for this lane — the existing queue <c>linked_issue</c> + GitHub closing
/// reference is deterministic enough on its own.
///
/// In both lanes the repair is fully deterministic and can be applied
/// with <c>--write</c>; ambiguity (multiple matching PRs or queue items),
/// repo mismatch, missing publish artifact, or missing closing reference
/// produce structured unsafe stops instead.
///
/// Pure data in, pure data out — no `gh` calls, no file I/O. The
/// command layer captures the inputs and applies the writes.
/// </summary>
internal static class PublishRecoveryAnalyzer
{
    public const string RepairType = "publish-artifact-linked-refs-recovery";
    public const string RepairTypeLinkedIssueClosingPr = "queue-linked-issue-closing-pr-recovery";
    public const string UnsafeMissingPublishArtifact = "missing-publish-artifact";
    public const string UnsafeMissingCreatedIssue = "publish-artifact-no-created-issue";
    public const string UnsafeNoClosingPr = "no-closing-pr-for-published-issue";
    public const string UnsafeMultipleClosingPrs = "multiple-closing-prs-for-published-issue";
    public const string UnsafeRepoMismatch = "publish-recovery-repo-mismatch";
    // G315 unsafe stop kinds — distinct from the G303 lane so operators can
    // tell which evidence path produced the stop.
    public const string UnsafeNoClosingPrForLinkedIssue = "no-closing-pr-for-linked-issue";
    public const string UnsafeMultipleClosingPrsForLinkedIssue = "multiple-closing-prs-for-linked-issue";
    public const string UnsafeMultipleQueueItemsForLinkedIssue = "multiple-queue-items-for-linked-issue";
    public const string UnsafeLinkedIssueRepoMismatch = "linked-issue-repo-mismatch";

    private static readonly Regex ClosesIssueRegex = new(
        @"(?i)\b(?:close[sd]?|fix(?:es|ed)?|resolve[sd]?)\s+(?:(?<repo>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+))?#(?<number>\d+)\b",
        RegexOptions.Compiled);

    /// <summary>
    /// G351: PR-scoped analysis. Finds the selected PR from
    /// <paramref name="openPrs"/>, extracts its closing-issue references, and
    /// then runs the standard analysis only on the candidates whose
    /// <c>linked_issue</c> number matches one of those closing issues. At most
    /// one queue item is in scope, so the result contains at most one repair or
    /// one unsafe stop — unrelated G* items are never included.
    ///
    /// If the selected PR is not found in <paramref name="openPrs"/>, or if
    /// none of its closing issues match any candidate, the analysis returns
    /// empty repairs and empty unsafe stops (no-op; the PR may already be
    /// linked or closed).
    /// </summary>
    public static PublishRecoveryAnalysis AnalyzeScopedToPr(
        string repo,
        IReadOnlyList<PublishRecoveryCandidate> candidates,
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs,
        int prNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(openPrs);

        var selectedPr = openPrs.FirstOrDefault(pr => pr.Number == prNumber);
        if (selectedPr is null)
        {
            // PR not found in the open list; nothing to scope to.
            return new PublishRecoveryAnalysis { Repo = repo, SafeRepairs = [], UnsafeStops = [] };
        }

        var closingIssues = new HashSet<int>(ExtractLinkedIssueNumbers(repo, selectedPr));
        if (closingIssues.Count == 0)
        {
            // PR has no closing-issue references — cannot determine which queue
            // item to scope to. Surface as a single scoped unsafe stop so the
            // operator knows the PR body needs a Closes/Fixes/Resolves reference.
            return new PublishRecoveryAnalysis
            {
                Repo = repo,
                SafeRepairs = [],
                UnsafeStops = new[]
                {
                    new PublishRecoveryUnsafeStop
                    {
                        Kind = UnsafeNoClosingPrForLinkedIssue,
                        ExecutionUnit = $"<selected-pr-{prNumber}>",
                        Reason = $"PR #{prNumber} has no Closes/Fixes/Resolves reference. Cannot scope recovery to a queue item without a deterministic closing-issue link (G311).",
                        PublishArtifactPath = string.Empty
                    }
                }
            };
        }

        // Filter candidates to those tied to the selected PR's closing issue.
        // Two evidence sources qualify:
        //   1. queue-state linked_issue already matches (Lane B / G315); or
        //   2. G391: the queue item's publish-artifact created_issue matches,
        //      even when linked_issue is still null. This is the same
        //      deterministic selected-PR evidence `reconcile` reports as
        //      advisory; including it lets the G303 publish-artifact lane in
        //      Analyze promote it to a high-confidence writeable repair (with
        //      its existing repo / unique-closing-PR / single-unit guards),
        //      instead of `publish-recovery --pr` silently returning no repair.
        var scopedCandidates = candidates
            .Where(c =>
                (c.LinkedIssueNumber is int n && closingIssues.Contains(n))
                || (c.LinkedIssueNumber is null
                    && c.PublishArtifact?.CreatedIssueNumber is int created
                    && closingIssues.Contains(created)))
            .ToArray();

        if (scopedCandidates.Length == 0)
        {
            // No queue item has a linked_issue that matches this PR's closing
            // references. The PR may be for an untracked issue; no-op.
            return new PublishRecoveryAnalysis { Repo = repo, SafeRepairs = [], UnsafeStops = [] };
        }

        // Run the standard analysis restricted to the scoped candidates. The
        // normal ambiguity checks in AnalyzeLinkedIssueLane still apply within
        // the scoped set (multiple queue items for the same linked_issue would
        // produce an unsafe stop rather than a repair).
        return Analyze(repo, scopedCandidates, openPrs);
    }

    public static PublishRecoveryAnalysis Analyze(
        string repo,
        IReadOnlyList<PublishRecoveryCandidate> candidates,
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(openPrs);

        var repairs = new List<PublishRecoveryRepair>();
        var unsafeStops = new List<PublishRecoveryUnsafeStop>();

        // G315 ambiguity guard: precompute how many in-scope queue items
        // claim each linked_issue number for `repo`. If two queue items
        // both reference the same linked_issue, we cannot deterministically
        // decide which one should be paired with a single closing PR — both
        // surface as unsafe stops instead of either getting a repair.
        var linkedIssueQueueOccurrences = new Dictionary<int, int>();
        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c.LinkedPrUrl))
            {
                continue; // already linked end-to-end; not in scope.
            }
            if (c.LinkedIssueNumber is not int n)
            {
                continue;
            }
            if (!string.IsNullOrEmpty(c.LinkedIssueRepo)
                && !string.Equals(c.LinkedIssueRepo, repo, StringComparison.OrdinalIgnoreCase))
            {
                continue; // cross-repo link is out of this analyzer's scope.
            }
            linkedIssueQueueOccurrences[n] = linkedIssueQueueOccurrences.TryGetValue(n, out var existing)
                ? existing + 1
                : 1;
        }

        foreach (var candidate in candidates)
        {
            // Already linked end-to-end: nothing for either lane to do.
            if (!string.IsNullOrEmpty(candidate.LinkedPrUrl))
            {
                continue;
            }

            // Lane B (G315): existing linked_issue, missing linked_pr.
            if (candidate.LinkedIssueNumber is int linkedIssueNumber)
            {
                AnalyzeLinkedIssueLane(
                    repo,
                    candidate,
                    linkedIssueNumber,
                    linkedIssueQueueOccurrences,
                    openPrs,
                    repairs,
                    unsafeStops);
                continue;
            }

            // Lane A (G303): both linked_issue and linked_pr null. Falls
            // through to the publish-artifact-driven path below.
            if (!string.IsNullOrEmpty(candidate.LinkedIssueRepo))
            {
                // Defensive: linked_issue.repo set but no number — this is
                // a malformed row; we can't repair it deterministically.
                continue;
            }

            if (candidate.PublishArtifact is null)
            {
                unsafeStops.Add(NewStop(candidate, UnsafeMissingPublishArtifact,
                    $"queue item '{candidate.ExecutionUnit}' has no linked refs and no publish.yaml artifact at '{candidate.PublishArtifactExpectedPath}'; cannot recover linked_issue / linked_pr."));
                continue;
            }

            var artifact = candidate.PublishArtifact;
            if (artifact.CreatedIssueNumber is not int issueNumber)
            {
                unsafeStops.Add(NewStop(candidate, UnsafeMissingCreatedIssue,
                    $"publish.yaml at '{candidate.PublishArtifactExpectedPath}' for '{candidate.ExecutionUnit}' has no created_issue_number; cannot recover host metadata without a deterministic GitHub issue ref."));
                continue;
            }

            // The publish artifact's URL implicitly carries the host repo
            // (the parent host repo we're reconciling). Refuse to repair
            // when the URL points at a different repo than `--repo`.
            if (!string.IsNullOrEmpty(artifact.CreatedIssueUrl)
                && !ArtifactUrlMatchesRepo(artifact.CreatedIssueUrl!, repo))
            {
                unsafeStops.Add(NewStop(candidate, UnsafeRepoMismatch,
                    $"publish.yaml at '{candidate.PublishArtifactExpectedPath}' targets '{artifact.CreatedIssueUrl}' which does not belong to repo '{repo}'; refusing to repair host metadata against the wrong repo."));
                continue;
            }

            var closingPrs = openPrs
                .Where(pr => ExtractLinkedIssueNumbers(repo, pr).Contains(issueNumber))
                .ToArray();

            if (closingPrs.Length == 0)
            {
                unsafeStops.Add(NewStop(candidate, UnsafeNoClosingPr,
                    $"no open PR in '{repo}' closes published issue #{issueNumber} for '{candidate.ExecutionUnit}'. Operator must publish or open a PR closing the issue before host metadata can recover."));
                continue;
            }

            if (closingPrs.Length > 1)
            {
                unsafeStops.Add(NewStop(candidate, UnsafeMultipleClosingPrs,
                    $"{closingPrs.Length} open PRs close published issue #{issueNumber} for '{candidate.ExecutionUnit}': {string.Join(", ", closingPrs.Select(pr => "#" + pr.Number))}. Cannot deterministically pick which PR to record as linked_pr."));
                continue;
            }

            var pr = closingPrs[0];
            repairs.Add(new PublishRecoveryRepair
            {
                Type = RepairType,
                ExecutionUnit = candidate.ExecutionUnit,
                LinkedIssueRepo = repo,
                LinkedIssueNumber = issueNumber,
                LinkedIssueUrl = artifact.CreatedIssueUrl,
                LinkedPrNumber = pr.Number,
                LinkedPrUrl = pr.Url,
                PublishArtifactPath = candidate.PublishArtifactExpectedPath,
                Confidence = AutomationReconcileConfidence.High,
                Evidence = new[]
                {
                    $"publish.yaml at '{candidate.PublishArtifactExpectedPath}' records created_issue_number = {issueNumber}.",
                    $"queue item '{candidate.ExecutionUnit}' has linked_issue = null and linked_pr = null.",
                    $"PR #{pr.Number} ({pr.Url}) is the only open PR in '{repo}' that closes issue #{issueNumber}."
                },
                Summary = $"Recover linked_issue/#{issueNumber} and linked_pr/#{pr.Number} for '{candidate.ExecutionUnit}' in queue-state."
            });
        }

        return new PublishRecoveryAnalysis
        {
            Repo = repo,
            SafeRepairs = repairs,
            UnsafeStops = unsafeStops
        };
    }

    private static void AnalyzeLinkedIssueLane(
        string repo,
        PublishRecoveryCandidate candidate,
        int linkedIssueNumber,
        IReadOnlyDictionary<int, int> linkedIssueQueueOccurrences,
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs,
        List<PublishRecoveryRepair> repairs,
        List<PublishRecoveryUnsafeStop> unsafeStops)
    {
        // Repo mismatch: if the queue item explicitly tags a different
        // repo for its linked_issue, this analyzer (scoped to `--repo`)
        // is not authoritative.
        if (!string.IsNullOrEmpty(candidate.LinkedIssueRepo)
            && !string.Equals(candidate.LinkedIssueRepo, repo, StringComparison.OrdinalIgnoreCase))
        {
            unsafeStops.Add(NewStop(candidate, UnsafeLinkedIssueRepoMismatch,
                $"queue item '{candidate.ExecutionUnit}' has linked_issue.repo = '{candidate.LinkedIssueRepo}' which does not match analyzer repo '{repo}'; refusing to attach a linked_pr from a different repository."));
            return;
        }

        // Multiple queue items point at the same linked_issue: cannot
        // deterministically pair one PR with one queue item without an
        // operator nudge.
        if (linkedIssueQueueOccurrences.TryGetValue(linkedIssueNumber, out var occurrences)
            && occurrences > 1)
        {
            unsafeStops.Add(NewStop(candidate, UnsafeMultipleQueueItemsForLinkedIssue,
                $"{occurrences} queue items reference linked_issue #{linkedIssueNumber}; cannot deterministically decide which queue item to attach a closing PR to."));
            return;
        }

        var closingPrs = openPrs
            .Where(pr => ExtractLinkedIssueNumbers(repo, pr).Contains(linkedIssueNumber))
            .ToArray();

        if (closingPrs.Length == 0)
        {
            unsafeStops.Add(NewStop(candidate, UnsafeNoClosingPrForLinkedIssue,
                $"queue item '{candidate.ExecutionUnit}' has linked_issue #{linkedIssueNumber} in '{repo}' but no open PR closes that issue. PR may be missing a Closes/Fixes/Resolves reference (G311) — repair the PR body before retrying linked_pr recovery."));
            return;
        }

        if (closingPrs.Length > 1)
        {
            unsafeStops.Add(NewStop(candidate, UnsafeMultipleClosingPrsForLinkedIssue,
                $"{closingPrs.Length} open PRs close linked_issue #{linkedIssueNumber} for '{candidate.ExecutionUnit}': {string.Join(", ", closingPrs.Select(pr => "#" + pr.Number))}. Cannot deterministically pick which PR to record as linked_pr."));
            return;
        }

        var pr = closingPrs[0];
        var linkedIssueUrl = !string.IsNullOrEmpty(candidate.LinkedIssueUrl)
            ? candidate.LinkedIssueUrl
            : $"https://github.com/{repo}/issues/{linkedIssueNumber}";

        repairs.Add(new PublishRecoveryRepair
        {
            Type = RepairTypeLinkedIssueClosingPr,
            ExecutionUnit = candidate.ExecutionUnit,
            LinkedIssueRepo = repo,
            LinkedIssueNumber = linkedIssueNumber,
            LinkedIssueUrl = linkedIssueUrl,
            LinkedPrNumber = pr.Number,
            LinkedPrUrl = pr.Url,
            PublishArtifactPath = candidate.PublishArtifactExpectedPath,
            Confidence = AutomationReconcileConfidence.High,
            Evidence = new[]
            {
                $"queue item '{candidate.ExecutionUnit}' has linked_issue #{linkedIssueNumber} (durable host source-of-truth) and linked_pr = null.",
                $"PR #{pr.Number} ({pr.Url}) is the only open PR in '{repo}' that closes issue #{linkedIssueNumber}.",
                "no other queue item references the same linked_issue, so the pairing is unambiguous."
            },
            Summary = $"Recover linked_pr/#{pr.Number} for '{candidate.ExecutionUnit}' (linked_issue #{linkedIssueNumber} already in queue-state)."
        });
    }

    private static PublishRecoveryUnsafeStop NewStop(
        PublishRecoveryCandidate candidate,
        string kind,
        string reason)
    {
        return new PublishRecoveryUnsafeStop
        {
            Kind = kind,
            ExecutionUnit = candidate.ExecutionUnit,
            Reason = reason,
            PublishArtifactPath = candidate.PublishArtifactExpectedPath
        };
    }

    private static IReadOnlyList<int> ExtractLinkedIssueNumbers(
        string repo,
        GitHubAutomationPrCandidate pr)
    {
        var numbers = new List<int>();

        // Prefer GitHub's structured `closingIssuesReferences` graph data
        // — it is the same signal `gh pr view` returns and is what the
        // GitHub UI uses to render "Linked issues". Fall back to
        // body/title regex parsing for callers (and tests) that don't
        // populate the structured field.
        foreach (var reference in pr.ClosingIssuesReferences)
        {
            if (reference.Number <= 0)
            {
                continue;
            }
            if (reference.Repository is { } refRepo
                && !string.IsNullOrEmpty(refRepo.Name)
                && refRepo.Owner is { } refOwner
                && !string.IsNullOrEmpty(refOwner.Login))
            {
                var refDescriptor = $"{refOwner.Login}/{refRepo.Name}";
                if (!string.Equals(refDescriptor, repo, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }
            numbers.Add(reference.Number);
        }

        var sources = new[] { pr.Body ?? string.Empty, pr.Title ?? string.Empty };
        foreach (var source in sources)
        {
            foreach (Match match in ClosesIssueRegex.Matches(source))
            {
                var prefixedRepo = match.Groups["repo"].Value;
                if (!string.IsNullOrEmpty(prefixedRepo)
                    && !string.Equals(prefixedRepo, repo, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (int.TryParse(match.Groups["number"].Value, out var n) && n > 0)
                {
                    numbers.Add(n);
                }
            }
        }

        return numbers.Distinct().ToArray();
    }

    private static bool ArtifactUrlMatchesRepo(string artifactUrl, string repo)
    {
        var marker = $"github.com/{repo}/issues/";
        return artifactUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

internal sealed record PublishRecoveryCandidate
{
    public required string ExecutionUnit { get; init; }
    public required string? LinkedIssueRepo { get; init; }
    public required int? LinkedIssueNumber { get; init; }
    /// <summary>
    /// G315: optional existing linked_issue URL captured from queue-state.
    /// When set, the linked-issue lane preserves this URL on the repair so
    /// `linked_issue` is not silently rewritten when only `linked_pr` was
    /// missing.
    /// </summary>
    public string? LinkedIssueUrl { get; init; }
    public required string? LinkedPrUrl { get; init; }
    public required IssuePublishArtifact? PublishArtifact { get; init; }
    public required string PublishArtifactExpectedPath { get; init; }
}

internal sealed record PublishRecoveryAnalysis
{
    public required string Repo { get; init; }
    public required IReadOnlyList<PublishRecoveryRepair> SafeRepairs { get; init; }
    public required IReadOnlyList<PublishRecoveryUnsafeStop> UnsafeStops { get; init; }
}

internal sealed record PublishRecoveryRepair
{
    public required string Type { get; init; }
    public required string ExecutionUnit { get; init; }
    public required string LinkedIssueRepo { get; init; }
    public required int LinkedIssueNumber { get; init; }
    public required string? LinkedIssueUrl { get; init; }
    public required int LinkedPrNumber { get; init; }
    public required string? LinkedPrUrl { get; init; }
    public required string PublishArtifactPath { get; init; }
    public required string Confidence { get; init; }
    public required IReadOnlyList<string> Evidence { get; init; }
    public required string Summary { get; init; }
}

internal sealed record PublishRecoveryUnsafeStop
{
    public required string Kind { get; init; }
    public required string ExecutionUnit { get; init; }
    public required string Reason { get; init; }
    public required string PublishArtifactPath { get; init; }
}
