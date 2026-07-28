using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G208: Read-only result emitted by <c>intent-cli metadata update</c>.
/// Reports which files this run modified and whether the transition was
/// applied. Field names mirror G207 contract style: snake_case primary
/// + camelCase aliases per the issue contract.
///
/// Producing this record never mutates GitHub state, never creates
/// branches/worktrees, never launches any AI provider. The command itself
/// modifies a strictly bounded set of metadata files
/// (<c>.intent-cli/issues/&lt;execution-unit&gt;/publish.yaml</c>,
/// <c>.intent-cli/queue-state.json</c>, <c>.intent-cli/runs.jsonl</c>);
/// nothing else under <c>--root</c>.
/// </summary>
internal sealed record MetadataUpdateResult
{
    [JsonPropertyName("valid")]
    public required bool Valid { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    /// <summary>G208 issue contract alias — camelCase.</summary>
    [JsonPropertyName("executionUnit")]
    public string ExecutionUnitCamelCase => ExecutionUnit;

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("updated_files")]
    public required IReadOnlyList<string> UpdatedFiles { get; init; }

    /// <summary>G208 issue contract alias — camelCase.</summary>
    [JsonPropertyName("updatedFiles")]
    public IReadOnlyList<string> UpdatedFilesCamelCase => UpdatedFiles;

    [JsonPropertyName("event_appended")]
    public string? EventAppended { get; init; }

    /// <summary>G208 issue contract alias — camelCase.</summary>
    [JsonPropertyName("eventAppended")]
    public string? EventAppendedCamelCase => EventAppended;

    /// <summary>
    /// G548: true when <c>queue-state.json</c> had changed since this command
    /// read it (a concurrent canonical write from another loop or domain) and
    /// this command's own linkage/closeout change was re-applied onto that
    /// fresher state rather than overwriting it. This is the 2026-07-23
    /// <c>2ab082cf</c> shape — a bounded linkage writer starting from a stale
    /// base — so it is reported, never silent: an operator reading this output
    /// must be able to see that contention occurred and on which units.
    /// </summary>
    [JsonPropertyName("queue_state_reapplied")]
    public bool QueueStateReapplied { get; init; }

    /// <summary>G208 contract alias — camelCase.</summary>
    [JsonPropertyName("queueStateReapplied")]
    public bool QueueStateReappliedCamelCase => QueueStateReapplied;

    /// <summary>G548: the execution units the re-applied mutation covered.</summary>
    [JsonPropertyName("queue_state_reapplied_execution_units")]
    public IReadOnlyList<string> QueueStateReappliedExecutionUnits { get; init; } = Array.Empty<string>();

    /// <summary>G208 contract alias — camelCase.</summary>
    [JsonPropertyName("queueStateReappliedExecutionUnits")]
    public IReadOnlyList<string> QueueStateReappliedExecutionUnitsCamelCase => QueueStateReappliedExecutionUnits;

    [JsonPropertyName("errors")]
    public required IReadOnlyList<MetadataValidateFinding> Errors { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<MetadataValidateFinding> Warnings { get; init; }
}

/// <summary>
/// G208: Stable string constants for <c>metadata update</c> modes and
/// finding codes that are specific to the writer (not the validator).
/// </summary>
internal static class MetadataUpdateConstants
{
    public static class Modes
    {
        /// <summary>
        /// First bounded transition: mark the selected queue item as
        /// <c>completed</c> with a <c>linked_pr</c> reference, append a
        /// <c>pr:</c> block to <c>publish.yaml</c>, and append a single
        /// <c>metadata-update.completed-closeout</c> event to
        /// <c>runs.jsonl</c>.
        /// </summary>
        public const string CompletedCloseout = "completed-closeout";
    }

    public static class Codes
    {
        public const string MissingMode = "update.missing.mode";
        public const string UnsupportedMode = "update.unsupported.mode";
        public const string MissingLinkedPr = "update.missing.linked_pr";
        public const string ValidationRejected = "update.validation.rejected";
        public const string AlreadyCompleted = "update.already.completed";
        public const string PublishAlreadyHasPr = "update.publish.pr_already_present";
    }

    public static class EventNames
    {
        public const string CompletedCloseout = "metadata-update.completed-closeout";
    }
}
