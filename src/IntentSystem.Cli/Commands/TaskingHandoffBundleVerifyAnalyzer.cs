namespace IntentSystem.Cli.Commands;

using CheckIds = TaskingHandoffBundleVerifyConstants.CheckIds;

/// <summary>
/// G196 — pure logic that takes a deserialized G194
/// <see cref="TaskingHandoffBundleArtifact"/> bundle (or null/parse-fail
/// signal) and runs the fixed verification checklist. No file I/O, no network,
/// and no process launches happen here — those concerns are owned by
/// <see cref="TaskingHandoffBundleVerifyCommand"/>.
///
/// The analyzer is contract-locked: <c>is_published</c>, <c>is_automation_visible</c>,
/// and <c>bundle_status</c> are read directly from the source bundle without
/// inversion or rewriting. A G194 bundle whose contract fields drifted will be
/// reported here as a failed check, never silently passed.
/// </summary>
internal static class TaskingHandoffBundleVerifyAnalyzer
{
    /// <summary>
    /// Build the <see cref="VerifyCheck"/> list for a successfully parsed G194
    /// bundle. Checks appear in the stable order declared by
    /// <see cref="TaskingHandoffBundleVerifyConstants.CheckIds.All"/>.
    /// </summary>
    public static IReadOnlyList<VerifyCheck> BuildChecks(TaskingHandoffBundleArtifact bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        return new[]
        {
            Pass(CheckIds.BundlePathPresentAndReadable, "Bundle path exists and was read."),
            Pass(CheckIds.BundleJsonParses, "Bundle JSON parsed as TaskingHandoffBundleArtifact."),

            CheckBundleStatusLocalOnly(bundle),
            CheckIsPublishedFalse(bundle),
            CheckIsAutomationVisibleFalse(bundle),

            CheckNonEmpty(
                CheckIds.TaskPacketPathPresent,
                bundle.SourceTaskPacketPath,
                "source_task_packet_path"),
            CheckNonEmpty(
                CheckIds.TaskPacketSha256Present,
                bundle.SourceTaskPacketSha256,
                "source_task_packet_sha256"),

            CheckNonEmpty(
                CheckIds.PreviewPathPresent,
                bundle.SourcePreviewPath,
                "source_preview_path"),
            CheckNonEmpty(
                CheckIds.PreviewSha256Present,
                bundle.SourcePreviewSha256,
                "source_preview_sha256"),

            CheckNonEmpty(
                CheckIds.HandoffPathPresent,
                bundle.SourceHandoffPath,
                "source_handoff_path"),
            CheckNonEmpty(
                CheckIds.HandoffSha256Present,
                bundle.SourceHandoffSha256,
                "source_handoff_sha256"),

            CheckNonEmpty(
                CheckIds.ChecklistPathPresent,
                bundle.SourceChecklistPath,
                "source_checklist_path"),
            CheckNonEmpty(
                CheckIds.ChecklistSha256Present,
                bundle.SourceChecklistSha256,
                "source_checklist_sha256"),

            CheckChecklistNotEmpty(bundle),

            CheckNonEmpty(
                CheckIds.RecommendedWorkerActionNonEmpty,
                bundle.RecommendedWorkerAction,
                "recommended_worker_action"),

            // Artifact-only invariant: the G194 bundle schema does NOT carry a
            // provider-launch directive field. Confirming the parse-success
            // path keeps the bundle artifact-only is a structural pass: the
            // schema cannot represent a launch directive in any field.
            Pass(
                CheckIds.BundleArtifactOnlyNoProviderLaunchDirective,
                "Bundle schema carries no provider-launch directive (G194 contract).")
        };
    }

    /// <summary>
    /// Build the (single-check) result for the case where <c>--from-bundle</c>
    /// points at a non-existent path. Later checks are skipped — the path
    /// pre-condition wasn't satisfied.
    /// </summary>
    public static IReadOnlyList<VerifyCheck> BuildPathMissingChecks(string pathDetail)
    {
        return new[]
        {
            Fail(
                CheckIds.BundlePathPresentAndReadable,
                $"Bundle path does not exist: {pathDetail}")
        };
    }

    /// <summary>
    /// Build the result for the case where the file exists but JSON parse
    /// failed. Path-readable passes, JSON-parses fails, later checks skipped.
    /// </summary>
    public static IReadOnlyList<VerifyCheck> BuildJsonParseFailureChecks(string parseErrorDetail)
    {
        return new[]
        {
            Pass(CheckIds.BundlePathPresentAndReadable, "Bundle path exists and was read."),
            Fail(
                CheckIds.BundleJsonParses,
                $"Failed to parse bundle JSON as TaskingHandoffBundleArtifact: {parseErrorDetail}")
        };
    }

    private static VerifyCheck CheckBundleStatusLocalOnly(TaskingHandoffBundleArtifact bundle)
    {
        var actual = bundle.BundleStatus ?? string.Empty;
        var passed = string.Equals(
            actual,
            TaskingHandoffBundleConstants.LocalOnlyStatus,
            StringComparison.Ordinal);
        return new VerifyCheck
        {
            Id = CheckIds.BundleStatusLocalOnly,
            Passed = passed,
            Detail = passed
                ? $"bundle_status == '{TaskingHandoffBundleConstants.LocalOnlyStatus}'."
                : $"bundle_status must be '{TaskingHandoffBundleConstants.LocalOnlyStatus}' (got '{actual}')."
        };
    }

    private static VerifyCheck CheckIsPublishedFalse(TaskingHandoffBundleArtifact bundle)
    {
        var passed = bundle.IsPublished == false;
        return new VerifyCheck
        {
            Id = CheckIds.BundleIsPublishedFalse,
            Passed = passed,
            Detail = passed
                ? "is_published == false."
                : "is_published must be literal false for a local-only bundle."
        };
    }

    private static VerifyCheck CheckIsAutomationVisibleFalse(TaskingHandoffBundleArtifact bundle)
    {
        var passed = bundle.IsAutomationVisible == false;
        return new VerifyCheck
        {
            Id = CheckIds.BundleIsAutomationVisibleFalse,
            Passed = passed,
            Detail = passed
                ? "is_automation_visible == false."
                : "is_automation_visible must be literal false for a local-only bundle."
        };
    }

    private static VerifyCheck CheckNonEmpty(string id, string? value, string fieldName)
    {
        var passed = !string.IsNullOrWhiteSpace(value);
        return new VerifyCheck
        {
            Id = id,
            Passed = passed,
            Detail = passed
                ? $"{fieldName} is present."
                : $"{fieldName} must be a non-empty string."
        };
    }

    private static VerifyCheck CheckChecklistNotEmpty(TaskingHandoffBundleArtifact bundle)
    {
        var passedCount = bundle.ChecklistPassedCheckIds?.Count ?? 0;
        var failedCount = bundle.ChecklistFailedCheckIds?.Count ?? 0;
        var passed = (passedCount + failedCount) > 0;
        return new VerifyCheck
        {
            Id = CheckIds.ChecklistPassedOrFailedCheckIdsPresent,
            Passed = passed,
            Detail = passed
                ? $"checklist contains {passedCount} passed and {failedCount} failed check ids."
                : "checklist_passed_check_ids and checklist_failed_check_ids are both empty."
        };
    }

    private static VerifyCheck Pass(string id, string detail) =>
        new() { Id = id, Passed = true, Detail = detail };

    private static VerifyCheck Fail(string id, string detail) =>
        new() { Id = id, Passed = false, Detail = detail };
}
