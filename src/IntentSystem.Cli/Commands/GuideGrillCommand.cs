using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G463: Read-only <c>intent-cli grill</c> / <c>intent-cli guide grill</c>.
/// Emits the canonical <em>persistent interview mode</em> guidance: once the
/// user asks to grill a topic, the design thread STAYS in grill mode and keeps
/// asking one question at a time, generating an open-question backlog from the
/// current intent context, until a structured stop condition is reached.
///
/// grill is the user-facing persistent mode BUILT ON the existing
/// <c>interview</c> artifacts (it records answers through
/// <c>interview record-answer</c> and reads pending questions through
/// <c>interview next-question</c>); it does not introduce a new durable store.
/// It is distinct from <c>clarification</c> (blocker resolution) and
/// <c>improve</c> (retrospective realignment). Host-state-free: works from any
/// cwd, never reads parent queue state, never launches an AI provider, and
/// never auto-publishes packets or issues.
/// </summary>
internal static class GuideGrillCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli grill [--domain <name>] [--format markdown|json]  (alias: intent-cli guide grill)";

    /// <summary>The shortest natural-language ask that starts persistent grill mode.</summary>
    public const string ShortPrompt = "intent-cli で <topic> を grill してください。";

    /// <summary>Returned only when the backlog is empty AND rediscovery finds no meaningful question.</summary>
    public const string EmptyBacklogResponse = "今のところ追加質問はありません";

    // Stop conditions the persistent grill loop recognizes (AC: all six required).
    public const string StopNoMoreQuestions = "no-more-questions";
    public const string StopPacketReady = "packet-ready";
    public const string StopIntentUpdateReady = "intent-update-ready";
    public const string StopClarificationNeeded = "clarification-needed";
    public const string StopBlockedByUserDecision = "blocked-by-user-decision";
    public const string StopTooBroadSplitNeeded = "too-broad-split-needed";

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

        var result = BuildResult(domain);
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

    internal static GuideGrillResult BuildResult(string? domain)
    {
        var domainArg = string.IsNullOrWhiteSpace(domain) ? "<domain>" : domain!;

        var prompt =
$@"Enter persistent grill mode for `{domainArg}`: the user asked to grill a topic, so the thread STAYS in grill mode and keeps extracting intent one question at a time until a stop condition holds. You talk to the user in chat; you call intent-cli internally and never launch an AI provider.

1. Research first: inspect current intents, recent packets, ADR / design notes, docs, and relevant implementation evidence BEFORE asking anything those artifacts already answer. Never make the user repeat known facts.
2. Generate an open-question backlog: enumerate the open product / technical / operational / verification questions implied by the topic and the gaps you found. Order them by dependency (blocking decisions first).
3. Ask exactly ONE question at a time and wait for the answer. Record each answer through the interview artifacts as it arrives.
4. After each answer, CONTINUE automatically — re-derive the backlog (some answers open new questions, some close several) and ask the next one. Do NOT require the user to repeat `grill`.
5. Stop only when a structured stop condition (below) holds. When the backlog is empty AND a fresh rediscovery pass finds no meaningful new question, return exactly: `{EmptyBacklogResponse}`.";

        var inspectionSources = new[]
        {
            $"Current intents — `intents/{domainArg}/` MVV, product goal, and intent-tree nodes the topic touches.",
            $"Recent packet history — `.intent-cli/issues/<unit>/` packets related to the topic, for decisions already made or still open.",
            $"ADR / design notes — architecture decision records and design notes that constrain or inform the topic.",
            "Docs — user-facing docs and README sections that describe current behavior for the topic.",
            "Relevant implementation evidence — related code, tests, and GitHub issues/PRs, when available, so questions target real gaps rather than re-asking settled facts.",
        };

        var backlogGeneration = new[]
        {
            "Derive open questions across four intent dimensions: product (what/why/for-whom), technical (how/architecture/constraints), operational (rollout/ownership/failure modes), and verification (acceptance criteria / how it is proven).",
            "Seed the backlog from the research gaps — every artifact that is missing, ambiguous, or contradicted by recent work becomes a candidate question.",
            "Order by dependency: resolve blocking decisions before the decisions that depend on them; split a question that secretly bundles several decisions.",
            "Keep the backlog visible: after each turn, restate what remains open so progress toward readiness is observable.",
        };

        var continuationBehavior = new[]
        {
            "grill is PERSISTENT: staying in grill mode after each answer is the default; the user does not re-issue `grill` to get the next question.",
            "One decision per turn: ask a single focused question and wait — never batch multiple decisions into one turn.",
            "Re-derive after every answer: an answer may close several backlog items or open new ones; recompute the backlog before asking the next question.",
            $"Continue until a stop condition holds. Only when the backlog is empty and rediscovery finds nothing meaningful do you return `{EmptyBacklogResponse}`.",
        };

        var interviewIntegration = new[]
        {
            "grill is persistent interview mode built ON the existing interview artifacts — it reuses them internally rather than introducing a new durable session store.",
            $"Read the pending question id: `intent-cli interview next-question --domain {domainArg} --format json`.",
            $"Record each answer as it arrives: `intent-cli interview record-answer --domain {domainArg} --question-id <id> --answer \"<the user's decision>\"`.",
            "The durable output is the interview decision record (resolved decisions + the answers that drove them), not a raw chat transcript.",
            "See also `intent-cli guide interview-mode` for the per-turn question structure; grill is the persistent, backlog-driven wrapper over that protocol.",
        };

        var stopConditions = new[]
        {
            new GuideGrillStopCondition { Name = StopNoMoreQuestions, Meaning = $"The backlog is empty and a fresh rediscovery pass finds no meaningful new question; return exactly `{EmptyBacklogResponse}` and leave grill mode." },
            new GuideGrillStopCondition { Name = StopPacketReady, Meaning = "Scope, constraints, acceptance criteria, and verification are resolved enough to draft the canonical packet; propose `intent-cli packet draft` (with operator acceptance). grill never auto-publishes." },
            new GuideGrillStopCondition { Name = StopIntentUpdateReady, Meaning = "The answers establish an intent that should be written into the intent tree / specs; propose the intent update through the normal repo path before further questioning." },
            new GuideGrillStopCondition { Name = StopClarificationNeeded, Meaning = "A blocking ambiguity surfaced that belongs in the clarification flow; stop grilling that thread and route it to `intent-cli clarification` rather than guessing." },
            new GuideGrillStopCondition { Name = StopBlockedByUserDecision, Meaning = "Progress is gated on an explicit product-owner decision that has not been given; stop and name the exact decision needed." },
            new GuideGrillStopCondition { Name = StopTooBroadSplitNeeded, Meaning = "The topic is too broad to grill as one thread; stop and propose splitting it into narrower sub-topics, then grill each in dependency order." },
        };

        return new GuideGrillResult
        {
            Process = "persistent-grill-interview",
            Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
            ShortPrompt = ShortPrompt,
            Summary =
                "grill is the user-facing PERSISTENT interview mode. Once the user asks to grill a topic, the thread stays "
                + "in grill mode: research the existing intents / packets / ADRs / docs / implementation evidence, generate an "
                + "open-question backlog, ask one question at a time, and CONTINUE after each answer without the user repeating "
                + "`grill` — until a structured stop condition holds. It is built on the existing interview artifacts; it is NOT "
                + "clarification (blocker resolution) and NOT improve (retrospective realignment), and it never auto-publishes "
                + "packets or issues.",
            EmptyBacklogResponse = EmptyBacklogResponse,
            NotThis = new[]
            {
                "grill does NOT auto-publish packets or issues — packet/issue creation stays an explicit, operator-accepted step at a stop condition.",
                "grill does NOT replace `clarification` (blocker resolution) or `improve` (retrospective realignment) — it is forward extraction of intent before packets are cut.",
                "intent-cli does NOT launch Claude/Codex/Copilot or any AI provider; the AI agent owns the semantic questioning and conversation.",
                "grill is NOT a one-shot question — stopping after a single shallow answer while scope / constraints / acceptance / verification gaps remain is explicitly not enough.",
            },
            DoNotSubstitute = new[]
            {
                "When the user asks to grill a topic, run `intent-cli grill` (or `intent-cli guide grill`) and stay persistent — do NOT ask one question and drop out of grill mode.",
                "Do NOT route grill to `clarification` for ordinary open questions — only an actual blocking ambiguity becomes `clarification-needed`.",
                "Do NOT route grill to `improve` — improve is retrospective realignment of already-shipped work, not forward intent extraction.",
                "If a first-class grill surface is not found in the installed CLI (e.g. `intent-cli grill --help` fails), report `grill guidance unavailable` and request a CLI update — do NOT silently substitute another workflow.",
            },
            InspectionSources = inspectionSources,
            BacklogGeneration = backlogGeneration,
            ContinuationBehavior = continuationBehavior,
            InterviewIntegration = interviewIntegration,
            StopConditions = stopConditions,
            MutationBoundary = new[]
            {
                "Record answers through `intent-cli interview record-answer`; do not generate a packet from unrecorded answers.",
                "Packet / issue / intent-update actions are proposed at a stop condition and applied only after explicit operator acceptance, through supported intent-cli / repo paths.",
                "Never hand-edit workflow labels, queue-state, or publish metadata from grill — grill only reads context and records interview answers.",
            },
            Prompt = prompt,
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideGrillResult result)
    {
        writer.WriteLine("# Guide grill — persistent interview mode");
        writer.WriteLine();
        writer.WriteLine($"_Start it by asking:_ **{result.ShortPrompt}**");
        writer.WriteLine();
        if (!string.IsNullOrWhiteSpace(result.Domain))
        {
            writer.WriteLine($"- domain: {result.Domain}");
            writer.WriteLine();
        }
        writer.WriteLine(result.Summary);
        writer.WriteLine();

        writer.WriteLine("## Protocol");
        writer.WriteLine();
        writer.WriteLine("```text");
        writer.WriteLine(result.Prompt);
        writer.WriteLine("```");
        writer.WriteLine();

        writer.WriteLine("## What this is NOT");
        foreach (var item in result.NotThis)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Do not substitute another workflow");
        foreach (var item in result.DoNotSubstitute)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Inspect before asking");
        foreach (var item in result.InspectionSources)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Open-question backlog generation");
        foreach (var item in result.BacklogGeneration)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Continuation after each answer");
        foreach (var item in result.ContinuationBehavior)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Interview integration");
        foreach (var item in result.InterviewIntegration)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Stop conditions");
        foreach (var condition in result.StopConditions)
        {
            writer.WriteLine($"- `{condition.Name}`: {condition.Meaning}");
        }
        writer.WriteLine();

        writer.WriteLine("## Mutation boundary");
        foreach (var item in result.MutationBoundary)
        {
            writer.WriteLine($"- {item}");
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
        writer.WriteLine("grill (alias: guide grill)");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only: persistent interview mode. Generates an open-question backlog from current intent context and keeps asking one question at a time, continuing after each answer until a stop condition holds. Built on the interview artifacts; never auto-publishes; never launches an AI provider.");
        writer.WriteLine("Start it by asking: " + ShortPrompt);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed record GuideGrillResult
{
    [JsonPropertyName("process")]
    public required string Process { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("short_prompt")]
    public required string ShortPrompt { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("empty_backlog_response")]
    public required string EmptyBacklogResponse { get; init; }

    [JsonPropertyName("not_this")]
    public required IReadOnlyList<string> NotThis { get; init; }

    [JsonPropertyName("do_not_substitute")]
    public required IReadOnlyList<string> DoNotSubstitute { get; init; }

    [JsonPropertyName("inspection_sources")]
    public required IReadOnlyList<string> InspectionSources { get; init; }

    [JsonPropertyName("backlog_generation")]
    public required IReadOnlyList<string> BacklogGeneration { get; init; }

    [JsonPropertyName("continuation_behavior")]
    public required IReadOnlyList<string> ContinuationBehavior { get; init; }

    [JsonPropertyName("interview_integration")]
    public required IReadOnlyList<string> InterviewIntegration { get; init; }

    [JsonPropertyName("stop_conditions")]
    public required IReadOnlyList<GuideGrillStopCondition> StopConditions { get; init; }

    [JsonPropertyName("mutation_boundary")]
    public required IReadOnlyList<string> MutationBoundary { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }
}

internal sealed record GuideGrillStopCondition
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("meaning")]
    public required string Meaning { get; init; }
}
