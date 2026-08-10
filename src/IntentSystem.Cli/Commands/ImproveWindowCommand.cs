using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>G662: explicitly declare the per-domain realignment recency window.</summary>
internal static class ImproveWindowCommand
{
    private const string UsageLine =
        "Usage: intent-cli improve window --domain <name> --days <n> [--write] [--format markdown|json]";

    internal static Func<DateTimeOffset> UtcNowFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        string? domain = null;
        var days = 0;
        var write = false;
        var format = "markdown";
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--help":
                    writer.WriteLine("improve window (G662 — preview-through-1.x)");
                    writer.WriteLine(UsageLine);
                    writer.WriteLine("Declare the recency window independently of improve runs; this records no schedule and executes nothing.");
                    return 0;
                case "--domain" when index + 1 < args.Length:
                    domain = args[++index].Trim();
                    break;
                case "--days" when index + 1 < args.Length
                    && int.TryParse(args[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                    && parsed > 0:
                    days = parsed;
                    index++;
                    break;
                case "--write":
                    write = true;
                    break;
                case "--format" when index + 1 < args.Length && args[index + 1] is "json" or "markdown":
                    format = args[++index];
                    break;
                default:
                    writer.WriteLine($"Invalid argument '{args[index]}'.");
                    writer.WriteLine(UsageLine);
                    return 1;
            }
        }

        if (string.IsNullOrWhiteSpace(domain) || days <= 0)
        {
            writer.WriteLine("--domain and a positive --days value are required.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        var record = new ImproveWindowRecord
        {
            Domain = domain,
            WindowDays = days,
            RecordedAt = UtcNowFactory().ToUniversalTime(),
        };
        var persisted = ImproveRealignmentWindowStore.Write(context.ResolveArtifactRootPath(), record, write);
        var result = new ImproveWindowResult
        {
            Operation = "improve-realignment-window",
            PreviewStatus = "preview-through-1.x",
            CommandMode = write ? "write" : "dry-run",
            Applied = persisted.Applied,
            RecordPath = persisted.Path,
            Record = record,
            Semantics = "Declared recency bound only. This creates no scheduler, cron, auto-run, or stalled-work debt class.",
            Error = persisted.Error,
        };
        if (format == "json")
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            writer.WriteLine("# Improve realignment window (G662 — preview-through-1.x)");
            writer.WriteLine($"- command mode: {result.CommandMode}");
            writer.WriteLine($"- applied: {(result.Applied ? "yes" : "no")}");
            writer.WriteLine($"- record path: {result.RecordPath}");
            writer.WriteLine($"- domain: {record.Domain}");
            writer.WriteLine($"- window days: {record.WindowDays}");
            writer.WriteLine($"- recorded at: {record.RecordedAt:O}");
            writer.WriteLine($"- semantics: {result.Semantics}");
        }
        return persisted.Error is null ? 0 : 1;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed record ImproveWindowResult
{
    public required string Operation { get; init; }
    public required string PreviewStatus { get; init; }
    public required string CommandMode { get; init; }
    public required bool Applied { get; init; }
    public required string RecordPath { get; init; }
    public required ImproveWindowRecord Record { get; init; }
    public required string Semantics { get; init; }
    public string? Error { get; init; }
}
