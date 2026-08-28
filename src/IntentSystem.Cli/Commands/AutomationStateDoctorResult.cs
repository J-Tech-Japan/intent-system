using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G448: Result of <c>intent-cli automation state-doctor</c> — a unified,
/// OSS-safe diagnostic over host metadata drift among queue-state, publish
/// artifacts, GitHub issues, and GitHub PRs. Read-only by default
/// (<c>mode = read-only</c>); <c>--write</c> applies ONLY high-confidence,
/// forward-only queue-state repairs and appends an append-only
/// <c>runs.jsonl</c> event per applied repair. Ambiguous drift is reported as
/// an <see cref="UnsafeFindings"/> entry and never mutated (fail-closed).
/// Host-only by policy — never produced from a child implementation loop.
/// </summary>
internal sealed record AutomationStateDoctorResult
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("capability_matrix")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TeamModeCapabilityMatrix? CapabilityMatrix { get; init; }

    [JsonPropertyName("host_only")]
    public required bool HostOnly { get; init; }

    [JsonPropertyName("hostOnly")]
    public bool HostOnlyCamel => HostOnly;

    /// <summary>
    /// Deterministic drift findings. <see cref="AutomationStateDoctorFinding.Confidence"/>
    /// <c>= high</c> entries are the only ones applied in <c>--write</c> mode;
    /// <c>advisory</c> entries are reported with evidence but never auto-mutated.
    /// </summary>
    [JsonPropertyName("findings")]
    public required IReadOnlyList<AutomationStateDoctorFinding> Findings { get; init; }

    /// <summary>
    /// Drift the doctor refuses to repair because the evidence is ambiguous
    /// (e.g. duplicate issue evidence, more than one PR closing the same
    /// issue). These require operator clarification and produce NO writes.
    /// </summary>
    [JsonPropertyName("unsafe_findings")]
    public required IReadOnlyList<AutomationStateDoctorUnsafe> UnsafeFindings { get; init; }

    [JsonPropertyName("unsafeFindings")]
    public IReadOnlyList<AutomationStateDoctorUnsafe> UnsafeFindingsCamel => UnsafeFindings;

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}

/// <summary>
/// G448: one deterministic drift finding. Forward-only by construction — the
/// repair only ever FILLS a missing metadata field or advances a lifecycle
/// state; it never clears, rewrites, or downgrades existing host data.
/// </summary>
internal sealed record AutomationStateDoctorFinding
{
    [JsonPropertyName("category")]
    public required string Category { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("executionUnit")]
    public string ExecutionUnitCamel => ExecutionUnit;

    [JsonPropertyName("repair_kind")]
    public required string RepairKind { get; init; }

    [JsonPropertyName("repairKind")]
    public string RepairKindCamel => RepairKind;

    [JsonPropertyName("issue_number")]
    public int? IssueNumber { get; init; }

    [JsonPropertyName("issueNumber")]
    public int? IssueNumberCamel => IssueNumber;

    [JsonPropertyName("issue_url")]
    public string? IssueUrl { get; init; }

    [JsonPropertyName("issueUrl")]
    public string? IssueUrlCamel => IssueUrl;

    [JsonPropertyName("issue_repo")]
    public string? IssueRepo { get; init; }

    [JsonPropertyName("issueRepo")]
    public string? IssueRepoCamel => IssueRepo;

    [JsonPropertyName("pr_number")]
    public int? PrNumber { get; init; }

    [JsonPropertyName("prNumber")]
    public int? PrNumberCamel => PrNumber;

    [JsonPropertyName("pr_url")]
    public string? PrUrl { get; init; }

    [JsonPropertyName("prUrl")]
    public string? PrUrlCamel => PrUrl;

    [JsonPropertyName("queue_item_index")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? QueueItemIndex { get; init; }

    [JsonPropertyName("remove_queue_item_indices")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? RemoveQueueItemIndices { get; init; }

    [JsonPropertyName("confidence")]
    public required string Confidence { get; init; }

    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }

    [JsonPropertyName("evidence")]
    public required IReadOnlyList<string> Evidence { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}

/// <summary>G448: ambiguous drift the doctor refuses to repair (fail-closed).</summary>
internal sealed record AutomationStateDoctorUnsafe
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("execution_unit")]
    public string? ExecutionUnit { get; init; }

    [JsonPropertyName("executionUnit")]
    public string? ExecutionUnitCamel => ExecutionUnit;

    [JsonPropertyName("issue_number")]
    public int? IssueNumber { get; init; }

    [JsonPropertyName("issueNumber")]
    public int? IssueNumberCamel => IssueNumber;

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("missing_evidence")]
    public required IReadOnlyList<string> MissingEvidence { get; init; }

    [JsonPropertyName("missingEvidence")]
    public IReadOnlyList<string> MissingEvidenceCamel => MissingEvidence;

    [JsonPropertyName("competing_entries")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? CompetingEntries { get; init; }
}

