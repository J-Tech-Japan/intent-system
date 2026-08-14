using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G695: one read-only surface for the durable completion-signal chain.
/// Querying is deliberately separate from the write paths so inspection never
/// looks like a lifecycle transition.
/// </summary>
internal static class AutomationContinuationChainCommand
{
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
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseArguments(
                args,
                out var domain,
                out var team,
                out var taskId,
                out var signalId,
                out var chainId,
                out var routingRoot,
                out var format,
                out var error))
        {
            writer.WriteLine(error);
            WriteHelp(writer);
            return 1;
        }

        var root = Path.GetFullPath(routingRoot ?? context.RepoRoot);
        var read = ContinuationChainStore.Read(root, domain!, team!, taskId, signalId, chainId);
        var result = new ContinuationChainQueryResult
        {
            Resolved = read.Resolved,
            Domain = domain!,
            Team = team!,
            RoutingRoot = root,
            Path = read.Path,
            TaskId = taskId,
            CompletionSignalId = signalId,
            ChainId = chainId,
            Records = read.Records,
            Error = read.Error,
            Summary = read.Resolved
                ? read.Records.Count == 0
                    ? "No continuation-chain record matched the supplied filter."
                    : $"Read {read.Records.Count} continuation-chain record(s); each record exposes its next missing link."
                : read.Error ?? "Continuation-chain state could not be read.",
        };

        if (string.Equals(format, FormatMarkdown, StringComparison.Ordinal))
        {
            WriteMarkdown(writer, result);
        }
        else
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }

        return read.Resolved ? 0 : 1;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? domain,
        out string? team,
        out string? taskId,
        out string? signalId,
        out string? chainId,
        out string? routingRoot,
        out string format,
        out string error)
    {
        domain = null;
        team = null;
        taskId = null;
        signalId = null;
        chainId = null;
        routingRoot = null;
        format = FormatJson;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--domain":
                    if (!ReadValue(args, ref index, argument, out domain, out error)) return false;
                    break;
                case "--team":
                    if (!ReadValue(args, ref index, argument, out team, out error)) return false;
                    break;
                case "--task-id":
                    if (!ReadValue(args, ref index, argument, out taskId, out error)) return false;
                    break;
                case "--completion-signal-id":
                case "--signal-id":
                    if (!ReadValue(args, ref index, argument, out signalId, out error)) return false;
                    break;
                case "--chain-id":
                    if (!ReadValue(args, ref index, argument, out chainId, out error)) return false;
                    break;
                case "--routing-root":
                    if (!ReadValue(args, ref index, argument, out routingRoot, out error)) return false;
                    break;
                case "--format":
                    if (!ReadValue(args, ref index, argument, out var requested, out error)) return false;
                    if (!string.Equals(requested, FormatJson, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatMarkdown, StringComparison.Ordinal))
                    {
                        error = "--format must be 'json' or 'markdown'.";
                        return false;
                    }
                    format = requested!;
                    break;
                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(team))
        {
            error = "automation continuation-chain requires --domain <d> and --team <t>.";
            return false;
        }

        if (taskId is null && signalId is null && chainId is null)
        {
            // No filter is useful and intentionally supported: it gives the
            // host one surface from which to inspect every active chain.
        }

        return true;
    }

    private static bool ReadValue(
        string[] args,
        ref int index,
        string option,
        out string? value,
        out string error)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            value = null;
            error = $"{option} requires a value.";
            return false;
        }

        value = args[++index];
        error = string.Empty;
        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine(
            "Usage: intent-cli automation continuation-chain --domain <d> --team <t> "
            + "[--task-id <id>|--completion-signal-id <id>|--chain-id <id>] "
            + "[--routing-root <host-root>] [--format json|markdown]");
        writer.WriteLine("Read the append-only report → wake → classification → continuation chain; no mutation is performed.");
    }

    private static void WriteMarkdown(TextWriter writer, ContinuationChainQueryResult result)
    {
        writer.WriteLine("# automation continuation-chain");
        writer.WriteLine();
        writer.WriteLine($"- domain: `{result.Domain}`");
        writer.WriteLine($"- team: `{result.Team}`");
        writer.WriteLine($"- path: `{result.Path}`");
        writer.WriteLine();
        writer.WriteLine(result.Summary);
        foreach (var record in result.Records)
        {
            writer.WriteLine();
            writer.WriteLine($"## `{record.ChainId}`");
            writer.WriteLine();
            writer.WriteLine($"- completion signal: `{record.CompletionSignalId}`");
            writer.WriteLine($"- task: `{record.TaskId}`");
            writer.WriteLine($"- complete: {(record.Complete ? "yes" : "no")}");
            writer.WriteLine($"- next missing link: `{record.NextMissingLink ?? "none"}`");
            foreach (var link in record.Links)
            {
                writer.WriteLine($"- {link.Name} at {link.Timestamp:O} ({link.Source})");
                foreach (var evidence in link.Evidence)
                {
                    writer.WriteLine($"  - {evidence}");
                }
                if (link.Blocker is not null)
                {
                    writer.WriteLine($"  - blocker: {link.Blocker}");
                }
            }
        }
    }
}

internal sealed record ContinuationChainQueryResult
{
    [JsonPropertyName("resolved")] public required bool Resolved { get; init; }
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("routing_root")] public required string RoutingRoot { get; init; }
    [JsonPropertyName("path")] public required string Path { get; init; }
    [JsonPropertyName("task_id")] public string? TaskId { get; init; }
    [JsonPropertyName("completion_signal_id")] public string? CompletionSignalId { get; init; }
    [JsonPropertyName("chain_id")] public string? ChainId { get; init; }
    [JsonPropertyName("records")] public required IReadOnlyList<ContinuationChainRecord> Records { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}
