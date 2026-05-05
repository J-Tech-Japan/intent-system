using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G254: Read-only <c>intent-cli guide rules list</c>. Lists the rule
/// topics supported by <c>guide rules --topic</c> together with stable
/// topic ids, a short description, and a category (automation,
/// issue-contract, interview, review, closeout) so AI agents can
/// discover available topics without opening local rules files. Never
/// mutates state. Never launches an AI provider.
/// </summary>
internal static class GuideRulesListCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide rules list [--format markdown|json]";

    internal static readonly IReadOnlyList<RulesTopicEntry> Topics = new[]
    {
        new RulesTopicEntry
        {
            Id = "label-ownership",
            Title = "Label ownership",
            Description = "intent-target / intent-pr-created boundaries and the installed transition surfaces.",
            Category = "automation"
        },
        new RulesTopicEntry
        {
            Id = "child-issue-contract",
            Title = "Child Issue Contract",
            Description = "Required github-body sections for an implementation handoff.",
            Category = "issue-contract"
        },
        new RulesTopicEntry
        {
            Id = "clarification",
            Title = "Hard Clarification rule",
            Description = "When source-of-truth is ambiguous, stop with clarification-required.",
            Category = "automation"
        },
        new RulesTopicEntry
        {
            Id = "review-closeout",
            Title = "Review and closeout",
            Description = "Stage 1 review and Stage 2 closeout boundaries; installed transition path.",
            Category = "review"
        },
        new RulesTopicEntry
        {
            Id = "intake-interview",
            Title = "Intake and interview",
            Description = "Operator-decides intake flow and durable interview Q/A artifacts.",
            Category = "interview"
        }
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

        if (!TryParseArguments(args, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            var payload = new RulesTopicListResult { Topics = Topics };
            writer.Write(JsonSerializer.Serialize(payload, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer);
        }

        return 0;
    }

    private static void WriteMarkdown(TextWriter writer)
    {
        writer.WriteLine("# Guide rules — supported topics");
        writer.WriteLine();
        writer.WriteLine("Use `intent-cli guide rules --topic <id>` to retrieve the full rule summary.");
        writer.WriteLine();

        writer.WriteLine("| id | category | title | description |");
        writer.WriteLine("|----|----------|-------|-------------|");
        foreach (var topic in Topics)
        {
            writer.WriteLine($"| {topic.Id} | {topic.Category} | {topic.Title} | {topic.Description} |");
        }
    }

    private static bool TryParseArguments(string[] args, out string format, out string error)
    {
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
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
        writer.WriteLine("guide rules list");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only listing of supported `guide rules --topic` topic ids, categories, and descriptions.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };
}

internal sealed record RulesTopicEntry
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("category")]
    public required string Category { get; init; }
}

internal sealed record RulesTopicListResult
{
    [JsonPropertyName("topics")]
    public required IReadOnlyList<RulesTopicEntry> Topics { get; init; }
}
