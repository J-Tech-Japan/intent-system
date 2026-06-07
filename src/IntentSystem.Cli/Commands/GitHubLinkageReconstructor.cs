using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G329: pure analyzer that reconstructs <c>linked_pr</c> ↔ execution
/// unit linkage from GitHub closing-issue facts and the current
/// queue-state, BEFORE the review closeout path stops with
/// <c>host-metadata-blocked: no queue item found with linked_pr</c>.
///
/// The reconstructor takes the list of issue numbers the PR closes
/// (as reported by <c>gh pr view &lt;n&gt; --json closingIssuesReferences</c>)
/// and walks the queue items for ones whose <c>linked_issue.number</c>
/// matches any of those closing issues. The outcome is:
///
/// <list type="bullet">
///   <item><c>Deterministic</c> — exactly one queue item matches; the
///   caller may treat <c>linked_pr</c> as recoverable for that item.</item>
///   <item><c>Ambiguous</c> — multiple queue items match (e.g. two
///   active items with the same linked_issue); the caller MUST surface
///   the ambiguity instead of guessing.</item>
///   <item><c>NoClosingReferences</c> — the PR body does not list any
///   closing issues; the caller falls through to the existing
///   host-metadata-blocked behavior (no guessing per G329 out-of-scope).</item>
///   <item><c>NoMatch</c> — closing issues were supplied but no queue
///   item links to any of them; the caller falls through to the
///   existing recovery path (publish-recovery / reconcile).</item>
/// </list>
///
/// Pure — no I/O, no GitHub calls. The caller is responsible for
/// fetching closing issues with <c>gh pr view</c> and passing them in.
/// </summary>
internal static class GitHubLinkageReconstructor
{
    /// <summary>
    /// Reconstruct linkage from closing-issue facts. The
    /// <paramref name="queueState"/> may be null (caller did not load
    /// it or failed to parse) in which case the result is
    /// <see cref="LinkageReconstructionKind.NoMatch"/> — the caller
    /// falls through to its normal flow.
    /// </summary>
    public static LinkageReconstructionResult Reconstruct(
        IReadOnlyList<int> closingIssueNumbers,
        QueueState? queueState,
        string? repo = null)
    {
        ArgumentNullException.ThrowIfNull(closingIssueNumbers);

        if (closingIssueNumbers.Count == 0)
        {
            return new LinkageReconstructionResult
            {
                Kind = LinkageReconstructionKind.NoClosingReferences,
                Candidates = Array.Empty<LinkageCandidate>()
            };
        }

        if (queueState is null)
        {
            return new LinkageReconstructionResult
            {
                Kind = LinkageReconstructionKind.NoMatch,
                Candidates = Array.Empty<LinkageCandidate>()
            };
        }

        var closingSet = closingIssueNumbers.ToHashSet();
        var candidates = new List<LinkageCandidate>();
        foreach (var item in queueState.Items)
        {
            var linkedIssueNumber = item.LinkedIssue?.Number;
            if (linkedIssueNumber is null)
            {
                continue;
            }
            if (!closingSet.Contains(linkedIssueNumber.Value))
            {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(repo)
                && !string.Equals(item.LinkedIssue!.Repo, repo, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            candidates.Add(new LinkageCandidate
            {
                ExecutionUnit = item.ExecutionUnit,
                LinkedIssueNumber = linkedIssueNumber.Value,
                LinkedIssueRepo = item.LinkedIssue!.Repo,
                LinkedIssueUrl = item.LinkedIssue!.Url
            });
        }

        return candidates.Count switch
        {
            0 => new LinkageReconstructionResult
            {
                Kind = LinkageReconstructionKind.NoMatch,
                Candidates = Array.Empty<LinkageCandidate>()
            },
            1 => new LinkageReconstructionResult
            {
                Kind = LinkageReconstructionKind.Deterministic,
                Candidates = candidates
            },
            _ => new LinkageReconstructionResult
            {
                Kind = LinkageReconstructionKind.Ambiguous,
                Candidates = candidates
            }
        };
    }
}

/// <summary>
/// G329: outcome of <see cref="GitHubLinkageReconstructor.Reconstruct"/>.
/// </summary>
internal enum LinkageReconstructionKind
{
    /// <summary>Exactly one queue item matches a closing issue.</summary>
    Deterministic,
    /// <summary>Multiple queue items match — caller must surface ambiguity.</summary>
    Ambiguous,
    /// <summary>The PR body did not list any closing issues.</summary>
    NoClosingReferences,
    /// <summary>Closing issues supplied but no queue item matches them.</summary>
    NoMatch
}

/// <summary>
/// G329: one candidate queue item matched against a PR closing issue.
/// </summary>
internal sealed record LinkageCandidate
{
    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("linked_issue_number")]
    public required int LinkedIssueNumber { get; init; }

    [JsonPropertyName("linked_issue_repo")]
    public required string LinkedIssueRepo { get; init; }

    [JsonPropertyName("linked_issue_url")]
    public string? LinkedIssueUrl { get; init; }
}

/// <summary>
/// G329: full reconstruction outcome surfaced to the closeout planner.
/// </summary>
internal sealed record LinkageReconstructionResult
{
    public required LinkageReconstructionKind Kind { get; init; }
    public required IReadOnlyList<LinkageCandidate> Candidates { get; init; }
}

/// <summary>
/// G329: recovered linkage payload emitted on
/// <see cref="ReviewCloseoutPlanResult"/> when the closeout planner
/// can deterministically reconstruct <c>linked_pr</c> from GitHub
/// closing-issue facts. The review runtime persists this payload
/// (out of scope for the child loop) — the child loop only surfaces
/// it for downstream automation to act on.
/// </summary>
internal sealed record RecoveredLinkagePayload
{
    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("linked_pr_number")]
    public required int LinkedPrNumber { get; init; }

    [JsonPropertyName("linked_pr_repo")]
    public required string LinkedPrRepo { get; init; }

    [JsonPropertyName("linked_pr_url")]
    public required string LinkedPrUrl { get; init; }

    [JsonPropertyName("linked_issue_number")]
    public required int LinkedIssueNumber { get; init; }

    [JsonPropertyName("linked_issue_repo")]
    public required string LinkedIssueRepo { get; init; }

    [JsonPropertyName("linked_issue_url")]
    public string? LinkedIssueUrl { get; init; }

    /// <summary>
    /// How the linkage was recovered. Always
    /// <c>github-closing-reference</c> in this slice — surfaces the
    /// recovery provenance so persisting consumers can record it in
    /// queue-state / runs.jsonl.
    /// </summary>
    [JsonPropertyName("recovery_source")]
    public required string RecoverySource { get; init; }
}

/// <summary>
/// G329: structured ambiguity payload emitted when the reconstructor
/// finds more than one queue item matching the PR closing issues.
/// The closeout planner surfaces this instead of guessing, and the
/// host loop must surface it to the operator for disambiguation.
/// </summary>
internal sealed record AmbiguousLinkagePayload
{
    [JsonPropertyName("candidates")]
    public required IReadOnlyList<LinkageCandidate> Candidates { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}

/// <summary>
/// G329: canonical recovery-source / gap-classification strings.
/// </summary>
internal static class LinkageReconstructionConstants
{
    public const string RecoverySourceGitHubClosingReference = "github-closing-reference";
    public const string GapClassificationLinkageAmbiguous = "linkage-ambiguous";
}
