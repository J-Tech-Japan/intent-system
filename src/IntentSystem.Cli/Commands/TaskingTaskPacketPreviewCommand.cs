using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G192: <c>intent-cli tasking task-packet-preview</c>. A LOCAL renderer that
/// reads a G191 <see cref="TaskingTaskPacketArtifact"/> task packet artifact
/// (JSON) and writes a deterministic operator-facing preview/worker-prompt
/// artifact. The command performs no GitHub network calls, applies no labels,
/// launches no provider processes, and does NOT touch
/// <c>.intent-cli/queue-state.json</c> or <c>.intent-cli/runs.jsonl</c>.
///
/// This command is a pure consumer of the G191 task packet. It does NOT
/// replace <c>tasking handoff</c>, <c>tasking task-packet</c>,
/// <c>issue plan-candidate</c>, <c>issue prepare</c>, or
/// <c>issue publish-reviewed</c>; it sits beside G190 <c>handoff</c> and G191
/// <c>task-packet</c> under the same <c>tasking</c> group.
///
/// Network-mutation invariance: this command's hot path contains no
/// <c>Process.Start</c>, no shell-out to <c>gh</c>, and no provider launcher.
/// The associated tests validate the no-provider-launch invariant via the
/// <see cref="NestedProviderLauncher"/> sentinel and a source-scan assertion.
///
/// Strict no-partial-output: on missing input file, malformed JSON, or a task
/// packet whose <c>task_packet_status</c> is not <c>"local_only"</c>, the
/// command exits 1 and does NOT write the output artifact.
/// </summary>
internal static class TaskingTaskPacketPreviewCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    private static readonly JsonSerializerOptions ArtifactSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Test seam mirroring G190/G191 <c>TimestampFactory</c>.
    /// </summary>
    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// Test seam mirroring G190/G191 <c>NestedProviderLauncher</c>. G192 must
    /// NEVER invoke this delegate. Tests register a sentinel that flips a flag
    /// if invoked; the preview path leaves it untouched.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var fromTaskPacket, out var outPath, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var resolvedFromTaskPacket = Path.GetFullPath(fromTaskPacket);
        if (!File.Exists(resolvedFromTaskPacket))
        {
            writer.WriteLine($"--from-task-packet path does not exist: {fromTaskPacket}");
            return 1;
        }

        byte[] taskPacketBytes;
        try
        {
            taskPacketBytes = File.ReadAllBytes(resolvedFromTaskPacket);
        }
        catch (Exception exception)
        {
            writer.WriteLine($"Failed to read --from-task-packet: {exception.Message}");
            return 1;
        }

        TaskingTaskPacketArtifact? sourceTaskPacket;
        try
        {
            sourceTaskPacket = JsonSerializer.Deserialize<TaskingTaskPacketArtifact>(taskPacketBytes);
        }
        catch (JsonException exception)
        {
            writer.WriteLine($"Failed to parse --from-task-packet as a G191 task packet: {exception.Message}");
            return 1;
        }

        if (sourceTaskPacket is null)
        {
            writer.WriteLine("--from-task-packet parsed to a null packet; expected a G191 task packet JSON object.");
            return 1;
        }

        if (!string.Equals(
                sourceTaskPacket.TaskPacketStatus,
                TaskingTaskPacketConstants.LocalOnlyStatus,
                StringComparison.Ordinal))
        {
            writer.WriteLine(
                $"--from-task-packet task_packet_status must be '{TaskingTaskPacketConstants.LocalOnlyStatus}' "
                + $"(got '{sourceTaskPacket.TaskPacketStatus}'). Refusing to derive a preview from a non-local task packet.");
            return 1;
        }

        // Reuse IssuePrepareCommand.ComputeSha256Hex for traceability hashing.
        var sourceSha256 = IssuePrepareCommand.ComputeSha256Hex(taskPacketBytes);

        // Reuse IssuePrepareCommand.FormatUtcTimestamp for ISO8601 UTC formatting.
        var generatedAt = IssuePrepareCommand.FormatUtcTimestamp(TimestampFactory());

        var resolvedOutPath = Path.GetFullPath(outPath);
        var artifactDirectory = Path.GetDirectoryName(resolvedOutPath);
        if (!string.IsNullOrEmpty(artifactDirectory))
        {
            Directory.CreateDirectory(artifactDirectory);
        }

        var preview = TaskingTaskPacketPreviewAnalyzer.Build(
            sourceTaskPacket,
            fromTaskPacket,
            sourceSha256,
            generatedAt,
            resolvedOutPath);

        var serialized = JsonSerializer.Serialize(preview, ArtifactSerializerOptions);
        File.WriteAllText(resolvedOutPath, serialized);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(serialized);
        }
        else
        {
            WriteTextSummary(writer, preview);
        }

        return 0;
    }

    private static void WriteTextSummary(TextWriter writer, TaskingTaskPacketPreviewArtifact preview)
    {
        writer.WriteLine(preview.SummaryLine);
        writer.WriteLine($"artifact_path: {preview.ArtifactPath}");
        writer.WriteLine($"source_task_packet_path: {preview.SourceTaskPacketPath}");
        writer.WriteLine($"source_task_packet_sha256: {preview.SourceTaskPacketSha256}");
        writer.WriteLine($"source_handoff_path: {preview.SourceHandoffPath}");
        writer.WriteLine($"source_handoff_sha256: {preview.SourceHandoffSha256}");
        writer.WriteLine($"domain: {preview.Domain}");
        writer.WriteLine($"is_published: {preview.IsPublished.ToString().ToLowerInvariant()}");
        writer.WriteLine(
            $"is_automation_visible: {preview.IsAutomationVisible.ToString().ToLowerInvariant()}");
        writer.WriteLine($"preview_status: {preview.PreviewStatus}");
        writer.WriteLine($"recommended_worker_action: {preview.RecommendedWorkerAction}");
        writer.WriteLine($"generated_at_utc: {preview.GeneratedAtUtc}");
    }

    private static bool TryParseArguments(
        string[] args,
        out string fromTaskPacket,
        out string outPath,
        out string format,
        out string error)
    {
        fromTaskPacket = string.Empty;
        outPath = string.Empty;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--from-task-packet":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--from-task-packet requires a non-empty value.";
                        return false;
                    }

                    fromTaskPacket = args[index + 1];
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
                    error = $"Unknown argument '{argument}'. Supported: --from-task-packet, --out, --format.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(fromTaskPacket))
        {
            error = "--from-task-packet is required.";
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
