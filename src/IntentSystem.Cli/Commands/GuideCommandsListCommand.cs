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
            Purpose = "Operator-facing guidance: collaboration model, rules-by-topic, workflow suggestion, public project-feedback, prompt-template catalog, one-shot/automation/review prompts. The single entry point an unfamiliar AI agent reads first."
        },
        new CommandGroupEntry
        {
            Name = "improve",
            Role = RoleDesign,
            Classification = ClassificationPrimary,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Design-thread improve / realignment process (G456/G457/G662): `intent-cli improve --domain <d>` returns the review guide; preview `improve window --write` declares recency independently like a supervision bound, and after human/agent review `improve record --write` appends run evidence without grading quality. Not a scheduler, auto-run, stalled-work class, or operational diagnostic."
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
            Purpose = "Design-side action advisor (G465/G644/G662/G672): `intent-cli next --domain <d> --team <team> --target-repo <r> [--role <role>]` recommends ONE process, `supervision-setup` when the team's cycle is missing, and realignment when no improve run falls within the independently declared recency window. When a role has an installed contract, `--role` puts its pointer first; roles without a contract receive no invented pointer. Read-only; no quality grading, scheduler, auto-run, or stalled-work class."
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
            Name = "update",
            Role = RoleChildImplementation,
            Classification = ClassificationPrimary,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerChatAgent,
            Purpose = "G703 channel-aware self-update: `intent-cli update` derives the installation channel from the fully resolved executable path on every run; `--check` reports current/latest/would-be action without process spawn or writes, and unknown paths fail closed."
        },
        new CommandGroupEntry
        {
            Name = "session-layer",
            Role = RoleDesign,
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerOperator,
            Purpose = "Session-layer transport, delivery topology, and host-local launch measurement (G570/G592/G604/G624/G685/G697/G713): `intent-cli session-layer show` reads the team transport and `session-layer set --mode agmsg|herdr-only` records the choice — preferred `herdr-only` has fewer dependencies for a collocated single-machine team; supported, non-retired `agmsg` + herdr remains for a distributed team or existing agmsg investment. The PRIMARY four-thread model is unchanged in either transport; no transport is primary. `session-layer topology record|show|validate|move --domain <d> --team <t>` canonically writes, checks, and deliberately moves machine-local `.intent-cli/topology/<domain>/<team>.json` with directory-local CLI-owned ignore and internal identity. Use `intent-cli guide topology-workspace-move` for the installed dry-run-first move recipe. `session-layer model-resolution record|query` appends or reads host-local verified/refused model invocation evidence; it launches no provider, validates no id, and ships no model catalogue. Topology record is dry-run by default, move is CAS-guarded and requires explicit `--write`, and show/validate are read-only; validate `--live` additionally reads herdr pane labels and never sends or sets them."
        },
        new CommandGroupEntry
        {
            Name = "team-mode",
            Role = RoleDesign,
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerOperator,
            Purpose = "G691 durable team shape, independent of session-layer transport: `intent-cli team-mode show|validate --domain <d> [--team <t>]` reads the command-produced record and `team-mode set --mode delivery|authoring-only --write` records explicit, reversible transitions. Delivery is the default and byte-identical; authoring-only is the zero-herdr front-door shape with authoring-only guide next/bootstrap and named not-applicable supervision."
        },
        new CommandGroupEntry
        {
            Name = "notify",
            Role = RoleDesign,
            Classification = ClassificationPrimary,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Transport-neutral role workflow (G578/G588/G629/G630/G658/G659/G671/G704/G712/G734): `intent-cli notify delegate|report|escalate|dispose|status|supervise`; `notify dispose` explicitly records a disposition for an open pending delegation, while late reports remain deliverable. Optional `notify supervise --event-mode` keeps blocking herdr waits inside the same supervisor process while interval cycles remain the safety floor; event-mode adoption still requires re-emission and explicit current-session registration because the artifact embeds the invocation, and any re-registration is an explicit operator action. `notify supervise install` rejects a bound below the interval, emits an observable artifact with routing-root/log paths, and reports success only after bounded first-cycle proof; it prints current-session registration/unregistration commands without executing install lifecycle commands. `notify supervise reconcile|uninstall --write` reports loaded-before/after, unloads managed jobs, and removes managed plus legacy login-persistent artifacts. `notify supervise shrink --domain <d> --team <t> --write` compacts existing stalls and cycles under the append lock, retains every record, resolves readable invariant evidence, and appends an audit. Duplicate writer findings use G699 backoff/park and never auto-kill. The CLI resolves the recorded session-layer mode internally, validates roles in the requested team topology, records pending delegations durably before delivery, and exposes preview status and bounded recovery supervision. In herdr-only mode pane residents receive at their recorded team-workspace pane while external residents receive delegate/report events through their recorded reader. Dry-run performs route, supervision, and scheduler-artifact resolution without prompting, replacing, redispatching, appending, writing, or executing; delivery, pending records, recovery mutations, dispositions, artifact writes, and lifecycle reconciliation require `--write`."
        },
        new CommandGroupEntry
        {
            Name = "prompt-class",
            Role = RoleDesign,
            Classification = ClassificationSupport,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "G689 read-only prompt vocabulary inspection: `prompt-class list` enumerates recipe-backed classes and `prompt-class describe codex:shell-command` exposes the extracted-payload contract and project-test / owned-scratch-delete / exact-command-once shell scopes. It never records policy, answers a dialog, or mutates host state."
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
            Name = "guide workspace-layout",
            Role = RoleDesign,
            Classification = ClassificationSupport,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerOperator,
            Purpose = "G637 preview-through-1.x layout convention: `intent-cli guide workspace-layout` consumes an operator-supplied workspace snapshot and renders explicit herdr pane rename/resize commands plus the temporary-tab round trip for non-conforming shapes. It never queries or executes herdr."
        },
        new CommandGroupEntry
        {
            Name = "guide orchestrator-thread",
            Role = RoleHostReview,
            Classification = ClassificationPrimary,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "G701/G487/G540 primary orchestrator-thread guide: renders the versioned `herdr-standard-layout/v1` registry (one team tab, orchestration left, implementation above review, labels, exact creation and measured repair commands), the non-blocking `layout-and-labels` setup check, and the `dialog-answering/v1` authority rule. Metadata-free and read-only; never queries or executes herdr.",
        },
        new CommandGroupEntry
        {
            Name = "guide design-thread",
            Role = RoleDesign,
            Classification = ClassificationPrimary,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "G701/G654 preview-through-1.x agent-kind-neutral design-thread operating contract: four outcome wakes, provenance, transaction-scoped approval, the structured `dialog-answering/v1` three-tier rule, merge-authority comparison, three-layer verification, orchestration-owned recovery, supervision-liveness checking, and outcome-shaped reporting."
        },
        new CommandGroupEntry
        {
            Name = "guide bootstrap",
            Role = RoleDesign,
            Classification = ClassificationPrimary,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "G664 preview-through-1.x application-front-door bootstrap: asks the human for each seat's CLI/model and the app kind, composes herdr-only workspace/topology/supervision/design-placement/first-delegation commands, detects join and partial states, and ends with the explicit front-door handoff. Render-only; executes nothing."
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
        new CommandGroupEntry
        {
            Name = "guide workflow task supervision-setup",
            Role = RoleHostReview,
            Classification = ClassificationPrimary,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "G712 metadata-free supervision setup route: `intent-cli guide workflow task supervision-setup --format json|markdown` renders the shipped session-scoped install, current-GUI registration, reconcile, and uninstall contract without reading host metadata or executing lifecycle commands."
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
