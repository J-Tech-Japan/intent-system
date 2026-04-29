namespace IntentSystem.Cli.Commands;

/// <summary>
/// G193 — pure logic that takes an already-deserialized G192
/// <see cref="TaskingTaskPacketPreviewArtifact"/> and builds a deterministic
/// <see cref="TaskingTaskPacketChecklistArtifact"/>. No file I/O, no network,
/// and no process launches happen here — those concerns are owned by
/// <see cref="TaskingTaskPacketChecklistCommand"/>.
///
/// The contract fields <see cref="TaskingTaskPacketChecklistArtifact.IsPublished"/>,
/// <see cref="TaskingTaskPacketChecklistArtifact.IsAutomationVisible"/>, and
/// <see cref="TaskingTaskPacketChecklistArtifact.ChecklistStatus"/> are always
/// literal false / false / "local_only" for this slice.
/// </summary>
internal static class TaskingTaskPacketChecklistAnalyzer
{
    /// <summary>
    /// Stable UNPUBLISHED status phrase asserted by G193 tests. Mirrors the
    /// G192 phrase so the worker handoff status is recognisable across the
    /// chain.
    /// </summary>
    public const string UnpublishedStatusPhrase =
        "UNPUBLISHED, local-only, not automation-visible";

    private const int RenderedPreviewExcerptMaxLength = 240;

    public static TaskingTaskPacketChecklistArtifact Build(
        TaskingTaskPacketPreviewArtifact sourcePreview,
        string sourcePreviewPath,
        string sourcePreviewSha256,
        string generatedAtUtc,
        string resolvedArtifactPath)
    {
        ArgumentNullException.ThrowIfNull(sourcePreview);
        ArgumentNullException.ThrowIfNull(sourcePreviewPath);
        ArgumentNullException.ThrowIfNull(sourcePreviewSha256);
        ArgumentNullException.ThrowIfNull(generatedAtUtc);
        ArgumentNullException.ThrowIfNull(resolvedArtifactPath);

        var checks = BuildChecks(sourcePreview);
        var readyForHandoff = checks.All(c => c.Passed);

        var summaryLine =
            $"Worker handoff checklist for {sourcePreview.Domain} derived from {sourcePreviewPath} — "
            + $"{UnpublishedStatusPhrase}. ready_for_handoff="
            + (readyForHandoff ? "true" : "false")
            + ".";

        return new TaskingTaskPacketChecklistArtifact
        {
            SourcePreviewPath = sourcePreviewPath,
            SourcePreviewSha256 = sourcePreviewSha256,
            SourceTaskPacketPath = sourcePreview.SourceTaskPacketPath,
            SourceTaskPacketSha256 = sourcePreview.SourceTaskPacketSha256,
            SourceHandoffPath = sourcePreview.SourceHandoffPath,
            SourceHandoffSha256 = sourcePreview.SourceHandoffSha256,
            Domain = sourcePreview.Domain,
            IsPublished = false,
            IsAutomationVisible = false,
            ChecklistStatus = TaskingTaskPacketChecklistConstants.LocalOnlyStatus,
            ReadyForHandoff = readyForHandoff,
            Checks = checks,
            RecommendedWorkerAction = sourcePreview.RecommendedWorkerAction,
            RenderedPreviewExcerpt = BuildRenderedPreviewExcerpt(sourcePreview.RenderedPreviewMarkdown),
            GeneratedAtUtc = generatedAtUtc,
            ArtifactPath = resolvedArtifactPath,
            SummaryLine = summaryLine
        };
    }

