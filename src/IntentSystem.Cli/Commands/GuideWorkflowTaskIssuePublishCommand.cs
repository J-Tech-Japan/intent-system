using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G337: read-only <c>intent-cli guide workflow task issue-publish</c>.
/// Surfaces the draft / create / publish-flow / automation-issue-publish
/// boundary so external agents do not collapse the four stages or
/// apply <c>intent-target</c> by hand. Every external user of
/// intent-cli has to learn this distinction once; the guide encodes
/// it so it can be discovered from the CLI alone.
///
/// Pure read-only — never reads parent host queue-state, never calls
/// <c>gh</c>, never mutates state, never launches an AI provider.
/// </summary>
internal static class GuideWorkflowTaskIssuePublishCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide workflow task issue-publish [--format markdown|json]";

    /// <summary>
    /// G337: the four publish stages, in order. Tests pin each stage
    /// id and its boundary description.
    /// </summary>
    internal static readonly IReadOnlyList<PublishStage> Stages = new[]
    {
        new PublishStage
        {
            Stage = "draft",
            Command = "intent-cli issue draft <execution-unit>",
            Purpose = "Render the draft GitHub body artifact locally for review. Read-only with respect to GitHub.",
            Boundary = "Stage boundary: ARTIFACT-only. No GitHub mutation, no `intent-target`, no queue-state advance. The operator inspects the rendered body and the packet directory before continuing.",
            FailsOpen = "Hand-editing `github-body.md` directly is allowed during design as long as the standalone issue contract still passes `issue validate-body`. Routine automation goes through `packet draft --write` instead."
        },
        new PublishStage
        {
            Stage = "create",
            Command = "intent-cli issue create <execution-unit>",
            Purpose = "Create a GitHub issue from the prepared body. Mutates GitHub but does NOT apply `intent-target` — the issue lives in the repo as a normal issue until the host loop promotes it.",
            Boundary = "Stage boundary: GITHUB issue exists; durable host state NOT yet advanced. The host's queue-state has not been moved through the publish lifecycle.",
            FailsOpen = "If `create` fails halfway (issue created but durable state not advanced), the host recovery lane is `intent-cli automation publish-recovery --repo <r> --write` (G313 / G315), not raw `gh issue` cleanup."
        },
        new PublishStage
        {
            Stage = "publish-flow",
            Command = "intent-cli issue publish-flow <execution-unit> --repo <owner/repo> [--domain <name>] [--team <team>] [--write] [--format json|markdown]",
            Purpose = "End-to-end publish: validate packet → create issue → advance durable state. For packets with root `scope_sources`, revalidate the pinned rulings and authored public block before lookup/create. Without `--write` this is a dry run that surfaces missing contract sections.",
            Boundary = "Stage boundary: durable host state is now advanced (queue-state, packet publish.yaml, runs.jsonl). The issue is on GitHub but the `intent-target` LABEL has not been applied yet; the drafting `execution-unit:<unit>` claim remains held until the attributed drafter explicitly hands it over.",
            FailsOpen = "If validation surfaces missing contract sections (Goal / AC / OOS / Verification / Closes ref etc.), STOP and repair the packet. `publish-flow` without `--write` is the canonical preflight."
        },
        new PublishStage
        {
            Stage = "automation issue-publish",
            Command = "intent-cli automation issue-publish --repo <owner/repo> --issue <n> --write [--dry-run] [--format text|json]",
            Purpose = "FINAL publish boundary: apply the `intent-target` label so the child implementation loop picks the issue up next. This is the ONE place `intent-target` is added.",
            Boundary = "Stage boundary: `intent-target` is now on the GitHub issue. The child loop's `worker next-action` selector will return this issue as `action: issue-to-pr` on its next wake; after this boundary, the attributed drafter must release `execution-unit:<unit>` so the implementation worker can acquire it.",
            FailsOpen = "Raw `gh issue edit --add-label intent-target` is FORBIDDEN — it skips the issue-publish capability check, the publish.yaml advance, and the host audit trail. If `automation issue-publish` refuses (missing publish.yaml, mismatched repo, host-sync-preflight not clean), repair the gap; never bypass. `publish-flow` and `automation issue-publish` do not guess the claim actor; run the attributed `intent-cli claim release` handoff explicitly."
        }
    };

    /// <summary>
    /// G337: explicit invariants every issue-publish surface advertises.
    /// Tests pin each invariant verbatim. The <c>intent-target</c>
    /// boundary statement is required by the acceptance criteria.
    /// </summary>
    internal static readonly IReadOnlyList<string> Invariants = new[]
    {
        "`intent-target` is the FINAL publish boundary, not the default for issue creation. It is applied ONLY by `intent-cli automation issue-publish --write` after `issue publish-flow` has advanced durable host state. Hand-applying `intent-target` via raw `gh` bypasses the publish lifecycle and is forbidden.",
        "G717 precedence: the execution-unit claim registry is authoritative over lifecycle labels; when they disagree, follow the claim and repair the label shadow through intent-cli rather than failing closed on the label.",
        "G717 publish handoff: `issue publish-flow` and `automation issue-publish` do not release the drafting claim automatically; after the final publish boundary, the attributed drafter must run `intent-cli claim release --scope execution-unit:<unit> --actor <design-actor> --team <team> --reason \"hand off after publish\" --write`, then the implementation worker acquires that scope.",
        "`automation issue-release` removes only the host-owned `intent-target` label; it does not release `intent-issue-in-progress`, change the claim registry, or hand off an execution-unit claim.",
        "`automation publish-lifecycle-repair --repo <r> --issue <n> --write` or `--execution-unit <unit> --write` scopes a lifecycle repair to one unit; omit both only when an intentional repo-wide repair is safe.",
        "G717 scenario outputs: (1) `claim acquire` → `status=acquired`; (2) `issue publish-flow` → `durable_state_synced=true`, `intent-target=false`; (3) `automation issue-publish` → `applied=true`; (4) attributed `claim release` → `status=released`; (5) worker preflight with stale `intent-issue-in-progress` → `classification=ready-to-implement`, `actionable=true`, and a claim/label disagreement reason; (6) scoped `publish-lifecycle-repair` → `scope=execution-unit:<unit>`, `applied_count=1`, other drift unchanged; (7) a second process relabel → the next preflight reports the disagreement again. This design does not prevent unauthorized relabeling and does not add retry/backoff.",
        "G680 (preview-through-1.x): `intent-cli automation queue-seed-from-packet --execution-unit <unit> --team <team>` and `issue publish-flow` run the same `claim verify` judgment before their existing gates. A configured store requires the invoking team to hold `execution-unit:<unit>`; refusals name scope, holder, and holder team. No claims store preserves legacy output byte-for-byte.",
        "draft → create → publish-flow → automation issue-publish is a sequence, not a synonym set. Skipping a stage breaks host audit trail (no publish.yaml entry, no runs.jsonl event) and can deadlock the host-loop's WIP-cap check (G288).",
        "`issue publish-flow` without `--write` is the canonical preflight: it surfaces missing contract sections (Goal / Why / AC / OOS / Verification / Closes ref) BEFORE any GitHub mutation. Always run the dry-run first.",
        "Optionally run `intent-cli packet validate-verification --execution-unit <unit> --format json` for structural coverage of an explicitly authored verification map. It never runs commands or verifies evidence, publication, or authority, and it is not a gate. G865 validates packet structure; G866/G867 provide local supplied-TRX binding and read-only coverage; G868 separately adds the explicit worker-completion gate. G869 optionally adds `verification_contract.design_review: { safety_critical_ac: <AC-id>, proof_id: <proof-id> }` to a valid G865 packet. The design `review cross-runtime request --kind design` renders the exact linked fact and authored command as inert request data, preserves legacy output when omitted, and keeps incomplete structure incomplete; it never executes the command. Hosted evidence and ruling-backed deferral remain open under #1866. For a packet with root `scope_sources`, run `intent-cli packet validate-sources --execution-unit <unit> --format json` before publish-flow and fix its named refusal. This local source check reports supplied evidence only; it does not authenticate operator authority or prove a commit/push. Design `review cross-runtime request`, `record`, and `status` also revalidate and report the declared local source proof. `unit status` separately reports pinned local ruling sources as a read-only observation; it does not authenticate the operator, verify publication, or contribute to a gate. `issue publish-flow` is the external issue create/publish boundary and rechecks these pins before mutation. `issue publish-reviewed`, other issue creation paths, automation publishers, and release consumers remain outside this integration.",
        "`intent-cli verification evidence record` previews by default and stores one canonical local report only with `--write`; `show` and `validate` read the exact requested ID. The report embeds supplied TRX bytes and describes structure only: execution is `not-observed`, producer is unauthenticated, acceptance is not assessed, and publication is not verified. It never runs a command, does not infer an AC passed, does not authenticate a producer, and does not gate a worker. Hosted-required proof and deferral are unsupported. Embedded TRX may contain test names, stdout, or machine paths; committing/pushing the artifact publishes those bytes, and this command does not scan them for privacy. Commit and plain-push a record and verify it from a fresh clone to establish remote visibility.",
        "`intent-cli verification evidence coverage --execution-unit <unit> --domain <domain> --team <team> --repo <owner/repo> --head-sha <40-lowerhex> --packet-sha256 <64-lowerhex> [--evidence <proof-id>=<record-id>]...` observes at most 16 explicitly selected local records. Missing, stale, failed, skipped, or unavailable local rows remain visible; hosted-required rows are not-applicable/not-observed. A complete report requires `structural_state=complete` with no G865 gaps and every local-required row current and passing; standalone coverage exits 0 only for complete and 1 for other states. `intent-cli worker result-summary --verification-report ...` optionally adds the same report but does not change lifecycle status, outcome, exit, or worker gates; omitting the marker preserves legacy output. Coverage never authenticates a producer or command, assesses AC acceptance, or verifies publication.",
        "G868 is a separate opt-in gate on `worker complete`, not the G867 summary projection: `--require-verification` is supported only for issue/pr-created and pr/repair-pushed and requires `--verification-unit`, `--verification-domain`, `--verification-team`, `--verification-head-sha <40-lowerhex>`, `--verification-packet-sha256 <64-lowerhex>`, and 0–16 repeated `--evidence <proof-id>=<record-id>` values. The G865 packet and G866 local records must be present under the selected repo root, including with `--github-only`. The gate requires complete passing local proofs with no hosted-required proofs. It reads the live target PR head, then repeats the head and typed coverage observations immediately before the first lifecycle write. Syntax errors exit 1 and well-formed gate refusals exit 2. The guarantee is non-atomic and does not roll back later partial publication; a missing marker preserves legacy output and provider budgets, and G785 remains independent with no waiver for a failed gate. The gate does not authenticate command execution or a producer, assess semantic AC acceptance, verify publication, or establish checkout identity. Keep a committed evidence record on a separate evidence ref when the target PR head must remain unchanged. Example: `intent-cli worker complete --kind issue --number <issue> --outcome pr-created --pr <target-pr> --repo <owner/repo> --require-verification --verification-unit <unit> --verification-domain <domain> --verification-team <team> --verification-head-sha <40-lowerhex> --verification-packet-sha256 <64-lowerhex> --evidence P1=E1 --dry-run --format json`. The advisory `worker result-summary --verification-report ... --verification-evidence P1=E1` is distinct from the gate flags `--require-verification` and `--evidence`. Hosted evidence, deferral, authenticated execution, and semantic AC evaluation remain unsupported.",
        "WIP cap (G288) — only one `intent-target` issue/PR at a time per domain by default. If an issue/PR is already `intent-target` and the operator wants to publish another, pass `--allow-wip-cap-override` explicitly; the publish surface refuses silently otherwise.",
        "Prefer intent-cli-backed metadata mutation over hand-editing. Routine automation MUST NOT directly edit queue-state, runs logs, publish artifacts, workflow labels, or runtime metadata by hand when a supported intent-cli command exists.",
        "Child implementation loops MUST NOT inspect or mutate parent host queue-state, runs logs, packet directories, intent tree, review-runtime state, local rules, or local skills (G300 / G330 / G333). The publish surfaces are host-owned; child agents only ever see the issue once `intent-target` is applied by the host." + " " + DispatcherSkillCarveOut.BoundaryClause,
        "Never launch AI providers (Claude / Codex / any LLM) from intent-cli. The chat-first model has the human agent driving the conversation."
    };

    /// <summary>
    /// G337: cross-stage stop conditions. The acceptance criterion
    /// "surfaces missing contract sections before GitHub mutation"
    /// pins the first entry; the rest enumerate the canonical refuse-
    /// before-mutate gates an external agent must check.
    /// </summary>
    internal static readonly IReadOnlyList<string> StopConditions = new[]
    {
        "A published packet's `github-body.md` changed after publication (for example after a review consultation corrected the approach): the implementer reads only the issue body, so republish it through `intent-cli issue sync-body <execution-unit> --repo <owner/repo> --format json` (dry-run) and then `intent-cli issue sync-body <execution-unit> --repo <owner/repo> --expected-remote-sha256 <hex> --write --format json` with the digest the dry-run reported. It validates the body, requires the issue to belong to `--repo`, and changes only the body. Guarantee: read-compare-write-verified, not atomic — a concurrent edit between the read and the write cannot be excluded, so a mismatch refuses and a failed read-back is reported, never retried. Never use raw `gh issue edit --body-file` (G825).",
        "Run `intent-cli issue publish-flow <execution-unit> --repo <r> --team <team> --format json` (no `--write`) FIRST. If shared claim verification refuses, stop with its named scope/holder; otherwise, if it reports `errors[]` naming missing contract sections, repair the packet — `intent-cli guide workflow task packet-draft --format json` lists the required sections.",
        "`intent-cli issue validate-body --from-file <github-body.md> --format json` reports errors (missing `Closes #<source-issue>` / G311 reference, missing AC, missing Verification): stop, repair body, validate again.",
        "`intent-cli automation host-sync-preflight --format json` (run from the host repo root) reports `dirty-host-durable-state` without `durable-state-preflight` having converged: stop, do not publish on top of a dirty host.",
        "`intent-cli automation publish-recovery --repo <r> --format json` reports `unsafe_stops`: stop, surface to the operator — never re-run `issue publish-flow` blindly.",
        "Operator names `intent-target` on the issue manually: refuse, point at `automation issue-publish` as the canonical write path."
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
            var payload = new IssuePublishGuidance
            {
                Usage = UsageLine,
                Stages = Stages,
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
        writer.WriteLine("# intent-cli — issue-publish workflow guide");
        writer.WriteLine();
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("Four stages, in order. Each has an explicit mutation boundary; the FINAL stage is the only one that applies `intent-target`.");
        writer.WriteLine();

        foreach (var stage in Stages)
        {
            writer.WriteLine($"## {stage.Stage}");
            writer.WriteLine();
            writer.WriteLine($"- Command: `{stage.Command}`");
            writer.WriteLine($"- Purpose: {stage.Purpose}");
            writer.WriteLine($"- Boundary: {stage.Boundary}");
            writer.WriteLine($"- Fails-open behavior: {stage.FailsOpen}");
            writer.WriteLine();
        }

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
/// G337: one publish stage with its mutation boundary.
/// </summary>
internal sealed record PublishStage
{
    [JsonPropertyName("stage")]
    public required string Stage { get; init; }

    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    [JsonPropertyName("boundary")]
    public required string Boundary { get; init; }

    [JsonPropertyName("fails_open")]
    public required string FailsOpen { get; init; }
}

/// <summary>
/// G337: full JSON payload.
/// </summary>
internal sealed record IssuePublishGuidance
{
    [JsonPropertyName("usage")]
    public required string Usage { get; init; }

    [JsonPropertyName("stages")]
    public required IReadOnlyList<PublishStage> Stages { get; init; }

    [JsonPropertyName("stop_conditions")]
    public required IReadOnlyList<string> StopConditions { get; init; }

    [JsonPropertyName("invariants")]
    public required IReadOnlyList<string> Invariants { get; init; }
}
