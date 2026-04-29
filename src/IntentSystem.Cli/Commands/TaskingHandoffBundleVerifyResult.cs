using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G196 verify result record. Snake_case JSON shape that
/// <c>intent-cli tasking handoff-bundle-verify</c> emits to STDOUT when
/// <c>--format json</c> is requested. The result is a deterministic, read-only
/// preflight projection of a G194 <see cref="TaskingHandoffBundleArtifact"/>:
/// the verify command does NOT write any artifact file.
///
/// <para>
/// The <c>valid</c> flag is true iff every <see cref="VerifyCheck"/> in
/// <see cref="Checks"/> has <c>passed == true</c>. <see cref="Errors"/> is a
/// human-readable mirror of failed-check details for automation logs.
/// </para>
/// </summary>
internal sealed record TaskingHandoffBundleVerifyResult
{
    [JsonPropertyName("bundle_path")]
    public required string BundlePath { get; init; }

    [JsonPropertyName("bundle_sha256")]
    public required string? BundleSha256 { get; init; }

    [JsonPropertyName("domain")]
    public required string? Domain { get; init; }

    [JsonPropertyName("valid")]
    public required bool Valid { get; init; }

    [JsonPropertyName("errors")]
    public required IReadOnlyList<string> Errors { get; init; }

    [JsonPropertyName("checks")]
    public required IReadOnlyList<VerifyCheck> Checks { get; init; }

    [JsonPropertyName("summary_line")]
    public required string SummaryLine { get; init; }
}

/// <summary>
/// G196 single-check record. Each <see cref="Id"/> matches a stable check id
/// constant in <see cref="TaskingHandoffBundleVerifyConstants.CheckIds"/>.
/// </summary>
internal sealed record VerifyCheck
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("passed")]
    public required bool Passed { get; init; }

    [JsonPropertyName("detail")]
    public required string Detail { get; init; }
}

/// <summary>
/// G196 stable check id constants. The verify analyzer emits checks in the
/// order declared in <see cref="CheckIds.All"/>; tests assert that every id
/// appears exactly once in the output regardless of pass/fail status.
/// </summary>
internal static class TaskingHandoffBundleVerifyConstants
{
    /// <summary>
    /// Stable status phrase appearing in both the text summary line and the
    /// JSON <c>summary_line</c> field. Tests assert the literal substring.
    /// </summary>
    public const string UnpublishedStatusPhrase =
        "UNPUBLISHED, local-only, not automation-visible";

    /// <summary>
    /// Stable summary line emitted at the top of both text and JSON outputs.
    /// </summary>
    public const string SummaryLineText =
        "Tasking handoff bundle verify — UNPUBLISHED, local-only, not automation-visible.";

    public static class CheckIds
    {
        public const string BundlePathPresentAndReadable = "bundle_path_present_and_readable";
        public const string BundleJsonParses = "bundle_json_parses";
        public const string BundleStatusLocalOnly = "bundle_status_local_only";
        public const string BundleIsPublishedFalse = "bundle_is_published_false";
        public const string BundleIsAutomationVisibleFalse = "bundle_is_automation_visible_false";
        public const string TaskPacketPathPresent = "task_packet_path_present";
        public const string TaskPacketSha256Present = "task_packet_sha256_present";
        public const string PreviewPathPresent = "preview_path_present";
        public const string PreviewSha256Present = "preview_sha256_present";
        public const string HandoffPathPresent = "handoff_path_present";
        public const string HandoffSha256Present = "handoff_sha256_present";
        public const string ChecklistPathPresent = "checklist_path_present";
        public const string ChecklistSha256Present = "checklist_sha256_present";
        public const string ChecklistPassedOrFailedCheckIdsPresent =
            "checklist_passed_check_ids_present_or_failed_check_ids_present";
        public const string RecommendedWorkerActionNonEmpty = "recommended_worker_action_non_empty";
        public const string BundleArtifactOnlyNoProviderLaunchDirective =
            "bundle_artifact_only_no_provider_launch_directive";

        public static readonly IReadOnlyList<string> All = new[]
        {
            BundlePathPresentAndReadable,
            BundleJsonParses,
            BundleStatusLocalOnly,
            BundleIsPublishedFalse,
            BundleIsAutomationVisibleFalse,
            TaskPacketPathPresent,
            TaskPacketSha256Present,
            PreviewPathPresent,
            PreviewSha256Present,
            HandoffPathPresent,
            HandoffSha256Present,
            ChecklistPathPresent,
            ChecklistSha256Present,
            ChecklistPassedOrFailedCheckIdsPresent,
            RecommendedWorkerActionNonEmpty,
            BundleArtifactOnlyNoProviderLaunchDirective
        };
    }
}
