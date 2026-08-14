using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G268: Read-only <c>intent-cli guide closeout run</c>. Returns a paste-ready,
/// skill-free deterministic accepted-PR closeout prompt for AI agents, replacing
/// routine dependence on local <c>intent-closeout</c> skill files. The prompt
/// uses <c>closeout pr --dry-run</c> to plan, confirms merge state, closes the
/// linked issue, syncs the submodule pointer, applies queue/runs state with
/// <c>closeout pr --write</c>, and emits a parent commit/push checklist.
/// Never mutates state. Never launches an AI provider.
/// </summary>
internal static class GuideCloseoutRunCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide closeout run [--domain <name>] [--repo <owner/repo>] [--format markdown|json]";

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

        if (!TryParseArguments(args, out var domain, out var targetRepo, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var result = BuildCloseoutRun(domain, targetRepo);
        return EmitResult(writer, format, result);
    }

    private static int EmitResult(TextWriter writer, string format, GuideCloseoutRunResult result)
    {
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

    private static GuideCloseoutRunResult BuildCloseoutRun(string? domain, string? targetRepo)
    {
        var domainPlaceholder = string.IsNullOrWhiteSpace(domain) ? "<DOMAIN>" : domain;
        var targetRepoPlaceholder = string.IsNullOrWhiteSpace(targetRepo) ? "<TARGET_REPO>" : targetRepo;

        var prompt =
$@"Run one accepted-PR closeout wake for domain `{domainPlaceholder}` against `{targetRepoPlaceholder}`. Do not use the `intent-closeout` skill file, local skill files, or copied prompt files.

First-call sequence (read-only; required before any mutation):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — primary vs support vs advanced vs experimental classification.
4. `intent-cli automation summary --domain {domainPlaceholder} --format json` — canonical label-driven contract and capability JSON.

Stage 1 — select an accepted PR (read-only; required before any mutation):
1. `intent-cli automation host-review-preflight --repo {targetRepoPlaceholder} --format json` → selects an eligible PR (state must include `approved`/`accepted` label).
   - If the result has no eligible PR, stop with `idle`. Do not proceed.
2. Note the selected PR number from the preflight result.
3. `gh pr view <n> --repo {targetRepoPlaceholder}` → confirm the PR is merged (state: `MERGED`). If not yet merged, stop and report `not-yet-merged`.

Stage 2 — plan (dry-run; required before any write):
1. `intent-cli closeout pr --pr <n> --repo {targetRepoPlaceholder} --domain {domainPlaceholder} --dry-run --format json` → inspect planned queue transition, runs events, continuation hint, and next_steps.
   - G477: a missing `linked_pr` is host-owned projection drift, NOT an operator policy question. When the merged PR closes exactly one issue that maps to a single queue item, `closeout pr` recovers the linkage automatically and the result reports `recoverable_missing_linked_pr: true`, `inferred_issue`, and `recovery_action: recover-linked-pr-from-github-closing-reference`. No manual `--issue` rerun is needed — proceed.
   - Only if the result has an `error` field: a `linkage-ambiguous` error means GitHub closing references match more than one queue item — disambiguate by rerunning with the correct `intent-cli closeout pr --pr <n> --issue <linked-issue-n> ...`. Any other `error` → stop and report it. Do not apply.
   - Confirm: execution_unit, queue_state_before → completed, linked issue close command in next_steps.
2. Note the linked issue close command from `next_steps[0]` if present.

Stage 3 — linked issue close (required before queue write):
1. If `next_steps[0]` contains a concrete `gh issue close` command: run it exactly as emitted.
2. If it says `(number not resolved)`: resolve the issue number from `gh pr view <n>` or the linked issue URL, then run `gh issue close <resolved-n> --repo <issue-repo> --comment 'Closed by PR #<n>.'`.
3. If `next_steps[0]` says `linked_issue not set`: skip this step and note the gap.

Stage 4 — submodule pointer sync (manual operator step):
1. The operator must sync the parent submodule pointer to the merge commit SHA:
   ```
   git -C submodules/<child-repo-name> fetch
   git -C submodules/<child-repo-name> reset --hard <merge-sha>
   git add submodules/<child-repo-name>
   ```
2. Do not guess the merge SHA; read it from `gh pr view <n> --repo {targetRepoPlaceholder} --json mergeCommit`.

Stage 5 — apply queue/runs state (write):
1. `intent-cli closeout pr --pr <n> [--issue <linked-issue-n>] --repo {targetRepoPlaceholder} --domain {domainPlaceholder} --write --format json` → applies queue-state completed transition and appends runs events. G477: when linkage was auto-recovered, the write also repairs the missing `linked_pr` projection on the completed item.
   - Include `--issue <n>` ONLY if Stage 2 reported a `linkage-ambiguous` error; the deterministic auto-recovery case needs no flag.
   - If the result has an `error` field, stop and report the error.
   - Confirm: mode is `write`, queue_state_after is `completed`.

Stage 5b — knowledge writeback check (G461 / G564; SAME CADENCE as the closeout):
{IntentTreeCoEvolutionDuty.Duty}
{IntentTreeCoEvolutionDuty.RoleSplit}
1. Read the packet's declarations: any `knowledge_updates.*.required: true` (intent_tree / adr / diagram / docs) or `closeout_learning.write_back_required: true`. If every one is false or absent, this stage is a no-op — declining is a legitimate answer, and legacy packets carry no such block.
2. If anything was declared, DESIGN performs the write-back in the host repo now — in this same closeout wake, not ""later"". intent-cli never writes intent content; the tree is written by design.
3. Then RECORD it as design evidence, with the host commit: `intent-cli automation knowledge-writeback-record --execution-unit <execution-unit> --commit <host-commit-sha> --role design [--target <path>]... --write`. Orchestration records its own mechanical evidence with the same command and `--role orchestration`. The command is idempotent for the same role and commit, keeps the two role records side by side, and fails closed on an unknown unit, wrong role, or non-SHA evidence.
4. Until a record exists, the unit stays visible as a `knowledge-writeback-pending` item in `intent-cli automation stalled-work` / `automation heartbeat`, with its age measured from closeout and its declared target paths named. Merging and closing the PR do NOT clear it.
5. Do not block the closeout queue write on this — but do not report the closeout as complete while the declared write-back is unrecorded either; a still-pending item is part of the closeout report (below).
6. {IntentTreeCoEvolutionDuty.AuthoringRule}

Stage 5c — guide reachability check (G645; SAME CADENCE as the closeout):
{GuideReachabilityDuty.Standard}
{GuideReachabilityDuty.RoleSplit}
1. Read the packet's `guide_reachability` declaration. For each role-facing surface, it must name the guide
   surface, routing role, and target surface. If the slice adds no role-facing surface, the packet must say
   `no_role_facing_surface: true`; an absent or blank declaration is not the same answer.
2. If routes were declared, DESIGN confirms the named guide routes in the host and records the evidence with
   `intent-cli automation guide-reachability-record --execution-unit <execution-unit> --commit <host-commit-sha> --role design --write`. Orchestration may record a separate mechanical observation with `--role orchestration`.
3. Until that record exists, `intent-cli automation stalled-work` reports a `guide-reachability-pending` debt
   naming the execution unit, declared guide surface, and role. An explicit no-surface declaration produces no debt.
4. This is closeout debt, not a merge gate; intent-cli never infers reachability, judges guide wording, or writes
   guide content on design's behalf. {GuideReachabilityDuty.AuthoringRule}

Stage 6 — parent commit/push checklist (required last step):
1. Stage parent durable state: `git add .intent-cli/queue-state.json .intent-cli/runs.jsonl submodules/<child-repo-name>`.
2. Commit: `git commit -m ""closeout: PR #{targetRepoPlaceholder}#<n> — <execution-unit>""`.
3. Push: `git push`.
4. Report continuation hint from the closeout result (`next-slice-ready`, `no-actionable-item`, or `clarification-required`).
5. G564: the closeout report to the design thread NAMES the packet's declared write-backs — each declared facet and target path, and whether it is `recorded` (with the host commit) or `pending`. Design cannot act on an obligation it is never told about, so a closeout report that omits a declared-but-unrecorded write-back is incomplete.

Hard rules:
- Do not use the `intent-closeout` skill file or any local skill file.
- {DispatcherSkillCarveOut.Sentence}
- Do not merge the PR from this loop; merging must have happened before closeout is triggered.
- All label transitions go through installed `intent-cli automation pr-transition`. No manual `gh ... edit --add-label` / `--remove-label` fallback for workflow labels.
- Do not call `intent-cli run`. `run` is for integration smoke/replay/dogfooding, not the host closeout path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- Process at most one PR closeout per wake.";

        return new GuideCloseoutRunResult
        {
            Kind = "run",
            Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
            TargetRepo = string.IsNullOrWhiteSpace(targetRepo) ? null : targetRepo,
            Prompt = prompt,
            FirstCalls = new[]
            {
                "intent-cli guide model --format json",
                "intent-cli guide onboarding --format json",
                "intent-cli guide commands list --format json",
                $"intent-cli automation summary --domain {domainPlaceholder} --format json"
            },
            ForbiddenSources = new[]
            {
                "intent-closeout skill file",
                DispatcherSkillCarveOut.ForbiddenSourceItem,
                "copied prompt files",
                "intents/rules/**"
            },
            LabelOwnership = "All closeout label transitions delegated to installed `intent-cli automation pr-transition`. Manual `gh ... edit --label` fallback is forbidden.",
            WorktreeFriendly = "The prompt resolves domain and target-repo from CLI args; no hard-coded local paths."
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideCloseoutRunResult result)
    {
        writer.WriteLine($"# Guide closeout — {result.Kind}");
        writer.WriteLine();
        if (!string.IsNullOrWhiteSpace(result.Domain))
        {
            writer.WriteLine($"- domain: {result.Domain}");
        }
        if (!string.IsNullOrWhiteSpace(result.TargetRepo))
        {
            writer.WriteLine($"- target repo: {result.TargetRepo}");
        }
        writer.WriteLine();

        writer.WriteLine("## First-call sequence (read-only)");
        foreach (var call in result.FirstCalls)
        {
            writer.WriteLine($"- `{call}`");
        }
        writer.WriteLine();

        writer.WriteLine("## Forbidden rule sources");
        foreach (var src in result.ForbiddenSources)
        {
            writer.WriteLine($"- {src}");
        }
        writer.WriteLine();

        writer.WriteLine("## Label ownership");
        writer.WriteLine();
        writer.WriteLine(result.LabelOwnership);
        writer.WriteLine();

        writer.WriteLine("## Worktree-friendly assumption");
        writer.WriteLine();
        writer.WriteLine(result.WorktreeFriendly);
        writer.WriteLine();

        writer.WriteLine("## Prompt");
        writer.WriteLine();
        writer.WriteLine("```text");
        writer.WriteLine(result.Prompt);
        writer.WriteLine("```");
    }

    private static bool TryParseArguments(
        string[] args,
        out string? domain,
        out string? targetRepo,
        out string format,
        out string error)
    {
        domain = null;
        targetRepo = null;
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

                    domain = args[index + 1];
                    index++;
                    break;

                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value.";
                        return false;
                    }

                    targetRepo = args[index + 1];
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
        writer.WriteLine("guide closeout run");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only paste-ready skill-free accepted-PR closeout prompt for AI agents.");
        writer.WriteLine();
        writer.WriteLine("  --domain is optional; omit to emit a <DOMAIN> placeholder.");
        writer.WriteLine("  --repo is optional; omit to emit a <TARGET_REPO> placeholder.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record GuideCloseoutRunResult
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("target_repo")]
    public string? TargetRepo { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    [JsonPropertyName("first_calls")]
    public required IReadOnlyList<string> FirstCalls { get; init; }

    [JsonPropertyName("forbidden_sources")]
    public required IReadOnlyList<string> ForbiddenSources { get; init; }

    [JsonPropertyName("label_ownership")]
    public required string LabelOwnership { get; init; }

    [JsonPropertyName("worktree_friendly")]
    public required string WorktreeFriendly { get; init; }
}
