using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G334: External-user self-discovery for the <c>intent-cli guide</c>
/// family. An AI agent or human operator that knows nothing about the
/// project can run <c>intent-cli guide help</c> (or
/// <c>intent-cli guide --help</c>) and discover:
/// <list type="bullet">
///   <item>which guide subcommands exist and what each is for;</item>
///   <item>concrete one-line examples per subcommand;</item>
///   <item>the workflow-guide pointers for the major phases — init,
///         interview, packet, issue, automation, and bug repair — so
///         the agent can find the canonical entry without reading
///         local rules or skill files;</item>
///   <item>the standing prohibition against hand-editing metadata
///         when an installed <c>intent-cli</c> command exists.</item>
/// </list>
///
/// This command is read-only. It never reads parent host queue-state,
/// never calls <c>gh</c>, never mutates GitHub or files, and never
/// launches an AI provider. It is safe from a child implementation
/// cwd that does not carry its own <c>.intent-cli/</c> directory
/// (G300 / G333) and is therefore included in the G299 guide
/// bootstrap allow-list.
/// </summary>
internal static class GuideHelpCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide help [--format markdown|json]";

    /// <summary>
    /// G334 + G338 + G339: the canonical workflow-guide pointers.
    /// Each entry names a real <c>intent-cli</c> entry point so an
    /// external agent can follow it without rummaging through local
    /// rules or skill files. Phase IDs are stable: <c>init</c>,
    /// <c>interview</c>, <c>packet</c>, <c>issue</c>,
    /// <c>automation</c>, <c>bug-repair</c>, <c>implementation-loop</c>
    /// (G338), <c>review-next-slice-loop</c> (G338),
    /// <c>bug-to-intent-repair</c> (G339), and <c>supervision-setup</c>
    /// (G712).
    /// </summary>
    internal static readonly IReadOnlyList<WorkflowGuidePointer> WorkflowGuides = new[]
    {
        new WorkflowGuidePointer
        {
            Phase = "init",
            Command = "intent-cli guide workflow task init-host --format json",
            Purpose = "Pick a role for a NEW project (design / review-runtime / child-implementation) and get a scaffold plan + the exact `intent-cli intent init` incantation. Refuses to scaffold a child cwd that already carries `.intent-cli/` unless --force-host (G335).",
            SeeAlso = new[] { "intent-cli intent init --domain <name> --target-repo <owner/repo> --write", "intent-cli intake init", "intent-cli guide model --format json" }
        },
        new WorkflowGuidePointer
        {
            Phase = "interview",
            Command = "intent-cli guide workflow task intent-interview --format json",
            Purpose = "Product-owner interview / clarification loop guide (G336). Explains the background/question/options/pros-cons/recommendation question structure, distinguishes interview (new concept) from clarification (existing blocker), names durable artifact paths, and lists the canonical `intent-cli interview` / `intent-cli clarification` commands.",
            SeeAlso = new[] { "intent-cli interview next-question --domain <d> --format json", "intent-cli interview record-answer", "intent-cli interview compile", "intent-cli clarification next" }
        },
        new WorkflowGuidePointer
        {
            Phase = "packet",
            Command = "intent-cli guide workflow task packet-draft --format json",
            Purpose = "Packet directory layout + standalone issue contract sections every `github-body.md` must satisfy BEFORE `issue publish-flow` (G337). After reading the contract, run `intent-cli packet draft` to scaffold the four files.",
            SeeAlso = new[] { "intent-cli packet draft --execution-unit <id> --target-repo <owner/repo> --format markdown", "intent-cli issue validate-body --from-file <path> --format json", "intent-cli guide intent-work --format json" }
        },
        new WorkflowGuidePointer
        {
            Phase = "issue",
            Command = "intent-cli guide workflow task issue-publish --format json",
            Purpose = "Draft → create → publish-flow → automation issue-publish boundary guide (G337). Names the four publish stages, the intent-target FINAL-boundary rule, and the stop conditions that surface missing contract sections before GitHub mutation.",
            SeeAlso = new[] { "intent-cli issue publish-flow <id> --repo <r> --write --format json", "intent-cli automation issue-publish --repo <r> --issue <n> --write", "intent-cli guide intent-work --format json" }
        },
        new WorkflowGuidePointer
        {
            Phase = "automation",
            Command = "intent-cli automation summary --domain <name> --format json",
            Purpose = "Read the canonical label-driven capability JSON: which command performs which transition. Use `automation doctor --format json` to verify installed CLI surfaces are not stale.",
            SeeAlso = new[] { "intent-cli automation doctor", "intent-cli guide automation --format json" }
        },
        new WorkflowGuidePointer
        {
            Phase = "bug-repair",
            Command = "intent-cli guide worker pr-comment-fix --format json",
            Purpose = "Repair the narrow requested change on a PR branch. Selector: `intent-cli worker next-action ...` returning `action: pr-comment-fix`. Process at most one repair per wake.",
            SeeAlso = new[] { "intent-cli worker claim", "intent-cli worker complete", "intent-cli task fix-pr-comments" }
        },
        new WorkflowGuidePointer
        {
            Phase = "implementation-loop",
            Command = "intent-cli guide workflow task implementation-loop --target-repo <owner/repo> --agent claude --frequency 5m --format markdown",
            Purpose = "Generate a paste-ready child implementation-loop prompt from minimal inputs (target-repo, agent, frequency, base-branch-policy). Forwards to `guide prompt-matrix --mode child-loop`; the generated prompt carries the current label/claim/complete rules, G300/G330/G333 child-cwd contract, G311 closing reference gate, and G314 same-thread scheduler rule so the operator does not need them from memory (G338).",
            SeeAlso = new[] { "intent-cli guide prompt-matrix --mode child-loop --format json", "intent-cli worker next-action --repo <r> --format json", "intent-cli guide worker --format json" }
        },
        new WorkflowGuidePointer
        {
            Phase = "review-next-slice-loop",
            Command = "intent-cli guide workflow task review-next-slice-loop --domain <name> --target-repo <owner/repo> --agent claude --frequency 20m --format markdown",
            Purpose = "Generate a paste-ready host review / next-slice-loop prompt from minimal inputs (domain, target-repo, agent, frequency). Forwards to `guide prompt-matrix --mode host-loop`; the generated prompt carries the host-sync preflight gate, automation summary call, packet/issue lifecycle handoff, and label transition rules so the operator does not need them from memory (G338).",
            SeeAlso = new[] { "intent-cli guide prompt-matrix --mode host-loop --format json", "intent-cli automation host-sync-preflight --format json", "intent-cli automation summary --domain <name> --format json" }
        },
        new WorkflowGuidePointer
        {
            Phase = "supervision-setup",
            Command = "intent-cli guide workflow task supervision-setup --format json",
            Purpose = "Render the G712 session-scoped supervision contract from a bare metadata-free directory: install authors and proves first-cycle evidence, current-GUI registration is explicit, and reconcile/uninstall remove managed drift without touching unrelated jobs.",
            SeeAlso = new[] { "intent-cli notify supervise install", "intent-cli notify supervise reconcile --write --format json", "intent-cli notify supervise uninstall --write --format json", "intent-cli guide orchestrator-thread --format markdown" }
        },
        new WorkflowGuidePointer
        {
            Phase = "bug-to-intent-repair",
            Command = "intent-cli guide workflow task bug-to-intent-repair --format json",
            Purpose = "Guided bug-to-intent-repair workflow (G339). Five stages (report → triage → plan → intent-repair → implementation-repair) and five gap classifications (implementation-mismatch, intent-gap, packet-gap, rule-gap, metadata-workflow-gap). Recommends packet creation when the bug is in intent-cli rules/guidance rather than child code, preserves the original instruction reference and linked issue/PR refs across every link in the chain, and pins the FORBIDDEN raw `gh issue edit --add-label intent-target` rule from G337.",
            SeeAlso = new[] { "intent-cli guide workflow task packet-draft --format json", "intent-cli guide workflow task issue-publish --format json", "intent-cli packet draft --execution-unit <unit> --target-repo <owner/repo> --format markdown", "intent-cli intent next-slice --dry-run --domain <name> --target-repo <owner/repo> --format json" }
        }
    };

    /// <summary>
    /// G334: catalog entries for every <c>guide</c> subcommand the
    /// router exposes. Mirrors the dispatch table in
    /// <see cref="CommandRouter"/>; tests assert parity so the help
    /// surface cannot drift away from the implementation.
    /// </summary>
    internal static readonly IReadOnlyList<GuideSubcommandEntry> Subcommands = new[]
    {
        new GuideSubcommandEntry
        {
            Name = "start",
            Purpose = "Guide-first entrypoint (G393): the single command to run before intent/packet/issue/review/loop work. Points at the per-phase guide command, states the host/design vs metadata-free child-implementation roles, and emits short AGENTS.md/CLAUDE.md guide-first snippets.",
            Example = "intent-cli guide start --format json"
        },
        new GuideSubcommandEntry
        {
            Name = "help",
            Purpose = "List guide subcommands with examples and workflow-guide pointers (this surface).",
            Example = "intent-cli guide help --format json"
        },
        new GuideSubcommandEntry
        {
            Name = "model",
            Purpose = "Read-only collaboration model: chat-first, intent-cli internal, no AI providers launched from intent-cli.",
            Example = "intent-cli guide model --format json"
        },
        new GuideSubcommandEntry
        {
            Name = "improve",
            Purpose = "Design-thread improve / realignment process (G456/G662): review MVV, ADR/design notes, intent tree, packet history, and clarification history; preview `improve window --write` declares the independent bound and `improve record --write` appends durable run evidence after human/agent review without grading quality. Not a scheduler / provider launcher / loop-recovery diagnostic.",
            Example = "intent-cli guide improve --domain <domain> --format markdown"
        },
        new GuideSubcommandEntry
        {
            Name = "grill",
            Purpose = "Persistent interview mode (G463): once the user asks to grill a topic, stay in grill mode — generate an open-question backlog from current intents/packets/ADRs/docs, ask one question at a time, and continue after each answer until a stop condition holds. Built on the interview artifacts; not clarification (blocker resolution) and not improve (retrospective realignment); never auto-publishes.",
            Example = "intent-cli guide grill --domain <domain> --format markdown"
        },
        new GuideSubcommandEntry
        {
            Name = "stack",
            Purpose = "Packet backlog creation (G464): forward planning that creates an ordered packet backlog from the current intents (often ~10), commits/pushes durable state, and publishes at most the first GitHub issue by default. Distinct from improve (retrospective realignment), grill (open-question interview), clarification (blocker resolution), and runtime queue transitions.",
            Example = "intent-cli guide stack --domain <domain> --target-repo <owner/repo> --format markdown"
        },
        new GuideSubcommandEntry
        {
            Name = "next",
            Purpose = "Design-side action advisor (G465/G644/G662/G672): recommends one design-side process, checks supervision setup with --domain plus --team, and recommends realignment when no durable improve run falls within the independently declared recency window. With optional --role, a role that has an installed contract receives its pointer first; roles without a contract receive no invented pointer. Read-only and recency-only; never grades quality, schedules, or auto-executes.",
            Example = "intent-cli guide next --domain <domain> --team <team> --target-repo <owner/repo> --role design --format markdown"
        },
        new GuideSubcommandEntry
        {
            Name = "inspect",
            Purpose = "Evidence-backed observation (G466): observe the real app/CLI/UI/log/test behavior, separate observed evidence from inference, compare against expected intent, and turn gaps into packet candidates. First pass read-only; routes to stack/grill/improve/recovery/no-action. Guides how to use browser/computer-use/log/test tooling rather than replacing it; distinct from grill, stack, and improve.",
            Example = "intent-cli guide inspect --domain <domain> --target-repo <owner/repo> --format markdown"
        },
        new GuideSubcommandEntry
        {
            Name = "onboarding",
            Purpose = "First-call sequence for a fresh agent. With optional --role, a role that has an installed contract receives its pointer before the unchanged ordered list of guide / automation surfaces to read before any mutation.",
            Example = "intent-cli guide onboarding --role implementation --format json"
        },
        // G705: render-only public project-feedback guidance.
        new GuideSubcommandEntry
        {
            Name = "feedback",
            Purpose = "Public project-feedback channel guidance: names J-Tech-Japan/intent-system, renders a `gh issue create` form for deliberate human/design use, warns that issues are world-readable permanently, and never sends, writes telemetry, or publishes a child issue.",
            Example = "intent-cli guide feedback --format markdown"
        },
        new GuideSubcommandEntry
        {
            Name = "commands",
            Purpose = "Top-level command-group catalog with primary/support/advanced/experimental classification. Drives `guide commands list`.",
            Example = "intent-cli guide commands list --format json"
        },
        new GuideSubcommandEntry
        {
            Name = "rules",
            Purpose = "Read-only rules-by-topic surface; supports `guide rules list`.",
            Example = "intent-cli guide rules list --format json"
        },
        new GuideSubcommandEntry
        {
            Name = "workflow",
            Purpose = "Workflow suggestion / scaffold plans. Subcommands: suggest (pick the right intent-cli entry for an operator goal); task <name> (bounded scaffold/init/loop/repair/operations plan — today: `task init-host` (G335), `task intent-interview` (G336), `task packet-draft` / `task issue-publish` (G337), `task implementation-loop` / `task review-next-slice-loop` (G338), `task bug-to-intent-repair` (G339), `task supervision-setup` (G712)).",
            Example = "intent-cli guide workflow task supervision-setup --format markdown"
        },
        new GuideSubcommandEntry
        {
            Name = "collaborate",
            Purpose = "Chat-first collaboration prompt: how the human + AI agent + intent-cli model maps onto a single conversation.",
            Example = "intent-cli guide collaborate --format json"
        },
        new GuideSubcommandEntry
        {
            Name = "intent-work",
            Purpose = "Issue-publish / next-slice / packet workflow. Subcommands: setup / audit / next-slice-execution.",
            Example = "intent-cli guide intent-work --format json"
        },
        new GuideSubcommandEntry
        {
            Name = "automation",
            Purpose = "Host-side label transitions and capability prompts. Subcommands: setup / lint / local-loop.",
            Example = "intent-cli guide automation --format json"
        },
        new GuideSubcommandEntry
        {
            Name = "worker",
            Purpose = "Child implementation loop prompts. Subcommands: issue-to-pr / pr-comment-fix.",
            Example = "intent-cli guide worker --format json"
        },
        new GuideSubcommandEntry
        {
            Name = "review",
            Purpose = "Review-side prompts. Subcommand: run (G316 packet/intent-aware review).",
            Example = "intent-cli guide review --pr <n> --repo <owner/repo> --domain <d> --format json"
        },
        // G696: per-kind command-form guidance is an installed, read-only
        // registry rather than a permission or settings editor.
        new GuideSubcommandEntry
        {
            Name = "seat-commands",
            Purpose = "G696 versioned per-kind seat command-form registry: sanctioned forms, prefix-matching breakers, and sanctioned alternatives for Claude and Codex. Read-only; it never edits settings or approves commands.",
            Example = "intent-cli guide seat-commands --kind claude|codex --format markdown"
        },
        // G697: installed, read-only topology workspace-move recipe.
        new GuideSubcommandEntry
        {
            Name = "topology-workspace-move",
            Purpose = "G697 dry-run-first, CAS-guarded recipe for an operator-supplied topology workspace rebuild: inspect, preview full before/after, explicitly write, validate, and run notify preflight without querying herdr or editing topology JSON by hand.",
            Example = "intent-cli guide topology-workspace-move --domain <domain> --team <team> --format markdown"
        },
        new GuideSubcommandEntry
        {
            Name = "closeout",
            Purpose = "PR closeout prompts. Subcommand: run.",
            Example = "intent-cli guide closeout --format json"
        },
        new GuideSubcommandEntry
        {
            Name = "oneshot",
            Purpose = "Single-page deterministic prompt for a one-shot run — used when an agent has exactly one PR/issue scope.",
            Example = "intent-cli guide oneshot --pr <n> --repo <owner/repo> --format json"
        },
        new GuideSubcommandEntry
        {
            Name = "prompt-matrix",
            Purpose = "Mode-by-mode prompt matrix: child-loop, host-loop, child-oneshot, host-oneshot.",
            Example = "intent-cli guide prompt-matrix --mode child-loop --format json"
        },
        new GuideSubcommandEntry
        {
            Name = "prompt-template",
            Purpose = "Short outer-prompt catalog for common automation requests. Detailed conditions still come from intent-cli at execution time.",
            Example = "intent-cli guide prompt-template --kind implementation-loop --domain intent-cli --target-repo J-Tech-Japan/intent-system --format json"
        },
        new GuideSubcommandEntry
        {
            Name = "host-ownership",
            Purpose = "G326 role-scoped host ownership model: which role owns which durable-state slice.",
            Example = "intent-cli guide host-ownership --format json"
        },
        new GuideSubcommandEntry
        {
            Name = "question-style",
            Purpose = "G380 direct answer to how to ask product-owner clarification/interview questions: required elements + copyable template (one focused question, options, tradeoffs, recommendation, recording, stop-on-ambiguity).",
            Example = "intent-cli guide question-style --format json"
        },
        new GuideSubcommandEntry
        {
            Name = "interview-mode",
            Purpose = "G381 persistent goal-seeking interview protocol: research-first, one question at a time in dependency order, definition-of-ready stop conditions (packet-ready / issue-ready / clarification-required / blocked-by-user-decision / insufficient-context-after-research), decision-record output.",
            Example = "intent-cli guide interview-mode --format json"
        },
        new GuideSubcommandEntry
        {
            Name = "interview-readiness",
            Purpose = "G382 interview readiness checklist + scoring: pass --resolved <dimensions> to classify packet-ready / issue-ready / clarification-required / remaining-gaps, list missing dimensions, and get the next highest-value question.",
            Example = "intent-cli guide interview-readiness --resolved goal,scope,target,acceptance,verification --format json"
        },
        new GuideSubcommandEntry
        {
            Name = "review-verification-policy",
            Purpose = "G383 deterministic route for visible/manual/runtime-gated verification ACs so the review loop never re-asks the operator: standing-policy-approve / implementation-finding (PR feedback) / review-policy-gap (durable host signal recorded once).",
            Example = "intent-cli guide review-verification-policy --standing-policy --evidence source-mapping --format json"
        },
        // G438: external artifact intake guidance.
        new GuideSubcommandEntry
        {
            Name = "artifact-intake",
            Purpose = "G438 AI-agent-facing guidance for importing external GitHub issues and PRs. Three lanes: external-issue (issue intake before intent-target), external-pr-review (PR review before transitions), external-pr-adopt (rare explicit host adoption). Each lane requires lightweight packet/review-context metadata before any label mutation.",
            Example = "intent-cli guide artifact-intake --lane external-issue --repo <owner/repo> --format markdown"
        },
        // G487/G540: PRIMARY four-thread orchestrator-thread guidance over the
        // selected session transport.
        new GuideSubcommandEntry
        {
            Name = "orchestrator-thread",
            Purpose = "G701/G487/G540 paste-ready prompts for the PRIMARY four-thread orchestrator model (design/orchestrator/implementation/review) over its selected session transport, plus the implementation/review threads it delegates to. Renders `herdr-standard-layout/v1` (one team tab, labeled three-pane shape, exact creation and measured repair commands), the non-blocking layout-and-labels setup check, and `dialog-answering/v1`. agmsg is a message/progress/completion signal layer only; intent-cli and GitHub stay authoritative. Distinguishes the primary orchestrator-message model from the simpler timer-loop alternative (no mixed-mode timer races), pins the structured reply contract, the design-orchestrator double-check rule, an orchestrator first-wake, and safety boundaries. Timer-loop mode remains fully supported, not replaced.",
            Example = "intent-cli guide orchestrator-thread --domain <name> --target-repo <owner/repo> --agent <agent> --format markdown"
        },
        // G654: design-role operating contract, independent of agent kind.
        new GuideSubcommandEntry
        {
            Name = "design-thread",
            Purpose = "G701/G654 preview-through-1.x agent-kind-neutral design-thread operating contract: four valid wake outcomes, provenance and approval semantics, the structured `dialog-answering/v1` three-tier rule with the G690 distinction, merge-authority comparison, three-layer delegation verification, orchestration-owned recovery, record-based supervision-liveness checking, and outcome-shaped reporting.",
            Example = "intent-cli guide design-thread --domain <name> --team <team> --routing-root <host-root> --format markdown"
        },
        new GuideSubcommandEntry
        {
            Name = "steward-thread",
            Purpose = "G807 metadata-free Steward operating contract: relay evidence, hand design judgment to architect, review judgment to reviewer, dispatch to orchestrator, preserve G796 ruling bytes, and refuse fabricated authority.",
            Example = "intent-cli guide steward-thread --format markdown"
        },
        // G664: application conversation to herdr-only team genesis.
        new GuideSubcommandEntry
        {
            Name = "bootstrap",
            Purpose = "G664 preview-through-1.x app-front-door bootstrap: one guided pass asks the human for CLI/model and app kind, emits but never executes team/topology/supervision/delegation commands, resumes named partial state, and makes the application conversation's handoff explicit.",
            Example = "intent-cli guide bootstrap --domain <name> --team <team> --target-repo <owner/repo> --routing-root <host-root> --format markdown"
        },
        // G637: preview workspace convention; render-only and host-state-free.
        new GuideSubcommandEntry
        {
            Name = "workspace-layout",
            Purpose = "G637 preview-through-1.x team workspace convention: consume an operator-observed shape and explicit workspace/tab/pane IDs, then render the 40% orchestration-left / 60%-even implementation-review layout, topology-role labels, and the temporary-tab round trip for non-conforming shapes. Never queries or executes herdr.",
            Example = "intent-cli guide workspace-layout --workspace-id <workspace-id> --tab-id <tab-id> --shape three-column --format markdown"
        },
        // G563: G488's renderer is retired to a pointer — `intent-cli skill`
        // ships and installs the one artifact named `intent-cli`.
        new GuideSubcommandEntry
        {
            Name = "skill-pack",
            Purpose = "DEPRECATED (G563): renders only a pointer at the `skill` command group. The `intent-cli` agent skill is embedded in this CLI and installed by `intent-cli skill install`; `intent-cli skill list` / `diff` show what ships and whether an install has drifted. This command no longer renders a skill body or any copy-out instruction.",
            Example = "intent-cli skill install --target claude --scope user"
        }
    };

    /// <summary>
    /// G334: the metadata-mutation guidance the issue requires every
    /// guide-help surface to advertise — prefer intent-cli-backed
    /// metadata mutation over hand-editing.
    /// </summary>
    internal static readonly IReadOnlyList<string> MetadataMutationGuidance = new[]
    {
        "Prefer intent-cli-backed metadata mutation over hand-editing. Ask `intent-cli guide commands list --format json` (or `intent-cli automation summary --domain <d> --format json`) which command performs the transition, run that command, then validate the result.",
        "Routine automation MUST NOT directly edit queue-state, runs logs, publish artifacts, workflow labels, or runtime metadata by hand when a supported intent-cli command exists. Raw `gh ... edit --add-label` / `--remove-label` is forbidden for workflow labels.",
        "Child implementation loops operate from GitHub issues / PRs / comments / labels / implementation-repo files only. They MUST NOT inspect or mutate parent host queue-state, runs logs, packet directories, intent tree, review-runtime state, local rules, or local skills. Host metadata gaps are host-owned blockers, not child implementation tasks (G300 / G330 / G333). " + DispatcherSkillCarveOut.BoundaryClause
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            var payload = new GuideHelpResult
            {
                Usage = UsageLine,
                Subcommands = Subcommands,
                WorkflowGuides = WorkflowGuides,
                MetadataMutationGuidance = MetadataMutationGuidance
            };
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
        writer.WriteLine("# intent-cli guide — self-discovery for external users");
        writer.WriteLine();
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("`guide` is the read-only entry surface. Every subcommand returns Markdown by default and JSON via `--format json`. None of the guide subcommands mutate state or launch AI providers.");
        writer.WriteLine();

        writer.WriteLine("## Surfaces by operator role (G467)");
        writer.WriteLine();
        writer.WriteLine("Pick the surface by what you are trying to do. `intent-cli guide commands list` carries the same `role` category on every command group.");
        writer.WriteLine();
        writer.WriteLine("- **Design-side planning** — shape intent and cut work: `intent-cli improve` (realignment), `intent-cli grill` (persistent interview), `intent-cli stack` (packet backlog + first issue), `intent-cli inspect` (evidence-backed observation), `intent-cli next` (which of these to run), plus `intent` / `interview` / `packet` / `clarify`.");
        writer.WriteLine("- **Host review / next-slice** — review PRs and plan the next slice: `intent-cli guide review`, `intent-cli review closeout-plan`, `intent-cli automation host-review-preflight`, `intent-cli closeout pr`, `intent-cli issue publish-flow`, and the `guide workflow task review-next-slice-loop` prompt generator.");
        writer.WriteLine("- **Child implementation** — implement an issue into a PR: `intent-cli worker next-action / claim / complete / result-summary` (GitHub-contract-only with `--github-only`), and the `guide workflow task implementation-loop` prompt generator.");
        writer.WriteLine("- **Recovery / diagnostics** — repair operational state: `intent-cli automation doctor`, `intent-cli automation reconcile`, `intent-cli automation publish-recovery`, plus `metadata` / `queue` inspection.");
        writer.WriteLine("- **Loop-prompt creation** — turn a minimal user ask into a paste-ready loop prompt: `intent-cli guide prompt-template` / `prompt-matrix` (catalog) and `intent-cli guide workflow task implementation-loop|review-next-slice-loop` (generators with current fixed conditions).");
        writer.WriteLine();

        writer.WriteLine("## Subcommands");
        writer.WriteLine();
        writer.WriteLine("| subcommand | purpose | example |");
        writer.WriteLine("|------------|---------|---------|");
        foreach (var entry in Subcommands)
        {
            writer.WriteLine($"| `{entry.Name}` | {entry.Purpose} | `{entry.Example}` |");
        }
        writer.WriteLine();

        writer.WriteLine("## Workflow-guide pointers");
        writer.WriteLine();
        writer.WriteLine("Each major phase has a canonical entry. An external agent that does not know intent-system can follow these without reading local rules or skill files.");
        writer.WriteLine();
        foreach (var pointer in WorkflowGuides)
        {
            writer.WriteLine($"### {pointer.Phase}");
            writer.WriteLine();
            writer.WriteLine($"- Command: `{pointer.Command}`");
            writer.WriteLine($"- Purpose: {pointer.Purpose}");
            if (pointer.SeeAlso is { Count: > 0 } seeAlso)
            {
                writer.WriteLine("- See also:");
                foreach (var see in seeAlso)
                {
                    writer.WriteLine($"  - `{see}`");
                }
            }
            writer.WriteLine();
        }

        writer.WriteLine("## Metadata mutation guidance");
        writer.WriteLine();
        foreach (var line in MetadataMutationGuidance)
        {
            writer.WriteLine($"- {line}");
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
                case "--help":
                    // `guide help --help` is harmless; treat as an alias for
                    // running the help surface itself with the default
                    // format.
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };
}

