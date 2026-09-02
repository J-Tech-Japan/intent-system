using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G337: read-only <c>intent-cli guide workflow task packet-draft</c>.
/// Surfaces the canonical packet directory layout (the four files an
/// execution unit produces) and the standalone issue contract every
/// GitHub-body must satisfy BEFORE the operator runs
/// <c>intent-cli issue publish-flow</c>. External agents that have
/// never seen the project should be able to derive the packet shape
/// and the contract requirements from this surface alone — no local
/// rules, no skill prompts.
///
/// Pure read-only — never reads parent host queue-state, never calls
/// <c>gh</c>, never mutates state, never launches an AI provider.
/// </summary>
internal static class GuideWorkflowTaskPacketDraftCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide workflow task packet-draft [--format markdown|json]";

    /// <summary>
    /// G337: the four files every packet draft produces. Tests pin
    /// each entry by name and require the purpose to mention what
    /// the file does and when it is read.
    /// </summary>
    internal static readonly IReadOnlyList<PacketFileEntry> PacketFiles = new[]
    {
        new PacketFileEntry
        {
            Name = "packet.yaml",
            Purpose = "Structured metadata for the slice (execution-unit id, title, summary, intent references, packet ownership, prepared-by-runtime flag). Drives the validate / publish lifecycle.",
            ReadBy = "intent-cli packet draft / issue publish-flow / review closeout-plan / review run (G316)."
        },
        new PacketFileEntry
        {
            Name = "implementation.md",
            Purpose = "Design + implementation notes for the slice. Captures the design host's reasoning so review and child-implementation agents can converge.",
            ReadBy = "intent-cli review run / review closeout-plan / guide review (operators reading the spec)."
        },
        new PacketFileEntry
        {
            Name = "review-context.md",
            Purpose = "G316 packet-aware review context: the intent references, scope boundaries, prior decisions, and the explicit packet-versus-PR delta the reviewer must cite.",
            ReadBy = "intent-cli review run / guide review (the canonical input to the reviewer's approval summary)."
        },
        new PacketFileEntry
        {
            Name = "github-body.md",
            Purpose = "The standalone GitHub issue body. MUST satisfy the standalone issue contract below — it is rendered into the public GitHub issue, so anything missing here breaks external-agent discoverability.",
            ReadBy = "intent-cli issue draft / issue prepare / issue validate-body / issue publish / issue publish-flow / automation issue-publish."
        }
    };

    /// <summary>
    /// G337: standalone issue contract sections every <c>github-body.md</c>
    /// must carry. Tests pin each section by id; the guide JSON
    /// payload uses the same stable ids so consumers can branch on them.
    /// </summary>
    internal static readonly IReadOnlyList<IssueContractSection> IssueContractSections = new[]
    {
        new IssueContractSection { Section = "goal", Purpose = "One-paragraph statement of what shipping this slice produces. The agent reading the issue should be able to act on this without context." },
        new IssueContractSection { Section = "why-this-slice-exists-now", Purpose = "Why this slice cannot wait. Names the blocker and the user observation that forced the slice." },
        new IssueContractSection { Section = "current-observed-state", Purpose = "What an external agent or operator sees today, before the slice lands. Pins the regression target." },
        new IssueContractSection { Section = "accepted-baseline-you-may-assume", Purpose = "What the agent does not have to relitigate (collaboration model, intent-cli routing, no-local-rule rule, etc.)." },
        new IssueContractSection { Section = "target-repo-path-part", Purpose = "Exact repository / folder / surface the change must land in. Disambiguates host-vs-child mutation targets. Include the literal bullet `- Target paths: <comma- or space-separated paths>` with authored paths; do not infer it from other fields." },
        new IssueContractSection { Section = "in-scope", Purpose = "Bullet list of what is in scope. Mirrors the slice goal; one slice cuts cleanly so this list stays small." },
        new IssueContractSection { Section = "out-of-scope", Purpose = "Bullet list of what is explicitly NOT in this slice. Reviewers refuse PRs that broaden scope without an out-of-scope amendment." },
        // G482: standalone restatement and base branch policy are publish-ready
        // contract sections too; list them so a scaffolded packet is complete by
        // default and the agent does not have to memorize the full shape.
        new IssueContractSection { Section = "standalone-child-issue-contract", Purpose = "One-paragraph restatement of exactly what the child PR must deliver, readable on its own without the surrounding design thread. The packet scaffold emits this section by default (G482)." },
        new IssueContractSection { Section = "acceptance-criteria", Purpose = "Testable, externally-verifiable criteria. Every criterion maps to a concrete assertion the reviewer can run. When durable PR-body evidence is required, use exactly `actual output pasted` or `actual counts pasted`; worker-side detection recognizes only those phrases and requires a named fenced collected-output block." },
        new IssueContractSection { Section = "verification", Purpose = "Which tests / generated assertions prove the criteria. CI + the reviewer cite this list before approval (G316)." },
        new IssueContractSection { Section = "related-links", Purpose = "Predecessor slices and spec references (e.g. `specs/15-external-user-guided-workflow.md`). Operators trace the lineage without reading the host repo." },
        new IssueContractSection { Section = "base-branch-policy", Purpose = "The expected PR base branch, stated in the body so child implementation agents pick the correct base without reading host metadata (G347). Required by the publish gate." },
        new IssueContractSection { Section = "additional-required-guidance", Purpose = "Standing guardrails: prefer intent-cli-backed mutation, child isolation rule, no AI provider launch. Repeated on every slice so the issue is self-contained." }
    };

    /// <summary>
    /// G461: packet-time intent-maintenance prompts. A packet draft is the
    /// earliest, cheapest moment to capture design knowledge (intent placement,
    /// ADR / diagram / docs candidates, closeout writeback) while the design
    /// context is fresh. These prompts are OPTIONAL and backward-compatible —
    /// the agent may explicitly decline each one — but they make the
    /// `improve` reflection process (G456 / G460) the safety net rather than
    /// the only place this knowledge is considered. Tests pin each id.
    /// </summary>
    internal static readonly IReadOnlyList<IntentMaintenancePrompt> IntentMaintenancePrompts = new[]
    {
        new IntentMaintenancePrompt
        {
            Id = "facet-check-before-publish",
            Prompt = "Before publish, run `intent-cli intent facet-check --domain <domain> --packet <execution-unit> --format json` and retain the actual output. `no_facet_data: true` means the lexical check DID NOT RUN because there were no facet-annotated nodes; it never means the packet passed. The measured intent-cli example currently has no facet nodes, so the honest result is no data and human/agent review remains responsible for semantic alignment. Do not author facet nodes merely to turn this check green."
        },
        new IntentMaintenancePrompt { Id = "intent-placement", Prompt = "Which intent node does this slice support? Name the primary intent path (and any supporting intents). If no existing node fits, flag `new_intent_needed: true` with a one-line placement rationale instead of leaving the design knowledge trapped in packet history." },
        new IntentMaintenancePrompt { Id = "adr-candidate", Prompt = "Does this slice make an ADR-worthy decision (a hard-to-reverse choice, a rejected alternative, a new constraint)? If so, name the decision title and target ADR path; otherwise explicitly decline (`adr.required: false`). ADRs are not required for every packet." },
        new IntentMaintenancePrompt { Id = "diagram-candidate", Prompt = "Does a concept / workflow / topology / state diagram need to change because of this slice? Name the diagram type and target path, or decline (`diagram.required: false`)." },
        new IntentMaintenancePrompt { Id = "docs-update", Prompt = "Which user-facing docs must change so the documented behavior matches this slice once it lands? List target doc paths, or decline (`docs.required: false`)." },
        new IntentMaintenancePrompt { Id = "closeout-learning", Prompt = "What knowledge should be written back AFTER this slice lands (intent tree, ADR, diagram, docs)? Set `closeout_learning.write_back_required` and name the write-back targets so review / closeout can verify it happened or open a follow-up packet." },
        // G564: the declarations above are only worth reading if they are
        // honest — an undeclared obligation is invisible to closeout, to
        // review, and to `automation stalled-work`.
        new IntentMaintenancePrompt { Id = "co-evolution-duty", Prompt = IntentTreeCoEvolutionDuty.Duty + " " + IntentTreeCoEvolutionDuty.AuthoringRule + " Whatever you declare here is enforced after the slice lands: a closed-out unit with a declared-but-unrecorded write-back becomes an aging `knowledge-writeback-pending` item, cleared only by `intent-cli automation knowledge-writeback-record --execution-unit <unit> --commit <host-sha> --write`." },
        // G645: reachability is a separate declaration from knowledge
        // write-back. It is explicit so an absent answer cannot look like a
        // considered no-surface decision.
        new IntentMaintenancePrompt { Id = "guide-reachability", Prompt = GuideReachabilityDuty.Standard + " " + GuideReachabilityDuty.AuthoringRule + " A closed-out unit with declared routes stays visible as `guide-reachability-pending` until `intent-cli automation guide-reachability-record --execution-unit <unit> --commit <host-sha> --write` records the host update; an explicit no-surface declaration produces no debt." }
    };

    /// <summary>
    /// G337: the canonical commands an external agent runs to produce
    /// a packet draft. Tests pin every flag against the corresponding
    /// command source file (regression for the G336 PR #776 finding).
    /// </summary>
    internal static readonly IReadOnlyList<string> Commands = new[]
    {
        "intent-cli packet draft --execution-unit <execution-unit-id> [--domain <name>] [--target-repo <owner/repo>] [--team <team>] --dry-run --format markdown",
        "intent-cli packet draft --execution-unit <execution-unit-id> [--domain <name>] [--target-repo <owner/repo>] [--team <team>] --format json"
    };

    /// <summary>
    /// G337: stop conditions before any GitHub mutation. The guide
    /// surfaces missing contract sections BEFORE the agent runs
    /// `issue publish-flow`; this is the acceptance criterion
    /// (`surfaces missing contract sections before GitHub mutation`).
    /// </summary>
    internal static readonly IReadOnlyList<string> StopConditions = new[]
    {
        "Before publish, run `intent-cli intent facet-check --domain <domain> --packet <execution-unit> --format json` and retain its output. If `no_facet_data: true`, state that the lexical check did not run — never report it as passed — and perform the semantic facet/alignment review manually. The current intent-cli domain is the measured example: it has no facet nodes. This slice does not invent them.",
        "On a claims-enabled host, `packet draft` runs the shared claim verification first. Stop when `execution-unit:<id>` is unheld or held by another team; the refusal names the scope, holder, and holder team. A host with no claims store follows the legacy path byte-for-byte.",
        "`packet draft --dry-run` flags missing files: stop, repair the design host packet directory before publishing. Hand-editing the four files is allowed during design; routine automation mutates them through `packet draft --write` only.",
        "**Dry-run the publish validation BEFORE declaring the packet issue-ready (G482)**: never call a packet ready for GitHub issue creation until a publish-validation dry-run reports zero missing contract sections. Run `intent-cli issue validate-body --from-file <github-body.md> --format json` (offline body check), `intent-cli packet draft --execution-unit <id> --dry-run --format json` (re-scaffold + contract check), and — for the host loop — `intent-cli intent next-slice --dry-run --format json`; all three share one required-section source of truth, so a clean result on one is a clean result on all. A freshly scaffolded packet already carries every required section (Goal, Current Observed State, Why This Slice Exists Now, Accepted Baseline You May Assume, Target Repo / Path / Part, In Scope, Out Of Scope, Standalone Child Issue Contract, Acceptance Criteria, Verification, Related Links, Base Branch Policy); fill the placeholders, do not delete sections.",
        "`issue validate-body --from-file <github-body.md> --format json` reports either missing headings or a separate `target_paths_invalid` declaration error: stop, repair the body, validate again, only then move to `issue publish-flow`. When `Target Repo / Path / Part` is present, it must contain the literal `- Target paths: <comma- or space-separated paths>` line.",
        "`packet.yaml` lacks `intent_reference_paths` or names them with broad-domain placeholders: stop, narrow to PR-specific references. Broad-domain intent paths defeat G316 review-context isolation.",
        "Operator names `intent-target` on the GitHub issue manually (raw `gh issue edit --add-label intent-target`): refuse, surface the gap — `intent-target` is the FINAL publish boundary applied by `automation issue-publish --write` after `issue publish-flow` succeeds, never the default."
    };

    /// <summary>
    /// G337: explicit invariants every packet-draft surface advertises.
    /// Tests pin each invariant verbatim.
    /// </summary>
    internal static readonly IReadOnlyList<string> Invariants = new[]
    {
        "A packet is the standalone unit of intent: the GitHub issue body alone must let an external agent reproduce the implementation, the review, and the closeout decision. If `github-body.md` requires reading the host packet directory to be understood, the slice is not yet ready to publish.",
        "G680 claim-then-draft numbering (preview-through-1.x): compute N, acquire `execution-unit:<N>` by successful plain push, then scaffold. A losing claimant fast-forwards, recomputes the next number, and retries exactly once; a second loss stops. GitHub labels are visibility only and never replace this acquisition fact.",
        "Packet ownership is design-side; runtime-created packets carry an explicit `runtime-created` flag in `packet.yaml`. The review-runtime workspace MUST NOT silently edit the four files; it goes through `packet draft --write` with the runtime ownership label (G326).",
        "Prefer intent-cli-backed metadata mutation over hand-editing. Ask `intent-cli guide commands list --format json` or `intent-cli automation summary --domain <d> --format json` which command performs the transition, run that command, then validate the result.",
        "Child implementation loops MUST NOT inspect or mutate parent host queue-state, runs logs, packet directories, intent tree, review-runtime state, local rules, or local skills (G300 / G330 / G333). The packet directory is host-owned; child agents see only the rendered GitHub issue body. " + DispatcherSkillCarveOut.BoundaryClause,
        "Never launch AI providers (Claude / Codex / any LLM) from intent-cli. The chat-first model has the human agent driving the conversation; intent-cli emits text the agent acts on.",
        "Packet-time intent maintenance is the NORMAL path: every packet draft considers intent placement, ADR / diagram / docs candidates, and closeout writeback while the design context is fresh (G461). The `improve` reflection process (G456 / G460) is the later SAFETY NET that catches drift the packet-time check missed — it is not a substitute for thinking about knowledge maintenance when the packet is written. This metadata is OPTIONAL and backward-compatible: legacy packets without it stay valid, and the agent may explicitly decline each prompt.",
        "Guide reachability is a separate explicit packet answer (G645): name guide surface + routing role + target surface for each role-facing addition, or set `no_role_facing_surface: true`. The tool never infers a route or judges guide wording; a declared route is closeout debt until recorded, while an explicit no-surface answer is silent.",
        "Facet-check honesty is mandatory before publish (G662): execute the lexical scaffold and preserve its actual output. `no_facet_data: true` is evidence that the lexical check did not run, never a pass; intent-cli currently has no facet nodes, and packet drafting must not fabricate them to manufacture green evidence."
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
            var payload = new PacketDraftGuidance
            {
                Usage = UsageLine,
                PacketFiles = PacketFiles,
                IssueContractSections = IssueContractSections,
                IntentMaintenancePrompts = IntentMaintenancePrompts,
                Commands = Commands,
                StopConditions = StopConditions,
                Invariants = Invariants
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
        writer.WriteLine("# intent-cli — packet draft workflow guide");
        writer.WriteLine();
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("A packet is the standalone unit of intent. Four files; one execution unit.");
        writer.WriteLine();

        writer.WriteLine("## Packet files");
        writer.WriteLine();
        writer.WriteLine("| file | purpose | read by |");
        writer.WriteLine("|------|---------|---------|");
        foreach (var f in PacketFiles)
        {
            writer.WriteLine($"| `{f.Name}` | {f.Purpose} | {f.ReadBy} |");
        }
        writer.WriteLine();

        writer.WriteLine("## Standalone issue contract");
        writer.WriteLine();
        writer.WriteLine("Every `github-body.md` MUST carry these sections in order. Missing sections are caught by `intent-cli issue validate-body --from-file <path> --format json` BEFORE any GitHub mutation:");
        writer.WriteLine();
        foreach (var section in IssueContractSections)
        {
            writer.WriteLine($"- **{section.Section}** — {section.Purpose}");
        }
        writer.WriteLine();
        writer.WriteLine("When composing **target-repo-path-part**, put this declaration inside the section with the authored paths (do not infer it):");
        writer.WriteLine();
        writer.WriteLine("- Target paths: `<comma- or space-separated paths>`");
        writer.WriteLine();
        writer.WriteLine("`issue validate-body` reports a missing heading separately from a missing target-path declaration, so the two omissions remain distinguishable before GitHub mutation.");
        writer.WriteLine();

        writer.WriteLine("## Packet-time intent maintenance (optional, normal path)");
        writer.WriteLine();
        writer.WriteLine("A packet draft is the earliest, cheapest moment to capture design knowledge while the context is fresh. Consider — and explicitly answer or decline — each prompt. The metadata is optional and backward-compatible; `improve` (G456 / G460) is the later safety net, not a substitute:");
        writer.WriteLine();
        foreach (var prompt in IntentMaintenancePrompts)
        {
            writer.WriteLine($"- **{prompt.Id}** — {prompt.Prompt}");
        }
        writer.WriteLine();

        writer.WriteLine("## Commands");
        foreach (var c in Commands)
        {
            writer.WriteLine($"- `{c}`");
        }
        writer.WriteLine();

        writer.WriteLine("## Stop conditions (surface BEFORE GitHub mutation)");
        foreach (var s in StopConditions)
        {
            writer.WriteLine($"- {s}");
        }
        writer.WriteLine();

        writer.WriteLine("## Invariants");
        foreach (var line in Invariants)
        {
            writer.WriteLine($"- {line}");
        }
    }

    private static bool TryParseArguments(string[] args, out string format, out string error)
    {
        format = FormatMarkdown;
        error = string.Empty;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help":
                    break;
                case "--format":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requested = args[i + 1];
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }
                    format = requested;
                    i++;
                    break;
                default:
                    error = $"Unknown argument '{arg}'.";
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
/// G337: one file in the canonical packet directory.
/// </summary>
internal sealed record PacketFileEntry
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    [JsonPropertyName("read_by")]
    public required string ReadBy { get; init; }
}

