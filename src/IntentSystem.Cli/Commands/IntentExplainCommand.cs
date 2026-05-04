using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G242: Read-only <c>intent-cli intent explain</c> command. Resolves an
/// execution-unit identifier (e.g. <c>G241</c>) and emits a deterministic
/// summary that includes the queue-state record, packet directory contents,
/// and a head excerpt of the GitHub-body packet when present. Never
/// mutates state and never launches an AI provider.
/// </summary>
internal static class IntentExplainCommand
{
    private const string FormatMarkdown = "markdown";
    private const string FormatJson = "json";

    private const int GithubBodyHeadLines = 12;

    private const string UsageLine =
        "Usage: intent-cli intent explain <execution-unit-id> [--domain <name>] [--format markdown|json]";

    private static readonly Regex ExecutionUnitPattern = new(
        @"^[A-Za-z][A-Za-z0-9-]*$",
        RegexOptions.Compiled);

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

        if (!TryParseArguments(args, out var executionUnit, out var domainOverride, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (!ExecutionUnitPattern.IsMatch(executionUnit!))
        {
            writer.WriteLine($"Invalid execution-unit id '{executionUnit}'. Expected an alphanumeric token like 'G241'.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        var result = Explain(context, executionUnit!, domainOverride);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        return result.Found ? 0 : 1;
    }

    internal static IntentExplainResult Explain(CliContext context, string executionUnit, string? domainOverride)
    {
        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride!;

        var packetDirectory = Path.Combine(context.RepoRoot, ".intent-cli", "issues", executionUnit);
        var packetExists = Directory.Exists(packetDirectory);

        IReadOnlyList<string> packetFiles = packetExists
            ? Directory.EnumerateFiles(packetDirectory)
                .Select(file => Path.GetFileName(file))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray()
            : Array.Empty<string>();

        var githubBodyPath = Path.Combine(packetDirectory, "github-body.md");
        var githubBodyHead = File.Exists(githubBodyPath)
            ? string.Join('\n', File.ReadAllLines(githubBodyPath).Take(GithubBodyHeadLines))
            : null;

        var queueStatePath = context.GetQueueStatePath();
        QueueItem? queueItem = null;
        if (File.Exists(queueStatePath))
        {
            try
            {
                var state = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
                queueItem = state.Items.FirstOrDefault(item =>
                    string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));
            }
            catch (JsonException)
            {
                // Queue parse errors surface in the result via Found=false context.
            }
            catch (InvalidOperationException)
            {
            }
        }

        return new IntentExplainResult
        {
            ExecutionUnit = executionUnit,
            Domain = domain,
            Found = packetExists || queueItem is not null,
            PacketDirectory = packetDirectory,
            PacketFiles = packetFiles,
            QueueItem = queueItem is null ? null : IntentExplainQueueItem.From(queueItem),
            GithubBodyHead = githubBodyHead
        };
    }

    private static void WriteMarkdown(TextWriter writer, IntentExplainResult result)
    {
        writer.WriteLine($"# Intent explain — {result.ExecutionUnit}");
        writer.WriteLine();
        writer.WriteLine($"- domain: {result.Domain}");
        writer.WriteLine($"- packet directory: {result.PacketDirectory}");
        writer.WriteLine($"- found: {(result.Found ? "yes" : "no")}");
        writer.WriteLine();

        if (result.QueueItem is not null)
        {
            writer.WriteLine("## Queue item");
            writer.WriteLine($"- title: {result.QueueItem.Title}");
            writer.WriteLine($"- state: {result.QueueItem.State}");
            writer.WriteLine($"- worker role: {result.QueueItem.WorkerRole}");
            writer.WriteLine($"- review role: {result.QueueItem.ReviewRole}");
            writer.WriteLine($"- priority: {result.QueueItem.Priority}");
            if (!string.IsNullOrWhiteSpace(result.QueueItem.LinkedPr))
            {
                writer.WriteLine($"- linked PR: {result.QueueItem.LinkedPr}");
            }
            writer.WriteLine();
        }

        writer.WriteLine("## Packet files");
        if (result.PacketFiles.Count == 0)
        {
            writer.WriteLine("- none");
        }
        else
        {
            foreach (var file in result.PacketFiles)
            {
                writer.WriteLine($"- {file}");
            }
        }
        writer.WriteLine();

        if (!string.IsNullOrWhiteSpace(result.GithubBodyHead))
        {
            writer.WriteLine($"## github-body.md head ({GithubBodyHeadLines} lines)");
            writer.WriteLine();
            writer.WriteLine("```");
            writer.WriteLine(result.GithubBodyHead);
            writer.WriteLine("```");
        }

        if (!result.Found)
        {
            writer.WriteLine("No packet directory or queue-state record found for this execution unit.");
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? executionUnit,
        out string? domainOverride,
        out string format,
        out string error)
    {
        executionUnit = null;
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
                    if (argument.StartsWith("--", StringComparison.Ordinal))
                    {
                        error = $"Unknown argument '{argument}'.";
                        return false;
                    }

                    if (executionUnit is not null)
                    {
                        error = $"Only one execution-unit id is allowed (got '{executionUnit}' and '{argument}').";
                        return false;
                    }

                    executionUnit = argument;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            error = "An execution-unit id is required.";
            return false;
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("intent explain");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only execution-unit summary using packet directory and queue-state.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record IntentExplainResult
{
    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("found")]
    public required bool Found { get; init; }

    [JsonPropertyName("packet_directory")]
    public required string PacketDirectory { get; init; }

    [JsonPropertyName("packet_files")]
    public required IReadOnlyList<string> PacketFiles { get; init; }

    [JsonPropertyName("queue_item")]
    public IntentExplainQueueItem? QueueItem { get; init; }

    [JsonPropertyName("github_body_head")]
    public string? GithubBodyHead { get; init; }
}

internal sealed record IntentExplainQueueItem
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("worker_role")]
    public required string WorkerRole { get; init; }

    [JsonPropertyName("review_role")]
    public required string ReviewRole { get; init; }

    [JsonPropertyName("priority")]
    public required string Priority { get; init; }

    [JsonPropertyName("linked_pr")]
    public string? LinkedPr { get; init; }

    public static IntentExplainQueueItem From(QueueItem item)
    {
        return new IntentExplainQueueItem
        {
            Title = item.Title,
            State = item.State.ToString().ToLowerInvariant(),
            WorkerRole = item.WorkerRole,
            ReviewRole = item.ReviewRole,
            Priority = item.Priority,
            LinkedPr = item.LinkedPr
        };
    }
}