    private static IReadOnlyList<ChecklistCheck> BuildChecks(TaskingTaskPacketPreviewArtifact preview)
    {
        var checks = new List<ChecklistCheck>(TaskingTaskPacketChecklistConstants.CheckIds.All.Count);

        var statusLocalOnly = string.Equals(
            preview.PreviewStatus,
            TaskingTaskPacketPreviewConstants.LocalOnlyStatus,
            StringComparison.Ordinal);
        checks.Add(new ChecklistCheck
        {
            Id = TaskingTaskPacketChecklistConstants.CheckIds.PreviewStatusLocalOnly,
            Passed = statusLocalOnly,
            Detail = statusLocalOnly
                ? $"preview_status == '{TaskingTaskPacketPreviewConstants.LocalOnlyStatus}'."
                : $"preview_status was '{preview.PreviewStatus}', expected '{TaskingTaskPacketPreviewConstants.LocalOnlyStatus}'."
        });

        var publishedFalse = !preview.IsPublished;
        checks.Add(new ChecklistCheck
        {
            Id = TaskingTaskPacketChecklistConstants.CheckIds.PreviewIsPublishedFalse,
            Passed = publishedFalse,
            Detail = publishedFalse
                ? "preview is_published == false (unpublished)."
                : "preview is_published == true; expected false for local-only handoff."
        });

        var automationFalse = !preview.IsAutomationVisible;
        checks.Add(new ChecklistCheck
        {
            Id = TaskingTaskPacketChecklistConstants.CheckIds.PreviewIsAutomationVisibleFalse,
            Passed = automationFalse,
            Detail = automationFalse
                ? "preview is_automation_visible == false (not automation-visible)."
                : "preview is_automation_visible == true; expected false for local-only handoff."
        });

        checks.Add(BuildPresenceCheck(
            TaskingTaskPacketChecklistConstants.CheckIds.SourceTaskPacketPathPresent,
            "source_task_packet_path",
            preview.SourceTaskPacketPath));

        checks.Add(BuildPresenceCheck(
            TaskingTaskPacketChecklistConstants.CheckIds.SourceTaskPacketSha256Present,
            "source_task_packet_sha256",
            preview.SourceTaskPacketSha256));

        checks.Add(BuildPresenceCheck(
            TaskingTaskPacketChecklistConstants.CheckIds.SourceHandoffPathPresent,
            "source_handoff_path",
            preview.SourceHandoffPath));

        checks.Add(BuildPresenceCheck(
            TaskingTaskPacketChecklistConstants.CheckIds.SourceHandoffSha256Present,
            "source_handoff_sha256",
            preview.SourceHandoffSha256));

        var markdown = preview.RenderedPreviewMarkdown;
        var markdownPresent = !string.IsNullOrWhiteSpace(markdown);
        checks.Add(new ChecklistCheck
        {
            Id = TaskingTaskPacketChecklistConstants.CheckIds.RenderedPreviewMarkdownNonEmpty,
            Passed = markdownPresent,
            Detail = markdownPresent
                ? "rendered_preview_markdown is non-empty."
                : "rendered_preview_markdown is null or whitespace; expected operator-facing markdown."
        });

        var marksUnpublished = markdownPresent
            && markdown.Contains("UNPUBLISHED", StringComparison.Ordinal);
        checks.Add(new ChecklistCheck
        {
            Id = TaskingTaskPacketChecklistConstants.CheckIds.RenderedPreviewMarksUnpublished,
            Passed = marksUnpublished,
            Detail = marksUnpublished
                ? "rendered_preview_markdown contains the literal 'UNPUBLISHED'."
                : "rendered_preview_markdown does not contain the literal 'UNPUBLISHED'."
        });

        var recommendedActionPresent = !string.IsNullOrWhiteSpace(preview.RecommendedWorkerAction);
        checks.Add(new ChecklistCheck
        {
            Id = TaskingTaskPacketChecklistConstants.CheckIds.RecommendedWorkerActionNonEmpty,
            Passed = recommendedActionPresent,
            Detail = recommendedActionPresent
                ? "recommended_worker_action is non-empty."
                : "recommended_worker_action is null or whitespace; expected a worker action statement."
        });

        return checks;
    }

    private static ChecklistCheck BuildPresenceCheck(string id, string fieldName, string? value)
    {
        var present = !string.IsNullOrWhiteSpace(value);
        return new ChecklistCheck
        {
            Id = id,
            Passed = present,
            Detail = present
                ? $"{fieldName} is present."
                : $"{fieldName} is null or whitespace; expected a non-empty value from the source preview."
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