internal static class AutomationStateDoctorModes
{
    public const string ReadOnly = "read-only";
    public const string Write = "write";
}

internal static class AutomationStateDoctorConfidence
{
    public const string High = "high";
    public const string Advisory = "advisory";
}

internal static class AutomationStateDoctorCategories
{
    public const string MissingLinkedPr = "missing-linked-pr";
    public const string MissingLinkedIssue = "missing-linked-issue";
    public const string MergedPrNotCompleted = "merged-pr-not-completed";

    /// <summary>
    /// G481: a single execution unit resolves to MORE THAN ONE GitHub issue,
    /// but a unique canonical issue is determined from durable evidence and the
    /// non-canonical duplicate(s) carry no active PR. Reported as an advisory
    /// finding (safe repair offered: close the non-canonical issue) — never
    /// auto-applied, because closing a GitHub issue is outside the doctor's
    /// forward-only queue-state write contract.
    /// </summary>
    public const string DuplicateExecutionUnitIssue = "duplicate-execution-unit-issue-detected";

    public const string DuplicateQueueItem = "duplicate-queue-item";
}

internal static class AutomationStateDoctorRepairKinds
{
    /// <summary>No deterministic write; advisory only.</summary>
    public const string None = "none";
    public const string SetQueueLinkedPr = "queue-state-linked-pr";
    public const string SetQueueLinkedIssue = "queue-state-linked-issue";
    public const string MarkQueueCompleted = "queue-state-completed";
    public const string DeduplicateQueueItem = "queue-state-deduplicate";
}

internal static class AutomationStateDoctorUnsafeKinds
{
    public const string ChildLoopProhibited = "child-loop-prohibited";
    public const string StaleHostCli = "stale-host-cli";
    public const string DuplicateIssueEvidence = "duplicate-issue-evidence";
    public const string AmbiguousPrLinkage = "ambiguous-pr-linkage";
    public const string AmbiguousPublishEvidence = "ambiguous-publish-evidence";
    public const string DuplicateQueueItem = "duplicate-queue-item";

    /// <summary>
    /// G481: a single execution unit resolves to more than one GitHub issue and
    /// no durable source uniquely anchors the canonical issue (e.g. two publish
    /// artifacts for the same unit record different created issues). Concurrent
    /// or duplicate host publish — fail-closed; never pick a winner by recency.
    /// </summary>
    public const string ConcurrentHostPublishDetected = "concurrent-host-publish-detected";

    /// <summary>
    /// G481: durable sources disagree on the canonical issue for an execution
    /// unit (e.g. queue-state <c>linked_issue</c> = #A but the packet
    /// <c>publish.yaml</c> records created issue #B). Fail-closed: never
    /// overwrite one durable record with another.
    /// </summary>
    public const string CanonicalIssueMismatch = "canonical-issue-mismatch";

    /// <summary>
    /// G481: an implementation PR closes a NON-canonical duplicate issue while a
    /// different issue is canonical from durable evidence. Classified
    /// separately from ordinary missing-<c>linked_pr</c> recovery and from
    /// ambiguous-PR-linkage; fail-closed (do not auto-edit the PR body or
    /// reopen/close issues during a race).
    /// </summary>
    public const string PrClosesNoncanonicalIssue = "pr-closes-noncanonical-issue";
}

/// <summary>
/// G448: minimal queue-state projection the analyzer reads. Avoids tying the
/// pure analyzer to the full QueueState model so test fakes supply only the
/// fields the drift checks consume.
/// </summary>
internal sealed record StateDoctorQueueItem
{
    public required string ExecutionUnit { get; init; }
    public string? LinkedIssueRepo { get; init; }
    public int? LinkedIssueNumber { get; init; }
    public string? LinkedIssueUrl { get; init; }
    public string? LinkedPrUrl { get; init; }
    public required bool Completed { get; init; }
    public int SourceIndex { get; init; } = -1;
    public string? State { get; init; }
    public string? FullEntryJson { get; init; }
    public IReadOnlyDictionary<string, string?>? ComparableFields { get; init; }
}

/// <summary>
/// G448: publish-artifact evidence projection (one created GitHub issue per
/// execution unit, as recorded in <c>.intent-cli/issues/&lt;unit&gt;/publish.yaml</c>).
/// </summary>
internal sealed record StateDoctorPublishEvidence
{
    public required string ExecutionUnit { get; init; }
    public required string IssueRepo { get; init; }
    public required int IssueNumber { get; init; }
    public string? IssueUrl { get; init; }
}

/// <summary>G448: PR projection (open or merged) with its closing-issue refs.</summary>
internal sealed record StateDoctorPr
{
    public required int Number { get; init; }
    public required string Url { get; init; }
    public required bool Merged { get; init; }
    public required IReadOnlyList<int> ClosingIssueNumbers { get; init; }
}

internal sealed record AutomationStateDoctorAnalysis(
    IReadOnlyList<AutomationStateDoctorFinding> Findings,
    IReadOnlyList<AutomationStateDoctorUnsafe> UnsafeFindings);
