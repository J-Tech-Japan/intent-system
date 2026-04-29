using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G191: <c>intent-cli tasking task-packet</c>. A LOCAL bridge that reads a
/// G190 <see cref="TaskingHandoffPacket"/> handoff artifact (JSON) and writes
/// a deterministic operator-facing worker-task packet artifact. The command
/// performs no GitHub network calls, applies no labels, launches no provider
/// processes, and does NOT touch <c>.intent-cli/queue-state.json</c> or
/// <c>.intent-cli/runs.jsonl</c>.
///
/// This command is a pure consumer of the G190 handoff. It does NOT replace
/// <c>tasking handoff</c>, <c>issue plan-candidate</c>, <c>issue prepare</c>,
/// or <c>issue publish-reviewed</c>; it sits beside <c>tasking handoff</c>
/// under the same <c>tasking</c> group.
///
/// Network-mutation invariance: this command's hot path contains no
/// <c>Process.Start</c>, no shell-out to <c>gh</c>, and no provider launcher.
/// The associated tests validate the no-provider-launch invariant via the
/// <see cref="NestedProviderLauncher"/> sentinel and a source-scan assertion.
///
/// Strict no-partial-output: on missing input file, malformed JSON, or a
/// handoff whose <c>tasking_handoff_status</c> is not <c>"local_only"</c>,
/// the command exits 1 and does NOT write the output artifact.
/// </summary>
internal static class TaskingTaskPacketCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    private static readonly JsonSerializerOptions ArtifactSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Test seam mirroring G190 <c>TaskingHandoffCommand.TimestampFactory</c>.
    /// </summary>
    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// Test seam mirroring G187 <c>SafetyNestedProviderHandoffCommand.NestedProviderLauncher</c>
    /// and G190 <c>TaskingHandoffCommand.NestedProviderLauncher</c>. G191 must
    /// NEVER invoke this delegate. Tests register a sentinel that flips a flag
    /// if invoked; the task-packet path leaves it untouched.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var fromHandoff, out var outPath, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var resolvedFromHandoff = Path.GetFullPath(fromHandoff);
        if (!File.Exists(resolvedFromHandoff))
        {
            writer.WriteLine($"--from-handoff path does not exist: {fromHandoff}");
            return 1;
        }

        byte[] handoffBytes;
        try
        {
            handoffBytes = File.ReadAllBytes(resolvedFromHandoff);
        }
        catch (Exception exception)
        {
            writer.WriteLine($"Failed to read --from-handoff: {exception.Message}");
            return 1;
        }

        TaskingHandoffPacket? sourceHandoff;
        try
        {
            sourceHandoff = JsonSerializer.Deserialize<TaskingHandoffPacket>(handoffBytes);
        }
        catch (JsonException exception)
        {
            writer.WriteLine($"Failed to parse --from-handoff as a G190 tasking handoff packet: {exception.Message}");
            return 1;
        }

        if (sourceHandoff is null)
        {
            writer.WriteLine("--from-handoff parsed to a null packet; expected a G190 tasking handoff JSON object.");
            return 1;
        }

        if (!string.Equals(
                sourceHandoff.TaskingHandoffStatus,
                TaskingHandoffConstants.LocalOnlyStatus,
                StringComparison.Ordinal))
        {
            writer.WriteLine(
                $"--from-handoff tasking_handoff_status must be '{TaskingHandoffConstants.LocalOnlyStatus}' "
                + $"(got '{sourceHandoff.TaskingHandoffStatus}'). Refusing to derive a task packet from a non-local handoff.");
            return 1;
        }

        // Reuse IssuePrepareCommand.ComputeSha256Hex for traceability hashing.
        var sourceSha256 = IssuePrepareCommand.ComputeSha256Hex(handoffBytes);

        // Reuse IssuePrepareCommand.FormatUtcTimestamp for ISO8601 UTC formatting.
        var generatedAt = IssuePrepareCommand.FormatUtcTimestamp(TimestampFactory());

        var resolvedOutPath = Path.GetFullPath(outPath);
        var artifactDirectory = Path.GetDirectoryName(resolvedOutPath);
        if (!string.IsNullOrEmpty(artifactDirectory))
        {
            Directory.CreateDirectory(artifactDirectory);
        }

        var packet = TaskingTaskPacketAnalyzer.Build(
            sourceHandoff,
            fromHandoff,
            sourceSha256,
            generatedAt,
            resolvedOutPath);

        var serialized = JsonSerializer.Serialize(packet, ArtifactSerializerOptions);
        File.WriteAllText(resolvedOutPath, serialized);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(serialized);
        }
        else
        {
            WriteTextSummary(writer, packet);
        }

        return 0;
    }

    private static void WriteTextSummary(TextWriter writer, TaskingTaskPacketArtifact packet)
    {
        writer.WriteLine(packet.SummaryLine);
        writer.WriteLine($"artifact_path: {packet.ArtifactPath}");
        writer.WriteLine($"source_handoff_path: {packet.SourceHandoffPath}");
        writer.WriteLine($"source_handoff_sha256: {packet.SourceHandoffSha256}");
        writer.WriteLine($"domain: {packet.Domain}");
        writer.WriteLine($"is_published: {packet.IsPublished.ToString().ToLowerInvariant()}");
        writer.WriteLine(
            $"is_automation_visible: {packet.IsAutomationVisible.ToString().ToLowerInvariant()}");
        writer.WriteLine($"task_packet_status: {packet.TaskPacketStatus}");
        writer.WriteLine(
            $"embedded_status_brief: {(packet.EmbeddedStatusBrief is null ? "(null)" : "embedded")}");
        writer.WriteLine(
            $"embedded_context_collect: {(packet.EmbeddedContextCollect is null ? "(null)" : "embedded")}");
        writer.WriteLine(
            $"embedded_next_slice_classify: {(packet.EmbeddedNextSliceClassify is null ? "(null)" : packet.EmbeddedNextSliceClassify.Classification)}");
        writer.WriteLine($"recommended_worker_action: {packet.RecommendedWorkerAction}");
        writer.WriteLine($"generated_at_utc: {packet.GeneratedAtUtc}");
    }

    private static bool TryParseArguments(
        string[] args,
        out string fromHandoff,
        out string outPath,
        out string format,
        out string error)
    {
        fromHandoff = string.Empty;
        outPath = string.Empty;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--from-handoff":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--from-handoff requires a non-empty value.";
                        return false;
                    }

                    fromHandoff = args[index + 1];
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
                    error = $"Unknown argument '{argument}'. Supported: --from-handoff, --out, --format.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(fromHandoff))
        {
            error = "--from-handoff is required.";
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
