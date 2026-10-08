using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>Plain input material for the G855 evaluator; it contains no readers or runner seams.</summary>
internal sealed record UnitStatusEvidenceSnapshot
{
    public required string ExecutionUnit { get; init; }
    public string? Domain { get; init; }
    public string? Team { get; init; }
    public string? Repo { get; init; }
    public int? Issue { get; init; }
    public int? Pr { get; init; }
    public string? HeadSha { get; init; }
    public string? TeamMode { get; init; }
    public string? ModeBasis { get; init; }
    public string? ModeEntryPath { get; init; }
    public string? ModeEntryUpdatedAt { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
    public string? LocalHeadSha { get; init; }
    public string? LocalHeadRef { get; init; }
    public string? ClaimMetadataRef { get; init; }
    public string? ClaimMetadataOid { get; init; }
    public string? ApplicabilityState { get; init; }
    public string? ApplicabilityCause { get; init; }
    public string? ApplicabilityDetail { get; init; }
    public string? UnavailableClass { get; init; }
    public IReadOnlyList<string> IdentitySources { get; init; } = [];
    public IReadOnlyList<string> LocalSources { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<UnitStatusFact> Facts { get; init; } = [];
    public IReadOnlyList<UnitStatusObservedReview> Reviews { get; init; } = [];
    public IReadOnlyList<UnitStatusObservedCheck> Checks { get; init; } = [];
    public string? GitHubSnapshotState { get; init; }
    public string? GitHubSnapshotCause { get; init; }
    public string? GitHubSnapshotDetail { get; init; }
    public string? GitHubHeadBefore { get; init; }
    public string? GitHubHeadAfter { get; init; }
    public string? MergeCommitSha { get; init; }
    public bool? PullRequestMerged { get; init; }
    public IReadOnlyList<string> IssueLabels { get; init; } = [];
    public IReadOnlyList<string> PullRequestLabels { get; init; } = [];
}

/// <summary>A classified plain-data observation consumed by the evaluator.</summary>
internal sealed record UnitStatusFact
{
    public required string Id { get; init; }
    public required string State { get; init; }
    public string? Cause { get; init; }
    public string? Detail { get; init; }
    public string? UnavailableClass { get; init; }
    public IReadOnlyList<UnitStatusEvidencePointer> Evidence { get; init; } = [];
    public IReadOnlyList<string> RepairCommands { get; init; } = [];
    public string? RepairUnavailableReason { get; init; }
}

internal sealed record UnitStatusEvidencePointer
{
    public required string Kind { get; init; }
    public string? Path { get; init; }
    public string? Url { get; init; }
    public string? RecordId { get; init; }
    public int? Line { get; init; }
    public string? Role { get; init; }
    public string? ExecutionUnit { get; init; }
    public string? Repo { get; init; }
    public int? Pr { get; init; }
    public string? HeadSha { get; init; }
    public DateTimeOffset? RecordedAt { get; init; }
    public required string Provenance { get; init; }
}

internal sealed record UnitStatusObservedReview
{
    public required string Source { get; init; }
    public required string HeadSha { get; init; }
    public required string Verdict { get; init; }
    public string? ReviewState { get; init; }
    public string? Reviewer { get; init; }
    public string? Runtime { get; init; }
    public string? Relation { get; init; }
    public DateTimeOffset? At { get; init; }
    public string? RecordId { get; init; }
    public string? CitedRecordPath { get; init; }
    public string? Url { get; init; }
    public bool Dismissed { get; init; }
}

internal sealed record UnitStatusObservedCheck
{
    public required string Source { get; init; }
    public required string Identity { get; init; }
    public string? RecordId { get; init; }
    public required string Sha { get; init; }
    public required string Status { get; init; }
    public string? Conclusion { get; init; }
    public long? RunId { get; init; }
    public int? Attempt { get; init; }
    public string? AttemptBasis { get; init; }
    public string? Url { get; init; }
}

internal sealed record UnitStatusReport
{
    [JsonPropertyName("schema_version")] public string SchemaVersion { get; init; } = "1";
    [JsonPropertyName("execution_unit")] public required string ExecutionUnit { get; init; }
    [JsonPropertyName("domain")] public string? Domain { get; init; }
    [JsonPropertyName("team")] public string? Team { get; init; }
    [JsonPropertyName("team_mode")] public string? TeamMode { get; init; }
    [JsonPropertyName("mode_basis")] public string? ModeBasis { get; init; }
    [JsonPropertyName("identity")] public required UnitStatusIdentity Identity { get; init; }
    [JsonPropertyName("applicability")] public required UnitStatusApplicability Applicability { get; init; }
    [JsonPropertyName("observation")] public required UnitStatusObservation Observation { get; init; }
    [JsonPropertyName("steps")] public required IReadOnlyList<UnitStatusStep> Steps { get; init; }
    [JsonPropertyName("summary")] public required UnitStatusSummary Summary { get; init; }
}

internal sealed record UnitStatusIdentity
{
    [JsonPropertyName("repo")] public string? Repo { get; init; }
    [JsonPropertyName("issue")] public int? Issue { get; init; }
    [JsonPropertyName("pr")] public int? Pr { get; init; }
    [JsonPropertyName("head_sha")] public string? HeadSha { get; init; }
    [JsonPropertyName("sources")] public required IReadOnlyList<string> Sources { get; init; }
}

internal sealed record UnitStatusApplicability
{
    [JsonPropertyName("state")] public required string State { get; init; }
    [JsonPropertyName("cause")] public string? Cause { get; init; }
    [JsonPropertyName("detail")] public string? Detail { get; init; }
    [JsonPropertyName("unavailable_class")] public string? UnavailableClass { get; init; }
    [JsonPropertyName("mode_source")] public string? ModeSource { get; init; }
    [JsonPropertyName("mode_entry_path")] public string? ModeEntryPath { get; init; }
    [JsonPropertyName("mode_entry_updated_at")] public string? ModeEntryUpdatedAt { get; init; }
}

internal sealed record UnitStatusObservation
{
    [JsonPropertyName("observed_at")] public required DateTimeOffset ObservedAt { get; init; }
    [JsonPropertyName("local_head_sha")] public string? LocalHeadSha { get; init; }
    [JsonPropertyName("local_head_ref")] public string? LocalHeadRef { get; init; }
    [JsonPropertyName("claim_metadata_ref")] public string? ClaimMetadataRef { get; init; }
    [JsonPropertyName("claim_metadata_oid")] public string? ClaimMetadataOid { get; init; }
    [JsonPropertyName("local_sources")] public required IReadOnlyList<string> LocalSources { get; init; }
    [JsonPropertyName("github_snapshot")] public required UnitStatusGitHubSnapshot GitHubSnapshot { get; init; }
    [JsonPropertyName("warnings")] public required IReadOnlyList<string> Warnings { get; init; }
}

internal sealed record UnitStatusGitHubSnapshot
{
    [JsonPropertyName("state")] public required string State { get; init; }
    [JsonPropertyName("cause")] public string? Cause { get; init; }
    [JsonPropertyName("detail")] public string? Detail { get; init; }
    [JsonPropertyName("head_before")] public string? HeadBefore { get; init; }
    [JsonPropertyName("head_after")] public string? HeadAfter { get; init; }
    [JsonPropertyName("inventory_scope")] public string InventoryScope { get; init; } = "observed-checks";
    [JsonPropertyName("branch_requirements_evaluated")] public bool BranchRequirementsEvaluated { get; init; }
    [JsonPropertyName("issue_labels")] public required IReadOnlyList<string> IssueLabels { get; init; }
    [JsonPropertyName("pull_request_labels")] public required IReadOnlyList<string> PullRequestLabels { get; init; }
    [JsonPropertyName("checks")] public required IReadOnlyList<UnitStatusObservedCheck> Checks { get; init; }
    [JsonPropertyName("reviews")] public required IReadOnlyList<UnitStatusObservedReview> Reviews { get; init; }
    [JsonPropertyName("merged")] public bool? Merged { get; init; }
    [JsonPropertyName("merge_commit_sha")] public string? MergeCommitSha { get; init; }
}

internal sealed record UnitStatusStep
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("state")] public required string State { get; init; }
    [JsonPropertyName("cause")] public string? Cause { get; init; }
    [JsonPropertyName("detail")] public string? Detail { get; init; }
    [JsonPropertyName("subchecks")] public required IReadOnlyList<UnitStatusFact> Subchecks { get; init; }
}

internal sealed record UnitStatusSummary
{
    [JsonPropertyName("state_counts")] public required IReadOnlyDictionary<string, int> StateCounts { get; init; }
    [JsonPropertyName("unavailable_class_counts")] public required IReadOnlyDictionary<string, int> UnavailableClassCounts { get; init; }
    [JsonPropertyName("observation_exit_code")] public required int ObservationExitCode { get; init; }
}

internal static class UnitStatusStates
{
    public const string Done = "done";
    public const string Missing = "missing";
    public const string NotApplicable = "not-applicable";
    public const string Unavailable = "unavailable";
    public const string ProvenanceLimit = "provenance-limit";
    public const string ReadFailure = "read-failure";
    public const string IdentityConflict = "identity-conflict";
    public const string ApplicabilityUnresolved = "applicability-unresolved";
    public const string InvalidRequest = "invalid-request";
}
