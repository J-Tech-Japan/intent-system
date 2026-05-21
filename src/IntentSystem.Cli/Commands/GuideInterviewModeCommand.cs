using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G381: read-only <c>intent-cli guide interview-mode</c>. The direct
/// answer to "how do I run a persistent, goal-seeking product-owner
/// interview?". It strengthens the per-question guidance from
/// <c>guide question-style</c> (G380) into a durable, repo-backed
/// protocol: research existing artifacts before asking, walk the
/// decision tree one focused question at a time, and keep going until a
/// definition-of-ready stop condition is reached — stopping after a
/// shallow answer while scope / constraints / acceptance / verification
/// gaps remain is explicitly not enough. The durable output is a decision
/// record + packet/issue readiness, not a raw transcript. Host-state-free:
/// works from any cwd, never reads queue state, never launches an AI
/// provider.
/// </summary>
internal static class GuideInterviewModeCommand
{
    private const string FormatMarkdown = "markdown";
    private const string FormatJson = "json";

    private const string UsageLine =
        "Usage: intent-cli guide interview-mode [--domain <name>] [--format markdown|json]";

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

    private static GuideInterviewModeResult Build(string? domain)
    {
        var domainArg = string.IsNullOrWhiteSpace(domain) ? "<domain>" : domain;

        var prompt =
$@"Run a persistent, goal-seeking product-owner interview: keep clarifying a broad request until it is packet-ready / issue-ready, or until a structured stop condition is justified. The product owner talks to you in chat; you call intent-cli internally and never operate prompts for them, and never launch an AI provider.

Do NOT stop after a single shallow answer. Stopping is only justified when a definition-of-ready stop condition (below) holds — while scope, constraints, acceptance criteria, or verification remain unresolved, ask the next question. Research the repo / intents / packets / GitHub context BEFORE asking the human anything those artifacts already answer; never make the owner repeat known facts. The durable output is a decision record + packet/issue readiness, not a raw transcript.";

        var questionStructure = new[]
        {
            "Current understanding: a one-line restatement of where the shaping stands so the owner can correct drift.",
            "Why this question matters: the decision or readiness gap it unblocks.",
            "One focused question: exactly one decision per turn (never batch).",
            "Options: 2-3 concrete candidates when a choice is useful.",
            "Pros/cons: the tradeoffs for each option.",
            "Recommendation: your lean and why (omit when you genuinely have none).",
            "What the answer will decide: the scope/constraint/acceptance/verification item it resolves.",
            "Remaining gaps: what is still open after this answer, so progress toward ready is visible.",
        };

        var agentBehavior = new[]
        {
            "Research first: inspect repo files, intents, packets, and GitHub issues/PRs before asking a question the artifacts can already answer.",
            "One question at a time: ask a single focused decision per turn and wait for the answer.",
            "Dependency order: walk the decision tree so blocking decisions are resolved before the decisions that depend on them.",
            "Persist to ready: continue interviewing until an explicit stop condition holds — a shallow answer is not a reason to stop while readiness gaps remain.",
            "Record durably: capture each answer/decision (see recording) so the result is a decision record, not a transcript; do not generate a packet from unrecorded answers.",
        };

        var stopConditions = new[]
        {
            new GuideInterviewStopCondition
            {
                Name = "packet-ready",
                Meaning = "Scope, constraints, acceptance criteria, and verification are resolved enough to draft the canonical packet; proceed to `intent-cli packet draft` (with operator acceptance).",
            },
            new GuideInterviewStopCondition
            {
                Name = "issue-ready",
                Meaning = "The standalone child-issue contract is complete enough to publish; proceed to `intent-cli issue publish-flow` (with operator acceptance).",
            },
            new GuideInterviewStopCondition
            {
                Name = "clarification-required",
                Meaning = "A blocking ambiguity remains that only the product owner can resolve; stop and surface it as a structured clarification rather than guessing.",
            },
            new GuideInterviewStopCondition
            {
                Name = "blocked-by-user-decision",
                Meaning = "Progress is gated on an explicit product-owner decision that has not been given; stop and name the exact decision needed.",
            },
            new GuideInterviewStopCondition
            {
                Name = "insufficient-context-after-research",
                Meaning = "Required facts cannot be found in repo / intents / packets / GitHub and the owner cannot supply them; stop and report the missing context rather than fabricating it.",
            },
        };

        return new GuideInterviewModeResult
        {
            Kind = "interview-mode",
            Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
            Prompt = prompt,
            QuestionStructure = questionStructure,
            AgentBehavior = agentBehavior,
            StopConditions = stopConditions,
            DecisionRecord = new[]
            {
                "The durable output is a decision record: the resolved decisions, the answers that drove them, and the current readiness — not a raw chat transcript.",
                $"Record each answer as it arrives: `intent-cli interview record-answer --domain {domainArg} --question-id <id> --answer \"<the owner's decision>\"` (get the pending id from `intent-cli interview next-question --domain {domainArg} --format json`); use `intent-cli clarify record ...` for clarification-scoped decisions.",
                "Only after readiness is reached and answers are recorded, proceed (with operator acceptance) to `intent-cli packet draft` / `intent-cli issue publish-flow`.",
            },
            Related = new[]
            {
                "intent-cli guide question-style --format json — the per-question element checklist + copyable template (G380).",
            },
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideInterviewModeResult result)
    {
        writer.WriteLine("# Guide — persistent goal-seeking interview mode");
        writer.WriteLine();
        if (!string.IsNullOrWhiteSpace(result.Domain))
        {
            writer.WriteLine($"- domain: {result.Domain}");
            writer.WriteLine();
        }

        writer.WriteLine("## Protocol");
        writer.WriteLine();
        writer.WriteLine("```text");
        writer.WriteLine(result.Prompt);
        writer.WriteLine("```");
        writer.WriteLine();

        writer.WriteLine("## Question structure (each turn)");
        foreach (var element in result.QuestionStructure)
        {
            writer.WriteLine($"- {element}");
        }
        writer.WriteLine();

        writer.WriteLine("## Agent behavior");
        foreach (var line in result.AgentBehavior)
        {
            writer.WriteLine($"- {line}");
        }
        writer.WriteLine();

        writer.WriteLine("## Stop conditions");
        foreach (var condition in result.StopConditions)
        {
            writer.WriteLine($"- `{condition.Name}`: {condition.Meaning}");
        }
        writer.WriteLine();

        writer.WriteLine("## Decision record");
        foreach (var line in result.DecisionRecord)
        {
            writer.WriteLine($"- {line}");
        }
        writer.WriteLine();

        writer.WriteLine("## Related");
        foreach (var line in result.Related)
        {
            writer.WriteLine($"- {line}");
        }
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
        writer.WriteLine("guide interview-mode");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only: persistent goal-seeking interview protocol — research-first, one question at a time, explicit definition-of-ready stop conditions, decision-record output.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed record GuideInterviewModeResult
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    [JsonPropertyName("question_structure")]
    public required IReadOnlyList<string> QuestionStructure { get; init; }

    [JsonPropertyName("agent_behavior")]
    public required IReadOnlyList<string> AgentBehavior { get; init; }

    [JsonPropertyName("stop_conditions")]
    public required IReadOnlyList<GuideInterviewStopCondition> StopConditions { get; init; }

    [JsonPropertyName("decision_record")]
    public required IReadOnlyList<string> DecisionRecord { get; init; }

    [JsonPropertyName("related")]
    public required IReadOnlyList<string> Related { get; init; }
}

internal sealed record GuideInterviewStopCondition
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("meaning")]
    public required string Meaning { get; init; }
}
