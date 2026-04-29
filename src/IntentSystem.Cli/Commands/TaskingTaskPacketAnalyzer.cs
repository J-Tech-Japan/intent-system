namespace IntentSystem.Cli.Commands;

/// <summary>
/// G191 — pure logic that takes an already-deserialized G190
/// <see cref="TaskingHandoffPacket"/> and builds a deterministic
/// <see cref="TaskingTaskPacketArtifact"/>. No file I/O, no network, and no
/// process launches happen here — those concerns are owned by
/// <see cref="TaskingTaskPacketCommand"/>.
///
/// Embedded sub-packets are propagated by reference: the existing G179/G180/
/// G185 records are immutable <c>record</c>s with init-only properties, so
/// they are effectively deep-copies for outer consumers.
///
/// The contract fields <see cref="TaskingTaskPacketArtifact.IsPublished"/>,
/// <see cref="TaskingTaskPacketArtifact.IsAutomationVisible"/>, and
/// <see cref="TaskingTaskPacketArtifact.TaskPacketStatus"/> are always literal
/// false / false / "local_only" for this slice.
/// </summary>
internal static class TaskingTaskPacketAnalyzer
{
    public static TaskingTaskPacketArtifact Build(
        TaskingHandoffPacket sourceHandoff,
        string sourceHandoffPath,
        string sourceHandoffSha256,
        string generatedAtUtc,
        string resolvedArtifactPath)
    {
        ArgumentNullException.ThrowIfNull(sourceHandoff);
        ArgumentNullException.ThrowIfNull(sourceHandoffPath);
        ArgumentNullException.ThrowIfNull(sourceHandoffSha256);
        ArgumentNullException.ThrowIfNull(generatedAtUtc);
        ArgumentNullException.ThrowIfNull(resolvedArtifactPath);

        var recommendedAction = !string.IsNullOrWhiteSpace(
            sourceHandoff.NextSliceClassify?.RecommendedNextAction)
            ? sourceHandoff.NextSliceClassify!.RecommendedNextAction
            : TaskingTaskPacketConstants.FallbackRecommendedWorkerAction;

        var summaryLine =
            $"Worker task packet for {sourceHandoff.Domain} derived from {sourceHandoffPath} — UNPUBLISHED, local-only, not automation-visible.";

        return new TaskingTaskPacketArtifact
        {
            SourceHandoffPath = sourceHandoffPath,
            SourceHandoffSha256 = sourceHandoffSha256,
            Domain = sourceHandoff.Domain,
            IsPublished = false,
            IsAutomationVisible = false,
            TaskPacketStatus = TaskingTaskPacketConstants.LocalOnlyStatus,
            EmbeddedStatusBrief = sourceHandoff.StatusBrief,
            EmbeddedContextCollect = sourceHandoff.ContextCollect,
            EmbeddedNextSliceClassify = sourceHandoff.NextSliceClassify,
            RecommendedWorkerAction = recommendedAction,
            GeneratedAtUtc = generatedAtUtc,
            ArtifactPath = resolvedArtifactPath,
            SummaryLine = summaryLine
        };
    }
}
