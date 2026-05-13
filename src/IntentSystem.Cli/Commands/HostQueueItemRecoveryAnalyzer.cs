using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G344: Pure analyzer that detects the specific host-metadata drift where
/// review closeout reaches a non-draft PR with valid GitHub facts (the PR
/// closes a published intent-target issue, the packet directory and
/// publish.yaml both exist for the execution unit), but
/// <c>.intent-cli/queue-state.json</c> is missing the corresponding queue
/// item or has a stale <c>linked_pr</c>. Reconstructs the missing queue
/// item deterministically when all evidence agrees on a single execution
/// unit, target repo, issue number, and PR number; otherwise surfaces a
/// structured unsafe stop with a reason code.
///
/// Pure data in / pure data out: no <c>gh</c> calls, no file I/O. The
/// command layer captures the inputs and writes through
/// <see cref="QueueStateSerializer"/> when <c>--write</c> is supplied.
/// </summary>
internal static class HostQueueItemRecoveryAnalyzer
{
    public const string ResultRecoverable = "recoverable";
    public const string ResultNotApplicable = "not-applicable";
    public const string ResultUnsafeStop = "unsafe-stop";

    public const string ReasonPacketPublishUnitMismatch = "packet-publish-unit-mismatch";
    public const string ReasonPublishIssueMismatch = "publish-issue-mismatch";
    public const string ReasonPrDoesNotCloseIssue = "pr-does-not-close-issue";
    public const string ReasonRepoMismatch = "repo-mismatch";
    public const string ReasonMultipleMatchingPackets = "multiple-matching-packets";
    public const string ReasonConflictingExistingQueueItem = "conflicting-existing-queue-item";
    public const string ReasonMissingPublishArtifact = "missing-publish-artifact";
    public const string ReasonMissingPacketArtifact = "missing-packet-artifact";
    public const string ReasonClosedIssue = "closed-issue";
    public const string ReasonClosedPr = "closed-pr";
    public const string ReasonPrIsDraft = "pr-is-draft";

