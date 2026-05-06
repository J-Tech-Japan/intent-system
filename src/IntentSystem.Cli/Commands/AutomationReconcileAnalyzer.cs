using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G277: Pure analyzer for the host-side safe reconcile lane. Inspects open
/// intent-target PRs and issues plus their cross-links to detect mechanically
/// provable label drift (high confidence) and link-drift advisory entries
/// (lower confidence). Never reads files, never mutates GitHub or local state,
/// and never launches an AI provider. The command layer wraps this with I/O
/// and the optional <c>--write</c> path that goes through the host-owned
/// <see cref="IGitHubLabelMutator.ApplyReconcileTransitions"/> entry point.
/// </summary>
internal static class AutomationReconcileAnalyzer
{
    private static readonly Regex ClosesIssueRegex = new(
        @"(?i)\b(?:close[sd]?|fix(?:es|ed)?|resolve[sd]?)\s+(?:(?<repo>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+))?#(?<number>\d+)\b",
        RegexOptions.Compiled);

    public static AutomationReconcileAnalysis AnalyzeHostReview(
        string repo,
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs,
        IReadOnlyList<GitHubAutomationIssueCandidate> publishedIntentTargetIssues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(openPrs);
        ArgumentNullException.ThrowIfNull(publishedIntentTargetIssues);

        var safeRepairs = new List<AutomationReconcileRepair>();
        var unsafeStops = new List<AutomationReconcileUnsafeStop>();

        var publishedIssuesByNumber = publishedIntentTargetIssues
            .Where(IsPublishedIntentTargetIssue)
            .ToDictionary(issue => issue.Number);

        var prsLinkingPublishedIssue = new Dictionary<int, List<int>>();

        foreach (var pr in openPrs)
        {
            var prLabels = LabelNames(pr.Labels);
            var linkedIssueNumbers = ExtractLinkedIssueNumbers(repo, pr);
            var publishedLinkedIssues = linkedIssueNumbers
                .Where(publishedIssuesByNumber.ContainsKey)
                .ToArray();

            foreach (var issueNumber in publishedLinkedIssues)
            {
                if (!prsLinkingPublishedIssue.TryGetValue(issueNumber, out var list))
                {
                    list = new List<int>();
                    prsLinkingPublishedIssue[issueNumber] = list;
                }

                if (!list.Contains(pr.Number))
                {
                    list.Add(pr.Number);
                }
            }

            // Repair 1: PR closes a published intent-target issue but lacks intent-target.
            if (publishedLinkedIssues.Length > 0
                && !prLabels.Contains(WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal))
            {
                var primary = publishedLinkedIssues[0];
                safeRepairs.Add(new AutomationReconcileRepair
                {
                    Type = AutomationReconcileRepairTypes.MissingPrIntentTarget,
                    TargetKind = GhCliGitHubLabelMutator.Kinds.Pr,
                    TargetNumber = pr.Number,
                    TargetUrl = pr.Url,
                    AddLabels = [WorkerNextActionConstants.Labels.IntentTarget],
                    RemoveLabels = Array.Empty<string>(),
                    Evidence =
                    [
                        $"PR #{pr.Number} body or closing-issue references include #{primary}",
                        $"issue #{primary} carries 'intent-target' and 'intent-pr-created' (published intent-target issue)"
                    ],
                    Confidence = AutomationReconcileConfidence.High,
                    Applied = false,
                    Summary = $"Add intent-target to PR #{pr.Number} (links published intent-target issue #{primary}).",
                    RequiresFollowupCommand = null,
                });
            }

            // Repair 2: PR carries intent-pr-created (which is issue-only by policy).
            if (prLabels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal))
            {
                safeRepairs.Add(new AutomationReconcileRepair
                {
                    Type = AutomationReconcileRepairTypes.MisplacedPrIntentPrCreated,
                    TargetKind = GhCliGitHubLabelMutator.Kinds.Pr,
                    TargetNumber = pr.Number,
                    TargetUrl = pr.Url,
                    AddLabels = Array.Empty<string>(),
                    RemoveLabels = [WorkerNextActionConstants.Labels.IntentPrCreated],
                    Evidence =
                    [
                        $"PR #{pr.Number} carries 'intent-pr-created' which is issue-only by policy"
                    ],
                    Confidence = AutomationReconcileConfidence.High,
                    Applied = false,
                    Summary = $"Remove misplaced intent-pr-created from PR #{pr.Number}.",
                    RequiresFollowupCommand = null,
                });
            }

            // Advisory: PR is intent-target but body has no Closes/Fixes/Resolves keyword
            // and no closing-issue reference. Cannot be repaired without operator input.
            if (prLabels.Contains(WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal)
                && linkedIssueNumbers.Count == 0)
            {
                unsafeStops.Add(new AutomationReconcileUnsafeStop
                {
                    Kind = AutomationReconcileUnsafeStopKinds.AmbiguousIssueLink,
                    TargetKind = GhCliGitHubLabelMutator.Kinds.Pr,
                    TargetNumber = pr.Number,
                    TargetUrl = pr.Url,
                    Reason = $"PR #{pr.Number} is intent-target but body has no Closes/Fixes/Resolves keyword and no closing-issue reference; closeout keyword cannot be derived deterministically.",
                    MissingEvidence =
                    [
                        "PR body lacks 'Closes #N' / 'Fixes #N' / 'Resolves #N'",
                        "no GitHub closing-issue reference present"
                    ],
                });
            }

            // Advisory: PR closes a published issue, so linked_pr metadata is derivable.
            // We do not mutate queue-state here; we surface the followup for a host
            // closeout/packet command to apply through its existing surface.
            if (publishedLinkedIssues.Length == 1)
            {
                var issueNumber = publishedLinkedIssues[0];
                safeRepairs.Add(new AutomationReconcileRepair
                {
                    Type = AutomationReconcileRepairTypes.MissingLinkedPrMetadata,
                    TargetKind = "queue-state",
                    TargetNumber = issueNumber,
                    TargetUrl = publishedIssuesByNumber[issueNumber].Url,
                    AddLabels = Array.Empty<string>(),
                    RemoveLabels = Array.Empty<string>(),
                    Evidence =
                    [
                        $"PR #{pr.Number} closing-issue references uniquely include #{issueNumber}",
                        $"issue #{issueNumber} is a published intent-target issue"
                    ],
                    Confidence = AutomationReconcileConfidence.Advisory,
                    Applied = false,
                    Summary = $"linked_pr for execution-unit of issue #{issueNumber} can be derived as PR #{pr.Number}; apply through the host closeout/packet surface, not this lane.",
                    RequiresFollowupCommand = $"intent-cli closeout pr --pr {pr.Number} --repo {repo}",
                });
            }
        }

