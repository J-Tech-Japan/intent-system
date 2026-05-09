using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G307: pure analyzer that compares the recorded lifecycle in
/// <see cref="IssuePublishArtifact.LifecycleState"/> against the
/// observable reality (queue-state item state, GitHub issue / PR
/// labels) and classifies each execution unit's lifecycle drift.
///
/// The analyzer only proposes a repair when the evidence is
/// deterministic: the GitHub label / queue-state shape unambiguously
/// identifies the correct lifecycle state. Ambiguous cases (e.g.
/// `intent-pr-created` on the issue but no queue item linked, or a
/// closed issue with no linked PR record) surface as
/// <c>unsafe-stale-lifecycle</c> instead of recommending a guess.
///
/// Pure data in / pure data out: no `gh` calls, no file I/O, no
/// state mutation. The command layer captures the inputs and applies
/// the writes when <c>--write</c> is supplied.
/// </summary>
internal static class PublishLifecycleAnalyzer
{
    public const string ClassificationCoherent = "coherent";
    public const string ClassificationStaleIssueCreated = "stale-issue-created";
    public const string ClassificationStalePublished = "stale-published";
    public const string ClassificationStalePrCreated = "stale-pr-created";
    public const string ClassificationUnsafe = "unsafe-stale-lifecycle";
    public const string ClassificationMissingArtifact = "missing-publish-artifact";

    public static PublishLifecycleAnalysis Analyze(
        IReadOnlyList<PublishLifecycleCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var entries = new List<PublishLifecycleEntry>();
        foreach (var candidate in candidates)
        {
            entries.Add(Classify(candidate));
        }

        return new PublishLifecycleAnalysis
        {
            Entries = entries,
            CoherentCount = entries.Count(e => e.Classification == ClassificationCoherent),
            DriftCount = entries.Count(e =>
                e.Classification == ClassificationStaleIssueCreated
                || e.Classification == ClassificationStalePublished
                || e.Classification == ClassificationStalePrCreated),
            UnsafeCount = entries.Count(e => e.Classification == ClassificationUnsafe),
            MissingArtifactCount = entries.Count(e => e.Classification == ClassificationMissingArtifact)
        };
    }

    private static PublishLifecycleEntry Classify(PublishLifecycleCandidate candidate)
    {
        if (candidate.Artifact is null)
        {
            return new PublishLifecycleEntry
            {
                ExecutionUnit = candidate.ExecutionUnit,
                ArtifactPath = candidate.ArtifactPath,
                CurrentLifecycleState = null,
                RecommendedLifecycleState = null,
                Classification = ClassificationMissingArtifact,
                Evidence = new[]
                {
                    $"no publish.yaml at '{candidate.ArtifactPath}'; cannot classify lifecycle for '{candidate.ExecutionUnit}'."
                },
                RecommendedLinkedPrNumber = null,
                RecommendedLinkedPrUrl = null,
                RecommendedClosedOutAt = null
            };
        }

        var artifact = candidate.Artifact;
        var current = string.IsNullOrEmpty(artifact.LifecycleState)
            ? IssuePublishLifecycle.IssueCreated
            : artifact.LifecycleState!;

        // Decide what reality says.
        var queueCompleted = candidate.QueueItemState == QueueItemState.Completed;
        var issueClosed = string.Equals(candidate.IssueState, "closed", StringComparison.OrdinalIgnoreCase);
        var hasIntentTargetOnIssue = candidate.IssueLabels.Contains(WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal);
        var hasIntentPrCreated = candidate.IssueLabels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal);
        var linkedPrNumber = candidate.LinkedPrNumber;
        var linkedPrUrl = candidate.LinkedPrUrl;

        // Determine the canonical lifecycle from reality.
        string? canonical;
        var evidence = new List<string>();

        if (queueCompleted && issueClosed)
        {
            canonical = IssuePublishLifecycle.ClosedOut;
            evidence.Add($"queue item state = completed AND GitHub issue state = closed → lifecycle should be `closed-out`.");
        }
        else if (hasIntentPrCreated && linkedPrNumber is { } prNum)
        {
            canonical = IssuePublishLifecycle.PrCreated;
            evidence.Add($"GitHub issue carries `intent-pr-created` and queue records linked_pr=#{prNum} → lifecycle should be `pr-created`.");
        }
        else if (hasIntentPrCreated && linkedPrNumber is null)
        {
            // The issue says a PR exists but the queue has no linked_pr —
            // this is exactly the G303 publish-recovery case. Surface as
            // unsafe so the operator routes it through the deterministic
            // recovery lane instead of guessing here.
            return new PublishLifecycleEntry
            {
                ExecutionUnit = candidate.ExecutionUnit,
                ArtifactPath = candidate.ArtifactPath,
                CurrentLifecycleState = current,
                RecommendedLifecycleState = null,
                Classification = ClassificationUnsafe,
                Evidence = new[]
                {
                    "GitHub issue has `intent-pr-created` but the queue item has no linked_pr.",
                    "Refusing to set `lifecycle_state = pr-created` without the linked PR ref. Run `intent-cli automation publish-recovery` (G303) first to repair the linked refs deterministically, then re-run lifecycle repair."
                },
                RecommendedLinkedPrNumber = null,
                RecommendedLinkedPrUrl = null,
                RecommendedClosedOutAt = null
            };
        }
        else if (hasIntentTargetOnIssue)
        {
            canonical = IssuePublishLifecycle.Published;
            evidence.Add("GitHub issue carries `intent-target` and there is no `intent-pr-created` yet → lifecycle should be `published`.");
        }
        else
        {
            canonical = IssuePublishLifecycle.IssueCreated;
            evidence.Add("GitHub issue does not carry `intent-target` yet → lifecycle is `issue-created` (pre-publish).");
        }

