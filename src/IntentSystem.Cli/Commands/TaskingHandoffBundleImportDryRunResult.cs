using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G198 dry-run import/restore result. Snake_case JSON shape that
/// <c>intent-cli tasking handoff-bundle-import-dry-run</c> emits to STDOUT when
/// <c>--format json</c> is requested. Round-trippable through
/// <see cref="System.Text.Json.JsonSerializer"/>.
///
/// <para>
/// This command is a DRY-RUN only — the result describes what a future real
/// restore would do, but the command itself never overwrites any file, never
/// publishes any GitHub issue, never mutates queue/runs state, never launches
/// providers, and never creates branches/worktrees. See
/// <see cref="TaskingHandoffBundleImportDryRunConstants.RestorePlanDisclaimer"/>.
/// </para>
/// </summary>
internal sealed record TaskingHandoffBundleImportDryRunResult
{
    [JsonPropertyName("bundle_path")]
    public required string BundlePath { get; init; }

    [JsonPropertyName("bundle_sha256")]
    public required string? BundleSha256 { get; init; }

    [JsonPropertyName("domain")]
    public required string? Domain { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("errors")]
    public required IReadOnlyList<string> Errors { get; init; }

    [JsonPropertyName("verify_summary")]
    public required TaskingHandoffBundleImportDryRunVerifySummary VerifySummary { get; init; }

    [JsonPropertyName("restore_plan")]
    public required IReadOnlyList<RestorePlanEntry> RestorePlan { get; init; }

    [JsonPropertyName("restore_plan_disclaimer")]
    public required string RestorePlanDisclaimer { get; init; }

    [JsonPropertyName("summary_line")]
    public required string SummaryLine { get; init; }
}

/// <summary>
/// G198 — derived projection of the G196/G197 verify result. Mirrors the
/// minimum fields automation logs need without re-emitting the full verify
/// check list.
/// </summary>
internal sealed record TaskingHandoffBundleImportDryRunVerifySummary
{
    [JsonPropertyName("valid")]
    public required bool Valid { get; init; }

    [JsonPropertyName("failed_check_ids")]
    public required IReadOnlyList<string> FailedCheckIds { get; init; }
}

/// <summary>
/// G198 single restore-plan entry. One per referenced source artifact in the
/// bundle (task_packet, preview, checklist, handoff). All three string fields
/// are drawn from <see cref="TaskingHandoffBundleImportDryRunConstants"/> to
/// lock the contract against drift.
/// </summary>
internal sealed record RestorePlanEntry
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("recorded_path")]
    public required string RecordedPath { get; init; }

    [JsonPropertyName("recorded_sha256")]
    public required string RecordedSha256 { get; init; }

    [JsonPropertyName("current_status")]
    public required string CurrentStatus { get; init; }

    [JsonPropertyName("dry_run_action")]
    public required string DryRunAction { get; init; }
}

/// <summary>
/// G198 stable constants for the dry-run command's output contract. Status
/// values, role names, current-status enums, dry-run-action enums, and the
/// disclaimer text are all centralized here so future drift breaks the
/// regression test deterministically.
/// </summary>
internal static class TaskingHandoffBundleImportDryRunConstants
{
    /// <summary>
    /// Stable disclaimer string emitted both in the text output and in the
    /// JSON <c>restore_plan_disclaimer</c> field. Tests assert the literal
    /// "DRY-RUN ONLY: this is a restore plan preview" substring.
    /// </summary>
    public const string RestorePlanDisclaimer =
        "DRY-RUN ONLY: this is a restore plan preview. The handoff bundle records artifact paths and sha256 values, not artifact bytes; a future real restore would need access to the original referenced files (or a separate content store) to perform the listed actions. This command performs none of them.";

    /// <summary>
    /// Stable summary line — same structural shape as the G196 verify
    /// summary. The <c>{0}</c> placeholder is the bundle's domain (or
    /// <c>(unavailable)</c> when not parseable) and <c>{1}</c> is the status
    /// enum value. Tests assert the literal phrase
    /// "UNPUBLISHED, local-only, not automation-visible".
    /// </summary>
    public const string SummaryLineFormat =
        "Tasking handoff bundle import dry-run for {0} — UNPUBLISHED, local-only, not automation-visible. status={1}.";

    public const string UnpublishedStatusPhrase =
        "UNPUBLISHED, local-only, not automation-visible";

    public static class Statuses
    {
        public const string Ok = "ok";
        public const string VerifyFailed = "verify_failed";
        public const string MissingBundle = "missing_bundle";
        public const string MalformedBundle = "malformed_bundle";
    }

    public static class Roles
    {
        public const string TaskPacket = "task_packet";
        public const string Preview = "preview";
        public const string Checklist = "checklist";
        public const string Handoff = "handoff";

        public static readonly IReadOnlyList<string> InOrder = new[]
        {
            TaskPacket,
            Preview,
            Checklist,
            Handoff
        };
    }

    public static class CurrentStatuses
    {
        public const string AlreadyPresentAndMatching = "already_present_and_matching";
        public const string PresentButHashDiffers = "present_but_hash_differs";
        public const string Missing = "missing";
    }

    public static class Actions
    {
        public const string NoOpAlreadyMatches = "no_op_already_matches";
        public const string WouldOverwriteWithRecordedContent = "would_overwrite_with_recorded_content";
        public const string WouldRecreateFromRecordedContent = "would_recreate_from_recorded_content";
    }
}
