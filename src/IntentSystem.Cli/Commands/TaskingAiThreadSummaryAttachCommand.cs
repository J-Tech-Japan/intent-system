using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G201: <c>intent-cli tasking ai-thread-summary-attach</c>. A LOCAL operator
/// command that binds an operator-authored AI-thread session summary text file
/// to one of the existing local tasking chain artifacts (G190 handoff packet,
/// G191 task packet, G192 preview, G193 checklist, or G194 handoff bundle) by
/// writing a deterministic provenance-only attachment artifact.
///
/// The command is artifact-only and operator-input driven:
/// <list type="bullet">
/// <item><description>It NEVER launches Codex, Claude, workers, or any
/// provider — the summary text is operator-authored input, not generated.</description></item>
/// <item><description>It NEVER mutates GitHub (no labels, no issues, no PRs).</description></item>
/// <item><description>It NEVER touches <c>.intent-cli/queue-state.json</c> or
/// <c>.intent-cli/runs.jsonl</c>.</description></item>
/// <item><description>It NEVER creates branches or worktrees.</description></item>
/// <item><description>It NEVER overwrites paths other than <c>--out</c>.</description></item>
/// <item><description>It records PROVENANCE — paths and digests — never the
/// actual summary text in the attachment artifact.</description></item>
/// </list>
/// </summary>
internal static class TaskingAiThreadSummaryAttachCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    private const string StatusOk = "ok";
    private const string StatusMissingArtifact = "missing_artifact";
    private const string StatusMalformedSourceArtifact = "malformed_source_artifact";
    private const string StatusMissingSummary = "missing_summary";
    private const string StatusBlankSummary = "blank_summary";

    private static readonly JsonSerializerOptions ArtifactSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    private static readonly JsonSerializerOptions PermissiveDeserializeOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = false
    };

    /// <summary>
    /// Test seam mirroring sibling tasking command timestamp factories.
    /// </summary>
    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// Test seam mirroring sibling tasking command no-provider sentinels.
    /// G201 must NEVER invoke this delegate. Tests register a sentinel that
    /// flips a flag if invoked; the attach path leaves it untouched.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(
                args,
                out var fromArtifact,
                out var fromSummary,
                out var outPath,
                out var format,
                out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var resolvedFromArtifact = Path.GetFullPath(fromArtifact);
        if (!File.Exists(resolvedFromArtifact))
        {
            EmitErrorStatus(
                writer,
                StatusMissingArtifact,
                $"--from-artifact path does not exist: {fromArtifact}");
            return 1;
        }

        byte[] artifactBytes;
        try
        {
            artifactBytes = File.ReadAllBytes(resolvedFromArtifact);
        }
        catch (Exception exception)
        {
            EmitErrorStatus(
                writer,
                StatusMissingArtifact,
                $"Failed to read --from-artifact: {exception.Message}");
            return 1;
        }

        if (!TryDeserializeAnyKnownKind(
                artifactBytes,
                out var sourceKind,
                out var sourceDomain,
                out var parseFailureSummary))
        {
            EmitErrorStatus(
                writer,
                StatusMalformedSourceArtifact,
                $"Failed to parse --from-artifact as any known tasking chain artifact "
                + $"(handoff bundle / handoff packet / task packet / preview / checklist) — "
                + $"parse failure: {parseFailureSummary}");
            return 1;
        }

        var resolvedFromSummary = Path.GetFullPath(fromSummary);
        if (!File.Exists(resolvedFromSummary))
        {
            EmitErrorStatus(
                writer,
                StatusMissingSummary,
                $"--from-summary path does not exist: {fromSummary}");
            return 1;
        }

        byte[] summaryBytes;
        try
        {
            summaryBytes = File.ReadAllBytes(resolvedFromSummary);
        }
        catch (Exception exception)
        {
            EmitErrorStatus(
                writer,
                StatusMissingSummary,
                $"Failed to read --from-summary: {exception.Message}");
            return 1;
        }

        var summaryText = System.Text.Encoding.UTF8.GetString(summaryBytes);
        if (string.IsNullOrWhiteSpace(summaryText))
        {
            EmitErrorStatus(
                writer,
                StatusBlankSummary,
                $"--from-summary file contains empty content (blank or whitespace-only): {fromSummary}");
            return 1;
        }

        // Reuse IssuePrepareCommand.ComputeSha256Hex for both digest fields.
        var sourceArtifactSha256 = IssuePrepareCommand.ComputeSha256Hex(artifactBytes);
        var sourceSummarySha256 = IssuePrepareCommand.ComputeSha256Hex(summaryBytes);

        // Reuse IssuePrepareCommand.FormatUtcTimestamp for ISO8601 UTC formatting.
        var generatedAt = IssuePrepareCommand.FormatUtcTimestamp(TimestampFactory());

        var resolvedOutPath = Path.GetFullPath(outPath);
        var artifactDirectory = Path.GetDirectoryName(resolvedOutPath);
        if (!string.IsNullOrEmpty(artifactDirectory))
        {
            Directory.CreateDirectory(artifactDirectory);
        }

        var attachment = TaskingAiThreadSummaryAttachAnalyzer.Build(
            fromArtifact,
            sourceArtifactSha256,
            sourceKind,
            fromSummary,
            sourceSummarySha256,
            summaryBytes.Length,
            sourceDomain,
            generatedAt,
            resolvedOutPath);

        var serialized = JsonSerializer.Serialize(attachment, ArtifactSerializerOptions);
        File.WriteAllText(resolvedOutPath, serialized);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(serialized);
        }
        else
        {
            WriteTextSummary(writer, attachment);
        }

        return 0;
    }

    private static void EmitErrorStatus(TextWriter writer, string status, string errorMessage)
    {
        var payload = new
        {
            status,
            errors = new[] { errorMessage }
        };

        writer.WriteLine(JsonSerializer.Serialize(payload, ArtifactSerializerOptions));
    }

    private static bool TryDeserializeAnyKnownKind(
        byte[] bytes,
        out string kind,
        out string domain,
        out string parseFailureSummary)
    {
        kind = string.Empty;
        domain = string.Empty;
        var failures = new List<string>();

        if (TryDeserialize<TaskingHandoffBundleArtifact>(bytes, out var bundle, out var bundleError)
            && bundle is not null
            && !string.IsNullOrWhiteSpace(bundle.Domain))
        {
            kind = TaskingAiThreadSummaryAttachConstants.SourceArtifactKinds.HandoffBundle;
            domain = bundle.Domain;
            parseFailureSummary = string.Empty;
            return true;
        }
        if (bundleError is not null)
        {
            failures.Add($"handoff_bundle: {bundleError}");
        }

        if (TryDeserialize<TaskingHandoffPacket>(bytes, out var handoff, out var handoffError)
            && handoff is not null
            && !string.IsNullOrWhiteSpace(handoff.Domain))
        {
            kind = TaskingAiThreadSummaryAttachConstants.SourceArtifactKinds.HandoffPacket;
            domain = handoff.Domain;
            parseFailureSummary = string.Empty;
            return true;
        }
        if (handoffError is not null)
        {
            failures.Add($"handoff_packet: {handoffError}");
        }

        if (TryDeserialize<TaskingTaskPacketArtifact>(bytes, out var taskPacket, out var taskError)
            && taskPacket is not null
            && !string.IsNullOrWhiteSpace(taskPacket.Domain))
        {
            kind = TaskingAiThreadSummaryAttachConstants.SourceArtifactKinds.TaskPacket;
            domain = taskPacket.Domain;
            parseFailureSummary = string.Empty;
            return true;
        }
        if (taskError is not null)
        {
            failures.Add($"task_packet: {taskError}");
        }

        if (TryDeserialize<TaskingTaskPacketPreviewArtifact>(bytes, out var preview, out var previewError)
            && preview is not null
            && !string.IsNullOrWhiteSpace(preview.Domain))
        {
            kind = TaskingAiThreadSummaryAttachConstants.SourceArtifactKinds.TaskPacketPreview;
            domain = preview.Domain;
            parseFailureSummary = string.Empty;
            return true;
        }
        if (previewError is not null)
        {
            failures.Add($"task_packet_preview: {previewError}");
        }

        if (TryDeserialize<TaskingTaskPacketChecklistArtifact>(bytes, out var checklist, out var checklistError)
            && checklist is not null
            && !string.IsNullOrWhiteSpace(checklist.Domain))
        {
            kind = TaskingAiThreadSummaryAttachConstants.SourceArtifactKinds.TaskPacketChecklist;
            domain = checklist.Domain;
            parseFailureSummary = string.Empty;
            return true;
        }
        if (checklistError is not null)
        {
            failures.Add($"task_packet_checklist: {checklistError}");
        }

        parseFailureSummary = failures.Count == 0
            ? "no candidate kind matched"
            : string.Join("; ", failures);
        return false;
    }

    private static bool TryDeserialize<T>(byte[] bytes, out T? value, out string? errorMessage)
        where T : class
    {
        try
        {
            value = JsonSerializer.Deserialize<T>(bytes, PermissiveDeserializeOptions);
            errorMessage = value is null ? "deserialized to null" : null;
            return value is not null;
        }
        catch (JsonException exception)
        {
            value = null;
            errorMessage = exception.Message;
            return false;
        }
        catch (NotSupportedException exception)
        {
            // Triggered when System.Text.Json sees a missing required property.
            value = null;
            errorMessage = exception.Message;
            return false;
        }
        catch (InvalidOperationException exception)
        {
            // Triggered when a required init-only property is missing from the source JSON.
            value = null;
            errorMessage = exception.Message;
            return false;
        }
    }

    private static void WriteTextSummary(TextWriter writer, TaskingAiThreadSummaryAttachArtifact attachment)
    {
        writer.WriteLine(attachment.SummaryLine);
        writer.WriteLine($"artifact_path: {attachment.ArtifactPath}");
        writer.WriteLine($"source_artifact_path: {attachment.SourceArtifactPath}");
        writer.WriteLine($"source_artifact_sha256: {attachment.SourceArtifactSha256}");
        writer.WriteLine($"source_artifact_kind: {attachment.SourceArtifactKind}");
        writer.WriteLine($"source_summary_path: {attachment.SourceSummaryPath}");
        writer.WriteLine($"source_summary_sha256: {attachment.SourceSummarySha256}");
        writer.WriteLine($"source_summary_byte_count: {attachment.SourceSummaryByteCount}");
        writer.WriteLine($"domain: {attachment.Domain}");
        writer.WriteLine($"is_published: {attachment.IsPublished.ToString().ToLowerInvariant()}");
        writer.WriteLine(
            $"is_automation_visible: {attachment.IsAutomationVisible.ToString().ToLowerInvariant()}");
        writer.WriteLine($"attachment_status: {attachment.AttachmentStatus}");
        writer.WriteLine($"generated_at_utc: {attachment.GeneratedAtUtc}");
        writer.WriteLine($"status: {StatusOk}");
    }

    private static bool TryParseArguments(
        string[] args,
        out string fromArtifact,
        out string fromSummary,
        out string outPath,
        out string format,
        out string error)
    {
        fromArtifact = string.Empty;
        fromSummary = string.Empty;
        outPath = string.Empty;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--from-artifact":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--from-artifact requires a non-empty value.";
                        return false;
                    }

                    fromArtifact = args[index + 1];
                    index++;
                    break;

                case "--from-summary":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--from-summary requires a non-empty value.";
                        return false;
                    }

                    fromSummary = args[index + 1];
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
                    error =
                        $"Unknown argument '{argument}'. Supported: --from-artifact, --from-summary, --out, --format.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(fromArtifact))
        {
            error = "--from-artifact is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(fromSummary))
        {
            error = "--from-summary is required.";
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
