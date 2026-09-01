using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G773's deliberate, evidence-preserving recovery path for JSONL records
/// which G767 readers have already established cannot be parsed.
/// </summary>
internal static class NotifySuperviseRepairUnreadableCommand
{
    public const string Operation = "repair-unreadable";
    public const string Usage =
        "Usage: intent-cli notify supervise repair-unreadable --domain <d> --team <t> "
        + "[--routing-root <host-root>] [--dry-run|--write] [--format markdown|json]";

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
            EmitFailure(writer, error, options?.Format ?? FormatMarkdown);
            writer.WriteLine(Usage);
            return 1;
        }

        string routingRoot;
        string artifactRoot;
        try
        {
            routingRoot = Path.GetFullPath(options.RoutingRoot ?? context.RepoRoot);
            artifactRoot = options.RoutingRoot is null
                ? context.ResolveSupervisionArtifactRootPath()
                : NotifySuperviseLivenessCommand.ResolveSupervisionArtifactRootPath(context, routingRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            EmitFailure(writer, $"invalid-routing-root: {exception.Message}", options.Format);
            return 1;
        }

        var result = NotifySupervisionStore.RepairUnreadable(
            artifactRoot,
            options.Domain,
            options.Team,
            options.Write,
            (NotifyCommand.UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            NotifySupervisionWriterIdentity.Current());
        Emit(writer, options, routingRoot, result);
        return result.Error is null ? 0 : 1;
    }

    private static void EmitFailure(TextWriter writer, string error, string format)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                operation = "supervise-repair-unreadable",
                success = false,
                error,
            }, JsonOptions));
            return;
        }

        writer.WriteLine($"supervise-repair-unreadable-failed: {error}");
    }

    private static void Emit(
        TextWriter writer,
        RepairUnreadableOptions options,
        string routingRoot,
        NotifySupervisionRepairUnreadableResult result)
    {
        var repairState = ResolveRepairState(options.Write, result);
        var payload = new
        {
            operation = "supervise-repair-unreadable",
            routing_root = routingRoot,
            domain = options.Domain,
            team = options.Team,
            command_mode = options.Write ? "write" : "dry-run",
            repair_state = repairState,
            applied = result.Applied,
            would_repair = result.WouldRepair,
            directory = result.Directory,
            unreadable_record_count = result.UnreadableRecords.Count,
            unreadable_records = result.UnreadableRecords,
            files = result.Files,
            audit_path = result.AuditPath,
            audit = result.Audit,
            quarantine_guarantee = "Unreadable line byte ranges are moved verbatim and in order to per-file sidecars before their live read-path bytes are replaced.",
            limitation = "The repair preserves corruption as evidence and does not reconstruct what damaged records once said.",
            error = result.Error,
            summary = BuildSummary(options.Write, result),
        };

        if (string.Equals(options.Format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
            return;
        }

        writer.WriteLine("# notify supervise repair-unreadable");
        writer.WriteLine();
        writer.WriteLine($"- command mode: {(options.Write ? "write" : "dry-run")}");
        writer.WriteLine($"- repair state: {repairState}");
        writer.WriteLine($"- directory: `{result.Directory}`");
        writer.WriteLine($"- unreadable records: {result.UnreadableRecords.Count}");
        foreach (var record in result.UnreadableRecords)
        {
            writer.WriteLine($"- unreadable record: `{record.File}` line {record.Line} ({record.Reason}; {record.ByteLength} bytes)");
        }
        foreach (var file in result.Files)
        {
            writer.WriteLine($"- file: `{file.File}` → `{file.QuarantineFile}`; bytes {file.BeforeLiveBytes} → {file.AfterLiveBytes}; quarantined {file.QuarantinedBytes}; lines {string.Join(",", file.LineNumbers)}");
        }
        if (result.AuditPath is not null)
        {
            writer.WriteLine($"- audit path: `{result.AuditPath}`");
        }
        writer.WriteLine("- guarantee: unreadable line bytes are quarantined verbatim and in order before the live read path is rewritten.");
        writer.WriteLine("- limitation: this preserves evidence and does not reconstruct damaged records.");
        writer.WriteLine($"- summary: {BuildSummary(options.Write, result)}");
        if (result.Error is not null)
        {
            writer.WriteLine($"- error: {result.Error}");
        }
    }

    private static string ResolveRepairState(bool write, NotifySupervisionRepairUnreadableResult result) =>
        result.Error is not null
            ? "failed"
            : !result.WouldRepair
                ? "nothing-to-repair"
                : write && result.Applied
                    ? "completed-repair"
                    : "would-repair";

    private static string BuildSummary(bool write, NotifySupervisionRepairUnreadableResult result) =>
        result.Error is not null
            ? $"Unreadable supervision repair failed closed: {result.Error}"
            : !result.WouldRepair
                ? "Nothing to repair: the supervision store was left byte-for-byte and identity unchanged."
                : write && result.Applied
                    ? $"Completed evidence-preserving repair for {result.UnreadableRecords.Count} unreadable record(s); live readers now consume only readable bytes."
                    : $"Dry-run would quarantine {result.UnreadableRecords.Count} unreadable record(s) verbatim and rewrite only their live files' readable bytes.";

    private static bool TryParse(string[] args, out RepairUnreadableOptions options, out string error)
    {
        string? domain = null;
        string? team = null;
        string? routingRoot = null;
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
                case "--routing-root":
                    if (!ReadValue(args, ref index, "--routing-root", out routingRoot, out error)) return Fail(out options);
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

        options = new RepairUnreadableOptions(domain!, team!, routingRoot, write, format!);
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
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-');

    private static bool Fail(out RepairUnreadableOptions options)
    {
        options = null!;
        return false;
    }

    private sealed record RepairUnreadableOptions(
        string Domain,
        string Team,
        string? RoutingRoot,
        bool Write,
        string Format);
}