        // Repair 3: published intent-target issue with a linked open PR but no
        // intent-pr-created on the issue (state mismatch).
        foreach (var (issueNumber, prsLinking) in prsLinkingPublishedIssue)
        {
            var issue = publishedIssuesByNumber[issueNumber];
            var issueLabels = LabelNames(issue.Labels);
            if (!issueLabels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal))
            {
                var primaryPr = prsLinking.OrderBy(n => n).First();
                safeRepairs.Add(new AutomationReconcileRepair
                {
                    Type = AutomationReconcileRepairTypes.MissingIssueIntentPrCreated,
                    TargetKind = GhCliGitHubLabelMutator.Kinds.Issue,
                    TargetNumber = issueNumber,
                    TargetUrl = issue.Url,
                    AddLabels = [WorkerNextActionConstants.Labels.IntentPrCreated],
                    RemoveLabels = Array.Empty<string>(),
                    Evidence =
                    [
                        $"open PR #{primaryPr} closes issue #{issueNumber}",
                        $"issue #{issueNumber} lacks intent-pr-created completion marker"
                    ],
                    Confidence = AutomationReconcileConfidence.High,
                    Applied = false,
                    Summary = $"Add intent-pr-created to source issue #{issueNumber} (linked PR #{primaryPr}).",
                    RequiresFollowupCommand = null,
                });
            }
        }

        return new AutomationReconcileAnalysis(safeRepairs, unsafeStops);
    }

    public static AutomationReconcileAnalysis AnalyzeNextSlice(
        bool nextSliceClassifiedClarificationRequired,
        bool clarificationFilesShowNoOpenBlocker)
    {
        var safeRepairs = new List<AutomationReconcileRepair>();
        var unsafeStops = new List<AutomationReconcileUnsafeStop>();

        if (nextSliceClassifiedClarificationRequired && clarificationFilesShowNoOpenBlocker)
        {
            safeRepairs.Add(new AutomationReconcileRepair
            {
                Type = AutomationReconcileRepairTypes.StaleNextSliceCandidateCache,
                TargetKind = "queue-state",
                TargetNumber = null,
                TargetUrl = null,
                AddLabels = Array.Empty<string>(),
                RemoveLabels = Array.Empty<string>(),
                Evidence =
                [
                    "next-slice classified clarification-required",
                    "no open Hard Clarification entries are present in the source clarifications"
                ],
                Confidence = AutomationReconcileConfidence.Advisory,
                Applied = false,
                Summary = "Next-slice candidate cache appears stale; rerun next-slice classifier with refreshed inputs to clear the false-positive clarification-required state.",
                RequiresFollowupCommand = "intent-cli intent next-slice --dry-run --format json",
            });
        }

        if (nextSliceClassifiedClarificationRequired && !clarificationFilesShowNoOpenBlocker)
        {
            unsafeStops.Add(new AutomationReconcileUnsafeStop
            {
                Kind = "open-clarification-present",
                TargetKind = null,
                TargetNumber = null,
                TargetUrl = null,
                Reason = "next-slice classifier returned clarification-required and the source clarifications still show at least one open Hard Clarification; reconcile cannot bypass an unresolved blocker.",
                MissingEvidence =
                [
                    "no source clarification file currently states 'no open blockers'"
                ],
            });
        }

        return new AutomationReconcileAnalysis(safeRepairs, unsafeStops);
    }

    public static AutomationReconcileAnalysis Combine(
        AutomationReconcileAnalysis a,
        AutomationReconcileAnalysis b) =>
        new(
            a.SafeRepairs.Concat(b.SafeRepairs).ToArray(),
            a.UnsafeStops.Concat(b.UnsafeStops).ToArray());

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

    private static bool IsPublishedIntentTargetIssue(GitHubAutomationIssueCandidate issue)
    {
        var labels = LabelNames(issue.Labels);
        return labels.Contains(WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal);
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

internal sealed record AutomationReconcileAnalysis(
    IReadOnlyList<AutomationReconcileRepair> SafeRepairs,
    IReadOnlyList<AutomationReconcileUnsafeStop> UnsafeStops);