    /// <summary>
    /// Analyze one candidate recovery: a single execution unit identified
    /// by packet + publish artifact + GitHub issue + GitHub PR facts,
    /// against the host queue-state items. Returns <c>recoverable</c>
    /// when every deterministic rule passes; <c>not-applicable</c> when
    /// queue-state already binds the same execution unit to the same
    /// linked PR and linked issue; otherwise <c>unsafe-stop</c> with a
    /// reason code.
    /// </summary>
    public static HostQueueItemRecoveryAnalysis Analyze(HostQueueItemRecoveryCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate.TargetRepo);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate.ExecutionUnit);

        var repo = candidate.TargetRepo;
        var unit = candidate.ExecutionUnit;

        // Packet artifact must exist for the execution unit.
        if (!candidate.PacketArtifactExists)
        {
            return UnsafeStop(
                ReasonMissingPacketArtifact,
                $"packet artifact for '{unit}' is missing (.intent-cli/issues/{unit}/packet.yaml); "
                + "cannot reconstruct queue item without the packet facts.");
        }

        // Publish artifact must exist for the execution unit.
        if (candidate.Publish is null)
        {
            return UnsafeStop(
                ReasonMissingPublishArtifact,
                $"publish artifact for '{unit}' is missing (.intent-cli/issues/{unit}/publish.yaml); "
                + "cannot reconstruct queue item without the publish-side facts.");
        }

        // Packet target repo must match the requested repo. Empty / null
        // packet target repo is tolerated (older packets); only an
        // explicit mismatch is unsafe.
        if (!string.IsNullOrWhiteSpace(candidate.PacketTargetRepo)
            && !string.Equals(candidate.PacketTargetRepo, repo, StringComparison.OrdinalIgnoreCase))
        {
            return UnsafeStop(
                ReasonRepoMismatch,
                $"packet target_repo '{candidate.PacketTargetRepo}' does not match requested repo '{repo}'; "
                + "refusing to reconstruct queue item against the wrong repository.");
        }

        // publish.yaml's created_issue_url (when present) must point at
        // the requested repo.
        if (!string.IsNullOrEmpty(candidate.Publish.CreatedIssueUrl)
            && !ArtifactUrlMatchesRepo(candidate.Publish.CreatedIssueUrl!, repo))
        {
            return UnsafeStop(
                ReasonRepoMismatch,
                $"publish.yaml created_issue_url '{candidate.Publish.CreatedIssueUrl}' does not match requested repo '{repo}'.");
        }

        // Packet & publish must agree on the execution unit.
        if (!string.Equals(candidate.Publish.ExecutionUnit, unit, StringComparison.Ordinal))
        {
            return UnsafeStop(
                ReasonPacketPublishUnitMismatch,
                $"publish.yaml execution_unit '{candidate.Publish.ExecutionUnit}' does not match packet execution unit '{unit}'.");
        }

        // publish.yaml must record a created issue number.
        if (candidate.Publish.CreatedIssueNumber is not int publishIssueNumber || publishIssueNumber <= 0)
        {
            return UnsafeStop(
                ReasonPublishIssueMismatch,
                $"publish.yaml for '{unit}' has no created_issue_number; cannot bind queue item to a GitHub issue.");
        }

        // The GitHub closing-issue number must equal the publish-issue
        // number.
        if (candidate.ClosingIssue is null)
        {
            return UnsafeStop(
                ReasonPrDoesNotCloseIssue,
                $"PR #{candidate.PrNumber} in '{repo}' has no closing-issue reference for issue #{publishIssueNumber}; "
                + "cannot recover queue item without a deterministic PR<->issue link.");
        }

        if (candidate.ClosingIssue.Number != publishIssueNumber)
        {
            return UnsafeStop(
                ReasonPublishIssueMismatch,
                $"PR #{candidate.PrNumber} closing-issue #{candidate.ClosingIssue.Number} does not match publish.yaml "
                + $"created_issue_number #{publishIssueNumber} for '{unit}'.");
        }

        // The closing issue's repo (if reported) must equal the requested repo.
        if (!string.IsNullOrEmpty(candidate.ClosingIssue.Repo)
            && !string.Equals(candidate.ClosingIssue.Repo, repo, StringComparison.OrdinalIgnoreCase))
        {
            return UnsafeStop(
                ReasonRepoMismatch,
                $"PR #{candidate.PrNumber} closes issue in repo '{candidate.ClosingIssue.Repo}', not in requested repo '{repo}'.");
        }

        // PR target repo (when present) must equal requested repo.
        if (!string.IsNullOrWhiteSpace(candidate.PrRepo)
            && !string.Equals(candidate.PrRepo, repo, StringComparison.OrdinalIgnoreCase))
        {
            return UnsafeStop(
                ReasonRepoMismatch,
                $"PR #{candidate.PrNumber} target repo '{candidate.PrRepo}' does not match requested repo '{repo}'.");
        }

        // Issue must be open OR closed-with-intent-pr-created (host has
        // progressed past publish boundary). A bare closed issue without
        // intent-pr-created is an explicit operator close — refuse.
        var issueClosed = string.Equals(candidate.ClosingIssue.State, "CLOSED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.ClosingIssue.State, "closed", StringComparison.OrdinalIgnoreCase);
        var issueHasIntentPrCreated = candidate.ClosingIssue.Labels.Contains(
            WorkerNextActionConstants.Labels.IntentPrCreated,
            StringComparer.Ordinal);
        if (issueClosed && !issueHasIntentPrCreated)
        {
            return UnsafeStop(
                ReasonClosedIssue,
                $"GitHub issue #{publishIssueNumber} in '{repo}' is closed without 'intent-pr-created'; "
                + "refusing to reconstruct queue item against an explicitly-closed issue.");
        }

        // PR must be open and non-draft. A merged / closed PR is not a
        // recoverable review wake.
        var prClosed = string.Equals(candidate.PrState, "CLOSED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.PrState, "MERGED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.PrState, "closed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.PrState, "merged", StringComparison.OrdinalIgnoreCase);
        if (prClosed)
        {
            return UnsafeStop(
                ReasonClosedPr,
                $"PR #{candidate.PrNumber} in '{repo}' is in state '{candidate.PrState}'; "
                + "host queue-item recovery only applies while the PR is open for review.");
        }

        // Draft PRs are not recoverable review wakes — the host loop
        // must not advance a draft into approval/closeout paths.
        if (candidate.PrIsDraft)
        {
            return UnsafeStop(
                ReasonPrIsDraft,
                $"PR #{candidate.PrNumber} in '{repo}' is a draft; "
                + "review-safe recovery requires a non-draft PR.");
        }

        // No more than one packet/publish pair may claim this unit.
        if (candidate.OtherUnitsClaimingSameIssue is { Count: > 0 })
        {
            return UnsafeStop(
                ReasonMultipleMatchingPackets,
                $"multiple packets/publish artifacts claim GitHub issue #{publishIssueNumber} in '{repo}': "
                + $"'{unit}' plus {string.Join(", ", candidate.OtherUnitsClaimingSameIssue)}. "
                + "Cannot deterministically pick which execution unit owns the queue item recovery.");
        }

        // Conflicting existing queue item — an entry with the same unit
        // already bound to a *different* PR or issue, or another unit
        // already bound to this PR/issue.
        foreach (var existing in candidate.ExistingQueueItems ?? Array.Empty<HostQueueItemRecoveryExistingItem>())
        {
            var sameUnit = string.Equals(existing.ExecutionUnit, unit, StringComparison.Ordinal);
            var bindsThisPr = existing.LinkedPrNumber == candidate.PrNumber;
            var bindsThisIssue = existing.LinkedIssueNumber == publishIssueNumber;

            if (!sameUnit && (bindsThisPr || bindsThisIssue))
            {
                return UnsafeStop(
                    ReasonConflictingExistingQueueItem,
                    $"queue item for '{existing.ExecutionUnit}' already binds " +
                    (bindsThisPr ? $"PR #{candidate.PrNumber}" : $"issue #{publishIssueNumber}") +
                    $" — cannot reconstruct queue item for '{unit}' without resolving the conflict first.");
            }

            if (sameUnit
                && existing.LinkedPrNumber is int otherPr && otherPr > 0 && otherPr != candidate.PrNumber)
            {
                return UnsafeStop(
                    ReasonConflictingExistingQueueItem,
                    $"queue item for '{unit}' already binds linked_pr #{otherPr}, not #{candidate.PrNumber}; "
                    + "refusing to overwrite without operator review.");
            }

            if (sameUnit
                && existing.LinkedIssueNumber is int otherIssue && otherIssue > 0 && otherIssue != publishIssueNumber)
            {
                return UnsafeStop(
                    ReasonConflictingExistingQueueItem,
                    $"queue item for '{unit}' already binds linked_issue #{otherIssue}, not #{publishIssueNumber}; "
                    + "refusing to overwrite without operator review.");
            }
        }

        // If queue-state already has a fully-linked entry for this unit
        // pointing at the correct PR + issue, nothing to recover.
        var sameUnitItem = (candidate.ExistingQueueItems ?? Array.Empty<HostQueueItemRecoveryExistingItem>())
            .FirstOrDefault(i => string.Equals(i.ExecutionUnit, unit, StringComparison.Ordinal));
        if (sameUnitItem is not null
            && sameUnitItem.LinkedPrNumber == candidate.PrNumber
            && sameUnitItem.LinkedIssueNumber == publishIssueNumber)
        {
            return new HostQueueItemRecoveryAnalysis
            {
                Result = ResultNotApplicable,
                ReasonCode = null,
                Explanation = $"queue item for '{unit}' already binds PR #{candidate.PrNumber} and issue #{publishIssueNumber}; nothing to recover.",
                ProposedQueueItem = null,
                RecommendedCommand = null,
                Evidence = Array.Empty<string>(),
            };
        }

        // High-confidence recovery — build the proposed queue item.
        var prUrl = candidate.PrUrl ?? $"https://github.com/{repo}/pull/{candidate.PrNumber}";
        var issueUrl = candidate.ClosingIssue.Url
            ?? candidate.Publish.CreatedIssueUrl
            ?? $"https://github.com/{repo}/issues/{publishIssueNumber}";
        var existingUnitTitle = sameUnitItem?.Title;

        var proposed = new HostQueueItemRecoveryProposedItem
        {
            ExecutionUnit = unit,
            TargetRepo = repo,
            LinkedIssueNumber = publishIssueNumber,
            LinkedIssueRepo = repo,
            LinkedIssueUrl = issueUrl,
            LinkedPrNumber = candidate.PrNumber,
            LinkedPrUrl = prUrl,
            Title = existingUnitTitle,
        };

        var command = $"intent-cli automation host-queue-item-recovery --repo {repo} "
            + $"--unit {unit} --issue {publishIssueNumber} --pr {candidate.PrNumber} --write";

        var evidence = new List<string>
        {
            $"packet.yaml for '{unit}' exists at .intent-cli/issues/{unit}/packet.yaml.",
            $"publish.yaml for '{unit}' records created_issue_number = #{publishIssueNumber}.",
            $"PR #{candidate.PrNumber} in '{repo}' closes issue #{publishIssueNumber} (matching closing-issue reference).",
            $"GitHub issue #{publishIssueNumber} state = '{candidate.ClosingIssue.State}', labels = [{string.Join(", ", candidate.ClosingIssue.Labels)}].",
            $"no other packet/publish artifact in repo '{repo}' claims this issue; recovery is deterministic.",
        };

        return new HostQueueItemRecoveryAnalysis
        {
            Result = ResultRecoverable,
            ReasonCode = null,
            Explanation = $"Reconstruct queue item for '{unit}' binding linked_issue #{publishIssueNumber} and linked_pr #{candidate.PrNumber} (host-metadata recovery from packet + publish + GitHub facts).",
            ProposedQueueItem = proposed,
            RecommendedCommand = command,
            Evidence = evidence,
        };
    }

    private static HostQueueItemRecoveryAnalysis UnsafeStop(string reasonCode, string explanation) =>
        new()
        {
            Result = ResultUnsafeStop,
            ReasonCode = reasonCode,
            Explanation = explanation,
            ProposedQueueItem = null,
            RecommendedCommand = null,
            Evidence = Array.Empty<string>(),
        };

    private static bool ArtifactUrlMatchesRepo(string artifactUrl, string repo)
    {
        var marker = $"github.com/{repo}/issues/";
        return artifactUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

/// <summary>
/// G344: Input to the host queue-item recovery analyzer for a single
/// execution unit candidate. All fields are pure values: the command
/// layer is responsible for collecting them via filesystem reads and
/// <c>gh</c> calls before invoking the analyzer.
/// </summary>
internal sealed record HostQueueItemRecoveryCandidate
{
    public required string TargetRepo { get; init; }
    public required string ExecutionUnit { get; init; }

    public required bool PacketArtifactExists { get; init; }
    public string? PacketTargetRepo { get; init; }

    public required IssuePublishArtifact? Publish { get; init; }

    public required int PrNumber { get; init; }
    public string? PrUrl { get; init; }
    public string? PrRepo { get; init; }
    public string? PrState { get; init; }
    public bool PrIsDraft { get; init; }

    public required HostQueueItemRecoveryClosingIssue? ClosingIssue { get; init; }

    public IReadOnlyList<HostQueueItemRecoveryExistingItem>? ExistingQueueItems { get; init; }

    /// <summary>
    /// Names of other execution units whose publish.yaml also claims the
    /// same issue number (collected by the command layer). Non-empty
    /// triggers <see cref="HostQueueItemRecoveryAnalyzer.ReasonMultipleMatchingPackets"/>.
    /// </summary>
    public IReadOnlyList<string>? OtherUnitsClaimingSameIssue { get; init; }
}

/// <summary>
/// GitHub-side facts for the issue that the PR closes.
/// </summary>
internal sealed record HostQueueItemRecoveryClosingIssue
{
    public required int Number { get; init; }
    public string? Repo { get; init; }
    public required string State { get; init; }
    public required IReadOnlyList<string> Labels { get; init; }
    public string? Url { get; init; }
}

/// <summary>
/// Snapshot of an existing queue-state item used by the analyzer to
/// detect conflicts. The command layer projects the full
/// <see cref="QueueItem"/> down to just the fields the analyzer needs so
/// the analyzer can stay pure.
/// </summary>
internal sealed record HostQueueItemRecoveryExistingItem
{
    public required string ExecutionUnit { get; init; }
    public required int? LinkedPrNumber { get; init; }
    public required int? LinkedIssueNumber { get; init; }
    public string? Title { get; init; }
}

internal sealed record HostQueueItemRecoveryAnalysis
{
    [JsonPropertyName("result")]
    public required string Result { get; init; }

    [JsonPropertyName("reason_code")]
    public required string? ReasonCode { get; init; }

    [JsonPropertyName("explanation")]
    public required string Explanation { get; init; }

    [JsonPropertyName("proposed_queue_item")]
    public required HostQueueItemRecoveryProposedItem? ProposedQueueItem { get; init; }

    [JsonPropertyName("recommended_command")]
    public required string? RecommendedCommand { get; init; }

    [JsonPropertyName("evidence")]
    public required IReadOnlyList<string> Evidence { get; init; }
}

internal sealed record HostQueueItemRecoveryProposedItem
{
    [JsonPropertyName("unit_id")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("target_repo")]
    public required string TargetRepo { get; init; }

    [JsonPropertyName("linked_issue")]
    public required int LinkedIssueNumber { get; init; }

    [JsonPropertyName("linked_issue_repo")]
    public required string LinkedIssueRepo { get; init; }

    [JsonPropertyName("linked_issue_url")]
    public required string LinkedIssueUrl { get; init; }

    [JsonPropertyName("linked_pr")]
    public required int LinkedPrNumber { get; init; }

    [JsonPropertyName("linked_pr_url")]
    public required string LinkedPrUrl { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }
}
