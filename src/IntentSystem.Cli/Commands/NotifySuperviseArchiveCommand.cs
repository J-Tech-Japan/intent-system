using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class NotifySuperviseArchiveCommand
{
    public const string Operation = "archive";
    public const int DefaultLiveWindowDays = NotifySupervisionStore.DefaultLiveWindowDays;
    public const string Usage =
        "Usage: intent-cli notify supervise archive --domain <d> --team <t> "
        + "[--live-window-days <days>] [--dry-run|--write] [--format markdown|json]";

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

        var state = NotifySupervisionStore.Read(artifactRoot, options.Domain, options.Team);
        var supervisorWriter = state.LastCycle?.Writer;
        var supervisorState = ResolveSupervisorState(state, supervisorWriter);
        var occurredAt = (NotifyCommand.UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var result = NotifySupervisionStore.Archive(
            artifactRoot,
            options.Domain,
            options.Team,
            options.Write,
            occurredAt,
            options.LiveWindowDays);

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
        writer.WriteLine($"supervise-archive-failed: {error}");
    }

    private static void Emit(
        TextWriter writer,
        ArchiveOptions options,
        NotifySupervisionArchiveResult result,
        string supervisorState,
        NotifySupervisionWriterIdentity? supervisorWriter)
    {
        var payload = new
        {
            operation = "supervise-archive",
            domain = options.Domain,
            team = options.Team,
            command_mode = options.Write ? "write" : "dry-run",
            applied = result.Applied,
            would_change = result.WouldChange,
            live_safe = true,
            live_window_days = result.LiveWindowDays,
            live_window_default_days = DefaultLiveWindowDays,
            cutoff = result.Cutoff,
            supervisor_state = supervisorState,
            supervisor_writer = supervisorWriter,
            directory = result.Directory,
            live_file = result.LivePath,
            archive_directory = result.ArchiveDirectory,
            before_live_bytes = result.BeforeLiveBytes,
            after_live_bytes = result.AfterLiveBytes,
            before_live_record_count = result.BeforeLiveRecordCount,
            after_live_record_count = result.AfterLiveRecordCount,
            records_moved = result.RecordsMoved,
            records_retained = result.RecordsRetained,
            records_discarded = result.RecordsDiscarded,
            archive_files = result.Archives.Select(archive => new
            {
                period = archive.Period,
                path = archive.Path,
                before_bytes = archive.BeforeBytes,
                after_bytes = archive.AfterBytes,
                before_record_count = archive.BeforeRecordCount,
                after_record_count = archive.AfterRecordCount,
                moved_record_count = archive.MovedRecordCount,
            }).ToArray(),
            safety = "The archive move takes the same directory lock as appenders, publishes archive files before the live replacement, and journals every replacement. A concurrent append waits and then lands in the live file exactly once; no supervisor is stopped and no record is discarded by default.",
            distinction = "archive bounds the live file by moving older records to period-named files; shrink compacts stalls and cycles in place and reports archived 0, discarded 0, rotated 0.",
            error = result.Error,
            summary = BuildSummary(result, supervisorState, options.Write),
        };

        if (string.Equals(options.Format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
            return;
        }

        writer.WriteLine("# notify supervise archive");
        writer.WriteLine();
        writer.WriteLine($"- command mode: {(options.Write ? "write" : "dry-run")}");
        writer.WriteLine($"- supervisor state: {supervisorState}");
        writer.WriteLine($"- live window: {result.LiveWindowDays} days; cutoff {result.Cutoff:O}");
        writer.WriteLine($"- live file: {result.BeforeLiveBytes} → {result.AfterLiveBytes} bytes; records {result.BeforeLiveRecordCount} → {result.AfterLiveRecordCount}");
        writer.WriteLine($"- records moved: {result.RecordsMoved}; retained: {result.RecordsRetained}; discarded: {result.RecordsDiscarded}");
        foreach (var archive in result.Archives)
        {
            writer.WriteLine($"- archive {archive.Period}: {archive.Path}; records {archive.BeforeRecordCount} → {archive.AfterRecordCount}; bytes {archive.BeforeBytes} → {archive.AfterBytes}");
        }
        writer.WriteLine($"- summary: {BuildSummary(result, supervisorState, options.Write)}");
        if (result.Error is not null)
        {
            writer.WriteLine($"- error: {result.Error}");
        }
    }

    private static string BuildSummary(
        NotifySupervisionArchiveResult result,
        string supervisorState,
        bool write) => result.Error is not null
        ? $"Supervision archive failed while the observed supervisor state was '{supervisorState}': {result.Error}"
        : write
            ? $"Moved {result.RecordsMoved} older records to period-addressable archives and retained {result.RecordsRetained} records in the {result.LiveWindowDays}-day live window; discarded 0 records."
            : $"Dry-run would move {result.RecordsMoved} older records to period-addressable archives and retain {result.RecordsRetained} records; no files were changed.";

    private static bool TryParse(string[] args, out ArchiveOptions options, out string error)
    {
        string? domain = null;
        string? team = null;
        var liveWindowDays = DefaultLiveWindowDays;
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
                case "--live-window-days":
                    if (!ReadValue(args, ref index, "--live-window-days", out var rawDays, out error))
                    {
                        return Fail(out options);
                    }
                    if (!int.TryParse(rawDays, NumberStyles.None, CultureInfo.InvariantCulture, out liveWindowDays)
                        || liveWindowDays <= 0
                        || liveWindowDays > 3650)
                    {
                        error = "--live-window-days must be an integer from 1 through 3650.";
                        return Fail(out options);
                    }
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

        options = new ArchiveOptions(domain!, team!, liveWindowDays, write, format!);
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

    private static bool Fail(out ArchiveOptions options)
    {
        options = null!;
        return false;
    }

    private sealed record ArchiveOptions(
        string Domain,
        string Team,
        int LiveWindowDays,
        bool Write,
        string Format);
}
