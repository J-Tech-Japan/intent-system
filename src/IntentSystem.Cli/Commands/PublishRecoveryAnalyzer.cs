using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G303: pure analyzer for publish-artifact-backed host-review metadata
/// recovery. The G284 reconcile lane handles items that already have a
/// <c>linked_issue</c> but no <c>linked_pr</c>; this analyzer covers the
/// opposite case where a queued execution unit has BOTH refs null but
/// the host repo's <c>.intent-cli/issues/&lt;execution-unit&gt;/publish.yaml</c>
/// recorded a created issue number, AND exactly one open PR uniquely
/// closes that issue. In that case the repair is fully deterministic and
/// can be applied with <c>--write</c>; ambiguity, repo mismatch,
/// missing publish artifact, or already-conflicting metadata produce
/// structured unsafe stops instead.
///
/// Pure data in, pure data out — no `gh` calls, no file I/O. The
/// command layer captures the inputs and applies the writes.
/// </summary>
internal static class PublishRecoveryAnalyzer
{
    public const string RepairType = "publish-artifact-linked-refs-recovery";
    public const string UnsafeMissingPublishArtifact = "missing-publish-artifact";
    public const string UnsafeMissingCreatedIssue = "publish-artifact-no-created-issue";
    public const string UnsafeNoClosingPr = "no-closing-pr-for-published-issue";
    public const string UnsafeMultipleClosingPrs = "multiple-closing-prs-for-published-issue";
    public const string UnsafeRepoMismatch = "publish-recovery-repo-mismatch";

    private static readonly Regex ClosesIssueRegex = new(
        @"(?i)\b(?:close[sd]?|fix(?:es|ed)?|resolve[sd]?)\s+(?:(?<repo>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+))?#(?<number>\d+)\b",
        RegexOptions.Compiled);

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

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate.LinkedIssueRepo)
                || candidate.LinkedIssueNumber is not null
                || !string.IsNullOrEmpty(candidate.LinkedPrUrl))
            {
                // The queue row already has at least one linked ref. G284
                // handles the linked_issue → linked_pr case; this analyzer
                // is scoped to BOTH-null rows so we don't accidentally
                // overwrite live state.
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
