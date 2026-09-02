using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G211: Read-only result emitted by <c>intent-cli worker complete</c>.
/// Mirrors the shape of <see cref="WorkerClaimResult"/>: kind, number,
/// mode, proceed/applied flags, add/remove transition, current labels,
/// errors, warnings, summary. The complete-side adds the originating
/// outcome (an issue-to-pr / pr-comment-fix outcome from G205) so
/// downstream summaries can tie the claim/complete pair together.
///
/// In dry-run mode (the default), the record describes the transition
/// without applying it. The command never launches an AI provider,
/// never creates branches, PRs, issues, or comments, and never touches
/// the queue or runs logs.
/// </summary>
internal sealed record WorkerCompleteResult
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("number")]
    public required int Number { get; init; }

    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("proceed")]
    public required bool Proceed { get; init; }

    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }

    [JsonPropertyName("add_labels")]
    public required IReadOnlyList<string> AddLabels { get; init; }

    /// <summary>G211 contract alias — camelCase for AI-thread JSON ingestion.</summary>
    [JsonPropertyName("addLabels")]
    public IReadOnlyList<string> AddLabelsCamelCase => AddLabels;

    [JsonPropertyName("remove_labels")]
    public required IReadOnlyList<string> RemoveLabels { get; init; }

    /// <summary>G211 contract alias — camelCase for AI-thread JSON ingestion.</summary>
    [JsonPropertyName("removeLabels")]
    public IReadOnlyList<string> RemoveLabelsCamelCase => RemoveLabels;

    [JsonPropertyName("current_labels")]
    public required IReadOnlyList<string> CurrentLabels { get; init; }

    /// <summary>G211 contract alias — camelCase for AI-thread JSON ingestion.</summary>
    [JsonPropertyName("currentLabels")]
    public IReadOnlyList<string> CurrentLabelsCamelCase => CurrentLabels;

    [JsonPropertyName("errors")]
    public required IReadOnlyList<string> Errors { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    /// <summary>G283: PR number paired with this issue completion when outcome is pr-created.</summary>
    [JsonPropertyName("pr_number")]
    public int? PrNumber { get; init; }

    [JsonPropertyName("prNumber")]
    public int? PrNumberCamel => PrNumber;

    /// <summary>G283: whether PR-side intent-target was applied as part of issue-to-pr completion.</summary>
    [JsonPropertyName("pr_target_applied")]
    public bool? PrTargetApplied { get; init; }

    [JsonPropertyName("prTargetApplied")]
    public bool? PrTargetAppliedCamel => PrTargetApplied;

    /// <summary>G283: whether queue-state's linked_pr was synced for the matching execution unit.</summary>
    [JsonPropertyName("linked_pr_synced")]
    public bool? LinkedPrSynced { get; init; }

    [JsonPropertyName("linkedPrSynced")]
    public bool? LinkedPrSyncedCamel => LinkedPrSynced;

    /// <summary>
    /// G724: durable execution-unit domain resolved for host-side issue-to-PR
    /// completion. This is deliberately separate from the startup marker,
    /// which is display-only.
    /// </summary>
    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("domain_source")]
    public string? DomainSource { get; init; }

    [JsonPropertyName("execution_unit")]
    public string? ExecutionUnit { get; init; }

    /// <summary>
    /// G330: true when the command ran in child-cwd mode — either the
    /// operator passed <c>--child-cwd</c> explicitly OR the
    /// environment is implicitly a child cwd (no parent intent repo
    /// root configured AND no local <c>.intent-cli/queue-state.json</c>
    /// on disk). In child-cwd mode the command performs GitHub label
    /// transitions but never touches parent durable state; linked_pr
    /// sync is host-owned and runs via G329
    /// <c>review closeout-plan --write-recovered-linkage</c> on the
    /// review-runtime workspace. False on host/review-runtime hosts.
    /// </summary>
    [JsonPropertyName("child_cwd")]
    public bool? ChildCwd { get; init; }

    [JsonPropertyName("childCwd")]
    public bool? ChildCwdCamel => ChildCwd;

    /// <summary>
    /// G333: true when the command ran in strict child-loop GitHub-only
    /// mode (operator passed <c>--github-only</c>). Implies
    /// <see cref="ChildCwd"/>: never touches parent durable state and
    /// never reads queue-state regardless of disk presence. Designed
    /// for cloud Claude / Codex child implementation loops invoked
    /// from a standalone child repo cwd that has no
    /// <c>.intent-cli/</c> directory and no configured parent host
    /// root. Default false on host / review-runtime invocations.
    /// </summary>
    [JsonPropertyName("github_only")]
    public bool? GithubOnly { get; init; }

    [JsonPropertyName("githubOnly")]
    public bool? GithubOnlyCamel => GithubOnly;

    /// <summary>G785: criteria that required a named fenced evidence block.</summary>
    [JsonPropertyName("evidence_required")]
    public required IReadOnlyList<WorkerEvidenceCriterion> EvidenceRequired { get; init; }

    /// <summary>G785: required criteria with a named collected-output fence.</summary>
    [JsonPropertyName("evidence_blocks_present")]
    public required IReadOnlyList<WorkerEvidenceCriterion> EvidenceBlocksPresent { get; init; }

    /// <summary>G785: required criteria that remain unsatisfied by the PR body.</summary>
    [JsonPropertyName("evidence_gap")]
    public required IReadOnlyList<WorkerEvidenceCriterion> EvidenceGap { get; init; }

    /// <summary>
    /// G785: nonempty recorded reason supplied through
    /// <c>--accept-evidence-gap</c> when a pr-created completion intentionally
    /// proceeds despite <see cref="EvidenceGap"/>.
    /// </summary>
    [JsonPropertyName("evidence_gap_accepted")]
    public string? EvidenceGapAccepted { get; init; }
}
