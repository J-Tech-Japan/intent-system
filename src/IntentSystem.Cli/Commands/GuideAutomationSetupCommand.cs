using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G260: Read-only <c>intent-cli guide automation setup</c>. Returns
/// paste-ready, worktree-friendly setup prompts an AI coding agent
/// (Codex/Claude) consumes when an operator says "set up the
/// implementation loop" or "set up the host review/next-slice loop".
/// The generated prompts always instruct the AI agent to call
/// <c>guide model</c> / <c>guide onboarding</c> / <c>guide commands
/// list</c> / <c>automation summary</c> first; forbid reading
/// <c>intents/rules/**</c>, local skill files, or copied prompts; and
/// delegate label transitions to installed <c>intent-cli automation</c>
/// / <c>worker</c> commands. Never mutates state. Never launches an AI
/// provider.
/// </summary>
internal static class GuideAutomationSetupCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string KindChildImplement = "child-implement";
    private const string KindHostReviewNextSlice = "host-review-next-slice";

    private const string UsageLine =
        "Usage: intent-cli guide automation setup --kind <child-implement|host-review-next-slice> "
        + "[--repo <owner/repo>] [--domain <name>] [--target-repo <owner/repo>] [--format markdown|json]";

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

        if (!TryParseArguments(args, out var kind, out var repo, out var domain, out var targetRepo, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        switch (kind)
        {
            case KindChildImplement:
                return EmitResult(writer, format, BuildChildImplement(repo, domain));

            case KindHostReviewNextSlice:
                if (string.IsNullOrWhiteSpace(domain))
                {
                    writer.WriteLine("--domain is required for --kind host-review-next-slice.");
                    writer.WriteLine(UsageLine);
                    return 1;
                }
                if (string.IsNullOrWhiteSpace(targetRepo))
                {
                    writer.WriteLine("--target-repo is required for --kind host-review-next-slice.");
                    writer.WriteLine(UsageLine);
                    return 1;
                }

                return EmitResult(writer, format, BuildHostReviewNextSlice(domain!, targetRepo!));

            default:
                writer.WriteLine(
                    $"--kind must be '{KindChildImplement}' or '{KindHostReviewNextSlice}' (got '{kind}').");
                writer.WriteLine(UsageLine);
                return 1;
        }
    }

    private static int EmitResult(TextWriter writer, string format, GuideAutomationSetupResult result)
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

    private static GuideAutomationSetupResult BuildChildImplement(string? repo, string? domain)
    {
        var repoLabel = string.IsNullOrWhiteSpace(repo) ? "the repo in the current worktree" : $"`{repo}`";
        var domainPlaceholder = string.IsNullOrWhiteSpace(domain) ? "<DOMAIN>" : domain;

        var prompt =
$@"Set up the child implementation loop for {repoLabel} once. Do not register any cron, monitor, reminder, scheduler, or recurring wakeup as part of this setup unless the operator explicitly asks.

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
- Process at most one action per wake.

Frequency policy (applies only when a recurring local loop is explicitly requested; the default is one-wake execution):
- This setup prompt describes a single-wake run, not a recurring loop. One-wake execution does not create any scheduler.
- If the operator asks for a recurring loop, ask for the frequency before creating any cron, monitor, or scheduler. Never guess or use a tool-default interval.
- High-frequency local loops (active development): 5 minutes.
- Low-frequency local loops (background / idle polling): about 20 minutes.
- Local same-thread loops are the baseline for workflows that depend on local paths or local `.intent-cli` packages. Cloud or new-thread schedulers cannot access local paths.";

        return new GuideAutomationSetupResult
        {
            Kind = KindChildImplement,
            Repo = string.IsNullOrWhiteSpace(repo) ? null : repo,
            Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
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
                "intents/rules/**",
                "local skill files (gh-issue-to-pr, gh-fix-pr-comment, etc.)",
                "copied prompt files"
            },
            LabelOwnership = "All label transitions delegated to installed intent-cli automation / worker commands. Manual `gh ... edit --label` fallback is forbidden.",
            WorktreeFriendly = "The prompt resolves the repo from the child worktree's `gh` / `git remote` and runs the selector from the parent host root with --workdir; no hard-coded paths, and the same prompt works across local worktrees."
        };
    }

    private static GuideAutomationSetupResult BuildHostReviewNextSlice(string domain, string targetRepo)
    {
        var prompt =
$@"Set up the host review and next-slice loop for domain `{domain}` against `{targetRepo}` once, in this existing chat thread.

IMPORTANT — do not create a new chat, a new Claude Code session, a cron job, a monitor, a cloud schedule, or any other recurring wakeup unless the operator explicitly asks for one. Run the loop body exactly once, in the current thread, then stop.

Cloud and new-thread schedulers CANNOT access local paths (e.g. `/Users/.../.intent-cli`) or local dotnet packages. Only use a remote/cloud scheduler if the operator explicitly provides a cloud-compatible intent-cli endpoint.

First-call sequence (read-only; required before any mutation):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — surface `primary` / `support` / `advanced` / `experimental` buckets.
4. `intent-cli automation summary --domain {domain} --format json` — canonical label-driven contract and capability JSON.
5. `intent-cli intent status --domain {domain} --format json` — current baseline / WIP / queued / clarifications.
6. `intent-cli intent next-slice --dry-run --domain {domain} --target-repo {targetRepo} --format json` — verify WIP cap and clarification gates.

Loop body (single wake):
1. Confirm cwd is the parent host repo root.
2. Stage 1 — review/closeout:
   - `intent-cli automation host-review-preflight --repo {targetRepo} --format json` to find an eligible PR.
   - For the selected PR: `intent-cli review closeout-plan --pr <n> --repo {targetRepo} --domain {domain} --format json` and `intent-cli guide review --pr <n> --repo {targetRepo} --domain {domain} --format json`.
   - If review passes: `intent-cli automation pr-transition --transition approved --repo {targetRepo} --pr <n> --write --format json`, merge via the host's existing merge step, then `intent-cli closeout pr --pr <n> --repo {targetRepo} --write --format json`.
   - If review needs repair: leave an actionable PR comment, then `intent-cli automation pr-transition --transition request-update --repo {targetRepo} --pr <n> --write --format json`.
3. Stage 2 — next-slice (only if WIP cap and clarification gates allow):
   - `intent-cli intent next-slice --dry-run --domain {domain} --target-repo {targetRepo} --format json` — confirm `recommended_outcome` is `issue-cut-ready`.
   - `intent-cli packet draft --execution-unit <id> --target-repo {targetRepo} --dry-run --format markdown` — preview the packet.
   - With operator acceptance: `intent-cli packet draft --execution-unit <id> --target-repo {targetRepo} --format json` then `intent-cli issue publish-flow <id> --repo {targetRepo} --write --format json`.
   - After parent durable state is pushed: `intent-cli automation issue-publish --repo {targetRepo} --issue <n> --write --format json`.

Hard rules:
- Do not read `intents/rules/**`, local skill files, or copied prompt files for routine review/closeout. Use `intent-cli guide ...` and `intent-cli automation ...` instead.
- Do not call `intent-cli run`. `run` is advanced runtime, not the host review/closeout path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- Do not open a new chat, session, cron, monitor, or scheduler. Run one wake in this thread; the operator controls subsequent wakes.
- Every label transition (`intent-target`, `intent-pr-reviewing`, `intent-pr-request-update`, `intent-pr-approved`, `intent-pr-rereview-ready`, `intent-pr-update-in-progress`, `intent-issue-in-progress`, `intent-pr-created`) goes through installed `intent-cli automation pr-transition` / `intent-cli automation issue-publish` / `intent-cli worker claim` / `intent-cli worker complete`. No manual `gh ... edit --add-label` / `--remove-label` fallback.
- Never apply `intent-pr-created` to a PR.
- Honor the WIP cap: do not cut a new child issue while any open `intent-target` issue/PR remains in `{targetRepo}`.
- Stop on Hard Clarification rather than guessing when source-of-truth is ambiguous.
- Process at most one PR review and one new child issue per wake.

Frequency policy (applies only when a recurring local loop is explicitly requested; the default is one-wake execution):
- This setup prompt describes a single-wake run, not a recurring loop. One-wake execution does not create any scheduler.
- If the operator asks for a recurring loop, ask for the frequency before creating any cron, monitor, or scheduler. Never guess or use a tool-default interval.
- High-frequency local loops (active development): 5 minutes.
- Low-frequency local loops (background / idle polling): about 20 minutes.
- Local same-thread loops are the baseline for workflows that depend on local paths or local `.intent-cli` packages. Cloud or new-thread schedulers cannot access local paths.";

        return new GuideAutomationSetupResult
        {
            Kind = KindHostReviewNextSlice,
            Domain = domain,
            TargetRepo = targetRepo,
            Prompt = prompt,
            FirstCalls = new[]
            {
                "intent-cli guide model --format json",
                "intent-cli guide onboarding --format json",
                "intent-cli guide commands list --format json",
                $"intent-cli automation summary --domain {domain} --format json",
                $"intent-cli intent status --domain {domain} --format json",
                $"intent-cli intent next-slice --dry-run --domain {domain} --target-repo {targetRepo} --format json"
            },
            ForbiddenSources = new[]
            {
                "intents/rules/**",
                "local skill files",
                "copied prompt files"
            },
            LabelOwnership = "All review-side and issue-side label transitions delegated to installed `intent-cli automation pr-transition` / `automation issue-publish` / `worker claim` / `worker complete`. Manual `gh ... edit --label` fallback is forbidden.",
            WorktreeFriendly = "The prompt names the parent host root as cwd but does not hardcode any operator-specific path beyond that; the same prompt works across host-side checkouts."
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideAutomationSetupResult result)
    {
        writer.WriteLine($"# Guide automation setup — {result.Kind}");
        writer.WriteLine();
        if (!string.IsNullOrWhiteSpace(result.Repo))
        {
            writer.WriteLine($"- repo: {result.Repo}");
        }
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
        out string? kind,
        out string? repo,
        out string? domain,
        out string? targetRepo,
        out string format,
        out string error)
    {
        kind = null;
        repo = null;
        domain = null;
        targetRepo = null;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--kind":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--kind requires a value.";
                        return false;
                    }

                    kind = args[index + 1];
                    index++;
                    break;

                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value.";
                        return false;
                    }

                    repo = args[index + 1];
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

        if (string.IsNullOrWhiteSpace(kind))
        {
            error = "--kind is required.";
            return false;
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide automation setup");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only paste-ready setup prompts for the child implementation loop or the host review / next-slice loop.");
        writer.WriteLine();
        writer.WriteLine("For --kind child-implement:");
        writer.WriteLine("  --repo is optional; omit to derive the repo from the current child worktree via gh/git.");
        writer.WriteLine("  --domain is optional; omit to emit a <DOMAIN> placeholder in the generated prompt.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record GuideAutomationSetupResult
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("repo")]
    public string? Repo { get; init; }

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
