using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G302: <c>intent-cli clarification status / next / answer</c>. New
/// structured clarification surface so an AI agent can ask the host
/// directly which product-owner question to put to the human, record the
/// answer durably, and have <c>intent status</c> /
/// <c>intent next-slice</c> reflect the resulting state without
/// free-form markdown interpretation.
///
/// <list type="bullet">
/// <item><description><c>status</c> — list every clarification under
/// <c>intents/&lt;domain&gt;/clarifications/*.toml</c> (open + answered).</description></item>
/// <item><description><c>next</c> — return the first open clarification with
/// background, options, pros/cons, and recommendation.</description></item>
/// <item><description><c>answer --id &lt;id&gt; --choice &lt;option-id&gt; [--note &lt;text&gt;] --write</c>
///   — set <c>status = "answered"</c> and write the
///   <c>[answer]</c> table.</description></item>
/// </list>
/// </summary>
internal static class ClarificationCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    /// <summary>Test seam — replaces the default UTC timestamp source.</summary>
    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static int ExecuteStatus(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseDomainAndFormat(args, context, out var domain, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        IReadOnlyList<StructuredClarification> all;
        try
        {
            all = StructuredClarificationsDirectory.ReadAll(context.RepoRoot, domain);
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }

        var openCount = all.Count(c => c.IsOpen());
        var result = new ClarificationStatusResult
        {
            Domain = domain,
            DirectoryPath = StructuredClarificationsDirectory.ResolveDirectory(context.RepoRoot, domain),
            OpenCount = openCount,
            AnsweredCount = all.Count - openCount,
            Items = all
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteStatusMarkdown(writer, result);
        }
        return 0;
    }

    public static int ExecuteNext(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseDomainAndFormat(args, context, out var domain, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        IReadOnlyList<StructuredClarification> all;
        try
        {
            all = StructuredClarificationsDirectory.ReadAll(context.RepoRoot, domain);
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }

        var next = all.FirstOrDefault(c => c.IsOpen());
        var result = new ClarificationNextResult
        {
            Domain = domain,
            HasOpen = next is not null,
            Clarification = next
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteNextMarkdown(writer, result);
        }
        return 0;
    }

    public static int ExecuteAnswer(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseAnswerArgs(args, context, out var domain, out var id, out var choice, out var note, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var path = StructuredClarificationsDirectory.ResolveFile(context.RepoRoot, domain, id);
        if (!File.Exists(path))
        {
            writer.WriteLine($"No structured clarification found at '{path}' for domain '{domain}' id '{id}'.");
            return 1;
        }

        StructuredClarification existing;
        try
        {
            existing = StructuredClarificationToml.Deserialize(File.ReadAllText(path), sourcePath: path);
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }

        if (!existing.Options.Any(o => string.Equals(o.Id, choice, StringComparison.Ordinal)))
        {
            writer.WriteLine(
                $"Choice '{choice}' is not one of the recorded option ids for clarification '{id}': [{string.Join(", ", existing.Options.Select(o => o.Id))}].");
            return 1;
        }

        var answeredAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow)
            .ToUniversalTime()
            .ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

        var updated = existing with
        {
            Status = StructuredClarificationStatus.Answered,
            Answer = new StructuredClarificationAnswer
            {
                Choice = choice,
                Note = note,
                AnsweredAt = answeredAt
            }
        };

        if (write)
        {
            File.WriteAllText(path, StructuredClarificationToml.Serialize(updated));
        }

        var result = new ClarificationAnswerResult
        {
            Domain = domain,
            Id = id,
            Mode = write ? "write" : "dry-run",
            Path = path,
            Clarification = updated
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteAnswerMarkdown(writer, result);
        }
        return 0;
    }

    private static void WriteStatusMarkdown(TextWriter writer, ClarificationStatusResult result)
    {
        writer.WriteLine($"# Clarification status — `{result.Domain}`");
        writer.WriteLine();
        writer.WriteLine($"- Directory: `{result.DirectoryPath}`");
        writer.WriteLine($"- Open: {result.OpenCount}");
        writer.WriteLine($"- Answered: {result.AnsweredCount}");
        writer.WriteLine();

        if (result.Items.Count == 0)
        {
            writer.WriteLine("_No structured clarifications found._");
            return;
        }

        foreach (var item in result.Items)
        {
            writer.WriteLine($"## `{item.Id}` — **{item.Status}**");
            writer.WriteLine();
            writer.WriteLine(item.Question);
            writer.WriteLine();
            if (item.Blocks.Count > 0)
            {
                writer.WriteLine($"- Blocks: {string.Join(", ", item.Blocks.Select(b => "`" + b + "`"))}");
            }
            if (item.Answer is { } answer)
            {
                writer.WriteLine($"- Answer: `{answer.Choice}` (recorded {answer.AnsweredAt})");
            }
            writer.WriteLine();
        }
    }

    private static void WriteNextMarkdown(TextWriter writer, ClarificationNextResult result)
    {
        writer.WriteLine($"# Clarification next — `{result.Domain}`");
        writer.WriteLine();
        if (!result.HasOpen || result.Clarification is null)
        {
            writer.WriteLine("_No open structured clarifications._");
            return;
        }

        var c = result.Clarification;
        writer.WriteLine($"## `{c.Id}`");
        writer.WriteLine();
        if (!string.IsNullOrEmpty(c.Background))
        {
            writer.WriteLine("### Background");
            writer.WriteLine();
            writer.WriteLine(c.Background);
            writer.WriteLine();
        }
        writer.WriteLine("### Question");
        writer.WriteLine();
        writer.WriteLine(c.Question);
        writer.WriteLine();
        if (c.Options.Count > 0)
        {
            writer.WriteLine("### Options");
            writer.WriteLine();
            foreach (var option in c.Options)
            {
                writer.WriteLine($"- **`{option.Id}`** — {option.Label}");
                if (option.Pros.Count > 0)
                {
                    writer.WriteLine($"  - Pros: {string.Join("; ", option.Pros)}");
                }
                if (option.Cons.Count > 0)
                {
                    writer.WriteLine($"  - Cons: {string.Join("; ", option.Cons)}");
                }
            }
            writer.WriteLine();
        }
        if (!string.IsNullOrEmpty(c.Recommendation))
        {
            writer.WriteLine($"### Recommendation: `{c.Recommendation}`");
            writer.WriteLine();
        }
        if (c.Blocks.Count > 0)
        {
            writer.WriteLine($"Blocks: {string.Join(", ", c.Blocks.Select(b => "`" + b + "`"))}");
        }
    }

    private static void WriteAnswerMarkdown(TextWriter writer, ClarificationAnswerResult result)
    {
        writer.WriteLine($"# Clarification answer — `{result.Domain}` / `{result.Id}` ({result.Mode})");
        writer.WriteLine();
        var c = result.Clarification;
        if (c.Answer is { } answer)
        {
            writer.WriteLine($"- Choice: `{answer.Choice}`");
            if (!string.IsNullOrEmpty(answer.Note))
            {
                writer.WriteLine($"- Note: {answer.Note}");
            }
            writer.WriteLine($"- Answered at: {answer.AnsweredAt}");
            writer.WriteLine($"- Path: `{result.Path}`");
        }
    }

    private static bool TryParseDomainAndFormat(
        string[] args,
        CliContext context,
        out string domain,
        out string format,
        out string error)
    {
        domain = string.Empty;
        format = FormatMarkdown;
        error = string.Empty;
        string? domainOverride = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }
                    domainOverride = args[++index].Trim();
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var candidate = args[++index].Trim();
                    if (!string.Equals(candidate, FormatJson, StringComparison.Ordinal)
                        && !string.Equals(candidate, FormatMarkdown, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{candidate}').";
                        return false;
                    }
                    format = candidate;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        domain = string.IsNullOrWhiteSpace(domainOverride) ? context.Config.Project.Domain : domainOverride!;
        if (string.IsNullOrWhiteSpace(domain))
        {
            error = "A domain must be specified via --domain or via [project] domain in the host config.";
            return false;
        }
        return true;
    }

    private static bool TryParseAnswerArgs(
        string[] args,
        CliContext context,
        out string domain,
        out string id,
        out string choice,
        out string? note,
        out bool write,
        out string format,
        out string error)
    {
        domain = string.Empty;
        id = string.Empty;
        choice = string.Empty;
        note = null;
        write = false;
        format = FormatMarkdown;
        error = string.Empty;

        string? domainOverride = null;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }
                    domainOverride = args[++index].Trim();
                    break;
                case "--id":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--id requires a value.";
                        return false;
                    }
                    id = args[++index].Trim();
                    break;
                case "--choice":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--choice requires a value.";
                        return false;
                    }
                    choice = args[++index].Trim();
                    break;
                case "--note":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--note requires a value.";
                        return false;
                    }
                    note = args[++index];
                    break;
                case "--write":
                    write = true;
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var candidate = args[++index].Trim();
                    if (!string.Equals(candidate, FormatJson, StringComparison.Ordinal)
                        && !string.Equals(candidate, FormatMarkdown, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{candidate}').";
                        return false;
                    }
                    format = candidate;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        domain = string.IsNullOrWhiteSpace(domainOverride) ? context.Config.Project.Domain : domainOverride!;
        if (string.IsNullOrWhiteSpace(domain))
        {
            error = "A domain must be specified via --domain or via [project] domain in the host config.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(id))
        {
            error = "Clarification answer requires '--id <id>'.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(choice))
        {
            error = "Clarification answer requires '--choice <option-id>'.";
            return false;
        }
        return true;
    }
}

internal sealed record ClarificationStatusResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("directory_path")]
    public required string DirectoryPath { get; init; }

    [JsonPropertyName("open_count")]
    public required int OpenCount { get; init; }

    [JsonPropertyName("answered_count")]
    public required int AnsweredCount { get; init; }

    [JsonPropertyName("items")]
    public required IReadOnlyList<StructuredClarification> Items { get; init; }
}

internal sealed record ClarificationNextResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("has_open")]
    public required bool HasOpen { get; init; }

    [JsonPropertyName("clarification")]
    public StructuredClarification? Clarification { get; init; }
}

internal sealed record ClarificationAnswerResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("clarification")]
    public required StructuredClarification Clarification { get; init; }
}
