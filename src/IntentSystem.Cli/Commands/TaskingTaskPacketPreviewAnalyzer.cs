using System.Text;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G192 — pure logic that takes an already-deserialized G191
/// <see cref="TaskingTaskPacketArtifact"/> and builds a deterministic
/// <see cref="TaskingTaskPacketPreviewArtifact"/> plus the operator-facing
/// rendered markdown. No file I/O, no network, and no process launches happen
/// here — those concerns are owned by
/// <see cref="TaskingTaskPacketPreviewCommand"/>.
///
/// The contract fields <see cref="TaskingTaskPacketPreviewArtifact.IsPublished"/>,
/// <see cref="TaskingTaskPacketPreviewArtifact.IsAutomationVisible"/>, and
/// <see cref="TaskingTaskPacketPreviewArtifact.PreviewStatus"/> are always
/// literal false / false / "local_only" for this slice.
/// </summary>
internal static class TaskingTaskPacketPreviewAnalyzer
{
    /// <summary>
    /// Stable UNPUBLISHED status phrase asserted by G192 tests. Any future
    /// drift in this string deliberately breaks the contract test.
    /// </summary>
    public const string UnpublishedStatusPhrase =
        "UNPUBLISHED, local-only, not automation-visible";

    public static TaskingTaskPacketPreviewArtifact Build(
        TaskingTaskPacketArtifact sourceTaskPacket,
        string sourceTaskPacketPath,
        string sourceTaskPacketSha256,
        string generatedAtUtc,
        string resolvedArtifactPath)
    {
        ArgumentNullException.ThrowIfNull(sourceTaskPacket);
        ArgumentNullException.ThrowIfNull(sourceTaskPacketPath);
        ArgumentNullException.ThrowIfNull(sourceTaskPacketSha256);
        ArgumentNullException.ThrowIfNull(generatedAtUtc);
        ArgumentNullException.ThrowIfNull(resolvedArtifactPath);

        var renderedMarkdown = RenderPreviewMarkdown(
            sourceTaskPacket,
            sourceTaskPacketPath,
            sourceTaskPacketSha256);

        var summaryLine =
            $"Worker preview for {sourceTaskPacket.Domain} derived from {sourceTaskPacketPath} — {UnpublishedStatusPhrase}.";

        return new TaskingTaskPacketPreviewArtifact
        {
            SourceTaskPacketPath = sourceTaskPacketPath,
            SourceTaskPacketSha256 = sourceTaskPacketSha256,
            SourceHandoffPath = sourceTaskPacket.SourceHandoffPath,
            SourceHandoffSha256 = sourceTaskPacket.SourceHandoffSha256,
            Domain = sourceTaskPacket.Domain,
            IsPublished = false,
            IsAutomationVisible = false,
            PreviewStatus = TaskingTaskPacketPreviewConstants.LocalOnlyStatus,
            EmbeddedStatusBrief = sourceTaskPacket.EmbeddedStatusBrief,
            EmbeddedContextCollect = sourceTaskPacket.EmbeddedContextCollect,
            EmbeddedNextSliceClassify = sourceTaskPacket.EmbeddedNextSliceClassify,
            RecommendedWorkerAction = sourceTaskPacket.RecommendedWorkerAction,
            RenderedPreviewMarkdown = renderedMarkdown,
            GeneratedAtUtc = generatedAtUtc,
            ArtifactPath = resolvedArtifactPath,
            SummaryLine = summaryLine
        };
    }

    private static string RenderPreviewMarkdown(
        TaskingTaskPacketArtifact source,
        string sourceTaskPacketPath,
        string sourceTaskPacketSha256)
    {
        var builder = new StringBuilder();

        builder.Append("# Worker task preview — ").Append(source.Domain).Append('\n');
        builder.Append('\n');
        builder.Append("**Status: ").Append(UnpublishedStatusPhrase).Append(".**").Append('\n');
        builder.Append('\n');

        builder.Append("## Source references").Append('\n');
        builder.Append("- Task packet: ")
            .Append(sourceTaskPacketPath)
            .Append(" (sha256: ")
            .Append(sourceTaskPacketSha256)
            .Append(')')
            .Append('\n');
        builder.Append("- Handoff: ")
            .Append(source.SourceHandoffPath)
            .Append(" (sha256: ")
            .Append(source.SourceHandoffSha256)
            .Append(')')
            .Append('\n');
        builder.Append('\n');

        builder.Append("## Recommended worker action").Append('\n');
        builder.Append(source.RecommendedWorkerAction).Append('\n');
        builder.Append('\n');

        builder.Append("## Status brief").Append('\n');
        builder.Append(SummarizeStatusBrief(source.EmbeddedStatusBrief)).Append('\n');
        builder.Append('\n');

        builder.Append("## Context collect").Append('\n');
        builder.Append(SummarizeContextCollect(source.EmbeddedContextCollect)).Append('\n');
        builder.Append('\n');

        builder.Append("## Next-slice classify").Append('\n');
        builder.Append(SummarizeNextSliceClassify(source.EmbeddedNextSliceClassify));

        return builder.ToString();
    }

    private static string SummarizeStatusBrief(StatusBriefSummary? brief)
    {
        if (brief is null)
        {
            return "(none)";
        }

        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "Domain {0}: queue_state_present={1}, wip_present={2}, in_flight={3}, review={4}, recommended_action: {5}",
            brief.Domain,
            brief.QueueStatePresent ? "true" : "false",
            brief.WipPresent ? "true" : "false",
            brief.InFlightUnits.Count,
            brief.ReviewUnits.Count,
            brief.RecommendedAction);
    }

    private static string SummarizeContextCollect(ContextCollectPacket? context)
    {
        if (context is null)
        {
            return "(none)";
        }

        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "Domain {0}: queue_state_present={1}, in_flight={2}, review={3}, automation_bindings_present={4}.",
            context.Domain,
            context.QueueStatePresent ? "true" : "false",
            context.InFlightUnits.Count,
            context.ReviewUnits.Count,
            context.AutomationBindingsPresent ? "true" : "false");
    }

    private static string SummarizeNextSliceClassify(NextSliceClassifyResult? classify)
    {
        if (classify is null)
        {
            return "(none)";
        }

        var builder = new StringBuilder();
        builder.Append("- classification: ").Append(classify.Classification).Append('\n');
        builder.Append("- rationale: ").Append(classify.Rationale).Append('\n');
        builder.Append("- recommended_next_action: ").Append(classify.RecommendedNextAction);
        return builder.ToString();
    }
}
