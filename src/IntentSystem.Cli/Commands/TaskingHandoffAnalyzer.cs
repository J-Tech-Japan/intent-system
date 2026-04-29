namespace IntentSystem.Cli.Commands;

/// <summary>
/// G190 — pure logic that composes the existing G179 <see cref="StatusBriefAnalyzer"/>,
/// G180 <see cref="ContextCollectAnalyzer"/>, and G185 <see cref="NextSliceClassifyAnalyzer"/>
/// into a single combined <see cref="TaskingHandoffPacket"/> for outer tasking threads.
///
/// Reuse rather than duplicate. Each sub-analyzer is invoked in-process; no
/// child processes are spawned and no GitHub network calls are made. If a
/// sub-analyzer throws, the corresponding <c>&lt;sub_packet&gt;_error</c> field
/// is populated with the exception message and the surrounding sub_packet field
/// is left null — partial packets are still useful to the tasking thread.
///
/// The contract fields <see cref="TaskingHandoffPacket.IsPublished"/>,
/// <see cref="TaskingHandoffPacket.IsAutomationVisible"/>, and
/// <see cref="TaskingHandoffPacket.TaskingHandoffStatus"/> are always literal
/// false / false / "local_only" for this slice.
/// </summary>
internal static class TaskingHandoffAnalyzer
{
    public static TaskingHandoffPacket Build(
        CliContext context,
        string? domainOverride,
        string generatedAtUtc,
        string resolvedArtifactPath)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(generatedAtUtc);
        ArgumentNullException.ThrowIfNull(resolvedArtifactPath);

        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride;

        StatusBriefSummary? statusBrief = null;
        string? statusBriefError = null;
        try
        {
            // G190 is the second consumer of StatusBriefAnalyzer.Analyze (after the
            // standalone status brief command). The entry method is already
            // assembly-internal; no promotion required.
            statusBrief = StatusBriefAnalyzer.Analyze(context, domainOverride);
        }
        catch (Exception exception)
        {
            statusBriefError = exception.Message;
        }

        ContextCollectPacket? contextCollect = null;
        string? contextCollectError = null;
        try
        {
            // G190 is the second consumer of ContextCollectAnalyzer.Analyze.
            contextCollect = ContextCollectAnalyzer.Analyze(context, domainOverride);
        }
        catch (Exception exception)
        {
            contextCollectError = exception.Message;
        }

        NextSliceClassifyResult? nextSlice = null;
        string? nextSliceError = null;
        try
        {
            // G190 is the second consumer of NextSliceClassifyAnalyzer.Analyze.
            nextSlice = NextSliceClassifyAnalyzer.Analyze(context, domainOverride);
        }
        catch (Exception exception)
        {
            nextSliceError = exception.Message;
        }

        var summaryLine =
            $"Tasking-thread handoff packet for {domain} — UNPUBLISHED, local-only, not automation-visible.";

        return new TaskingHandoffPacket
        {
            Domain = domain,
            IsPublished = false,
            IsAutomationVisible = false,
            TaskingHandoffStatus = TaskingHandoffConstants.LocalOnlyStatus,
            StatusBrief = statusBrief,
            StatusBriefError = statusBriefError,
            ContextCollect = contextCollect,
            ContextCollectError = contextCollectError,
            NextSliceClassify = nextSlice,
            NextSliceClassifyError = nextSliceError,
            GeneratedAtUtc = generatedAtUtc,
            ArtifactPath = resolvedArtifactPath,
            SummaryLine = summaryLine
        };
    }
}
