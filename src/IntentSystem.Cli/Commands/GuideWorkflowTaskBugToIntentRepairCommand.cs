using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G339: read-only <c>intent-cli guide workflow task bug-to-intent-repair</c>.
/// Surfaces the guided bug-to-intent-repair workflow so observed
/// automation failures and product bugs feed back into intent repair
/// packets (and only optionally into child implementation issues),
/// not just ad-hoc PR comments. The guide carries five stages
/// (report → triage → plan → intent-repair → implementation-repair),
/// five gap classifications (implementation-mismatch, intent-gap,
/// packet-gap, rule-gap, metadata-workflow-gap), the canonical
/// intent-cli commands per stage, stop conditions that surface
/// missing artifacts BEFORE GitHub mutation, and the invariants
/// every backflow must preserve (original refs, intent-cli-backed
/// mutation, child isolation rule).
///
/// Pure read-only — never reads parent host queue-state, never calls
/// <c>gh</c>, never mutates state, never launches an AI provider.
/// </summary>
internal static class GuideWorkflowTaskBugToIntentRepairCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide workflow task bug-to-intent-repair [--format markdown|json]";

    /// <summary>
    /// G339: the five workflow stages. Stable IDs (report / triage /
    /// plan / intent-repair / implementation-repair) — tests pin
    /// each by ID and the order must remain stable.
    /// </summary>
    internal static readonly IReadOnlyList<BugRepairStage> Stages = new[]
    {
        new BugRepairStage
        {
            Stage = "report",
            Purpose = "Capture the observed problem as a durable artifact: symptom, repro, environment, observed-vs-expected, and the original instruction or guide output that produced the wrong behavior. The report is the only stage that may be authored by anyone (operator, external agent, child loop reporting a failure); every later stage cites it.",
            Command = "intent-cli bug report <domain> [<bug-id>] --title <text> [--text <text> | --from-file <path>] [--suspected-failure-locus <text>] [--instruction-refs <csv>] [--affected-intent-refs <csv>] [--affected-rule-spec-refs <csv>] [--clarification-candidates <csv>] [--execution-units <csv>] [--issues <csv>] [--prs <csv>] [--reviews <csv>]",
            Output = "A durable bug report under the host's bug projection. The `--instruction-refs` flag preserves the link to the prompt / guide command / spec section that produced the wrong behavior; `--affected-intent-refs` / `--affected-rule-spec-refs` carry the cited intent / rule paths; `--execution-units` / `--issues` / `--prs` / `--reviews` carry the linked artifacts so every later stage can cite them.",
            Boundary = "Runs on the HOST repo (the bug projection is host-owned). No GitHub mutation at this stage. If the operator wants the report public immediately, they can `gh issue create` against the implementation repo with the `bug` label — but the canonical handoff is into triage so the gap is classified before a repair lane is chosen.",
            FailsOpen = "If `bug report` is unavailable on the installed CLI (`automation doctor` reports `stale-host-cli`), STOP — refresh the installed CLI; do NOT fall back to ad-hoc gh issue comments. The bug projection is the durable head of the chain; freehand comments break the chain."
        },
        new BugRepairStage
        {
            Stage = "triage",
            Purpose = "Classify the gap. Pick exactly one of the five classifications below. Triage is the decision point that picks the repair lane (intent-repair vs implementation-repair vs metadata-repair). Triage must cite the bug report and the classification rationale.",
            Command = "intent-cli bug triage <bug-id>",
            Output = "A durable triage artifact (`.intent-cli/bugs/<bug-id>.triage.yaml`) carrying the classification id (one of the five below), rationale, recommended next-stage command, and references back to the bug report.",
            Boundary = "Runs on the HOST repo (the bug projection is host-owned). No GitHub mutation. Triage is a reading exercise: read the bug report, the cited spec/rule/packet, pick the lane. Triage does NOT create the repair artifact itself — that is the plan / intent-repair / implementation-repair stages.",
            FailsOpen = "If two or more classifications fit, prefer `intent-gap` or `rule-gap` over `implementation-mismatch`. Child code that does the wrong thing because it followed wrong guidance is an intent/rule gap; fixing only the child code leaves the bad guidance to bite the next agent. The exception is `metadata-workflow-gap`, which always wins (label/queue-state bugs are host-owned)."
        },
        new BugRepairStage
        {
            Stage = "plan",
            Purpose = "For the chosen classification, derive the repair packet shape (when the lane is intent-repair / packet-gap / rule-gap) OR the child implementation issue shape (when the lane is implementation-mismatch) OR the host repair task (when the lane is metadata-workflow-gap). Plan output carries the execution-unit id, the design-host packet directory path (host repo only), and the in/out-of-scope split.",
            Command = "intent-cli bug plan <bug-id>",
            Output = "A durable plan artifact (`.intent-cli/bugs/<bug-id>.plan.yaml`) carrying the execution-unit id (e.g. `G34X`), the repair lane, the design-host packet directory the operator will scaffold, the in-scope / out-of-scope split, and the acceptance-criteria draft.",
            Boundary = "Plan runs on the HOST repo (design role). Child cwd does NOT plan repair packets — that is host-owned. If the bug surfaced from a child loop, the child loop reports the bug and STOPS; planning is operator-driven on the host. Use `intent-cli intent next-slice --dry-run --domain <domain> --target-repo <owner/repo> --format json` to confirm WIP cap / clarification-blockers before publishing the plan.",
            FailsOpen = "If `intent next-slice --dry-run` reports `wip-cap` or `clarification-required`, STOP — the host queue cannot accept the repair packet yet. Drain the existing in-progress slice (or run `clarification next`) before scheduling the bug repair."
        },
        new BugRepairStage
        {
            Stage = "intent-repair",
            Purpose = "Scaffold the repair packet (when the lane is intent-gap / packet-gap / rule-gap). The repair packet follows the standard G337 packet contract (`packet draft` → four files → `issue validate-body` → `issue publish-flow` → `automation issue-publish --write`). The repair PACKET is the canonical fix surface for guidance bugs; a child PR alone does NOT close the loop because the bad guidance remains in the rule/packet file.",
            Command = "intent-cli bug intent-repair <bug-id>",
            Output = "An intent-repair record (`.intent-cli/bugs/<bug-id>.intent-repair.yaml`) tying the bug's report / triage / plan artifacts to the scaffolded packet. Run `intent-cli packet draft --execution-unit <unit>` to scaffold the four packet files (`packet.yaml`, `implementation.md`, `review-context.md`, `github-body.md`). The `github-body.md` MUST cite the original bug report in `Related Links` AND carry G311 `Closes #<bug-report-issue>`.",
            Boundary = "Packet scaffolding runs on the HOST repo. The repair PACKET is host-side; the repair PR (when needed) is opened from a child cwd via the standard child loop after `automation issue-publish --write` labels the repair issue `intent-target`.",
            FailsOpen = "If `issue validate-body --from-file <github-body.md> --format json` reports missing contract sections, STOP and repair the body before publishing. The G337 issue-publish boundary applies verbatim to repair packets; the FORBIDDEN raw `gh issue edit --add-label intent-target` rule still applies."
        },
        new BugRepairStage
        {
            Stage = "implementation-repair",
            Purpose = "OPTIONAL when the lane is implementation-mismatch — scaffold an implementation-repair issue tied to the bug report so the child loop picks it up via `worker next-action`. Implementation-repair is NOT the primary lane: most bugs that surface in child PRs are caused by intent-gap or rule-gap upstream, so check triage's classification before defaulting here.",
            Command = "intent-cli bug implementation-repair <bug-id> [--issue-number <n>] [--issue-url <url>] [--actor <name>] [--note <text>]",
            Output = "An implementation-repair record on the bug projection linking the original bug report to the implementation-repair issue / PR. The issue is created on the implementation repo (e.g. J-Tech-Japan/intent-system) and picks up `intent-target` through the standard G337 publish-flow.",
            Boundary = "The bug-implementation-repair record runs on the HOST repo (the bug projection is host-owned). The actual implementation-repair issue and PR run on the implementation repo via the standard issue/PR/child-loop lifecycle. Use `intent-cli issue draft <execution-unit>` if you want to scaffold the issue body directly from an existing packet artifact.",
            FailsOpen = "If triage classified the gap as intent-gap / packet-gap / rule-gap, do NOT also open an implementation-repair issue without an upstream intent-repair packet — the child PR would re-implement the wrong guidance. The intent-repair stage MUST land first."
        }
    };

    /// <summary>
    /// G339: the five gap classifications triage chooses from.
    /// Tests pin each by stable ID and verify the markdown +
    /// JSON outputs surface them.
    /// </summary>
    internal static readonly IReadOnlyList<GapClassification> Classifications = new[]
    {
        new GapClassification
        {
            Id = "implementation-mismatch",
            Description = "Child implementation code diverges from a correct intent/packet/rule. The guidance was right; the code is wrong.",
            RepairLane = "implementation-repair",
            ExampleSignal = "PR review finds a bug that contradicts the packet's `Acceptance Criteria` while the packet itself reads correctly to an external agent."
        },
        new GapClassification
        {
            Id = "intent-gap",
            Description = "The intent statement (the durable Why / What in the host intent tree) is missing a requirement or contradicts a different intent. The agent followed the intent literally and still produced a bug.",
            RepairLane = "intent-repair",
            ExampleSignal = "Bug repro shows the agent did what the intent said; the intent itself does not capture the case the bug exposed (e.g. `intent next-slice` says `WIP cap 1` but does not say how to drain an abandoned slice)."
        },
        new GapClassification
        {
            Id = "packet-gap",
            Description = "The packet (`github-body.md` / `implementation.md` / `review-context.md`) for an in-flight or recently-shipped execution unit is missing a contract section, an acceptance criterion, or an out-of-scope fence. The packet is host-owned, so the repair runs on the host design role.",
            RepairLane = "intent-repair",
            ExampleSignal = "A child PR shipped without the bug catch because the packet's `Verification` section did not require the test that would have caught it; or the packet's `Out Of Scope` did not fence a case the agent silently expanded into."
        },
        new GapClassification
        {
            Id = "rule-gap",
            Description = "A guidance rule under `intents/rules/**` or a `guide` surface (e.g. `guide automation` / `guide worker` / `guide prompt-matrix`) is missing or contradicts a fact. The agent did what the rule said; the rule was wrong.",
            RepairLane = "intent-repair",
            ExampleSignal = "An external agent runs `guide automation` and follows its instructions, then hits a CLI that rejects the flag the guide named (e.g. PR #776 / #778 / #780 G336/G337/G338 findings). Repair runs through a packet that updates BOTH the rule text AND the guide output."
        },
        new GapClassification
        {
            Id = "metadata-workflow-gap",
            Description = "GitHub workflow labels, host queue-state, runs.jsonl, publish artifacts, or runtime metadata are inconsistent with the actual GitHub state. This is a HOST-owned bug; the child loop cannot fix it and MUST NOT mutate parent metadata to compensate.",
            RepairLane = "metadata-repair (host-owned)",
            ExampleSignal = "`worker complete` reports `linked_pr_synced: false` from child-cwd mode (G330); the recovery path is `intent-cli review closeout-plan --pr <n> --repo <r> --write-recovered-linkage` on the HOST — never a raw gh label edit and never a child-side queue-state patch."
        }
    };

    /// <summary>
    /// G339: stop conditions that surface BEFORE any GitHub mutation.
    /// Mirrors the G337 pattern — caught at triage / plan time so
    /// the wrong repair lane never reaches publish-flow.
    /// </summary>
    internal static readonly IReadOnlyList<string> StopConditions = new[]
    {
        "Bug report is missing the original instruction reference (the link to the prompt / guide command / spec section that produced the wrong behavior): STOP — without the reference the triage cannot decide intent-gap vs rule-gap vs implementation-mismatch. Repair the report first.",
        "Triage classification not cited in the plan: STOP — the plan must say which lane it is in and why. A plan that names neither the classification nor the rationale cannot be reviewed; the repair packet would be invisible to G316 packet-aware review.",
        "Repair lane is `intent-gap` / `packet-gap` / `rule-gap` BUT the operator opens only an implementation-repair issue without an upstream intent-repair packet: STOP — the bad guidance remains; the next agent re-hits the same bug. The intent-repair packet is the canonical fix surface.",
        "`issue validate-body --from-file <github-body.md> --format json` on the repair packet's `github-body.md` reports `errors[]` (missing contract sections, missing G311 closing reference to the bug-report issue): STOP — repair the body, re-validate, only then `issue publish-flow`.",
        "Bug report references a metadata-workflow-gap (label/queue-state/runs/publish artifact wrong) BUT the operator is in a child cwd: STOP — child cwd is GitHub-contract-only (G300/G330/G333). The host runs the metadata repair via `review closeout-plan --write-recovered-linkage` / `automation publish-recovery` / `automation reconcile`; child cwd records the gap and exits.",
        "Repair-packet `github-body.md` does not preserve the original bug report's instruction ref and linked issue/PR refs in `Related Links`: STOP — the chain (report → triage → plan → repair) breaks when the repair packet does not cite its parent."
    };

    /// <summary>
    /// G339: explicit invariants every bug-to-intent-repair surface
    /// advertises. Tests pin each invariant verbatim.
    /// </summary>
    internal static readonly IReadOnlyList<string> Invariants = new[]
    {
        "Original refs MUST be preserved across the chain: the bug report cites the original instruction (prompt / guide command / spec section); the triage cites the bug report; the plan cites the triage; the repair packet's `github-body.md` cites the bug report in `Related Links` AND carries G311 `Closes #<bug-report-issue>` so merging the repair PR closes the bug. Breaking the chain at any link makes the repair invisible to G316 packet-aware review.",
        "Prefer intent-cli-backed metadata mutation over hand-editing. Ask `intent-cli guide commands list --format json` or `intent-cli automation summary --domain <d> --format json` which command performs the transition (`intent draft` / `packet draft` / `issue publish-flow` / `automation issue-publish` / `automation publish-recovery` / `review closeout-plan --write-recovered-linkage`), run that command, then validate the result. Do NOT directly edit queue-state, runs.jsonl, publish artifacts, workflow labels, or runtime metadata when a supported intent-cli command exists.",
        "Child implementation isolation is preserved: child loops report bugs and STOP; they do NOT plan repair packets, do NOT mutate parent host queue-state / runs.jsonl / packet directories / intent tree / review-runtime state / local rules / local skills. Host metadata gaps are HOST-owned blockers; the metadata-workflow-gap classification routes them to the host loop, never back to the child.",
        "Bugs in intent-cli rules or `guide` surfaces (rule-gap) repair through a PACKET that updates BOTH the underlying rule text AND the guide output, with a regression test that checks the guide output against the rule (mirrors G336 #776 / G337 #778 / G338 #780 fix pattern). A repair PR that only updates the rule text without the guide-output regression test leaves the next external agent reading stale guide output.",
        "`intent-target` is the FINAL publish boundary for repair packets too — applied ONLY by `automation issue-publish --write` after `issue publish-flow` succeeds. Raw `gh issue edit --add-label intent-target` is FORBIDDEN for repair packets just as it is for normal slice packets (G337 invariant carries through verbatim).",
        "Never launch AI providers (Claude / Codex / any LLM) from intent-cli. The chat-first model has the human agent driving the conversation; intent-cli emits text the agent acts on. This applies to every stage of the bug-to-intent-repair chain."
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
            var payload = new BugToIntentRepairGuidance
            {
                Usage = UsageLine,
                Stages = Stages,
                Classifications = Classifications,
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
        writer.WriteLine("# intent-cli — bug-to-intent-repair workflow guide");
        writer.WriteLine();
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("Observed automation failures and product bugs feed back into intent repair packets, not ad-hoc PR comments. Five stages; five gap classifications; the original refs carry through every link.");
        writer.WriteLine();

        writer.WriteLine("## Stages");
        writer.WriteLine();
        foreach (var stage in Stages)
        {
            writer.WriteLine($"### {stage.Stage}");
            writer.WriteLine();
            writer.WriteLine($"- Purpose: {stage.Purpose}");
            writer.WriteLine($"- Command: `{stage.Command}`");
            writer.WriteLine($"- Output: {stage.Output}");
            writer.WriteLine($"- Boundary: {stage.Boundary}");
            writer.WriteLine($"- Fails-open behavior: {stage.FailsOpen}");
            writer.WriteLine();
        }

        writer.WriteLine("## Gap classifications");
        writer.WriteLine();
        writer.WriteLine("Triage MUST pick exactly one. Most bugs that surface in child PRs are caused by intent-gap or rule-gap upstream — check the rule/packet text before defaulting to implementation-mismatch.");
        writer.WriteLine();
        writer.WriteLine("| id | description | repair lane | example signal |");
        writer.WriteLine("|----|-------------|-------------|----------------|");
        foreach (var c in Classifications)
        {
            writer.WriteLine($"| `{c.Id}` | {c.Description} | `{c.RepairLane}` | {c.ExampleSignal} |");
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
/// G339: one stage in the bug-to-intent-repair chain.
/// </summary>
internal sealed record BugRepairStage
{
    [JsonPropertyName("stage")]
    public required string Stage { get; init; }

    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("output")]
    public required string Output { get; init; }

    [JsonPropertyName("boundary")]
    public required string Boundary { get; init; }

    [JsonPropertyName("fails_open")]
    public required string FailsOpen { get; init; }
}

/// <summary>
/// G339: one gap classification triage chooses from.
/// </summary>
internal sealed record GapClassification
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("repair_lane")]
    public required string RepairLane { get; init; }

    [JsonPropertyName("example_signal")]
    public required string ExampleSignal { get; init; }
}

/// <summary>
/// G339: full JSON payload for <c>guide workflow task bug-to-intent-repair</c>.
/// </summary>
internal sealed record BugToIntentRepairGuidance
{
    [JsonPropertyName("usage")]
    public required string Usage { get; init; }

    [JsonPropertyName("stages")]
    public required IReadOnlyList<BugRepairStage> Stages { get; init; }

    [JsonPropertyName("classifications")]
    public required IReadOnlyList<GapClassification> Classifications { get; init; }

    [JsonPropertyName("stop_conditions")]
    public required IReadOnlyList<string> StopConditions { get; init; }

    [JsonPropertyName("invariants")]
    public required IReadOnlyList<string> Invariants { get; init; }
}
