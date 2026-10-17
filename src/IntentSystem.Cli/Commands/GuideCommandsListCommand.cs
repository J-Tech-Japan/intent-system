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
            Purpose = "Design-side action advisor (G465/G644/G662/G672): `intent-cli next --domain <d> --team <team> --target-repo <r> [--role <role>]` recommends ONE process, `supervision-setup` when a team declared in `[supervision] opt_in_teams` has no recorded cycle (G828: supervision is opt-in), and realignment when no improve run falls within the independently declared recency window. When a role has an installed contract, `--role` puts its pointer first; roles without a contract receive no invented pointer. Read-only; no quality grading, scheduler, auto-run, or stalled-work class."
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
            Purpose = "Scaffold the canonical packet directory (packet.yaml / implementation.md / review-context.md / github-body.md); read-only without --write. `packet validate-verification --execution-unit <unit> --format json|markdown` is an optional, config-independent structural coverage report for an explicit verification_contract; it never runs commands or verifies evidence, publication, or authority, and it is not a gate. G866/G867 provide local supplied-TRX binding and read-only coverage; G868 separately adds the explicit worker-completion gate. G869 optionally lets `review cross-runtime request --kind design` render one exact linked `verification_contract.design_review` selection using `safety_critical_ac` and `proof_id` as inert request data; omission preserves legacy request output, incomplete structure stays incomplete, and no command is executed. Hosted evidence and ruling-backed deferral remain open under #1866. `packet validate-sources --execution-unit <unit>` is a read-only, config-independent check for explicitly pinned local ruling sources and their authored body provenance; it is not full packet readiness."
        },
        new CommandGroupEntry
        {
            Name = "verification",
            Role = RoleChildImplementation,
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerImplementationLoop,
            Purpose = "Local supplied-TRX evidence observation: `verification evidence record|show|validate` checks one explicitly selected local-required G865 proof, embeds exact local TRX bytes in an opt-in canonical record, and compares its packet binding. Record defaults to preview; only explicit `--write` stores locally. `intent-cli verification evidence coverage --execution-unit <unit> --domain <domain> --team <team> --repo <owner/repo> --head-sha <40-lowerhex> --packet-sha256 <64-lowerhex> [--evidence <proof-id>=<record-id>]...` is a read-only report over at most 16 caller-selected local records; it reports missing/ineligible/unavailable proofs and leaves hosted-required rows not-applicable/not-observed. A complete report requires `structural_state=complete` with no G865 gaps and every local-required proof row current and passing. Standalone coverage exits 0 only for complete and 1 for other states. A complete local report does not authenticate a producer or command, assess AC acceptance, verify publication, or gate workers. `intent-cli worker result-summary --verification-report ...` can add the same advisory report without changing its exit; without the marker its prior output remains unchanged. Coverage does not change lifecycle status/outcome/exit. Execution is `not-observed`, producer unauthenticated, acceptance not assessed, and publication not verified; the record command does not infer an AC passed or run tests. Embedded TRX may contain test names, stdout, or machine paths; committing/pushing the artifact publishes those bytes, and this command does not scan them for privacy. Commit/plain-push and verify from a fresh clone for remote visibility."
        },
        new CommandGroupEntry
        {
            Name = "brief",
            Role = RoleChildImplementation,
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerImplementationLoop,
            Purpose = "Local deterministic role brief: `intent-cli brief render --role builder|reviewer --execution-unit <unit> --domain <domain> --team <team> --repo <owner/repo> --head-sha <40-lowerhex> --as-of <YYYY-MM-DDTHH:mm:ssZ> [--out <new-file>] --format json|markdown [--lessons-registry <registry-id> --lessons-sha256 <64-lowerhex> --lesson <lesson-id> ...]` combines only the role's fixed packet files with explicitly pinned, verified rulings and typed G865/G869 structure. The optional lesson group selects 1–16 exact rows from one local pinned registry; only selected applicable authored data is projected, never verified as authority or enforcement. Without the group, existing output and read behavior are unchanged and the registry is not inspected. The supplied head is not verified, execution is not observed, authority is not authenticated, and publication is not verified. No provider, Git, or GitHub operation occurs."
        },
        new CommandGroupEntry
        {
            Name = "worker",
            Role = RoleChildImplementation,
            Classification = ClassificationPrimary,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerImplementationLoop,
            Purpose = "Child implementation loop selector: next-action / claim / complete / result-summary / issue-preflight / pr-comment-preflight. GitHub-contract-only with --github-only. `worker complete` remains dry-run by default and keeps its exact legacy path and provider budgets without `--require-verification`. This explicit opt-in supports only issue/pr-created and pr/repair-pushed; it requires `--verification-unit`, `--verification-domain`, `--verification-team`, `--verification-head-sha <40-lowerhex>`, `--verification-packet-sha256 <64-lowerhex>`, and 0-16 repeated `--evidence <proof-id>=<record-id>` references. The G865 packet and G866 local records must exist under the selected repo root, also with --github-only. It reads G867 typed local coverage, observes the live target PR head, and refreshes head then coverage immediately before the first lifecycle write. Formats are text, JSON, and Markdown; syntax errors exit 1 and well-formed gate refusals exit 2. The guarantee is non-atomic and does not roll back later partial publication. It requires complete passing local proofs and refuses hosted proofs; G785 remains independent and its reason cannot bypass a failed gate. It does not authenticate execution or a producer, assess semantic acceptance, verify publication, or establish checkout identity. Commit a record on a separate evidence ref/root to preserve target PR head H1; committing it to the target PR branch changes that head. Hosted evidence, deferral, authenticated execution, and semantic AC evaluation remain unsupported. Example: `intent-cli worker complete --kind issue --number <issue> --outcome pr-created --pr <target-pr> --repo <owner/repo> --require-verification --verification-unit <unit> --verification-domain <domain> --verification-team <team> --verification-head-sha <40-lowerhex> --verification-packet-sha256 <64-lowerhex> --evidence P1=E1 --dry-run --format json`. The advisory `worker result-summary --verification-report ... --verification-evidence P1=E1` is distinct from gate `--require-verification` and `--evidence`."
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
            Purpose = "Session-layer transport, delivery topology, read-only live inspection, and host-local launch measurement (G570/G592/G604/G624/G685/G697/G713/G790): `intent-cli session-layer show` reads the team transport and `session-layer set --mode agmsg|herdr-only` records the choice — preferred `herdr-only` has fewer dependencies for a collocated single-machine team; deprecated `agmsg` + herdr (still works; removal planned after consumers are checked) remains for a distributed team or existing agmsg investment. The team shape (four-thread, five-thread or `solo-conductor`) is unchanged in either transport; no transport is primary. `session-layer topology record|record-orca-run|orca-runs|show|validate|move --domain <d> --team <t>` canonically writes, checks, and deliberately moves machine-local `.intent-cli/topology/<domain>/<team>.json` with directory-local CLI-owned ignore and internal identity; `record-orca-run` records Orca Run mailbox bindings on the required seat or solo sidecar, and `orca-runs` lists recorded binding health read-only across the host. `session-layer inspect --domain <d> --team <t>` is a read-only recorded-role/live-herdr observation with an optional bounded explicit-pane tail; it never focuses, prompts, sends keys, or manages processes. Use `intent-cli guide topology-workspace-move` for the installed dry-run-first move recipe. G814 `intent-cli session-layer model-resolution query` is a read-only exact scoped query and requires the newest matching scoped verified baseline plus stable observations of the exact selected target argv, local PID and actual process start time, host, and selected routing/role digest; omitted scope, missing baseline, refusal, incomplete identity, or mismatch stays human-required, and callers show returned scope, request, available observed evidence, and reason when asking the human. `model-resolution record` requires explicit READY target capture for verified outcomes; refused outcomes preserve the raw invocation/error without capture. Legacy unscoped rows remain readable diagnostics and cannot authorize a model. Topology record is dry-run by default, move is CAS-guarded and requires explicit `--write`, and show/validate/inspect are read-only; validate `--live` additionally reads herdr pane labels and never sends or sets them."
        },
        new CommandGroupEntry
        {
            Name = "team-mode",
            Role = RoleDesign,
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerOperator,
            Purpose = "G691 durable team shape, independent of session-layer transport: `intent-cli team-mode show|validate --domain <d> [--team <t>]` reads the command-produced record and `team-mode set --mode delivery|authoring-only|solo-conductor --write` records explicit, reversible transitions. Delivery is the default and byte-identical; authoring-only is the zero-herdr front-door shape with authoring-only guide next/bootstrap and named not-applicable supervision; solo-conductor (G833) is one conductor seat plus a fresh independent reviewer subagent, keeps every delivery gate, and needs no seat roster (`intent-cli guide solo-conductor`)."
        },
        new CommandGroupEntry
        {
            Name = "notify",
            Role = RoleDesign,
            Classification = ClassificationPrimary,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Transport-neutral role workflow (G578/G588/G629/G630/G658/G659/G671/G704/G712/G734/G744/G750): `intent-cli notify delegate|report|escalate|dispose|status|supervise`; `notify dispose` explicitly records a disposition for an open pending delegation, while late reports remain deliverable. Optional `notify supervise --event-mode` keeps blocking herdr waits inside the same supervisor process while interval cycles remain the safety floor; event-mode adoption still requires re-emission and explicit current-session registration because the artifact embeds the invocation, and any re-registration is an explicit operator action. `notify supervise install` rejects a bound below the interval, emits an observable artifact with routing-root/log paths, and reports success only after bounded first-cycle proof; it prints current-session registration/unregistration commands without executing install lifecycle commands. `notify supervise reconcile|uninstall --write` reports loaded-before/after, unloads managed jobs, and removes managed plus legacy login-persistent artifacts. `notify supervise shrink --domain <d> --team <t> --write` compacts existing stalls and cycles under the append lock, retains every record, resolves readable invariant evidence, and appends an audit. G744 `notify supervise archive --domain <d> --team <t> [--live-window-days <days>] --write` moves older cycle history into UTC period-named archives, keeps archive plus live readable, and discards nothing by default. G750/G827 `notify supervise repair-cycle-history --domain <d> --team <t> --write` adds the directory-local runtime-local ignore (cycle history and `stalls.jsonl`) and removes only already-tracked cycle-history and stall-log files from the index while preserving them on disk; shared policy/manifest files remain trackable. G827: shrink and archive stream files with memory bounded by the longest record. Duplicate writer findings use G699 backoff/park and never auto-kill. The CLI resolves the recorded session-layer mode internally, validates roles in the requested team topology, records pending delegations durably before delivery, and exposes preview status and bounded recovery supervision. In herdr-only mode pane residents receive at their recorded team-workspace pane while external residents receive delegate/report events through their recorded reader. Dry-run performs route, supervision, and scheduler-artifact resolution without prompting, replacing, redispatching, appending, writing, or executing; delivery, pending records, recovery mutations, dispositions, artifact writes, and lifecycle reconciliation require `--write`."
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
            Purpose = "Host-side label transitions and capability JSON: summary / doctor / host-review-preflight / issue-publish / pr-transition / check / complete / clarification-stop. doctor / reconcile / publish-recovery / stalled-work are the recovery-diagnostics subset (stalled-work is read-only: reports pending pipeline transitions with ages, never mutates). pr-created-stale-recovery (G836) removes a proven-stale intent-pr-created from an open issue whose recorded PR closed unmerged, dry-run by default, with a write-ahead audit."
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
            Purpose = "Review-side surfaces: closeout-plan (read-only), collect-signals / signal-handled (G374 worker-signal convergence), and `review cross-runtime request|record|status` (G834 implementation review and G835 `--kind design` packet-digest design review for teams declared in `[[cross_runtime_review.teams]]`: render a reviewer request the seat runs, record a verdict, and read the approval gate; `--model` is required for `copilot` and `opencode` on request and record (both kinds) and remains optional for `codex`, `claude`, and `cursor`; optional `--effort` is supported by `copilot` and `opencode`, and request-only optional `--opencode-provider-config` is supported by `opencode`; intent-cli never runs a reviewer)."
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
            Purpose = "GitHub issue surfaces incl. publish-flow (validate packet → create issue → durable-state next-step), draft / create / status / validate-body / prepare / publish-reviewed / plan-candidate, and sync-body (G825: validate-gated update of an already-published body; read-compare-write-verified, not atomic)."
        },
        new CommandGroupEntry
        {
            Name = "unit",
            Role = RoleChildImplementation,
            Classification = ClassificationPrimary,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Read-only per-unit lifecycle evidence: `unit status` composes current local receipts, a bounded exact-head GitHub review/CI observation, and optional pinned local ruling-source observations; unavailable provenance remains explicit and the result is never merge readiness or a gate."
        },
        new CommandGroupEntry
        {
            Name = "ruling",
            Role = RoleDesign,
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerOperator,
            Purpose = "Local immutable operator-ruling record and bounded read surfaces: `ruling record|show|validate` require explicit domain/team and record only the supplied authority assertion. They do not authenticate authority, publish to Git, or satisfy packet, status, approval, or release gates."
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
            Name = "guide steward-thread",
            Role = RoleHostReview,
            Classification = ClassificationPrimary,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "G807 Steward operating contract: a metadata-free transmission boundary that relays evidence, hands design questions to architect, review questions to reviewer, dispatch to orchestrator, preserves the G796 ruling boundary, and refuses fabricated authority."
        },
        new CommandGroupEntry
        {
            Name = "guide solo-conductor",
            Role = RoleDesign,
            Classification = ClassificationPrimary,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "G833 preview-through-1.x solo conductor contract: one conductor seat (architect, orchestrator, builder) runs the ten-step per-unit loop with labeled intent-cli/gh/git commands; a fresh independent reviewer subagent, started by the conductor, reviews every head. Metadata-free and render-only."
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
        new CommandGroupEntry
        {
            Name = "guide progress-supervision",
            Role = RoleHostReview,
            Classification = ClassificationPrimary,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "G812 correctness-first, metadata-free progress controller guide: durable transition deadlines, bounded recovery/bootstrap, compact health, incident learning, completion-driven receipts, and machine-readable role contracts."
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
