using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G734: compact supervision state through the same directory lock used by
/// appenders, then replace each affected JSONL file atomically. The command is
/// safe to run against a live supervisor: an append waits for the bounded
/// compaction critical section and then targets the replacement path.
/// </summary>
internal static class NotifySuperviseShrinkCommand
{
    public const string Operation = "shrink";
    public const string Usage =
        "Usage: intent-cli notify supervise shrink --domain <d> --team <t> "
        + "[--dry-run|--write] [--format markdown|json]";

    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(Usage);
            return 0;
        }

        if (!TryParse(args, out var options, out var error))
        {
            EmitFailure(writer, error);
            writer.WriteLine(Usage);
            return 1;
        }

        string artifactRoot;
        try
        {
            artifactRoot = context.ResolveSupervisionArtifactRootPath();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            EmitFailure(writer, $"supervision-artifact-root-unavailable: {exception.Message}");
            return 1;
        }

        var state = NotifySupervisionStore.Read(artifactRoot, options.Domain!, options.Team!);
        var supervisorWriter = state.LastCycle?.Writer;
        var supervisorState = ResolveSupervisorState(state, supervisorWriter);
        var result = NotifySupervisionStore.Shrink(
            artifactRoot,
            options.Domain!,
            options.Team!,
            options.Write,
            (NotifyCommand.UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            supervisorState,
            supervisorWriter);

        Emit(writer, options, result, supervisorState, supervisorWriter);
        return result.Error is null ? 0 : 1;
    }

    private static string ResolveSupervisorState(
        NotifySupervisionReadResult state,
        NotifySupervisionWriterIdentity? writer)
    {
        if (!state.Resolved)
        {
            return "unreadable";
        }

        if (writer is null)
        {
            return "unknown-no-cycle";
        }

        return writer.IsLiveOn(NotifySupervisionWriterIdentity.Current())
            ? "running"
            : "stopped";
    }

    private static void EmitFailure(TextWriter writer, string error)
    {
        writer.WriteLine($"supervise-shrink-failed: {error}");
    }

    private static void Emit(
        TextWriter writer,
        ShrinkOptions options,
        NotifySupervisionShrinkResult result,
        string supervisorState,
        NotifySupervisionWriterIdentity? supervisorWriter)
    {
        var payload = new
        {
            operation = "supervise-shrink",
            domain = options.Domain,
            team = options.Team,
            command_mode = options.Write ? "write" : "dry-run",
            applied = result.Applied,
            would_change = result.WouldChange,
            live_safe = true,
            supervisor_state = supervisorState,
            supervisor_writer = supervisorWriter,
            directory = result.Directory,
            before_bytes = result.BeforeBytes,
            after_bytes = result.AfterBytes,
            before_record_count = result.BeforeRecordCount,
            after_record_count = result.AfterRecordCount,
            before_average_bytes_per_record = result.BeforeAverageBytesPerRecord,
            after_average_bytes_per_record = result.AfterAverageBytesPerRecord,
            files = new
            {
                stalls = result.StallFile,
                cycles = result.CycleFile,
            },
            invariant_text = new
            {
                literal_bytes_removed_from_records = result.InvariantLiteralBytesRemoved,
                reference_bytes_added_to_records = result.InvariantReferenceBytesAdded,
                net_record_bytes_saved = result.InvariantBytesSavedInRecords,
                other_record_bytes_saved = result.OtherBytesSaved,
                definition_manifest = result.EvidenceDefinitionsPath,
                resolution = result.EvidenceDefinitionsPath is null
                    ? null
                    : $"Read {NotifySupervisionStore.EvidenceDefinitionsFileName} and resolve the evidence_ref '{NotifySupervisionStore.HerdrRegistrationEvidenceKey}'.",
            },
            audit = new
            {
                path = result.AuditPath,
                appended = options.Write && result.Error is null,
                record = result.Audit,
                records_archived = result.Audit?.RecordsArchived ?? 0,
                records_discarded = result.Audit?.RecordsDiscarded ?? 0,
                records_compacted = result.Audit?.RecordsCompacted ?? 0,
                records_rotated = result.Audit?.RecordsRotated ?? 0,
            },
            safety = "Appenders and shrink share a directory lock; shrink stages complete manifest/stalls/cycles replacements and durably journals their hashes before any target move. A restart verifies and completes the journal, or records an aborted outcome without overwriting an unexpected target, so a running supervisor keeps readable append-only state.",
            error = result.Error,
            summary = BuildSummary(result, supervisorState, options.Write),
        };

        if (string.Equals(options.Format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
            return;
        }

        writer.WriteLine("# notify supervise shrink");
        writer.WriteLine();
        writer.WriteLine($"- command mode: {(options.Write ? "write" : "dry-run")}");
        writer.WriteLine($"- supervisor state: {supervisorState}");
        writer.WriteLine($"- before: {result.BeforeBytes} bytes / {result.BeforeRecordCount} records (average {result.BeforeAverageBytesPerRecord?.ToString("F2") ?? "<none>"} bytes/record)");
        writer.WriteLine($"- after: {result.AfterBytes} bytes / {result.AfterRecordCount} records (average {result.AfterAverageBytesPerRecord?.ToString("F2") ?? "<none>"} bytes/record)");
        writer.WriteLine($"- stalls.jsonl: {result.StallFile.BeforeBytes} → {result.StallFile.AfterBytes} bytes; records {result.StallFile.BeforeRecords} → {result.StallFile.AfterRecords}");
        writer.WriteLine($"- cycles.jsonl: {result.CycleFile.BeforeBytes} → {result.CycleFile.AfterBytes} bytes; records {result.CycleFile.BeforeRecords} → {result.CycleFile.AfterRecords}");
        writer.WriteLine($"- invariant literal bytes removed: {result.InvariantLiteralBytesRemoved}; reference bytes added: {result.InvariantReferenceBytesAdded}; net record bytes saved: {result.InvariantBytesSavedInRecords}; other bytes saved: {result.OtherBytesSaved}");
        writer.WriteLine($"- audit: {(result.AuditPath ?? "<would append only with --write>")}");
        writer.WriteLine($"- summary: {BuildSummary(result, supervisorState, options.Write)}");
        if (result.Error is not null)
        {
            writer.WriteLine($"- error: {result.Error}");
        }
    }

    private static string BuildSummary(
        NotifySupervisionShrinkResult result,
        string supervisorState,
        bool write) => result.Error is not null
        ? $"Supervision shrink failed while the observed supervisor state was '{supervisorState}': {result.Error}"
        : write
            ? $"Compacted existing supervision state in place while supervisor state was '{supervisorState}'; retained {result.AfterRecordCount} records, archived 0, discarded 0, rotated 0, and appended a durable '{result.Audit?.Outcome ?? "completed"}' shrink audit record."
            : $"Dry-run would compact existing supervision state while the observed supervisor state is '{supervisorState}'; no files or audit records were changed."
              + " Use --write for the sanctioned lock-and-atomic-replace path.";

    private static bool TryParse(string[] args, out ShrinkOptions options, out string error)
    {
        string? domain = null;
        string? team = null;
        var write = false;
        var format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (!ReadValue(args, ref index, "--domain", out domain, out error)) return Fail(out options);
                    break;
                case "--team":
                    if (!ReadValue(args, ref index, "--team", out team, out error)) return Fail(out options);
                    break;
                case "--write": write = true; break;
                case "--dry-run": write = false; break;
                case "--format":
                    if (!ReadValue(args, ref index, "--format", out format, out error)) return Fail(out options);
                    if (format is not FormatJson and not FormatMarkdown)
                    {
                        error = "--format must be markdown or json.";
                        return Fail(out options);
                    }
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return Fail(out options);
            }
        }

        if (!IsSafeIdentity(domain) || !IsSafeIdentity(team))
        {
            error = "--domain and --team are required safe identity values.";
            return Fail(out options);
        }

        options = new ShrinkOptions(domain!, team!, write, format!);
        return true;
    }

    private static bool ReadValue(
        string[] args,
        ref int index,
        string argument,
        out string? value,
        out string error)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            value = null;
            error = $"{argument} requires a value.";
            return false;
        }

        value = args[index];
        error = string.Empty;
        return true;
    }

    private static bool IsSafeIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or ':' or '-');

    private static bool Fail(out ShrinkOptions options)
    {
        options = null!;
        return false;
    }

    private sealed record ShrinkOptions(string Domain, string Team, bool Write, string Format);
}
