using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G256: Read-only <c>intent-cli guide model</c>. Emits the canonical
/// description of the primary collaboration model — chat-first product
/// owner, Codex/Claude coding agent calls intent-cli internally,
/// canonical state lives in the host git repository, intent-cli does
/// not launch AI providers — so AI agents can discover the model
/// without reading rule files. Never mutates state. Never launches an
/// AI provider.
/// </summary>
internal static class GuideModelCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide model [--format markdown|json]";

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
            writer.Write(JsonSerializer.Serialize(BuildModel(), JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer);
        }

        return 0;
    }

    internal static GuideModelResult BuildModel()
    {
        return new GuideModelResult
        {
            PrimaryModel = "chat-first / CLI-internal: the human product owner stays in chat with a Codex/Claude coding agent, and the agent calls `intent-cli` internally for rules, workflows, questions, status, and validation.",
            ExecutionOrchestrationModel = new GuideModelExecutionOrchestration
            {
                Summary = "G540: for autonomous, multi-thread execution once intents are authored, the PRIMARY "
                    + "collaboration model is FOUR-THREAD agmsg orchestration — design / orchestrator / "
                    + "implementation / review coordinate over agmsg, with the orchestrator pacing loopless "
                    + "implementation/review receivers instead of independent timers. This is the practiced, "
                    + "maintained model (G520-G539: wake contract, stalled-work, heartbeat, issue-retire, "
                    + "priority override, publish reliability). Full setup/reference: "
                    + "`intent-cli guide orchestrator-thread --domain <d> --target-repo <owner/repo> --agent <a> --format markdown`.",
                Roles = new[]
                {
                    "design — authors intent, packet content and acceptance criteria, release scope, and prioritization rulings; consulted by the orchestrator before any of those take effect (double-check rule).",
                    "orchestrator — inspects canonical intent-cli/GitHub state, publishes exactly one already-authored `issue-cut-ready` packet per wake, delegates implementation/review over agmsg, tracks CI/review, and closes out approved PRs; never authors design content unilaterally.",
                    "implementation — a loopless receiver that implements exactly the delegated execution unit via `worker next-action` / `worker claim` / `worker complete`.",
                    "review — a loopless receiver that reviews exactly the delegated PR via the canonical review surfaces.",
                },
                MessageDrivenSteadyState = "Implementation/review agmsg replies (accepted/progress/completed/blocked) "
                    + "wake the orchestrator directly, so routine fast polling is not required in steady state; an "
                    + "explicit orchestrator timer remains supported only as a fallback/legacy option.",
                Alternative = "Timer-loop mode remains fully supported as the simpler ALTERNATIVE for a domain/repo "
                    + "that does not run an orchestrator thread: implementation and review threads self-schedule on "
                    + "recurring timers instead. Exactly one mode per domain/repo — the two must never run "
                    + "simultaneously for the same domain/repo. See `intent-cli guide prompt-matrix` for its setup.",
            },
            Roles = new[]
            {
                new GuideModelRole
                {
                    Actor = "Human product owner",
                    Responsibilities = new[]
                    {
                        "Express goals and constraints in chat.",
                        "Accept or reject drafts surfaced by the AI agent.",
                        "Authorize publish-boundary mutations (intent-target apply, draft promotion to GitHub issue).",
                        "Resolve clarifications when source-of-truth is ambiguous."
                    }
                },
                new GuideModelRole
                {
                    Actor = "Codex / Claude coding agent",
                    Responsibilities = new[]
                    {
                        "Run the conversation; ask one focused question at a time; surface tradeoffs.",
                        "Call `intent-cli` internally to retrieve rules (`guide rules`), state (`intent status`), discovery (`intent search` / `explain`), and planning facts (`intent next-slice --dry-run`).",
                        "Drive durable state through `interview record-answer --write` and `intent draft-from-interview --write` only after operator acceptance.",
                        "Implement child issues via the `worker next-action` / `worker claim` / `worker complete` flow when running the implementation loop."
                    }
                },
                new GuideModelRole
                {
                    Actor = "intent-cli",
                    Responsibilities = new[]
                    {
                        "Deterministic guide / artifact authority for rules, workflows, packet contracts, queue/runs state, and label transitions.",
                        "Read-only by default; explicit `--write` is required for any state mutation.",
                        "Owns label state for `intent-target`, `intent-pr-created`, and the installed transition surfaces.",
                        "Does not launch AI providers and does not replace operator decisions."
                    }
                },
                new GuideModelRole
                {
                    Actor = "Host git repository",
                    Responsibilities = new[]
                    {
                        "Canonical store for `intents/<domain>` (specs, rules, clarifications, drafts, interview sessions).",
                        "Canonical store for `.intent-cli` (queue-state, runs.jsonl, packets per execution unit).",
                        "Source of truth for submodule pointers and durable parent state."
                    }
                }
            },
            PrimaryDataPaths = new[]
            {
                "intents/<domain>/clarifications/open.md — open Hard Clarification blockers.",
                "intents/<domain>/interviews/<session>.json — durable interview Q/A artifacts.",
                "intents/<domain>/drafts/<session>.md — accepted-answer drafts before promotion.",
                ".intent-cli/queue-state.json — execution-unit lifecycle state.",
                ".intent-cli/runs.jsonl — append-only event log (pr-merged, closeout-recorded, etc.).",
                ".intent-cli/issues/<execution-unit>/ — per-unit packet (packet.yaml, implementation.md, review-context.md, github-body.md)."
            },
            OptionalAdvancedRuntime = new[]
            {
                "`intent-cli run` is for integration smoke / deterministic replay / local dogfooding; not the primary collaboration path.",
                "Direct AI-provider subprocess launch (Codex / Claude as a subprocess) is optional advanced runtime, not the default.",
                "Supervisor backend orchestration is optional advanced runtime; chat-first / CLI-internal is the default."
            },
            HardRules = new[]
            {
                "intent-cli must not launch Codex/Claude or any AI provider.",
                "intent-cli must not replace operator decisions; canonical mutation requires explicit operator acceptance.",
                "Routine collaboration must not require manually opening `intents/rules` files; agents call `intent-cli guide ...` instead.",
                "`intent-target` is the host-owned publish boundary; the child loop never adds or removes it.",
                "`intent-pr-created` is an issue-side completion marker; never apply it to a PR."
            }
        };
    }

    private static void WriteMarkdown(TextWriter writer)
    {
        var model = BuildModel();
        writer.WriteLine("# Guide model — primary collaboration model");
        writer.WriteLine();
        writer.WriteLine("## Primary model");
        writer.WriteLine();
        writer.WriteLine(model.PrimaryModel);
        writer.WriteLine();

        writer.WriteLine("## Execution orchestration model (PRIMARY for autonomous multi-thread execution)");
        writer.WriteLine();
        writer.WriteLine(model.ExecutionOrchestrationModel.Summary);
        writer.WriteLine();
        writer.WriteLine("### Four threads");
        writer.WriteLine();
        foreach (var role in model.ExecutionOrchestrationModel.Roles)
        {
            writer.WriteLine($"- {role}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **message-driven steady state** — {model.ExecutionOrchestrationModel.MessageDrivenSteadyState}");
        writer.WriteLine($"- **alternative** — {model.ExecutionOrchestrationModel.Alternative}");
        writer.WriteLine();

        writer.WriteLine("## Roles");
        foreach (var role in model.Roles)
        {
            writer.WriteLine();
            writer.WriteLine($"### {role.Actor}");
            writer.WriteLine();
            foreach (var responsibility in role.Responsibilities)
            {
                writer.WriteLine($"- {responsibility}");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Primary data paths (host git repository)");
        foreach (var path in model.PrimaryDataPaths)
        {
            writer.WriteLine($"- {path}");
        }
        writer.WriteLine();

        writer.WriteLine("## Optional advanced runtime");
        foreach (var item in model.OptionalAdvancedRuntime)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Hard rules");
        foreach (var rule in model.HardRules)
        {
            writer.WriteLine($"- {rule}");
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
        writer.WriteLine("guide model");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only summary of the chat-first / CLI-internal collaboration model and its hard rules.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };
}

internal sealed record GuideModelResult
{
    [JsonPropertyName("primary_model")]
    public required string PrimaryModel { get; init; }

    /// <summary>G540: the PRIMARY model for autonomous, multi-thread execution once intents are authored — four-thread agmsg orchestration, with timer-loop mode as the documented simpler alternative.</summary>
    [JsonPropertyName("execution_orchestration_model")]
    public required GuideModelExecutionOrchestration ExecutionOrchestrationModel { get; init; }

    [JsonPropertyName("roles")]
    public required IReadOnlyList<GuideModelRole> Roles { get; init; }

    [JsonPropertyName("primary_data_paths")]
    public required IReadOnlyList<string> PrimaryDataPaths { get; init; }

    [JsonPropertyName("optional_advanced_runtime")]
    public required IReadOnlyList<string> OptionalAdvancedRuntime { get; init; }

    [JsonPropertyName("hard_rules")]
    public required IReadOnlyList<string> HardRules { get; init; }
}

internal sealed record GuideModelRole
{
    [JsonPropertyName("actor")]
    public required string Actor { get; init; }

    [JsonPropertyName("responsibilities")]
    public required IReadOnlyList<string> Responsibilities { get; init; }
}

internal sealed record GuideModelExecutionOrchestration
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("roles")]
    public required IReadOnlyList<string> Roles { get; init; }

    [JsonPropertyName("message_driven_steady_state")]
    public required string MessageDrivenSteadyState { get; init; }

    [JsonPropertyName("alternative")]
    public required string Alternative { get; init; }
}
