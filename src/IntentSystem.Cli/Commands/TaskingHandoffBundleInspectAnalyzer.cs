namespace IntentSystem.Cli.Commands;

/// <summary>
/// G195 — pure logic that takes a deserialized G194
/// <see cref="TaskingHandoffBundleArtifact"/> bundle and builds a deterministic
/// <see cref="TaskingHandoffBundleInspectSummary"/>. No file I/O, no network,
/// and no process launches happen here — those concerns are owned by
/// <see cref="TaskingHandoffBundleInspectCommand"/>.
///
/// The fields <see cref="TaskingHandoffBundleInspectSummary.IsPublished"/>,
/// <see cref="TaskingHandoffBundleInspectSummary.IsAutomationVisible"/>, and
/// <see cref="TaskingHandoffBundleInspectSummary.BundleStatus"/> are propagated
/// verbatim from the source bundle. The command layer asserts the source bundle
/// has <c>bundle_status == "local_only"</c> before calling this analyzer; G194
/// contract makes that imply <c>is_published == false</c> and
/// <c>is_automation_visible == false</c>.
/// </summary>
internal static class TaskingHandoffBundleInspectAnalyzer
{
    /// <summary>
    /// Stable UNPUBLISHED status phrase asserted by G195 tests. Identical to the
    /// G194 phrase so an operator scanning either output can recognise it.
    /// </summary>
    public const string UnpublishedStatusPhrase =
        "UNPUBLISHED, local-only, not automation-visible";

    /// <summary>
    /// Stable summary line emitted at the top of both text and JSON outputs.
    /// Locked by tests so the wording cannot drift silently.
    /// </summary>
    public const string SummaryLineText =
        "Tasking handoff bundle inspect — UNPUBLISHED, local-only, not automation-visible.";

    public static TaskingHandoffBundleInspectSummary Build(TaskingHandoffBundleArtifact bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        return new TaskingHandoffBundleInspectSummary
        {
            Domain = bundle.Domain,
            BundleStatus = bundle.BundleStatus,
            IsPublished = bundle.IsPublished,
            IsAutomationVisible = bundle.IsAutomationVisible,
            ChecklistReadyForHandoff = bundle.ChecklistReadyForHandoff,
            PassedCheckCount = bundle.ChecklistPassedCheckIds.Count,
            FailedCheckCount = bundle.ChecklistFailedCheckIds.Count,
            PassedCheckIds = bundle.ChecklistPassedCheckIds,
            FailedCheckIds = bundle.ChecklistFailedCheckIds,
            SourcePreviewPath = bundle.SourcePreviewPath,
            SourcePreviewSha256 = bundle.SourcePreviewSha256,
            SourceChecklistPath = bundle.SourceChecklistPath,
            SourceChecklistSha256 = bundle.SourceChecklistSha256,
            SourceTaskPacketPath = bundle.SourceTaskPacketPath,
            SourceTaskPacketSha256 = bundle.SourceTaskPacketSha256,
            SourceHandoffPath = bundle.SourceHandoffPath,
            SourceHandoffSha256 = bundle.SourceHandoffSha256,
            RecommendedWorkerAction = bundle.RecommendedWorkerAction,
            SummaryLine = SummaryLineText
        };
    }
}
