using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G257: Read-only <c>intent-cli guide commands list</c>. Returns each
/// top-level command group with a lifecycle classification (primary,
/// support, advanced, experimental), short purpose, mutability hint
/// (read-only / write), and recommended caller, so AI agents can pick
/// the right surface without misreading legacy groups (e.g. <c>run</c>)
/// as the primary product path. Never mutates state. Never launches an
/// AI provider.
/// </summary>
internal static class GuideCommandsListCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string ClassificationPrimary = "primary";
    private const string ClassificationSupport = "support";
    private const string MutabilityReadOnly = "read-only";
    private const string MutabilityMixed = "mixed";

    private const string CallerChatAgent = "chat-agent";
    private const string CallerImplementationLoop = "implementation-loop";
    private const string CallerHostLoop = "host-loop";
    private const string CallerOperator = "operator";

    // G467: operator-role categories so an unfamiliar AI agent can find the
    // right surface by role, not just by lifecycle classification.
    private const string RoleDesign = "design";                       // design-side planning (host design thread)
    private const string RoleHostReview = "host-review";              // host review / next-slice / publish
    private const string RoleChildImplementation = "child-implementation"; // child implementation loop
    private const string RoleRecoveryDiagnostics = "recovery-diagnostics"; // operational recovery / diagnostics
    private const string RoleAdvancedDeveloper = "advanced-developer";     // advanced / developer / dogfooding

    private const string UsageLine =
        "Usage: intent-cli guide commands list [--format markdown|json]";

    internal static readonly IReadOnlyList<CommandGroupEntry> Groups = new[]
    {
        new CommandGroupEntry
        {
            Name = "guide",
            Role = RoleDesign,
            Classification = ClassificationPrimary,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Operator-facing guidance: collaboration model, rules-by-topic, workflow suggestion, prompt-template catalog, one-shot/automation/review prompts. The single entry point an unfamiliar AI agent reads first."
        },
        new CommandGroupEntry
        {
            Name = "improve",
            Role = RoleDesign,
            Classification = ClassificationPrimary,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Design-thread improve / realignment process (G456/G457): `intent-cli improve --domain <d>` (alias of `guide improve`) returns the periodic MVV / ADR / intent-tree / packet-history / clarification-history reflection review. A design-thread reflection process — NOT bug-to-intent-repair, host-loop recovery, state-doctor, dirty-state repair, or any operational diagnostic."
        },
        new CommandGroupEntry
        {
            Name = "grill",
            Role = RoleDesign,
            Classification = ClassificationPrimary,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Persistent interview mode (G463): `intent-cli grill --domain <d>` (alias of `guide grill`) stays in grill mode, generates an open-question backlog from current intents, and asks one question at a time until a stop condition. Built on the interview artifacts; not clarification, not improve."
        },
        new CommandGroupEntry
        {
            Name = "stack",
            Role = RoleDesign,
            Classification = ClassificationPrimary,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Packet backlog creation (G464): `intent-cli stack --domain <d> --target-repo <r>` (alias of `guide stack`) creates an ordered packet backlog from current intents, commits/pushes durable state, and publishes at most the first GitHub issue by default. Forward planning — not improve, grill, clarification, or a queue transition."
        },
        new CommandGroupEntry
        {
            Name = "next",
            Role = RoleDesign,
            Classification = ClassificationPrimary,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Design-side action advisor (G465): `intent-cli next --domain <d> --target-repo <r>` (alias of `guide next`) recommends ONE design-side process — grill / stack / improve / inspect / issue-publish / review / recovery / idle — with a paste-ready suggested prompt. Read-only; never auto-executes."
        },
        new CommandGroupEntry
        {
            Name = "inspect",
            Role = RoleDesign,
            Classification = ClassificationPrimary,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Evidence-backed observation (G466): `intent-cli inspect --domain <d> --target-repo <r>` (alias of `guide inspect`) observes real app/CLI/UI/log/test behavior, separates observed evidence from inference, and turns gaps into packet candidates. NOT status / next-slice checking; routes to stack / grill / improve / recovery / no-action."
        },
        new CommandGroupEntry
        {
            Name = "intent",
            Role = RoleDesign,
            Classification = ClassificationPrimary,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Host-domain init (`intent init --domain <name> [--target-repo <owner/repo>] [--write]`) plus read-only status / search / explain / next-slice planning and draft-from-interview (write requires --write)."
        },
        new CommandGroupEntry
        {
            Name = "interview",
            Role = RoleDesign,
            Classification = ClassificationPrimary,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Durable per-domain Q/A artifact: next-question/record-answer/compile (older start/answer/resume retained). grill is the persistent wrapper over this surface."
        },
        new CommandGroupEntry
        {
            Name = "packet",
            Role = RoleDesign,
            Classification = ClassificationPrimary,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Scaffold the canonical packet directory (packet.yaml / implementation.md / review-context.md / github-body.md); read-only without --write."
        },
        new CommandGroupEntry
        {
            Name = "worker",
            Role = RoleChildImplementation,
            Classification = ClassificationPrimary,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerImplementationLoop,
            Purpose = "Child implementation loop selector: next-action / claim / complete / result-summary / issue-preflight / pr-comment-preflight. GitHub-contract-only with --github-only."
        },
        // G563: the shipped SKILL.md routes agents to this catalog and then
        // names `skill list/diff/install`, so the catalog has to know the
        // group exists. Purpose stays consistent with CommandRouter's help.
        new CommandGroupEntry
        {
            Name = "skill",
            Role = RoleChildImplementation,
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerOperator,
            Purpose = "Agent-skill install surface (G559): `intent-cli skill list` / `install --target claude|codex|copilot|all [--scope user|repo] [--force]` / `diff` (installs the embedded SKILL.md into each platform's skill location). `list` / `diff` are read-only; `install` writes, and without `--force` it refuses the whole plan before any write when a destination has drifted."
        },
        new CommandGroupEntry
        {
            Name = "session-layer",
            Role = RoleDesign,
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerOperator,
            Purpose = "Session-layer transport selection (G570): `intent-cli session-layer show --domain <d> [--team <t>]` and `session-layer set --domain <d> [--team <t>] --mode agmsg|herdr-only [--write]`. Records which transport a team's four threads use — `agmsg` is PRIMARY, `herdr-only` is PREVIEW and scopes that qualifier to the TRANSPORT, never to the four-thread model. Defaults to agmsg when unrecorded, is idempotent, keeps a transition trail, and is reversible in both directions; `show` is read-only and `set` writes only with `--write`. The recorded mode routes `guide orchestrator-thread`."
        },
        new CommandGroupEntry
        {
            Name = "notify",
            Role = RoleDesign,
            Classification = ClassificationPrimary,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Transport-neutral role workflow (G578/G588): `intent-cli notify delegate|report|escalate`. The CLI resolves the recorded session-layer mode internally, validates roles in the requested team topology, requires only the recipient to be deliverable, and embeds the canonical report command in delegated work. In herdr-only mode pane residents receive at their recorded team-workspace pane while external residents receive delegate/report events through their recorded reader. Dry-run performs the same route resolution without prompting or appending; delivery and event append require `--write`."
        },
        new CommandGroupEntry
        {
            Name = "automation",
            Role = RoleHostReview,
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerHostLoop,
            Purpose = "Host-side label transitions and capability JSON: summary / doctor / host-review-preflight / issue-publish / pr-transition / check / complete / clarification-stop. doctor / reconcile / publish-recovery / stalled-work are the recovery-diagnostics subset (stalled-work is read-only: reports pending pipeline transitions with ages, never mutates)."
        },
        new CommandGroupEntry
        {
            Name = "metadata",
            Role = RoleRecoveryDiagnostics,
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerHostLoop,
            Purpose = "Read-only metadata validate plus the bounded controlled writer (`metadata update --mode completed-closeout`)."
        },
        new CommandGroupEntry
        {
            Name = "review",
            Role = RoleHostReview,
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerHostLoop,
            Purpose = "Review-side surfaces: closeout-plan (read-only), collect-signals / signal-handled (G374 worker-signal convergence)."
        },
        new CommandGroupEntry
        {
            Name = "closeout",
            Role = RoleHostReview,
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerHostLoop,
            Purpose = "PR closeout: queue completion + runs append + continuation hint (`closeout pr --pr <n> --write`)."
        },
        new CommandGroupEntry
        {
            Name = "issue",
            Role = RoleHostReview,
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerOperator,
            Purpose = "GitHub issue surfaces incl. publish-flow (validate packet → create issue → durable-state next-step), draft / create / status / validate-body / prepare / publish-reviewed / plan-candidate."
        },
        new CommandGroupEntry
        {
            Name = "queue",
            Role = RoleRecoveryDiagnostics,
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerOperator,
            Purpose = "Queue-state inspection and bounded transitions: list / show / next / enqueue / dispatch / transition."
        },
        new CommandGroupEntry
        {
            Name = "status",
            Role = RoleDesign,
            Classification = ClassificationSupport,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Read-only status brief used by AI tasking threads (G179); see `intent status` for the canonical domain summary."
        },
        new CommandGroupEntry
        {
            Name = "context",
            Role = RoleDesign,
            Classification = ClassificationSupport,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Read-only context surfaces consumed by AI tasking threads."
        },
        new CommandGroupEntry
        {
            Name = "clarify",
            Role = RoleDesign,
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerOperator,
            Purpose = "Clarification artifact CRUD: open / list / answer / draft / record. Operator-driven; not the chat-agent's first call."
        },
        new CommandGroupEntry
        {
            Name = "next-slice",
            Role = RoleDesign,
            Classification = ClassificationSupport,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerOperator,
            Purpose = "Older next-slice planning surface; prefer `intent next-slice --dry-run` for collaborative shaping."
        },
        new CommandGroupEntry
        {
            Name = "task",
            Role = RoleAdvancedDeveloper,
            Classification = ClassificationSupport,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerOperator,
            Purpose = "G317 explicit one-shot task planners (issue-to-pr / review-pr / fix-pr-comments / publish-next-issue). Returns a bounded executable contract — preconditions, steps, label transitions, abort conditions — for controllers that already know the target. Read-only: never calls gh, never mutates state, never launches AI providers."
        },
        new CommandGroupEntry
        {
            Name = "guide prompt-template",
            Role = RoleDesign,
            Classification = ClassificationSupport,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Loop-prompt creation: `intent-cli guide prompt-template` returns a single paste-ready prompt by name. The short way to turn a minimal user ask (`intent-cli に聞いて...`) into a fixed-condition prompt."
        },
        new CommandGroupEntry
        {
            Name = "guide prompt-matrix",
            Role = RoleDesign,
            Classification = ClassificationSupport,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Loop-prompt creation: `intent-cli guide prompt-matrix` is the catalog of available prompt templates (design / host-review / child-implementation / recovery) so an agent can discover which loop or one-shot prompt to request."
        },
        new CommandGroupEntry
        {
            Name = "guide workflow task implementation-loop",
            Role = RoleChildImplementation,
            Classification = ClassificationSupport,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Canonical child implementation-loop prompt generator: `intent-cli guide workflow task implementation-loop --target-repo <r> --agent claude --frequency 5m --format markdown` emits the paste-ready loop prompt with current claim/complete/label rules (G338)."
        },
        new CommandGroupEntry
        {
            Name = "guide workflow task review-next-slice-loop",
            Role = RoleHostReview,
            Classification = ClassificationSupport,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Canonical host review / next-slice-loop prompt generator: `intent-cli guide workflow task review-next-slice-loop --domain <d> --target-repo <r> --agent claude --frequency 20m --format markdown` emits the paste-ready host loop prompt with current preflight + packet/issue lifecycle rules (G338)."
        },
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
            var payload = new GuideCommandsListResult { Groups = Groups };
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
        writer.WriteLine("# Guide commands — top-level groups");
        writer.WriteLine();
        writer.WriteLine("Operator-role categories (G467): `design` (design-side planning — improve / grill / stack / next / inspect / intent / interview / packet / clarify), `host-review` (host review / next-slice / publish — review / closeout / automation / issue), `child-implementation` (child loop — worker), `recovery-diagnostics` (operational recovery — automation doctor / metadata / queue), `advanced-developer` (one-shot planners / dogfooding — task).");
        writer.WriteLine();
        writer.WriteLine("Lifecycle classification: `primary` (chat-agent's first calls), `support` (used inside the same flow).");
        writer.WriteLine();
        writer.WriteLine("Loop-prompt creation: ask for a loop prompt with a minimal user request — `intent-cli guide prompt-template` / `prompt-matrix` catalog them, and `intent-cli guide workflow task implementation-loop` / `review-next-slice-loop` emit the paste-ready prompt with current fixed conditions.");
        writer.WriteLine();
        writer.WriteLine("| group | role | classification | mutability | caller | purpose |");
        writer.WriteLine("|-------|------|----------------|------------|--------|---------|");
        foreach (var group in Groups)
        {
            writer.WriteLine($"| {group.Name} | {group.Role} | {group.Classification} | {group.Mutability} | {group.RecommendedCaller} | {EscapeCell(group.Purpose)} |");
        }
    }

    // G563: purposes may legitimately contain `|` (the `skill` group's
    // `--target claude|codex|...` alternatives). An unescaped pipe silently
    // splits the row into extra columns, so the cell is escaped at render
    // time rather than the text being reworded to dodge the character. JSON
    // output is unaffected — this is a markdown-table concern only.
    private static string EscapeCell(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

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
        writer.WriteLine("guide commands list");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only role-based catalog of intent-cli command groups: each entry carries an operator-role category (design / host-review / child-implementation / recovery-diagnostics / advanced-developer) and a primary/support lifecycle classification.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };
}

internal sealed record CommandGroupEntry
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>G467: operator-role category (design / host-review / child-implementation / recovery-diagnostics / advanced-developer).</summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("classification")]
    public required string Classification { get; init; }

    [JsonPropertyName("mutability")]
    public required string Mutability { get; init; }

    [JsonPropertyName("recommended_caller")]
    public required string RecommendedCaller { get; init; }

    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }
}

internal sealed record GuideCommandsListResult
{
    [JsonPropertyName("groups")]
    public required IReadOnlyList<CommandGroupEntry> Groups { get; init; }
}
