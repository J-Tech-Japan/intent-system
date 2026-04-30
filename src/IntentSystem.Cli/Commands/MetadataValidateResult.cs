using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G207: Read-only result emitted by <c>intent-cli metadata validate</c>.
/// Reports whether the packet/queue/runs metadata graph for one execution
/// unit is internally consistent. Field names mirror the issue contract
/// example (camelCase for <c>executionUnit</c> / <c>checkedFiles</c>) with
/// snake_case aliases for local style consistency, mirroring the
/// G202–G206 alias pattern.
///
/// Producing this record never mutates GitHub, never edits files, never
/// touches the queue/runs logs, and never launches any AI provider.
/// </summary>
internal sealed record MetadataValidateResult
{
    [JsonPropertyName("valid")]
    public required bool Valid { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    /// <summary>G207 issue contract alias — camelCase per #519 example.</summary>
    [JsonPropertyName("executionUnit")]
    public string ExecutionUnitCamelCase => ExecutionUnit;

    [JsonPropertyName("errors")]
    public required IReadOnlyList<MetadataValidateFinding> Errors { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<MetadataValidateFinding> Warnings { get; init; }

    [JsonPropertyName("checked_files")]
    public required IReadOnlyList<string> CheckedFiles { get; init; }

    /// <summary>G207 issue contract alias — camelCase per #519 example.</summary>
    [JsonPropertyName("checkedFiles")]
    public IReadOnlyList<string> CheckedFilesCamelCase => CheckedFiles;
}

/// <summary>
/// G207: Single error / warning entry. <see cref="Code"/> is a stable
/// machine-readable identifier; <see cref="Message"/> is the human form.
/// <see cref="Path"/> names the relative file path the finding came from
/// (or is empty when the finding spans multiple files).
/// </summary>
internal sealed record MetadataValidateFinding
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;
}

/// <summary>
/// G207: Stable string constants for the validation finding codes.
/// </summary>
internal static class MetadataValidateConstants
{
    public static class Codes
    {
        // Existence / parse errors.
        public const string PacketFileMissing = "packet.file.missing";
        public const string PacketYamlUnparseable = "packet.yaml.unparseable";
        public const string GithubBodyMissing = "github-body.missing";
        public const string ReviewContextMissing = "review-context.missing";
        public const string PublishYamlUnparseable = "publish.yaml.unparseable";
        public const string QueueStateMissing = "queue-state.missing";
        public const string QueueStateUnparseable = "queue-state.unparseable";
        public const string ImplementationFileMissing = "implementation.file.missing";

        // Required content.
        public const string PacketMissingExecutionUnit = "packet.missing.execution_unit";
        public const string PacketMissingTitle = "packet.missing.title";
        public const string PublishMissingIssueNumber = "publish.missing.issue_number";
        public const string PublishMissingIssueUrl = "publish.missing.issue_url";
        public const string GithubBodyMissingSection = "github-body.missing.section";
        public const string ReviewContextMissingSection = "review-context.missing.section";

        // Cross-file consistency.
        public const string PublishQueueIssueMismatch = "consistency.publish-queue.issue.mismatch";
        public const string CompletedMissingClosure = "consistency.queue.completed.missing_closure";
        public const string PacketQueueDependencyMismatch = "consistency.packet-queue.dependency.mismatch";
        public const string QueueEntryMissing = "consistency.queue.entry.missing";

        // Label-policy warnings.
        public const string LabelPolicyMisplacedPrCreated = "label-policy.misplaced.intent-pr-created";
    }

    public static IReadOnlyList<string> RequiredGithubBodySections { get; } = new[]
    {
        "Goal",
        "Why This Slice Exists Now",
        "Current Observed State",
        "Accepted Baseline You May Assume",
        "Target Repo",
        "In Scope",
        "Out Of Scope",
        "Acceptance Criteria",
        "Verification",
        "Related Links",
    };

    public static IReadOnlyList<string> RequiredReviewContextSections { get; } = new[]
    {
        // Per #519: "execution unit, child repo, linked issue, linked PR,
        // accepted baseline, deterministic review checks, and closeout
        // lookahead sections or equivalent fields".
        "Execution Unit",
        "Child Repo",
        "Linked Issue",
        "Linked PR",
        "Accepted Baseline",
        "Deterministic Review Checks",
        "Closeout Lookahead",
    };
}