        if (string.Equals(current, canonical, StringComparison.Ordinal))
        {
            return new PublishLifecycleEntry
            {
                ExecutionUnit = candidate.ExecutionUnit,
                ArtifactPath = candidate.ArtifactPath,
                CurrentLifecycleState = current,
                RecommendedLifecycleState = current,
                Classification = ClassificationCoherent,
                Evidence = evidence,
                RecommendedLinkedPrNumber = artifact.LinkedPrNumber,
                RecommendedLinkedPrUrl = artifact.LinkedPrUrl,
                RecommendedClosedOutAt = artifact.ClosedOutAt
            };
        }

        // Choose the drift classification by what the artifact currently
        // says, so the operator can grep for "stuck at issue-created" etc.
        var driftClassification = current switch
        {
            IssuePublishLifecycle.IssueCreated => ClassificationStaleIssueCreated,
            IssuePublishLifecycle.Published => ClassificationStalePublished,
            IssuePublishLifecycle.PrCreated => ClassificationStalePrCreated,
            _ => ClassificationStaleIssueCreated
        };

        // Refuse to recommend a downgrade (lower rank than current) — that
        // would only happen on label deletion / queue rewind, which is
        // host-policy-ambiguous and must be operator-driven.
        if (IssuePublishLifecycle.Rank(canonical) < IssuePublishLifecycle.Rank(current))
        {
            evidence.Add($"observed lifecycle `{canonical}` is BEFORE recorded `{current}`; refusing to downgrade automatically.");
            return new PublishLifecycleEntry
            {
                ExecutionUnit = candidate.ExecutionUnit,
                ArtifactPath = candidate.ArtifactPath,
                CurrentLifecycleState = current,
                RecommendedLifecycleState = null,
                Classification = ClassificationUnsafe,
                Evidence = evidence,
                RecommendedLinkedPrNumber = null,
                RecommendedLinkedPrUrl = null,
                RecommendedClosedOutAt = null
            };
        }

        return new PublishLifecycleEntry
        {
            ExecutionUnit = candidate.ExecutionUnit,
            ArtifactPath = candidate.ArtifactPath,
            CurrentLifecycleState = current,
            RecommendedLifecycleState = canonical,
            Classification = driftClassification,
            Evidence = evidence,
            RecommendedLinkedPrNumber = canonical == IssuePublishLifecycle.PrCreated
                || canonical == IssuePublishLifecycle.ClosedOut
                ? linkedPrNumber
                : null,
            RecommendedLinkedPrUrl = canonical == IssuePublishLifecycle.PrCreated
                || canonical == IssuePublishLifecycle.ClosedOut
                ? linkedPrUrl
                : null,
            RecommendedClosedOutAt = canonical == IssuePublishLifecycle.ClosedOut
                ? candidate.NowIso
                : null
        };
    }
}

internal sealed record PublishLifecycleCandidate
{
    public required string ExecutionUnit { get; init; }
    public required string ArtifactPath { get; init; }
    public required IssuePublishArtifact? Artifact { get; init; }
    public required QueueItemState? QueueItemState { get; init; }
    public required int? LinkedPrNumber { get; init; }
    public required string? LinkedPrUrl { get; init; }
    public required string? IssueState { get; init; }
    public required IReadOnlyCollection<string> IssueLabels { get; init; }
    public required string NowIso { get; init; }
}

internal sealed record PublishLifecycleAnalysis
{
    public required IReadOnlyList<PublishLifecycleEntry> Entries { get; init; }
    public required int CoherentCount { get; init; }
    public required int DriftCount { get; init; }
    public required int UnsafeCount { get; init; }
    public required int MissingArtifactCount { get; init; }
}

internal sealed record PublishLifecycleEntry
{
    public required string ExecutionUnit { get; init; }
    public required string ArtifactPath { get; init; }
    public required string? CurrentLifecycleState { get; init; }
    public required string? RecommendedLifecycleState { get; init; }
    public required string Classification { get; init; }
    public required IReadOnlyList<string> Evidence { get; init; }
    public required int? RecommendedLinkedPrNumber { get; init; }
    public required string? RecommendedLinkedPrUrl { get; init; }
    public required string? RecommendedClosedOutAt { get; init; }
}
