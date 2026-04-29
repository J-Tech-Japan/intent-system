namespace IntentSystem.Cli.Commands;

/// <summary>
/// G194 — pure logic that takes a deserialized G192
/// <see cref="TaskingTaskPacketPreviewArtifact"/> preview and a G193
/// <see cref="TaskingTaskPacketChecklistArtifact"/> checklist (already validated
/// to belong together by the command layer via SHA256 match) and builds a
/// deterministic <see cref="TaskingHandoffBundleArtifact"/>. No file I/O, no
/// network, and no process launches happen here — those concerns are owned by
/// <see cref="TaskingHandoffBundleCommand"/>.
///
/// The contract fields <see cref="TaskingHandoffBundleArtifact.IsPublished"/>,
/// <see cref="TaskingHandoffBundleArtifact.IsAutomationVisible"/>, and
/// <see cref="TaskingHandoffBundleArtifact.BundleStatus"/> are always literal
/// false / false / "local_only" for this slice.
/// </summary>
internal static class TaskingHandoffBundleAnalyzer
{
    /// <summary>
    /// Stable UNPUBLISHED status phrase asserted by G194 tests. Mirrors the
    /// G192/G193 phrase so the worker handoff status is recognisable across the
    /// chain.
    /// </summary>
    public const string UnpublishedStatusPhrase =
        "UNPUBLISHED, local-only, not automation-visible";

    private const int RenderedPreviewExcerptMaxLength = 240;

    public static TaskingHandoffBundleArtifact Build(
        TaskingTaskPacketPreviewArtifact sourcePreview,
        TaskingTaskPacketChecklistArtifact sourceChecklist,
        string sourcePreviewPath,
        string sourcePreviewSha256,
        string sourceChecklistPath,
        string sourceChecklistSha256,
        string generatedAtUtc,
        string resolvedArtifactPath)
    {
        ArgumentNullException.ThrowIfNull(sourcePreview);
        ArgumentNullException.ThrowIfNull(sourceChecklist);
        ArgumentNullException.ThrowIfNull(sourcePreviewPath);
        ArgumentNullException.ThrowIfNull(sourcePreviewSha256);
        ArgumentNullException.ThrowIfNull(sourceChecklistPath);
        ArgumentNullException.ThrowIfNull(sourceChecklistSha256);
        ArgumentNullException.ThrowIfNull(generatedAtUtc);
        ArgumentNullException.ThrowIfNull(resolvedArtifactPath);

        var passed = new List<string>();
        var failed = new List<string>();
        foreach (var check in sourceChecklist.Checks)
        {
            if (check.Passed)
            {
                passed.Add(check.Id);
            }
            else
            {
                failed.Add(check.Id);
            }
        }

        var renderedExcerpt = !string.IsNullOrEmpty(sourceChecklist.RenderedPreviewExcerpt)
            ? sourceChecklist.RenderedPreviewExcerpt
            : BuildRenderedPreviewExcerpt(sourcePreview.RenderedPreviewMarkdown);

        var summaryLine =
            $"Worker handoff bundle for {sourcePreview.Domain} derived from preview "
            + $"{sourcePreviewPath} + checklist {sourceChecklistPath} — "
            + $"{UnpublishedStatusPhrase}. ready_for_handoff="
            + (sourceChecklist.ReadyForHandoff ? "true" : "false")
            + ".";

        return new TaskingHandoffBundleArtifact
        {
            SourcePreviewPath = sourcePreviewPath,
            SourcePreviewSha256 = sourcePreviewSha256,
            SourceChecklistPath = sourceChecklistPath,
            SourceChecklistSha256 = sourceChecklistSha256,
            SourceTaskPacketPath = sourcePreview.SourceTaskPacketPath,
            SourceTaskPacketSha256 = sourcePreview.SourceTaskPacketSha256,
            SourceHandoffPath = sourcePreview.SourceHandoffPath,
            SourceHandoffSha256 = sourcePreview.SourceHandoffSha256,
            Domain = sourcePreview.Domain,
            IsPublished = false,
            IsAutomationVisible = false,
            BundleStatus = TaskingHandoffBundleConstants.LocalOnlyStatus,
            ChecklistReadyForHandoff = sourceChecklist.ReadyForHandoff,
            ChecklistPassedCheckIds = passed,
            ChecklistFailedCheckIds = failed,
            RenderedPreviewExcerpt = renderedExcerpt,
            RecommendedWorkerAction = sourcePreview.RecommendedWorkerAction,
            GeneratedAtUtc = generatedAtUtc,
            ArtifactPath = resolvedArtifactPath,
            SummaryLine = summaryLine
        };
    }

    private static string? BuildRenderedPreviewExcerpt(string? renderedMarkdown)
    {
        if (string.IsNullOrEmpty(renderedMarkdown))
        {
            return null;
        }

        if (renderedMarkdown.Length <= RenderedPreviewExcerptMaxLength)
        {
            return renderedMarkdown;
        }

        return renderedMarkdown.Substring(0, RenderedPreviewExcerptMaxLength) + "...";
    }
}
