using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G662: explicit durable record step after the human/agent has performed an
/// improve realignment review. This command records observable facts only and
/// never evaluates whether the review was good, complete, or correct.
/// </summary>
internal static class ImproveRunRecordCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";
    private const string UsageLine =
        "Usage: intent-cli improve record --domain <name> --mode implementation-aware|light --artifact <path> [--artifact <path> ...] [--write] [--format markdown|json]";

    internal static Func<DateTimeOffset> UtcNowFactory { get; set; } = () => DateTimeOffset.UtcNow;

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

        if (!TryParseArguments(args, out var domain, out var mode, out var artifacts, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var record = new ImproveRunRecord
        {
            Domain = domain!,
            Mode = mode!,
            RecordedAt = UtcNowFactory().ToUniversalTime(),
            TouchedArtifacts = artifacts,
        };
        var writeResult = ImproveRunStore.Append(context.ResolveArtifactRootPath(), record, write);
        var result = new ImproveRunRecordResult
        {
            Operation = "improve-run-record",
            PreviewStatus = "preview-through-1.x",
            CommandMode = write ? "write" : "dry-run",
            Applied = writeResult.Applied,
            RecordPath = writeResult.Path,
            Record = record,
            QualityAssessed = false,
            Semantics = "Recency evidence only. The realignment review is human/agent work; intent-cli records that it ran and never grades its quality.",
            Error = writeResult.Error,
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

        return writeResult.Error is null ? 0 : 1;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? domain,
        out string? mode,
        out IReadOnlyList<string> artifacts,
        out bool write,
        out string format,
        out string error)
    {
        domain = null;
        mode = null;
        var artifactList = new List<string>();
        write = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (!TryTakeValue(args, ref index, out domain))
                    {
                        error = "--domain requires a value.";
                        artifacts = [];
                        return false;
                    }
                    break;
                case "--mode":
                    if (!TryTakeValue(args, ref index, out mode))
                    {
                        error = "--mode requires a value.";
                        artifacts = [];
                        return false;
                    }
                    break;
                case "--artifact":
                    if (!TryTakeValue(args, ref index, out var artifact))
                    {
                        error = "--artifact requires a value.";
                        artifacts = [];
                        return false;
                    }
                    artifactList.Add(artifact!);
                    break;
                case "--write":
                    write = true;
                    break;
                case "--format":
                    if (!TryTakeValue(args, ref index, out var requestedFormat)
                        || requestedFormat is not (FormatJson or FormatMarkdown))
                    {
                        error = "--format must be 'markdown' or 'json'.";
                        artifacts = [];
                        return false;
                    }
                    format = requestedFormat!;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    artifacts = [];
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            error = "--domain is required.";
            artifacts = [];
            return false;
        }
        if (mode is not (GuideImproveCommand.ModeImplementationAware or GuideImproveCommand.ModeLight))
        {
            error = "--mode must be 'implementation-aware' or 'light'.";
            artifacts = [];
            return false;
        }
        artifacts = artifactList
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (artifacts.Count == 0)
        {
            error = "At least one --artifact <path> is required; record the artifacts the review actually touched.";
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
        value = args[++index].Trim();
        return true;
    }

    private static void WriteMarkdown(TextWriter writer, ImproveRunRecordResult result)
    {
        writer.WriteLine("# Improve run record (G662 — preview-through-1.x)");
        writer.WriteLine();
        writer.WriteLine($"- command mode: {result.CommandMode}");
        writer.WriteLine($"- applied: {(result.Applied ? "yes" : "no")}");
        writer.WriteLine($"- record path: {result.RecordPath}");
        writer.WriteLine($"- domain: {result.Record.Domain}");
        writer.WriteLine($"- mode: {result.Record.Mode}");
        writer.WriteLine($"- recorded at: {result.Record.RecordedAt:O}");
        writer.WriteLine("- touched artifacts:");
        foreach (var artifact in result.Record.TouchedArtifacts)
        {
            writer.WriteLine($"  - {artifact}");
        }
        writer.WriteLine($"- quality assessed: {(result.QualityAssessed ? "yes" : "no")}");
        writer.WriteLine($"- semantics: {result.Semantics}");
        if (result.Error is not null)
        {
            writer.WriteLine($"- error: {result.Error}");
        }
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("improve record (G662 — preview-through-1.x)");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Append recency evidence after a human/agent improve review. Records domain, mode, timestamp, and touched artifacts; never grades review quality. Declare cadence separately with `intent-cli improve window`. ");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed record ImproveRunRecordResult
{
    public required string Operation { get; init; }
    public required string PreviewStatus { get; init; }
    public required string CommandMode { get; init; }
    public required bool Applied { get; init; }
    public required string RecordPath { get; init; }
    public required ImproveRunRecord Record { get; init; }
    public required bool QualityAssessed { get; init; }
    public required string Semantics { get; init; }
    public string? Error { get; init; }
}
