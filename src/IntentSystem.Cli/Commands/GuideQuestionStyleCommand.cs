using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G380: read-only <c>intent-cli guide question-style</c>. The single,
/// directly-discoverable answer to "質問の方法を intent-cli に聞いて" /
/// "how should I ask the next clarification question, and what must it
/// include?". Emits the required elements of a product-owner
/// clarification / interview question plus a copyable template, so a
/// chat-first agent does not have to probe the older
/// <c>interview start/resume/answer</c> surfaces to reconstruct the
/// guidance. Host-state-free: works from any cwd, never reads queue
/// state, never launches an AI provider.
/// </summary>
internal static class GuideQuestionStyleCommand
{
    private const string FormatMarkdown = "markdown";
    private const string FormatJson = "json";

    private const string UsageLine =
        "Usage: intent-cli guide question-style [--domain <name>] [--format markdown|json]";

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

        if (!TryParseArguments(args, out var domain, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var result = Build(domain);
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

    private static GuideQuestionStyleResult Build(string? domain)
    {
        var domainArg = string.IsNullOrWhiteSpace(domain) ? "<domain>" : domain;

        var requiredElements = new[]
        {
            "Restate the understood request and confirm the in-scope / out-of-scope boundaries before any deep design.",
            "Ask exactly ONE focused question per turn — never batch multiple decisions into a single message.",
            "Offer 2-3 concrete options when a choice is useful (skip options for a simple factual gap).",
            "Give the pros/cons or tradeoffs for each option so the product owner can decide quickly.",
            "Add a recommendation when you have a clear lean, and say why (omit it when you genuinely have no preference).",
            "State how the answer will be recorded so the decision becomes durable before packet generation.",
            "If the intent is ambiguous, stop as clarification-required rather than guessing or proceeding to design.",
        };

        var template =
$@"Understood request: <one-line restatement of what you are being asked to build or decide>.
Scope I'm assuming: <in-scope> / out-of-scope: <out-of-scope> — please correct if wrong.

Question: <exactly one focused decision you need from the product owner>.

Options:
- A) <option A> — pros: <...>; cons: <...>
- B) <option B> — pros: <...>; cons: <...>
- C) <option C> — pros: <...>; cons: <...>   (omit if only two options apply)

Recommendation: <A / B / C> because <reason>.   (omit this line if you have no clear lean)

How I'll record your answer: once you choose, I'll record it durably (see `recording` below) before generating any packet or starting implementation.";

        var prompt =
$@"Ask product-owner clarification / interview questions the chat-first way: one focused question at a time, grounded in a restatement of the request, with options + tradeoffs + a recommendation when useful, and a clear path to record the answer.

Do this in the conversation directly — you do NOT need to probe `interview start` / `interview resume` / `interview answer` or other older surfaces to figure out the shape. Use the required elements and template below, then record the answer only AFTER the user replies.

If the request is ambiguous and you cannot frame a single concrete question, stop and surface it as clarification-required instead of guessing.";

        return new GuideQuestionStyleResult
        {
            Kind = "question-style",
            Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
            Prompt = prompt,
            RequiredElements = requiredElements,
            Template = template,
            Recording = new[]
            {
                "Record the answer ONLY after the user replies — never generate a packet from an unrecorded answer.",
                $"Durable interview Q/A: `intent-cli interview record-answer --domain {domainArg} --question-id <id> --answer \"<the user's choice>\"` (use `intent-cli interview next-question --domain {domainArg} --format json` to get the pending question id).",
                "Clarification artifacts: `intent-cli clarify record ...` when the decision belongs to an open clarification rather than the interview Q/A.",
            },
            AvoidProbing = new[]
            {
                "Do not probe the older `interview start` / `interview resume` / `interview answer` commands to derive question style — this command is the direct answer.",
                "Do not ask several questions at once, and do not proceed to design while a blocking clarification is unanswered.",
            },
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideQuestionStyleResult result)
    {
        writer.WriteLine("# Guide — clarification question style");
        writer.WriteLine();
        if (!string.IsNullOrWhiteSpace(result.Domain))
        {
            writer.WriteLine($"- domain: {result.Domain}");
            writer.WriteLine();
        }

        writer.WriteLine("## Required elements (every clarification question)");
        foreach (var element in result.RequiredElements)
        {
            writer.WriteLine($"- {element}");
        }
        writer.WriteLine();

        writer.WriteLine("## Copyable question template");
        writer.WriteLine();
        writer.WriteLine("```text");
        writer.WriteLine(result.Template);
        writer.WriteLine("```");
        writer.WriteLine();

        writer.WriteLine("## Recording the answer");
        foreach (var line in result.Recording)
        {
            writer.WriteLine($"- {line}");
        }
        writer.WriteLine();

        writer.WriteLine("## Avoid");
        foreach (var line in result.AvoidProbing)
        {
            writer.WriteLine($"- {line}");
        }
        writer.WriteLine();

        writer.WriteLine("## Prompt");
        writer.WriteLine();
        writer.WriteLine("```text");
        writer.WriteLine(result.Prompt);
        writer.WriteLine("```");
    }

    private static bool TryParseArguments(string[] args, out string? domain, out string format, out string error)
    {
        domain = null;
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
                    domain = args[index + 1].Trim();
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
        writer.WriteLine("guide question-style");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only: how to ask product-owner clarification/interview questions and what each must include, with a copyable template.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed record GuideQuestionStyleResult
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    [JsonPropertyName("required_elements")]
    public required IReadOnlyList<string> RequiredElements { get; init; }

    [JsonPropertyName("template")]
    public required string Template { get; init; }

    [JsonPropertyName("recording")]
    public required IReadOnlyList<string> Recording { get; init; }

    [JsonPropertyName("avoid_probing")]
    public required IReadOnlyList<string> AvoidProbing { get; init; }
}
