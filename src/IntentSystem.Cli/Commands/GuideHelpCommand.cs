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
    /// <c>bug-to-intent-repair</c> (G339).
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
            Purpose = "Design-thread improve / realignment process (G456): periodically review MVV, ADR/design notes, intent tree, packet history, and clarification history for drift, short-term loops, and intent-strengthening opportunities. Run it by asking: `intent-cli で improve プロセスを実行してください。`. A reflection process, not a scheduler / provider launcher / loop-recovery diagnostic.",
            Example = "intent-cli guide improve --domain <domain> --format markdown"
        },
        new GuideSubcommandEntry
        {
            Name = "onboarding",
            Purpose = "First-call sequence for a fresh agent. Ordered list of guide / automation surfaces to read before any mutation.",
            Example = "intent-cli guide onboarding --format json"
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
            Purpose = "Workflow suggestion / scaffold plans. Subcommands: suggest (pick the right intent-cli entry for an operator goal); task <name> (bounded scaffold/init/loop/repair plan — today: `task init-host` (G335), `task intent-interview` (G336), `task packet-draft` / `task issue-publish` (G337), `task implementation-loop` / `task review-next-slice-loop` (G338), `task bug-to-intent-repair` (G339)).",
            Example = "intent-cli guide workflow task bug-to-intent-repair --format markdown"
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
        "Child implementation loops operate from GitHub issues / PRs / comments / labels / implementation-repo files only. They MUST NOT inspect or mutate parent host queue-state, runs logs, packet directories, intent tree, review-runtime state, local rules, or local skills. Host metadata gaps are host-owned blockers, not child implementation tasks (G300 / G330 / G333)."
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
