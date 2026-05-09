using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G241: Read-only <c>intent-cli intent status</c> command. Summarizes a
/// domain's current baseline (latest completed execution units), in-flight
/// WIP, queued/preloaded packets, and open clarification state. Reads queue
/// state, runs log, and clarification file only — never mutates them and
/// never launches an AI provider.
/// </summary>
internal static class IntentStatusCommand
{
    private const string FormatMarkdown = "markdown";
    private const string FormatJson = "json";

    private const int LatestCompletedLimit = 5;

    private const string UsageLine =
        "Usage: intent-cli intent status [--domain <name>] [--format markdown|json]";

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

        if (!TryParseArguments(args, out var domainOverride, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var result = Analyze(context, domainOverride);

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

    internal static IntentStatusResult Analyze(CliContext context, string? domainOverride)
    {
        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride!;

        var notes = new List<string>();
        var queueStatePath = context.GetQueueStatePath();
        QueueState? queueState = null;

        if (File.Exists(queueStatePath))
        {
            try
            {
                queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            }
            catch (JsonException jsonException)
            {
                notes.Add($"queue-state JSON could not be parsed: {jsonException.Message}");
            }
            catch (InvalidOperationException invalidOperation)
            {
                notes.Add($"queue-state payload was invalid: {invalidOperation.Message}");
            }
        }
        else
        {
            notes.Add($"no queue-state file at {queueStatePath}");
        }

        var latestCompleted = new List<IntentStatusItem>();
        var wip = new List<IntentStatusItem>();
        var queued = new List<IntentStatusItem>();

        if (queueState is not null)
        {
            foreach (var item in queueState.Items.Reverse().Where(item => item.State == QueueItemState.Completed))
            {
                if (latestCompleted.Count >= LatestCompletedLimit)
                {
                    break;
                }

                latestCompleted.Add(IntentStatusItem.From(item));
            }

            foreach (var item in queueState.Items)
            {
                switch (item.State)
                {
                    case QueueItemState.Active:
                    case QueueItemState.Review:
                    case QueueItemState.Fixing:
                        wip.Add(IntentStatusItem.From(item));
                        break;

                    case QueueItemState.Queued:
                    case QueueItemState.Blocked:
                    case QueueItemState.ClarifyBlocked:
                        queued.Add(IntentStatusItem.From(item));
                        break;
                }
            }
        }

        var clarificationPath = ResolveClarificationPath(context, domain);
        var clarificationFilePresent = clarificationPath is not null && File.Exists(clarificationPath);
        var clarificationOpen = false;
        if (clarificationFilePresent)
        {
            clarificationOpen = ClarificationOpenDetector.HasOpenBlocker(File.ReadAllText(clarificationPath!));
        }

        // G302: structured clarifications under
        // `intents/<domain>/clarifications/*.toml` are an additive open
        // signal. ANY open structured clarification flips
        // `clarification_open` true regardless of the markdown shape.
        try
        {
            if (StructuredClarificationsDirectory.HasOpenBlocker(context.RepoRoot, domain))
            {
                clarificationOpen = true;
            }
        }
        catch (InvalidOperationException exception)
        {
            notes.Add($"structured clarification could not be parsed: {exception.Message}");
        }

        return new IntentStatusResult
        {
            Domain = domain,
            QueueStatePath = queueStatePath,
            QueueStatePresent = queueState is not null,
            LatestCompleted = latestCompleted,
            Wip = wip,
            Queued = queued,
            ClarificationPath = clarificationPath,
            ClarificationFilePresent = clarificationFilePresent,
            ClarificationOpen = clarificationOpen,
            Notes = notes
        };
    }

    private static string? ResolveClarificationPath(CliContext context, string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        return Path.Combine(context.RepoRoot, "intents", domain, "clarifications", "open.md");
    }

    private static void WriteMarkdown(TextWriter writer, IntentStatusResult result)
    {
        writer.WriteLine($"# Intent status — {result.Domain}");
        writer.WriteLine();
        writer.WriteLine($"- queue-state path: {result.QueueStatePath}");
        writer.WriteLine($"- queue-state present: {(result.QueueStatePresent ? "yes" : "no")}");
        writer.WriteLine();

        writer.WriteLine($"## Latest completed (up to {LatestCompletedLimit})");
        if (result.LatestCompleted.Count == 0)
        {
            writer.WriteLine("- none");
        }
        else
        {
            foreach (var item in result.LatestCompleted)
            {
                writer.WriteLine($"- {item.ExecutionUnit} — {item.Title}");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## WIP (in-flight)");
        if (result.Wip.Count == 0)
        {
            writer.WriteLine("- none");
        }
        else
        {
            foreach (var item in result.Wip)
            {
                writer.WriteLine($"- {item.ExecutionUnit} ({item.State}) — {item.Title}");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Queued / preloaded packets");
        if (result.Queued.Count == 0)
        {
            writer.WriteLine("- none");
        }
        else
        {
            foreach (var item in result.Queued)
            {
                writer.WriteLine($"- {item.ExecutionUnit} ({item.State}) — {item.Title}");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Open clarifications");
        writer.WriteLine($"- file: {(result.ClarificationPath ?? "(none)")}");
        writer.WriteLine($"- file present: {(result.ClarificationFilePresent ? "yes" : "no")}");
        writer.WriteLine($"- has open blocker: {(result.ClarificationOpen ? "yes" : "no")}");
        writer.WriteLine();

        if (result.Notes.Count > 0)
        {
            writer.WriteLine("## Notes");
            foreach (var note in result.Notes)
            {
                writer.WriteLine($"- {note}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? domainOverride,
        out string format,
        out string error)
    {
        domainOverride = null;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }

                    domainOverride = args[index + 1];
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }

                    var requested = args[index + 1];
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }

                    format = requested;
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("intent status");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only domain status: latest completed, WIP, queued packets, open clarifications.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record IntentStatusResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("queue_state_path")]
    public required string QueueStatePath { get; init; }

    [JsonPropertyName("queue_state_present")]
    public required bool QueueStatePresent { get; init; }

    [JsonPropertyName("latest_completed")]
    public required IReadOnlyList<IntentStatusItem> LatestCompleted { get; init; }

    [JsonPropertyName("wip")]
    public required IReadOnlyList<IntentStatusItem> Wip { get; init; }

    [JsonPropertyName("queued")]
    public required IReadOnlyList<IntentStatusItem> Queued { get; init; }

    [JsonPropertyName("clarification_path")]
    public string? ClarificationPath { get; init; }

    [JsonPropertyName("clarification_file_present")]
    public required bool ClarificationFilePresent { get; init; }

    [JsonPropertyName("clarification_open")]
    public required bool ClarificationOpen { get; init; }

    [JsonPropertyName("notes")]
    public required IReadOnlyList<string> Notes { get; init; }
}

internal sealed record IntentStatusItem
{
    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }

    public static IntentStatusItem From(QueueItem item)
    {
        return new IntentStatusItem
        {
            ExecutionUnit = item.ExecutionUnit,
            Title = item.Title,
            State = item.State.ToString().ToLowerInvariant()
        };
    }
}
