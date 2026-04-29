using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G194: <c>intent-cli tasking handoff-bundle</c>. A LOCAL bundle/export bridge
/// that reads BOTH a G192 <see cref="TaskingTaskPacketPreviewArtifact"/> preview
/// artifact (JSON) and a G193 <see cref="TaskingTaskPacketChecklistArtifact"/>
/// checklist artifact (JSON), validates that they belong together (the
/// checklist's <c>source_preview_sha256</c> MUST equal the actual SHA256 of the
/// preview file's bytes), and writes one deterministic worker handoff bundle.
/// The command performs no GitHub network calls, applies no labels, launches no
/// provider processes, and does NOT touch <c>.intent-cli/queue-state.json</c>
/// or <c>.intent-cli/runs.jsonl</c>.
///
/// Sits beside G190 <c>handoff</c>, G191 <c>task-packet</c>, G192
/// <c>task-packet-preview</c>, and G193 <c>task-packet-checklist</c> under the
/// same <c>tasking</c> group. It does NOT replace any of those commands, nor
/// does it touch <c>issue plan-candidate</c>, <c>issue prepare</c>, or
/// <c>issue publish-reviewed</c>.
///
/// Network-mutation invariance: this command's hot path contains no
/// <c>Process.Start</c>, no shell-out to <c>gh</c>, and no provider launcher.
/// The associated tests validate the no-provider-launch invariant via the
/// <see cref="NestedProviderLauncher"/> sentinel and a source-scan assertion.
///
/// Strict no-partial-output: on missing input file, malformed JSON on either
/// input, a preview/checklist whose status is not <c>"local_only"</c>, or a
/// preview/checklist SHA256 mismatch, the command exits 1 and does NOT write
/// the output artifact.
/// </summary>
internal static class TaskingHandoffBundleCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    private static readonly JsonSerializerOptions ArtifactSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Test seam mirroring G190/G191/G192/G193 <c>TimestampFactory</c>.
    /// </summary>
    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// Test seam mirroring G190/G191/G192/G193 <c>NestedProviderLauncher</c>.
    /// G194 must NEVER invoke this delegate. Tests register a sentinel that
    /// flips a flag if invoked; the bundle path leaves it untouched.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(
                args,
                out var fromPreview,
                out var fromChecklist,
                out var outPath,
                out var format,
                out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var resolvedFromPreview = Path.GetFullPath(fromPreview);
        if (!File.Exists(resolvedFromPreview))
        {
            writer.WriteLine($"--from-preview path does not exist: {fromPreview}");
            return 1;
        }

        var resolvedFromChecklist = Path.GetFullPath(fromChecklist);
        if (!File.Exists(resolvedFromChecklist))
        {
            writer.WriteLine($"--from-checklist path does not exist: {fromChecklist}");
            return 1;
        }

        byte[] previewBytes;
        try
        {
            previewBytes = File.ReadAllBytes(resolvedFromPreview);
        }
        catch (Exception exception)
        {
            writer.WriteLine($"Failed to read --from-preview: {exception.Message}");
            return 1;
        }

        byte[] checklistBytes;
        try
        {
            checklistBytes = File.ReadAllBytes(resolvedFromChecklist);
        }
        catch (Exception exception)
        {
            writer.WriteLine($"Failed to read --from-checklist: {exception.Message}");
            return 1;
        }

        TaskingTaskPacketPreviewArtifact? sourcePreview;
        try
        {
            sourcePreview = JsonSerializer.Deserialize<TaskingTaskPacketPreviewArtifact>(previewBytes);
        }
        catch (JsonException exception)
        {
            writer.WriteLine($"Failed to parse --from-preview as a G192 task packet preview: {exception.Message}");
            return 1;
        }

        if (sourcePreview is null)
        {
            writer.WriteLine("--from-preview parsed to a null preview; expected a G192 task packet preview JSON object.");
            return 1;
        }

        TaskingTaskPacketChecklistArtifact? sourceChecklist;
        try
        {
            sourceChecklist =
                JsonSerializer.Deserialize<TaskingTaskPacketChecklistArtifact>(checklistBytes);
        }
        catch (JsonException exception)
        {
            writer.WriteLine($"Failed to parse --from-checklist as a G193 task packet checklist: {exception.Message}");
            return 1;
        }

        if (sourceChecklist is null)
        {
            writer.WriteLine("--from-checklist parsed to a null checklist; expected a G193 task packet checklist JSON object.");
            return 1;
        }

        if (!string.Equals(
                sourcePreview.PreviewStatus,
                TaskingTaskPacketPreviewConstants.LocalOnlyStatus,
                StringComparison.Ordinal))
        {
            writer.WriteLine(
                $"--from-preview preview_status must be '{TaskingTaskPacketPreviewConstants.LocalOnlyStatus}' "
                + $"(got '{sourcePreview.PreviewStatus}'). Refusing to derive a bundle from a non-local preview.");
            return 1;
        }

        if (!string.Equals(
                sourceChecklist.ChecklistStatus,
                TaskingTaskPacketChecklistConstants.LocalOnlyStatus,
                StringComparison.Ordinal))
        {
            writer.WriteLine(
                $"--from-checklist checklist_status must be '{TaskingTaskPacketChecklistConstants.LocalOnlyStatus}' "
                + $"(got '{sourceChecklist.ChecklistStatus}'). Refusing to derive a bundle from a non-local checklist.");
            return 1;
        }

        // Reuse IssuePrepareCommand.ComputeSha256Hex for traceability hashing.
        var sourcePreviewSha256 = IssuePrepareCommand.ComputeSha256Hex(previewBytes);
        var sourceChecklistSha256 = IssuePrepareCommand.ComputeSha256Hex(checklistBytes);

        // Mismatch acceptance criterion: the checklist must have been derived
        // from the supplied preview, asserted by SHA256 equality of the preview
        // file bytes vs the checklist's recorded source_preview_sha256.
        if (!string.Equals(
                sourceChecklist.SourcePreviewSha256,
                sourcePreviewSha256,
                StringComparison.Ordinal))
        {
            writer.WriteLine(
                "--from-checklist source_preview_sha256 does not match --from-preview file SHA256. "
                + $"checklist.source_preview_sha256={sourceChecklist.SourcePreviewSha256} "
                + $"preview_file_sha256={sourcePreviewSha256}. "
                + "Refusing to bundle a checklist that was not derived from the supplied preview.");
            return 1;
        }

        // Reuse IssuePrepareCommand.FormatUtcTimestamp for ISO8601 UTC formatting.
        var generatedAt = IssuePrepareCommand.FormatUtcTimestamp(TimestampFactory());

        var resolvedOutPath = Path.GetFullPath(outPath);
        var artifactDirectory = Path.GetDirectoryName(resolvedOutPath);
        if (!string.IsNullOrEmpty(artifactDirectory))
        {
            Directory.CreateDirectory(artifactDirectory);
        }

        var bundle = TaskingHandoffBundleAnalyzer.Build(
            sourcePreview,
            sourceChecklist,
            fromPreview,
            sourcePreviewSha256,
            fromChecklist,
            sourceChecklistSha256,
            generatedAt,
            resolvedOutPath);

        var serialized = JsonSerializer.Serialize(bundle, ArtifactSerializerOptions);
        File.WriteAllText(resolvedOutPath, serialized);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(serialized);
        }
        else
        {
            WriteTextSummary(writer, bundle);
        }

        return 0;
    }

    private static void WriteTextSummary(TextWriter writer, TaskingHandoffBundleArtifact bundle)
    {
        writer.WriteLine(bundle.SummaryLine);
        writer.WriteLine($"artifact_path: {bundle.ArtifactPath}");
        writer.WriteLine($"source_preview_path: {bundle.SourcePreviewPath}");
        writer.WriteLine($"source_preview_sha256: {bundle.SourcePreviewSha256}");
        writer.WriteLine($"source_checklist_path: {bundle.SourceChecklistPath}");
        writer.WriteLine($"source_checklist_sha256: {bundle.SourceChecklistSha256}");
        writer.WriteLine($"source_task_packet_path: {bundle.SourceTaskPacketPath}");
        writer.WriteLine($"source_task_packet_sha256: {bundle.SourceTaskPacketSha256}");
        writer.WriteLine($"source_handoff_path: {bundle.SourceHandoffPath}");
        writer.WriteLine($"source_handoff_sha256: {bundle.SourceHandoffSha256}");
        writer.WriteLine($"domain: {bundle.Domain}");
        writer.WriteLine($"is_published: {bundle.IsPublished.ToString().ToLowerInvariant()}");
        writer.WriteLine(
            $"is_automation_visible: {bundle.IsAutomationVisible.ToString().ToLowerInvariant()}");
        writer.WriteLine($"bundle_status: {bundle.BundleStatus}");
        writer.WriteLine(
            $"checklist_ready_for_handoff: {bundle.ChecklistReadyForHandoff.ToString().ToLowerInvariant()}");
        writer.WriteLine($"recommended_worker_action: {bundle.RecommendedWorkerAction}");
        writer.WriteLine($"generated_at_utc: {bundle.GeneratedAtUtc}");

        writer.WriteLine($"checklist_passed_check_ids ({bundle.ChecklistPassedCheckIds.Count}):");
        foreach (var id in bundle.ChecklistPassedCheckIds)
        {
            writer.WriteLine($"- {id}");
        }

        writer.WriteLine($"checklist_failed_check_ids ({bundle.ChecklistFailedCheckIds.Count}):");
        foreach (var id in bundle.ChecklistFailedCheckIds)
        {
            writer.WriteLine($"- {id}");
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string fromPreview,
        out string fromChecklist,
        out string outPath,
        out string format,
        out string error)
    {
        fromPreview = string.Empty;
        fromChecklist = string.Empty;
        outPath = string.Empty;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--from-preview":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--from-preview requires a non-empty value.";
                        return false;
                    }

                    fromPreview = args[index + 1];
                    index++;
                    break;

                case "--from-checklist":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--from-checklist requires a non-empty value.";
                        return false;
                    }

                    fromChecklist = args[index + 1];
                    index++;
                    break;

                case "--out":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--out requires a non-empty value.";
                        return false;
                    }

                    outPath = args[index + 1];
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (text or json).";
                        return false;
                    }

                    var requestedFormat = args[index + 1];
                    if (!string.Equals(requestedFormat, FormatText, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'text' or 'json' (got '{requestedFormat}').";
                        return false;
                    }

                    format = requestedFormat;
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'. Supported: --from-preview, --from-checklist, --out, --format.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(fromPreview))
        {
            error = "--from-preview is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(fromChecklist))
        {
            error = "--from-checklist is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(outPath))
        {
            error = "--out is required.";
            return false;
        }

        return true;
    }
}
