using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G193: <c>intent-cli tasking task-packet-checklist</c>. A LOCAL acceptance
/// checklist that reads a G192 <see cref="TaskingTaskPacketPreviewArtifact"/>
/// preview artifact (JSON) and writes a deterministic operator-facing checklist
/// describing whether the preview is ready for worker handoff. The command
/// performs no GitHub network calls, applies no labels, launches no provider
/// processes, and does NOT touch <c>.intent-cli/queue-state.json</c> or
/// <c>.intent-cli/runs.jsonl</c>.
///
/// Sits beside G190 <c>handoff</c>, G191 <c>task-packet</c>, and G192
/// <c>task-packet-preview</c> under the same <c>tasking</c> group. It does NOT
/// replace any of those commands, nor does it touch
/// <c>issue plan-candidate</c>, <c>issue prepare</c>, or
/// <c>issue publish-reviewed</c>.
///
/// Network-mutation invariance: this command's hot path contains no
/// <c>Process.Start</c>, no shell-out to <c>gh</c>, and no provider launcher.
/// The associated tests validate the no-provider-launch invariant via the
/// <see cref="NestedProviderLauncher"/> sentinel and a source-scan assertion.
///
/// Strict no-partial-output: on missing input file, malformed JSON, or a
/// preview whose <c>preview_status</c> is not <c>"local_only"</c>, the command
/// exits 1 and does NOT write the output artifact.
/// </summary>
internal static class TaskingTaskPacketChecklistCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    private static readonly JsonSerializerOptions ArtifactSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Test seam mirroring G190/G191/G192 <c>TimestampFactory</c>.
    /// </summary>
    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// Test seam mirroring G190/G191/G192 <c>NestedProviderLauncher</c>. G193
    /// must NEVER invoke this delegate. Tests register a sentinel that flips a
    /// flag if invoked; the checklist path leaves it untouched.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var fromPreview, out var outPath, out var format, out var error))
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

        if (!string.Equals(
                sourcePreview.PreviewStatus,
                TaskingTaskPacketPreviewConstants.LocalOnlyStatus,
                StringComparison.Ordinal))
        {
            writer.WriteLine(
                $"--from-preview preview_status must be '{TaskingTaskPacketPreviewConstants.LocalOnlyStatus}' "
                + $"(got '{sourcePreview.PreviewStatus}'). Refusing to derive a checklist from a non-local preview.");
            return 1;
        }

        // Reuse IssuePrepareCommand.ComputeSha256Hex for traceability hashing.
        var sourceSha256 = IssuePrepareCommand.ComputeSha256Hex(previewBytes);

        // Reuse IssuePrepareCommand.FormatUtcTimestamp for ISO8601 UTC formatting.
        var generatedAt = IssuePrepareCommand.FormatUtcTimestamp(TimestampFactory());

        var resolvedOutPath = Path.GetFullPath(outPath);
        var artifactDirectory = Path.GetDirectoryName(resolvedOutPath);
        if (!string.IsNullOrEmpty(artifactDirectory))
        {
            Directory.CreateDirectory(artifactDirectory);
        }

        var checklist = TaskingTaskPacketChecklistAnalyzer.Build(
            sourcePreview,
            fromPreview,
            sourceSha256,
            generatedAt,
            resolvedOutPath);

        var serialized = JsonSerializer.Serialize(checklist, ArtifactSerializerOptions);
        File.WriteAllText(resolvedOutPath, serialized);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(serialized);
        }
        else
        {
            WriteTextSummary(writer, checklist);
        }

        return 0;
    }

    private static void WriteTextSummary(TextWriter writer, TaskingTaskPacketChecklistArtifact checklist)
    {
        writer.WriteLine(checklist.SummaryLine);
        writer.WriteLine($"artifact_path: {checklist.ArtifactPath}");
        writer.WriteLine($"source_preview_path: {checklist.SourcePreviewPath}");
        writer.WriteLine($"source_preview_sha256: {checklist.SourcePreviewSha256}");
        writer.WriteLine($"source_task_packet_path: {checklist.SourceTaskPacketPath}");
        writer.WriteLine($"source_task_packet_sha256: {checklist.SourceTaskPacketSha256}");
        writer.WriteLine($"source_handoff_path: {checklist.SourceHandoffPath}");
        writer.WriteLine($"source_handoff_sha256: {checklist.SourceHandoffSha256}");
        writer.WriteLine($"domain: {checklist.Domain}");
        writer.WriteLine($"is_published: {checklist.IsPublished.ToString().ToLowerInvariant()}");
        writer.WriteLine(
            $"is_automation_visible: {checklist.IsAutomationVisible.ToString().ToLowerInvariant()}");
        writer.WriteLine($"checklist_status: {checklist.ChecklistStatus}");
        writer.WriteLine(
            $"ready_for_handoff: {checklist.ReadyForHandoff.ToString().ToLowerInvariant()}");
        writer.WriteLine($"recommended_worker_action: {checklist.RecommendedWorkerAction}");
        writer.WriteLine($"generated_at_utc: {checklist.GeneratedAtUtc}");

        writer.WriteLine("checks:");
        foreach (var check in checklist.Checks)
        {
            writer.WriteLine(
                $"- {check.Id}: passed={check.Passed.ToString().ToLowerInvariant()} — {check.Detail}");
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string fromPreview,
        out string outPath,
        out string format,
        out string error)
    {
        fromPreview = string.Empty;
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
                    error = $"Unknown argument '{argument}'. Supported: --from-preview, --out, --format.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(fromPreview))
        {
            error = "--from-preview is required.";
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
