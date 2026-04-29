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
///
/// G197 — content-hash pinning checks (file_exists + hash_matches per
/// referenced source artifact) are appended to the existing check list. The
/// command pre-computes file observations via <see cref="SourceArtifactObservation"/>
/// and hands them to <see cref="BuildChecks"/>; the analyzer itself still does
/// no I/O.
/// </summary>
internal static class TaskingHandoffBundleVerifyAnalyzer
{
    /// <summary>
    /// Build the <see cref="VerifyCheck"/> list for a successfully parsed G194
    /// bundle. Checks appear in the stable order declared by
    /// <see cref="TaskingHandoffBundleVerifyConstants.CheckIds.All"/>.
    /// </summary>
    public static IReadOnlyList<VerifyCheck> BuildChecks(
        TaskingHandoffBundleArtifact bundle,
        SourceArtifactObservations observations)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(observations);

        var (taskPacketFileExists, taskPacketHashMatches) = BuildContentHashPair(
            CheckIds.TaskPacketFileExists,
            CheckIds.TaskPacketHashMatches,
            "source_task_packet_path",
            bundle.SourceTaskPacketPath,
            bundle.SourceTaskPacketSha256,
            observations.TaskPacket);

        var (previewFileExists, previewHashMatches) = BuildContentHashPair(
            CheckIds.PreviewFileExists,
            CheckIds.PreviewHashMatches,
            "source_preview_path",
            bundle.SourcePreviewPath,
            bundle.SourcePreviewSha256,
            observations.Preview);

        var (checklistFileExists, checklistHashMatches) = BuildContentHashPair(
            CheckIds.ChecklistFileExists,
            CheckIds.ChecklistHashMatches,
            "source_checklist_path",
            bundle.SourceChecklistPath,
            bundle.SourceChecklistSha256,
            observations.Checklist);

        var (handoffFileExists, handoffHashMatches) = BuildContentHashPair(
            CheckIds.HandoffFileExists,
            CheckIds.HandoffHashMatches,
            "source_handoff_path",
            bundle.SourceHandoffPath,
            bundle.SourceHandoffSha256,
            observations.Handoff);

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
                "Bundle schema carries no provider-launch directive (G194 contract)."),

            // G197 — content-hash pinning checks. Stable order:
            // task-packet (file_exists, hash_matches),
            // preview (file_exists, hash_matches),
            // checklist (file_exists, hash_matches),
            // handoff (file_exists, hash_matches).
            taskPacketFileExists,
            taskPacketHashMatches,
            previewFileExists,
            previewHashMatches,
            checklistFileExists,
            checklistHashMatches,
            handoffFileExists,
            handoffHashMatches
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

    /// <summary>
    /// Build the (file_exists, hash_matches) check pair for one referenced
    /// source artifact. Behavior precedence (G197):
    /// <list type="bullet">
    ///   <item>If the bundle's path field is empty/whitespace, both checks are
    ///   <c>passed=false</c> with skipped detail (path-presence already
    ///   reported the upstream cause).</item>
    ///   <item>If file does not exist on disk, file_exists fails;
    ///   hash_matches is <c>passed=false</c> with skipped detail.</item>
    ///   <item>If the file was unreadable (locked, permission denied),
    ///   file_exists is <c>passed=true</c> (we observed it) but hash_matches
    ///   fails with the read error.</item>
    ///   <item>Otherwise hash_matches compares actual sha256 to the
    ///   bundle-recorded value.</item>
    /// </list>
    /// </summary>
    private static (VerifyCheck FileExists, VerifyCheck HashMatches) BuildContentHashPair(
        string fileExistsId,
        string hashMatchesId,
        string pathFieldName,
        string? recordedPath,
        string? recordedSha256,
        SourceArtifactObservation observation)
    {
        if (string.IsNullOrWhiteSpace(recordedPath))
        {
            var skipped = $"skipped: {pathFieldName} is missing or empty";
            return (
                Fail(fileExistsId, skipped),
                Fail(hashMatchesId, "skipped: " + pathFieldName + " is missing or empty"));
        }

        if (!observation.FileExists)
        {
            return (
                Fail(fileExistsId, $"file does not exist: {recordedPath}"),
                Fail(hashMatchesId, "skipped: file does not exist"));
        }

        // File observed on disk — file_exists passes regardless of read outcome.
        var fileExistsCheck = Pass(fileExistsId, $"file exists: {recordedPath}");

        if (!string.IsNullOrEmpty(observation.ReadErrorMessage))
        {
            return (
                fileExistsCheck,
                Fail(hashMatchesId, $"could not read file: {observation.ReadErrorMessage}"));
        }

        var actual = observation.ActualSha256 ?? string.Empty;
        var recorded = recordedSha256 ?? string.Empty;
        var passed = !string.IsNullOrEmpty(actual)
            && !string.IsNullOrEmpty(recorded)
            && string.Equals(actual, recorded, StringComparison.Ordinal);

        var hashMatchesCheck = passed
            ? Pass(hashMatchesId, $"sha256 matches: {actual}")
            : Fail(
                hashMatchesId,
                $"sha256 mismatch — recorded: {recorded} actual: {actual}");

        return (fileExistsCheck, hashMatchesCheck);
    }

    private static VerifyCheck Pass(string id, string detail) =>
        new() { Id = id, Passed = true, Detail = detail };

    private static VerifyCheck Fail(string id, string detail) =>
        new() { Id = id, Passed = false, Detail = detail };
}

/// <summary>
/// G197 — pre-computed file observation for a single referenced source
/// artifact. The verify command computes these in one pass (file existence,
/// optional bytes, optional sha256, optional read error message) and passes
/// the bag to <see cref="TaskingHandoffBundleVerifyAnalyzer.BuildChecks"/>.
///
/// Keeping I/O in the command and inspection in the analyzer preserves the
/// G196 separation: the analyzer remains a pure function over a snapshot of
/// observed state.
/// </summary>
internal sealed record SourceArtifactObservation
{
    /// <summary>True iff <c>File.Exists</c> returned true for the recorded path.</summary>
    public required bool FileExists { get; init; }

    /// <summary>
    /// Hex sha256 of the file's bytes, or null when the file could not be
    /// read (does not exist, locked, permission denied) or the path field
    /// was empty.
    /// </summary>
    public string? ActualSha256 { get; init; }

    /// <summary>
    /// Non-null when <c>File.Exists</c> returned true but <c>File.ReadAllBytes</c>
    /// threw. Carries the exception message for the hash-match check detail.
    /// </summary>
    public string? ReadErrorMessage { get; init; }

    public static SourceArtifactObservation Skipped() =>
        new() { FileExists = false };
}

/// <summary>
/// Bundle of four <see cref="SourceArtifactObservation"/> records — one per
/// referenced source artifact (task-packet, preview, checklist, handoff).
/// </summary>
internal sealed record SourceArtifactObservations
{
    public required SourceArtifactObservation TaskPacket { get; init; }
    public required SourceArtifactObservation Preview { get; init; }
    public required SourceArtifactObservation Checklist { get; init; }
    public required SourceArtifactObservation Handoff { get; init; }
}