/// <summary>
/// G334: structured pointer to a canonical workflow entry. <c>phase</c>
/// is a stable identifier (init / interview / packet / issue /
/// automation / bug-repair); <c>command</c> is the one-line CLI an
/// external agent can copy/paste.
/// </summary>
internal sealed record WorkflowGuidePointer
{
    [JsonPropertyName("phase")]
    public required string Phase { get; init; }

    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    [JsonPropertyName("see_also")]
    public IReadOnlyList<string>? SeeAlso { get; init; }
}

/// <summary>
/// G334: one row in the guide subcommand catalog.
/// </summary>
internal sealed record GuideSubcommandEntry
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    [JsonPropertyName("example")]
    public required string Example { get; init; }
}

/// <summary>
/// G334: full JSON payload for <c>intent-cli guide help --format
/// json</c>. Stable shape; consumers may pin against
/// <c>workflow_guides[].phase</c> identifiers.
/// </summary>
internal sealed record GuideHelpResult
{
    [JsonPropertyName("usage")]
    public required string Usage { get; init; }

    [JsonPropertyName("subcommands")]
    public required IReadOnlyList<GuideSubcommandEntry> Subcommands { get; init; }

    [JsonPropertyName("workflow_guides")]
    public required IReadOnlyList<WorkflowGuidePointer> WorkflowGuides { get; init; }

    [JsonPropertyName("metadata_mutation_guidance")]
    public required IReadOnlyList<string> MetadataMutationGuidance { get; init; }
}
