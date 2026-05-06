using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G271: Read-only <c>intent-cli guide prompt-matrix</c>. Returns a canonical
/// matrix of the four operational modes: recurring child implement/update loop,
/// recurring host review/next-slice loop, one-shot child implement/update, and
/// one-shot host review/next-slice. Each entry includes paste-ready prompt text
/// and subordinate <c>intent-cli guide</c> commands. Never mutates state.
/// Never launches an AI provider.
/// </summary>
internal static class GuidePromptMatrixCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string ModeChildLoop = "child-loop";
    private const string ModeHostLoop = "host-loop";
    private const string ModeChildOneshot = "child-oneshot";
    private const string ModeHostOneshot = "host-oneshot";

    private const string KindLoop = "loop";
    private const string KindOneshot = "oneshot";

    private const string TargetChild = "child";
    private const string TargetHost = "host";

    private const string FrequencyGuidanceRecurring =
        "5 minutes for high-frequency local loops; ~20 minutes for low-frequency local loops; ask the operator for frequency before scheduling";

    private const string FrequencyGuidanceOneshot =
        "N/A — one-shot execution; frequency is forbidden";

    private const string UsageLine =
        "Usage: intent-cli guide prompt-matrix [--mode child-loop|host-loop|child-oneshot|host-oneshot] [--format markdown|json]";

    private static readonly string[] ForbiddenSources =
    [
        "intents/rules/**",
        "local skill files (gh-issue-to-pr, gh-fix-pr-comment, etc.)",
        "copied prompt files"
    ];

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

        if (!TryParseArguments(args, out var mode, out var format, out var domain, out var targetRepo, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var entries = BuildEntries(mode, domain, targetRepo);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            if (mode is not null)
            {
                // Single entry
                writer.Write(JsonSerializer.Serialize(entries[0], JsonOptions));
            }
            else
            {
                writer.Write(JsonSerializer.Serialize(entries, JsonOptions));
            }
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, entries);
        }

        return 0;
    }

    private static IReadOnlyList<GuidePromptMatrixEntry> BuildEntries(
        string? mode,
        string? domain,
        string? targetRepo)
    {
        var domainPlaceholder = string.IsNullOrWhiteSpace(domain) ? "<DOMAIN>" : domain;
        var targetRepoPlaceholder = string.IsNullOrWhiteSpace(targetRepo) ? "<TARGET-REPO>" : targetRepo;

        var all = new[]
        {
            BuildChildLoop(domainPlaceholder),
            BuildHostLoop(domainPlaceholder, targetRepoPlaceholder),
            BuildChildOneshot(domainPlaceholder),
            BuildHostOneshot(domainPlaceholder, targetRepoPlaceholder)
        };

        if (mode is null)
        {
            return all;
        }

        return mode switch
        {
            ModeChildLoop => [all[0]],
            ModeHostLoop => [all[1]],
            ModeChildOneshot => [all[2]],
            ModeHostOneshot => [all[3]],
            _ => all
        };
    }

    private static GuidePromptMatrixEntry BuildChildLoop(string domainPlaceholder)
    {
        var prompt =
$@"Set up the child implementation loop for the repo in the current worktree. Run the loop body exactly once per wake; the operator or scheduler drives subsequent wakes.

IMPORTANT — ask the operator for the desired frequency before creating any cron, monitor, or recurring wakeup. Never guess or use a tool-default interval.
- High-frequency local loops (active development): 5 minutes.
- Low-frequency local loops (background / idle polling): ~20 minutes.
- Local same-thread loops are the baseline for workflows that depend on local paths or local `.intent-cli` packages. Cloud or new-thread schedulers cannot access local paths.

First-call sequence (read-only; required before any mutation):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — `primary` vs `support` vs `advanced` (`run`) vs `experimental` classification.
4. `intent-cli automation summary --domain {domainPlaceholder} --format json` — canonical label-driven contract and capability JSON for the parent intent domain.

Loop body (single wake; the operator drives subsequent wakes if any):
1. Save the child worktree path: `CHILD_WORKTREE=""$PWD""`. Confirm it is a git worktree root. Stop with `wrong-worktree` if not.
2. Resolve `<OWNER>/<REPO>` from the child cwd: `gh repo view --json nameWithOwner --jq .nameWithOwner` (fall back to `git remote get-url origin`).
3. `git fetch --all --prune` and `git status --short`. If dirty in a dedicated automation worktree, clean local residue (`git reset --hard`, `git clean -fd`, submodule reset). Never `git clean -fdx`. Never clean a personal/shared checkout.
4. From the parent host root (NOT the child cwd), run `intent-cli worker next-action --repo <OWNER>/<REPO> --workdir $CHILD_WORKTREE --format json`. Dispatch on `action`:
   - `none` → stop with `idle`.
   - `issue-to-pr` → claim with `intent-cli worker claim --kind issue --number <n> --write --format json`, run the issue-to-PR workflow on the returned URL only, classify outcome, then `worker result-summary --kind issue-to-pr ...` and `worker complete --kind issue --number <n> --outcome <outcome> --write --format json`.
   - `pr-comment-fix` → claim with `intent-cli worker claim --kind pr --number <n> --write --format json`, repair only the narrow requested change on the PR branch, classify outcome, then `worker result-summary --kind pr-comment-fix ...` and `worker complete --kind pr --number <n> --outcome <outcome> --write --format json`.

Hard rules:
- Do not read `intents/rules/**`, local skill files (`gh-issue-to-pr`, `gh-fix-pr-comment`, etc.), or copied prompt files for routine collaboration. Use `intent-cli guide ...` instead.
- Do not call `intent-cli run` from this loop. `run` is advanced runtime (integration smoke / replay / dogfooding), not the chat-first path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- All label transitions go through installed `intent-cli automation` / `intent-cli worker` commands. No manual `gh ... edit --add-label` / `--remove-label` fallback for workflow labels.
- Never apply `intent-target` from the child loop; it is host-owned.
- Never apply `intent-pr-created` to a PR; it is an issue-side completion marker.
- Process at most one action per wake.";

        return new GuidePromptMatrixEntry
        {
            Mode = ModeChildLoop,
            Kind = KindLoop,
            Target = TargetChild,
            FrequencyGuidance = FrequencyGuidanceRecurring,
            ForbiddenSources = ForbiddenSources,
            FirstCalls =
            [
                "intent-cli guide model --format json",
                "intent-cli guide onboarding --format json",
                "intent-cli guide commands list --format json",
                $"intent-cli automation summary --domain {domainPlaceholder} --format json"
            ],
            Prompt = prompt
        };
    }

    private static GuidePromptMatrixEntry BuildHostLoop(string domainPlaceholder, string targetRepoPlaceholder)
    {
        var prompt =
$@"Set up the host review and next-slice loop for domain `{domainPlaceholder}` against `{targetRepoPlaceholder}`. Run the loop body exactly once per wake; the operator or scheduler drives subsequent wakes.

IMPORTANT — ask the operator for the desired frequency before creating any cron, monitor, or recurring wakeup. Never guess or use a tool-default interval.
- High-frequency local loops (active development): 5 minutes.
- Low-frequency local loops (background / idle polling): ~20 minutes.
- Local same-thread loops are the baseline for workflows that depend on local paths or local `.intent-cli` packages. Cloud or new-thread schedulers cannot access local paths.

First-call sequence (read-only; required before any mutation):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — surface `primary` / `support` / `advanced` / `experimental` buckets.
4. `intent-cli automation summary --domain {domainPlaceholder} --format json` — canonical label-driven contract and capability JSON.
5. `intent-cli intent status --domain {domainPlaceholder} --format json` — current baseline / WIP / queued / clarifications.
6. `intent-cli intent next-slice --dry-run --domain {domainPlaceholder} --target-repo {targetRepoPlaceholder} --format json` — verify WIP cap and clarification gates.

Loop body (single wake):
1. Confirm cwd is the parent host repo root.
2. Stage 1 — review/closeout:
   - `intent-cli automation host-review-preflight --repo {targetRepoPlaceholder} --format json` to find an eligible PR.
   - For the selected PR: `intent-cli review closeout-plan --pr <n> --repo {targetRepoPlaceholder} --domain {domainPlaceholder} --format json` and `intent-cli guide review --pr <n> --repo {targetRepoPlaceholder} --domain {domainPlaceholder} --format json`.
   - If review passes: `intent-cli automation pr-transition --transition approved --repo {targetRepoPlaceholder} --pr <n> --write --format json`, merge via the host's existing merge step, then `intent-cli closeout pr --pr <n> --repo {targetRepoPlaceholder} --write --format json`.
   - If review needs repair: leave an actionable PR comment, then `intent-cli automation pr-transition --transition request-update --repo {targetRepoPlaceholder} --pr <n> --write --format json`.
3. Stage 2 — next-slice (only if WIP cap and clarification gates allow):
   - `intent-cli intent next-slice --dry-run --domain {domainPlaceholder} --target-repo {targetRepoPlaceholder} --format json` — confirm `recommended_outcome` is `issue-cut-ready`.
   - `intent-cli packet draft --execution-unit <id> --target-repo {targetRepoPlaceholder} --dry-run --format markdown` — preview the packet.
   - With operator acceptance: `intent-cli packet draft --execution-unit <id> --target-repo {targetRepoPlaceholder} --format json` then `intent-cli issue publish-flow <id> --repo {targetRepoPlaceholder} --write --format json`.
   - After parent durable state is pushed: `intent-cli automation issue-publish --repo {targetRepoPlaceholder} --issue <n> --write --format json`.

Hard rules:
- Do not read `intents/rules/**`, local skill files, or copied prompt files for routine review/closeout. Use `intent-cli guide ...` and `intent-cli automation ...` instead.
- Do not call `intent-cli run`. `run` is advanced runtime, not the host review/closeout path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- Every label transition goes through installed `intent-cli automation pr-transition` / `automation issue-publish` / `worker claim` / `worker complete`. No manual `gh ... edit --add-label` / `--remove-label` fallback.
- Never apply `intent-pr-created` to a PR.
- Honor the WIP cap: do not cut a new child issue while any open `intent-target` issue/PR remains.
- Stop on Hard Clarification rather than guessing when source-of-truth is ambiguous.
- Process at most one PR review and one new child issue per wake.";

        return new GuidePromptMatrixEntry
        {
            Mode = ModeHostLoop,
            Kind = KindLoop,
            Target = TargetHost,
            FrequencyGuidance = FrequencyGuidanceRecurring,
            ForbiddenSources = ForbiddenSources,
            FirstCalls =
            [
                "intent-cli guide model --format json",
                "intent-cli guide onboarding --format json",
                "intent-cli guide commands list --format json",
                $"intent-cli automation summary --domain {domainPlaceholder} --format json",
                $"intent-cli intent status --domain {domainPlaceholder} --format json",
                $"intent-cli intent next-slice --dry-run --domain {domainPlaceholder} --target-repo {targetRepoPlaceholder} --format json"
            ],
            Prompt = prompt
        };
    }

    private static GuidePromptMatrixEntry BuildChildOneshot(string domainPlaceholder)
    {
        var prompt =
$@"Run one child implementation/update wake exactly once.

Do not create or update any automation, loop, cron, monitor, reminder, or recurring wakeup. This is a one-shot execution. Frequency is forbidden.

First-call sequence (read-only; required before any mutation):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — `primary` vs `support` vs `advanced` (`run`) vs `experimental` classification.
4. `intent-cli automation summary --domain {domainPlaceholder} --format json` — canonical label-driven contract and capability JSON for the parent intent domain.

Loop body (single wake only — do not repeat):
1. Save the child worktree path: `CHILD_WORKTREE=""$PWD""`. Confirm it is a git worktree root. Stop with `wrong-worktree` if not.
2. Resolve `<OWNER>/<REPO>` from the child cwd: `gh repo view --json nameWithOwner --jq .nameWithOwner` (fall back to `git remote get-url origin`).
3. `git fetch --all --prune` and `git status --short`. If dirty in a dedicated automation worktree, clean local residue (`git reset --hard`, `git clean -fd`, submodule reset). Never `git clean -fdx`. Never clean a personal/shared checkout.
4. From the parent host root (NOT the child cwd), run `intent-cli worker next-action --repo <OWNER>/<REPO> --workdir $CHILD_WORKTREE --format json`. Dispatch on `action`:
   - `none` → stop with `idle`.
   - `issue-to-pr` → claim with `intent-cli worker claim --kind issue --number <n> --write --format json`, run the issue-to-PR workflow on the returned URL only, classify outcome, then `worker result-summary --kind issue-to-pr ...` and `worker complete --kind issue --number <n> --outcome <outcome> --write --format json`.
   - `pr-comment-fix` → claim with `intent-cli worker claim --kind pr --number <n> --write --format json`, repair only the narrow requested change on the PR branch, classify outcome, then `worker result-summary --kind pr-comment-fix ...` and `worker complete --kind pr --number <n> --outcome <outcome> --write --format json`.

Hard rules:
- Do not read `intents/rules/**`, local skill files (`gh-issue-to-pr`, `gh-fix-pr-comment`, etc.), or copied prompt files for routine collaboration. Use `intent-cli guide ...` instead.
- Do not call `intent-cli run` from this loop. `run` is advanced runtime (integration smoke / replay / dogfooding), not the chat-first path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- All label transitions go through installed `intent-cli automation` / `intent-cli worker` commands. No manual `gh ... edit --add-label` / `--remove-label` fallback for workflow labels.
- Never apply `intent-target` from the child loop; it is host-owned.
- Never apply `intent-pr-created` to a PR; it is an issue-side completion marker.
- Process at most one action per wake.
- Do not create a cron, monitor, scheduler, reminder, or new thread after completing this wake.";

        return new GuidePromptMatrixEntry
        {
            Mode = ModeChildOneshot,
            Kind = KindOneshot,
            Target = TargetChild,
            FrequencyGuidance = FrequencyGuidanceOneshot,
            ForbiddenSources = ForbiddenSources,
            FirstCalls =
            [
                "intent-cli guide model --format json",
                "intent-cli guide onboarding --format json",
                "intent-cli guide commands list --format json",
                $"intent-cli automation summary --domain {domainPlaceholder} --format json"
            ],
            Prompt = prompt
        };
    }

    private static GuidePromptMatrixEntry BuildHostOneshot(string domainPlaceholder, string targetRepoPlaceholder)
    {
        var prompt =
$@"Run the host review and next-slice for domain `{domainPlaceholder}` against `{targetRepoPlaceholder}` exactly once.

Do not create or update any automation, loop, cron, monitor, reminder, or recurring wakeup. This is a one-shot execution. Frequency is forbidden.

First-call sequence (read-only; required before any mutation):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — surface `primary` / `support` / `advanced` / `experimental` buckets.
4. `intent-cli automation summary --domain {domainPlaceholder} --format json` — canonical label-driven contract and capability JSON.
5. `intent-cli intent status --domain {domainPlaceholder} --format json` — current baseline / WIP / queued / clarifications.
6. `intent-cli intent next-slice --dry-run --domain {domainPlaceholder} --target-repo {targetRepoPlaceholder} --format json` — verify WIP cap and clarification gates.

Loop body (single wake only — do not repeat):
1. Confirm cwd is the parent host repo root.
2. Stage 1 — review/closeout:
   - `intent-cli automation host-review-preflight --repo {targetRepoPlaceholder} --format json` to find an eligible PR.
   - For the selected PR: `intent-cli review closeout-plan --pr <n> --repo {targetRepoPlaceholder} --domain {domainPlaceholder} --format json` and `intent-cli guide review --pr <n> --repo {targetRepoPlaceholder} --domain {domainPlaceholder} --format json`.
   - If review passes: `intent-cli automation pr-transition --transition approved --repo {targetRepoPlaceholder} --pr <n> --write --format json`, merge via the host's existing merge step, then `intent-cli closeout pr --pr <n> --repo {targetRepoPlaceholder} --write --format json`.
   - If review needs repair: leave an actionable PR comment, then `intent-cli automation pr-transition --transition request-update --repo {targetRepoPlaceholder} --pr <n> --write --format json`.
3. Stage 2 — next-slice (only if WIP cap and clarification gates allow):
   - `intent-cli intent next-slice --dry-run --domain {domainPlaceholder} --target-repo {targetRepoPlaceholder} --format json` — confirm `recommended_outcome` is `issue-cut-ready`.
   - `intent-cli packet draft --execution-unit <id> --target-repo {targetRepoPlaceholder} --dry-run --format markdown` — preview the packet.
   - With operator acceptance: `intent-cli packet draft --execution-unit <id> --target-repo {targetRepoPlaceholder} --format json` then `intent-cli issue publish-flow <id> --repo {targetRepoPlaceholder} --write --format json`.
   - After parent durable state is pushed: `intent-cli automation issue-publish --repo {targetRepoPlaceholder} --issue <n> --write --format json`.

Hard rules:
- Do not read `intents/rules/**`, local skill files, or copied prompt files for routine review/closeout. Use `intent-cli guide ...` and `intent-cli automation ...` instead.
- Do not call `intent-cli run`. `run` is advanced runtime, not the host review/closeout path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- Every label transition goes through installed `intent-cli automation pr-transition` / `automation issue-publish` / `worker claim` / `worker complete`. No manual `gh ... edit --add-label` / `--remove-label` fallback.
- Never apply `intent-pr-created` to a PR.
- Honor the WIP cap: do not cut a new child issue while any open `intent-target` issue/PR remains.
- Stop on Hard Clarification rather than guessing when source-of-truth is ambiguous.
- Process at most one PR review and one new child issue per wake.
- Do not create a cron, monitor, scheduler, reminder, or new thread after completing this wake.";

        return new GuidePromptMatrixEntry
        {
            Mode = ModeHostOneshot,
            Kind = KindOneshot,
            Target = TargetHost,
            FrequencyGuidance = FrequencyGuidanceOneshot,
            ForbiddenSources = ForbiddenSources,
            FirstCalls =
            [
                "intent-cli guide model --format json",
                "intent-cli guide onboarding --format json",
                "intent-cli guide commands list --format json",
                $"intent-cli automation summary --domain {domainPlaceholder} --format json",
                $"intent-cli intent status --domain {domainPlaceholder} --format json",
                $"intent-cli intent next-slice --dry-run --domain {domainPlaceholder} --target-repo {targetRepoPlaceholder} --format json"
            ],
            Prompt = prompt
        };
    }

    private static void WriteMarkdown(TextWriter writer, IReadOnlyList<GuidePromptMatrixEntry> entries)
    {
        writer.WriteLine("# Guide prompt matrix");
        writer.WriteLine();
        writer.WriteLine("Canonical matrix of the four operational modes.");
        writer.WriteLine();

        foreach (var entry in entries)
        {
            writer.WriteLine($"## Mode: {entry.Mode}");
            writer.WriteLine();
            writer.WriteLine($"- kind: {entry.Kind}");
            writer.WriteLine($"- target: {entry.Target}");
            writer.WriteLine($"- frequency_guidance: {entry.FrequencyGuidance}");
            writer.WriteLine();

            writer.WriteLine("### First-call sequence (read-only)");
            foreach (var call in entry.FirstCalls)
            {
                writer.WriteLine($"- `{call}`");
            }
            writer.WriteLine();

            writer.WriteLine("### Forbidden rule sources");
            foreach (var src in entry.ForbiddenSources)
            {
                writer.WriteLine($"- {src}");
            }
            writer.WriteLine();

            writer.WriteLine("### Prompt");
            writer.WriteLine();
            writer.WriteLine("```text");
            writer.WriteLine(entry.Prompt);
            writer.WriteLine("```");
            writer.WriteLine();
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? mode,
        out string format,
        out string? domain,
        out string? targetRepo,
        out string error)
    {
        mode = null;
        format = FormatMarkdown;
        domain = null;
        targetRepo = null;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--mode":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--mode requires a value (child-loop, host-loop, child-oneshot, host-oneshot).";
                        return false;
                    }

                    var requestedMode = args[index + 1];
                    if (!string.Equals(requestedMode, ModeChildLoop, StringComparison.Ordinal)
                        && !string.Equals(requestedMode, ModeHostLoop, StringComparison.Ordinal)
                        && !string.Equals(requestedMode, ModeChildOneshot, StringComparison.Ordinal)
                        && !string.Equals(requestedMode, ModeHostOneshot, StringComparison.Ordinal))
                    {
                        error = $"--mode must be 'child-loop', 'host-loop', 'child-oneshot', or 'host-oneshot' (got '{requestedMode}').";
                        return false;
                    }

                    mode = requestedMode;
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }

                    var requestedFormat = args[index + 1];
                    if (!string.Equals(requestedFormat, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requestedFormat}').";
                        return false;
                    }

                    format = requestedFormat;
                    index++;
                    break;

                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }

                    domain = args[index + 1];
                    index++;
                    break;

                case "--target-repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--target-repo requires a value.";
                        return false;
                    }

                    targetRepo = args[index + 1];
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
        writer.WriteLine("guide prompt-matrix");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only canonical matrix of the four operational modes with paste-ready prompt text.");
        writer.WriteLine();
        writer.WriteLine("Modes:");
        writer.WriteLine($"- {ModeChildLoop}    recurring child implement/update loop");
        writer.WriteLine($"- {ModeHostLoop}     recurring host review/next-slice loop");
        writer.WriteLine($"- {ModeChildOneshot} one-shot child implement/update");
        writer.WriteLine($"- {ModeHostOneshot}  one-shot host review/next-slice");
        writer.WriteLine();
        writer.WriteLine("Omit --mode to get all four entries.");
        writer.WriteLine("--domain and --target-repo are optional; omit to use placeholders.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record GuidePromptMatrixEntry
{
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("target")]
    public required string Target { get; init; }

    [JsonPropertyName("frequency_guidance")]
    public required string FrequencyGuidance { get; init; }

    [JsonPropertyName("forbidden_sources")]
    public required IReadOnlyList<string> ForbiddenSources { get; init; }

    [JsonPropertyName("first_calls")]
    public required IReadOnlyList<string> FirstCalls { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }
}
