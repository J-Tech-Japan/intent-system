using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G474: <c>intent-cli packet retire</c> command. Records machine-readable
/// lifecycle retirement metadata for a packet directory
/// (<c>.intent-cli/issues/&lt;execution-unit&gt;/</c>) so absorbed / superseded
/// / retired packets are excluded from <c>intent-cli intent next-slice</c>
/// issue-cut-ready candidates without deleting the packet files (design
/// history is preserved). Writes a <c>lifecycle.yaml</c> sidecar and appends a
/// durable <c>packet-retired</c> run event. Idempotent: re-running with the
/// same arguments does not rewrite the sidecar or duplicate run events. Never
/// launches an AI provider.
/// </summary>
internal static class PacketRetireCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string ModeWrite = "write";
    private const string ModeDryRun = "dry-run";

    private const string StatusRetired = "retired";
    private const string StatusAlreadyRetired = "already-retired";
    private const string StatusPlanned = "planned";

    private const string RetireActor = "intent-cli";
    private const string RunEventName = "packet-retired";

    private const string UsageLine =
        "Usage: intent-cli packet retire --execution-unit <id> (--absorbed-by <unit> | --superseded-by <unit> | --retired) --reason <text> [--write] [--format markdown|json]";

    private static readonly Regex ExecutionUnitPattern = new(
        @"^[A-Za-z][A-Za-z0-9-]*$",
        RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Overridable timestamp source for deterministic tests.
    /// </summary>
    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseArguments(
            args,
            out var executionUnit,
            out var absorbedBy,
            out var supersededBy,
            out var retired,
            out var reason,
            out var write,
            out var format,
            out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (!ExecutionUnitPattern.IsMatch(executionUnit!))
        {
            writer.WriteLine($"Invalid execution-unit id '{executionUnit}'. Expected an alphanumeric token like 'G344'.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        var packetDirectory = Path.Combine(context.RepoRoot, ".intent-cli", "issues", executionUnit!);
        if (!Directory.Exists(packetDirectory))
        {
            writer.WriteLine($"Packet directory not found for execution-unit '{executionUnit}' at {packetDirectory}.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        var lifecycle = absorbedBy is not null
            ? PacketLifecycle.LifecycleAbsorbed
            : supersededBy is not null
                ? PacketLifecycle.LifecycleSuperseded
                : PacketLifecycle.LifecycleRetired;

        var existing = PacketLifecycle.TryRead(packetDirectory);
        var alreadyRetired = existing is not null
            && string.Equals(existing.Lifecycle, lifecycle, StringComparison.Ordinal)
            && string.Equals(existing.AbsorbedBy, absorbedBy, StringComparison.Ordinal)
            && string.Equals(existing.SupersededBy, supersededBy, StringComparison.Ordinal)
            && string.Equals(existing.RetiredReason, reason, StringComparison.Ordinal);

        var sidecarPath = PacketLifecycle.SidecarPath(packetDirectory);
        string mode;
        string status;
        string? retiredAt = existing?.RetiredAt;

        if (alreadyRetired)
        {
            // Idempotent: same retirement already recorded. Do not rewrite the
            // sidecar or append a duplicate run event.
            mode = write ? ModeWrite : ModeDryRun;
            status = StatusAlreadyRetired;
        }
        else if (write)
        {
            retiredAt = TimestampFactory().ToString("O");
            var metadata = new PacketLifecycleMetadata
            {
                Lifecycle = lifecycle,
                AbsorbedBy = absorbedBy,
                SupersededBy = supersededBy,
                RetiredReason = reason,
                RetiredAt = retiredAt
            };

            File.WriteAllText(sidecarPath, PacketLifecycle.Render(metadata));
            AppendRunEvent(
                context,
                new RunEvent
                {
                    Ts = TimestampFactory(),
                    ExecutionUnit = executionUnit!,
                    Event = RunEventName,
                    By = RetireActor,
                    Reason = reason,
                    PacketRef = sidecarPath
                });

            mode = ModeWrite;
            status = StatusRetired;
        }
        else
        {
            mode = ModeDryRun;
            status = StatusPlanned;
        }

        var result = new PacketRetireResult
        {
            ExecutionUnit = executionUnit!,
            PacketDirectory = packetDirectory,
            SidecarPath = sidecarPath,
            Mode = mode,
            Status = status,
            Lifecycle = lifecycle,
            AbsorbedBy = absorbedBy,
            SupersededBy = supersededBy,
            RetiredReason = reason,
            RetiredAt = retiredAt
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        return 0;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? executionUnit,
        out string? absorbedBy,
        out string? supersededBy,
        out bool retired,
        out string? reason,
        out bool write,
        out string format,
        out string error)
    {
        executionUnit = null;
        absorbedBy = null;
        supersededBy = null;
        retired = false;
        reason = null;
        write = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--execution-unit":
                    if (!TryTakeValue(args, ref index, out executionUnit))
                    {
                        error = "--execution-unit requires a value.";
                        return false;
                    }

                    break;
                case "--absorbed-by":
                    if (!TryTakeValue(args, ref index, out absorbedBy))
                    {
                        error = "--absorbed-by requires a value.";
                        return false;
                    }

                    break;
                case "--superseded-by":
                    if (!TryTakeValue(args, ref index, out supersededBy))
                    {
                        error = "--superseded-by requires a value.";
                        return false;
                    }

                    break;
                case "--retired":
                    retired = true;
                    break;
                case "--reason":
                    if (!TryTakeValue(args, ref index, out reason))
                    {
                        error = "--reason requires a value.";
                        return false;
                    }

                    break;
                case "--write":
                    write = true;
                    break;
                case "--format":
                    if (index + 1 >= args.Length)
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }

                    format = args[index + 1];
                    index++;
                    if (!string.Equals(format, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(format, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"Unsupported format '{format}'. Expected 'markdown' or 'json'.";
                        return false;
                    }

                    break;
                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            error = "--execution-unit is required.";
            return false;
        }

        var relationshipCount = (absorbedBy is not null ? 1 : 0)
            + (supersededBy is not null ? 1 : 0)
            + (retired ? 1 : 0);
        if (relationshipCount == 0)
        {
            error = "Exactly one of --absorbed-by, --superseded-by, or --retired is required.";
            return false;
        }

        if (relationshipCount > 1)
        {
            error = "--absorbed-by, --superseded-by, and --retired are mutually exclusive.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            error = "--reason is required; retirement must record a deterministic reason.";
            return false;
        }

        return true;
    }

    private static bool TryTakeValue(string[] args, ref int index, out string? value)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            value = null;
            return false;
        }

        value = args[index + 1];
        index++;
        return true;
    }

    private static void AppendRunEvent(CliContext context, RunEvent runEvent)
    {
        var runLogPath = context.GetRunLogPath();
        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");

        Directory.CreateDirectory(runLogDirectory);
        File.AppendAllText(runLogPath, RunLogSerializer.SerializeLine(runEvent) + Environment.NewLine);
    }

    private static void WriteMarkdown(TextWriter writer, PacketRetireResult result)
    {
        writer.WriteLine($"# packet retire — {result.ExecutionUnit}");
        writer.WriteLine();
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- status: {result.Status}");
        writer.WriteLine($"- lifecycle: {result.Lifecycle}");
        if (result.AbsorbedBy is not null)
        {
            writer.WriteLine($"- absorbed_by: {result.AbsorbedBy}");
        }

        if (result.SupersededBy is not null)
        {
            writer.WriteLine($"- superseded_by: {result.SupersededBy}");
        }

        writer.WriteLine($"- reason: {result.RetiredReason}");
        if (result.RetiredAt is not null)
        {
            writer.WriteLine($"- retired_at: {result.RetiredAt}");
        }

        writer.WriteLine($"- sidecar: {result.SidecarPath}");
        writer.WriteLine();
        if (result.Status == StatusPlanned)
        {
            writer.WriteLine("Dry-run: re-run with --write to record the retirement and preserve packet files.");
        }
        else if (result.Status == StatusAlreadyRetired)
        {
            writer.WriteLine("Already retired with the same metadata; no changes written (idempotent).");
        }
        else
        {
            writer.WriteLine("Packet files preserved; excluded from next-slice issue-cut-ready candidates.");
        }
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("intent-cli packet retire — record machine-readable packet lifecycle retirement (G474).");
        writer.WriteLine();
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("Marks an absorbed / superseded / retired packet so next-slice excludes it");
        writer.WriteLine("from issue-cut-ready candidates, while preserving the packet files in git history.");
        writer.WriteLine("Writes a lifecycle.yaml sidecar and appends a packet-retired run event. Idempotent.");
    }

    internal sealed record PacketRetireResult
    {
        [JsonPropertyName("execution_unit")]
        public required string ExecutionUnit { get; init; }

        [JsonPropertyName("packet_directory")]
        public required string PacketDirectory { get; init; }

        [JsonPropertyName("sidecar_path")]
        public required string SidecarPath { get; init; }

        [JsonPropertyName("mode")]
        public required string Mode { get; init; }

        [JsonPropertyName("status")]
        public required string Status { get; init; }

        [JsonPropertyName("lifecycle")]
        public required string Lifecycle { get; init; }

        [JsonPropertyName("absorbed_by")]
        public string? AbsorbedBy { get; init; }

        [JsonPropertyName("superseded_by")]
        public string? SupersededBy { get; init; }

        [JsonPropertyName("retired_reason")]
        public string? RetiredReason { get; init; }

        [JsonPropertyName("retired_at")]
        public string? RetiredAt { get; init; }
    }
}
