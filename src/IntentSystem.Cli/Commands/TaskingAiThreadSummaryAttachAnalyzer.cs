namespace IntentSystem.Cli.Commands;

/// <summary>
/// G201 — pure builder for <see cref="TaskingAiThreadSummaryAttachArtifact"/>.
/// Takes already-validated provenance values (paths, digests, kind, domain,
/// timestamp) and assembles the deterministic attachment record.
///
/// The analyzer never reads the filesystem, never launches providers, and never
/// looks at the actual summary text — only the digest and byte count are
/// passed in. This keeps the artifact purely provenance-only.
/// </summary>
internal static class TaskingAiThreadSummaryAttachAnalyzer
{
    public static TaskingAiThreadSummaryAttachArtifact Build(
        string sourceArtifactPath,
        string sourceArtifactSha256,
        string sourceArtifactKind,
        string sourceSummaryPath,
        string sourceSummarySha256,
        int sourceSummaryByteCount,
        string domain,
        string generatedAtUtc,
        string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(sourceArtifactPath);
        ArgumentNullException.ThrowIfNull(sourceArtifactSha256);
        ArgumentNullException.ThrowIfNull(sourceArtifactKind);
        ArgumentNullException.ThrowIfNull(sourceSummaryPath);
        ArgumentNullException.ThrowIfNull(sourceSummarySha256);
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(generatedAtUtc);
        ArgumentNullException.ThrowIfNull(artifactPath);

        var summaryLine =
            $"AI-thread session summary attachment for {domain} — UNPUBLISHED, local-only, "
            + "not automation-visible. status=ok.";

        return new TaskingAiThreadSummaryAttachArtifact
        {
            SourceArtifactPath = sourceArtifactPath,
            SourceArtifactSha256 = sourceArtifactSha256,
            SourceArtifactKind = sourceArtifactKind,
            SourceSummaryPath = sourceSummaryPath,
            SourceSummarySha256 = sourceSummarySha256,
            SourceSummaryByteCount = sourceSummaryByteCount,
            Domain = domain,
            IsPublished = false,
            IsAutomationVisible = false,
            AttachmentStatus = TaskingAiThreadSummaryAttachConstants.LocalOnlyStatus,
            GeneratedAtUtc = generatedAtUtc,
            ArtifactPath = artifactPath,
            SummaryLine = summaryLine
        };
    }
}