/// <summary>
/// G337: one section of the standalone issue contract.
/// </summary>
internal sealed record IssueContractSection
{
    [JsonPropertyName("section")]
    public required string Section { get; init; }

    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }
}

/// <summary>
/// G461: one packet-time intent-maintenance prompt the agent should answer
/// or explicitly decline while the design context is fresh.
/// </summary>
internal sealed record IntentMaintenancePrompt
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }
}

/// <summary>
/// G337: full JSON payload for <c>guide workflow task packet-draft</c>.
/// </summary>
internal sealed record PacketDraftGuidance
{
    [JsonPropertyName("usage")]
    public required string Usage { get; init; }

    [JsonPropertyName("packet_files")]
    public required IReadOnlyList<PacketFileEntry> PacketFiles { get; init; }

    [JsonPropertyName("issue_contract_sections")]
    public required IReadOnlyList<IssueContractSection> IssueContractSections { get; init; }

    [JsonPropertyName("intent_maintenance_prompts")]
    public required IReadOnlyList<IntentMaintenancePrompt> IntentMaintenancePrompts { get; init; }

    [JsonPropertyName("commands")]
    public required IReadOnlyList<string> Commands { get; init; }

    [JsonPropertyName("stop_conditions")]
    public required IReadOnlyList<string> StopConditions { get; init; }

    [JsonPropertyName("invariants")]
    public required IReadOnlyList<string> Invariants { get; init; }
}
