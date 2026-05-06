using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G261/G272: Read-only <c>intent-cli guide intent-work setup</c>. Returns
/// paste-ready, worktree-friendly prompts for parent-side intent work so
/// an AI agent can ask intent-cli how to organize intents, analyze next
/// slices, preload packets, ask clarifications, shape intents via
/// explain/interview, and prepare one issue without reading local rules
/// files, local skill files, or copied prompt files.
/// Never mutates state. Never launches an AI provider.
/// </summary>
internal static class GuideIntentWorkSetupCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string KindDomainOrganize = "domain-organize";
    private const string KindNextSlice = "next-slice";
    private const string KindPacketPreload = "packet-preload";
    private const string KindClarification = "clarification";
    private const string KindIntentShape = "intent-shape";

    private const string UsageLine =
        "Usage: intent-cli guide intent-work setup --kind <domain-organize|next-slice|packet-preload|clarification|intent-shape> "
        + "--domain <domain> --target-repo <owner/repo> [--format markdown|json]";

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

        if (!TryParseArguments(args, out var kind, out var domain, out var targetRepo, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            writer.WriteLine("--domain is required.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (string.IsNullOrWhiteSpace(targetRepo))
        {
            writer.WriteLine("--target-repo is required.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        var result = kind switch
        {
            KindDomainOrganize => BuildDomainOrganize(domain!, targetRepo!),
            KindNextSlice => BuildNextSlice(domain!, targetRepo!),
            KindPacketPreload => BuildPacketPreload(domain!, targetRepo!),
            KindClarification => BuildClarification(domain!, targetRepo!),
            KindIntentShape => BuildIntentShape(domain!, targetRepo!),
            _ => null
        };

        if (result is null)
        {
            writer.WriteLine(
                $"--kind must be '{KindDomainOrganize}', '{KindNextSlice}', '{KindPacketPreload}', '{KindClarification}', or '{KindIntentShape}' (got '{kind}').");
            writer.WriteLine(UsageLine);
            return 1;
        }

        return EmitResult(writer, format, result);
    }

    private static int EmitResult(TextWriter writer, string format, GuideIntentWorkSetupResult result)
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

    private static GuideIntentWorkSetupResult BuildDomainOrganize(string domain, string targetRepo)
    {
        var prompt =
$@"Organize the current intent state for domain `{domain}` against `{targetRepo}`. Do not publish any GitHub issue unless the operator explicitly approves it.

First-call sequence (read-only; required before any mutation):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — surface `primary` / `support` / `advanced` / `experimental` buckets.
4. `intent-cli automation summary --format json` — canonical label-driven contract and capability JSON.
5. `intent-cli intent status --domain {domain} --format json` — current baseline / WIP / queued / clarifications.
6. `intent-cli intent search --domain {domain} --format json` — locate related intents across the domain.
7. `intent-cli intent next-slice --dry-run --domain {domain} --target-repo {targetRepo} --format json` — verify WIP cap and clarification gates.

Organization steps:
1. Confirm cwd is the parent host repo root.
2. Summarize the current domain map: accepted intents, completed execution units, queued items, open clarifications.
3. Identify gaps: intents that are accepted but have no execution unit, intents blocked by open clarifications, or intents that conflict.
4. Propose a reorganization plan: which intents to group, which to split, which clarifications to resolve first.
5. Present the plan to the operator for acceptance before writing anything.
6. After acceptance: record only what is safe to write without a GitHub issue. Defer issue publication to the next-slice workflow.

Hard rules:
- Do not read `intents/rules/**`, local skill files, or copied prompt files. Use `intent-cli guide ...` and `intent-cli intent ...` instead.
- Do not call `intent-cli run`. `run` is advanced runtime, not the intent-work path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- All label transitions go through installed `intent-cli automation` commands.
- Do not publish a GitHub issue in this wake. Publish at most one future issue only after the operator accepts the next-slice plan.
- Stop on Hard Clarification rather than guessing when source-of-truth is ambiguous.";

        return new GuideIntentWorkSetupResult
        {
            Kind = KindDomainOrganize,
            Domain = domain,
            TargetRepo = targetRepo,
            Prompt = prompt,
            FirstCalls = new[]
            {
                "intent-cli guide model --format json",
                "intent-cli guide onboarding --format json",
                "intent-cli guide commands list --format json",
                "intent-cli automation summary --format json",
                $"intent-cli intent status --domain {domain} --format json",
                $"intent-cli intent search --domain {domain} --format json",
                $"intent-cli intent next-slice --dry-run --domain {domain} --target-repo {targetRepo} --format json"
            },
            ForbiddenSources = new[]
            {
                "intents/rules/**",
                "local skill files",
                "copied prompt files"
            },
            ClarificationFormat = "background, question, options, pros/cons, and recommendation",
            IssuePublishBoundary = "At most one GitHub issue publication per operator-accepted next-slice. Delegate to `intent-cli issue publish-flow` and `intent-cli automation issue-publish` after parent durable state is committed and pushed.",
            WorktreeFriendly = "The prompt names the parent host repo worktree root as cwd and references domain/target-repo from arguments; no operator-specific paths are hard-coded, so it works across host-side worktrees."
        };
    }

    private static GuideIntentWorkSetupResult BuildNextSlice(string domain, string targetRepo)
    {
        var prompt =
$@"Analyze and prepare the next execution slice for domain `{domain}` against `{targetRepo}`. Publish at most one GitHub issue per wake, only after the operator accepts the proposed execution unit.

First-call sequence (read-only; required before any mutation):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — surface `primary` / `support` / `advanced` / `experimental` buckets.
4. `intent-cli automation summary --format json` — canonical label-driven contract and capability JSON.
5. `intent-cli intent status --domain {domain} --format json` — current baseline / WIP / queued / clarifications.
6. `intent-cli intent search --domain {domain} --format json` — locate related intents across the domain.
7. `intent-cli intent next-slice --dry-run --domain {domain} --target-repo {targetRepo} --format json` — verify WIP cap and clarification gates; surface recommended next execution unit.

Next-slice steps:
1. Confirm cwd is the parent host repo root.
2. Read the dry-run output. If `recommended_outcome` is not `issue-cut-ready`, surface the blocker and stop.
3. Preview the next execution unit packet: `intent-cli packet draft --execution-unit <id> --target-repo {targetRepo} --dry-run --format markdown`.
4. Present the packet preview to the operator for acceptance.
5. After acceptance:
   a. Write the packet: `intent-cli packet draft --execution-unit <id> --target-repo {targetRepo} --format json`.
   b. Run `intent-cli issue publish-flow <id> --repo {targetRepo} --write --format json` to commit parent state and prepare the issue body.
   c. After parent durable state is pushed: `intent-cli automation issue-publish --repo {targetRepo} --issue <n> --write --format json`.
6. Multiple future packets may be preloaded (dry-run only) when parent state supports them, but publish at most one issue per wake.

Hard rules:
- Do not read `intents/rules/**`, local skill files, or copied prompt files. Use `intent-cli guide ...` and `intent-cli intent ...` instead.
- Do not call `intent-cli run`. `run` is advanced runtime, not the intent-work path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- All label transitions go through installed `intent-cli automation` commands.
- `intent-target` is the host-owned publish boundary; apply it only via `intent-cli automation issue-publish` after parent state is pushed.
- Publish at most one GitHub issue per wake.
- Stop on Hard Clarification rather than guessing when source-of-truth is ambiguous.";

        return new GuideIntentWorkSetupResult
        {
            Kind = KindNextSlice,
            Domain = domain,
            TargetRepo = targetRepo,
            Prompt = prompt,
            FirstCalls = new[]
            {
                "intent-cli guide model --format json",
                "intent-cli guide onboarding --format json",
                "intent-cli guide commands list --format json",
                "intent-cli automation summary --format json",
                $"intent-cli intent status --domain {domain} --format json",
                $"intent-cli intent search --domain {domain} --format json",
                $"intent-cli intent next-slice --dry-run --domain {domain} --target-repo {targetRepo} --format json"
            },
            ForbiddenSources = new[]
            {
                "intents/rules/**",
                "local skill files",
                "copied prompt files"
            },
            ClarificationFormat = "background, question, options, pros/cons, and recommendation",
            IssuePublishBoundary = "Publish at most one GitHub issue per wake. Use `intent-cli issue publish-flow` then `intent-cli automation issue-publish` after parent durable state is committed and pushed.",
            WorktreeFriendly = "The prompt names the parent host repo worktree root as cwd and references domain/target-repo from arguments; no operator-specific paths are hard-coded, so it works across host-side worktrees."
        };
    }

    private static GuideIntentWorkSetupResult BuildPacketPreload(string domain, string targetRepo)
    {
        var prompt =
$@"Preload future execution unit packets for domain `{domain}` against `{targetRepo}`. Packet preload is dry-run-friendly: write packet files locally but do not publish any GitHub issue unless the operator explicitly approves.

First-call sequence (read-only; required before any mutation):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — surface `primary` / `support` / `advanced` / `experimental` buckets.
4. `intent-cli automation summary --format json` — canonical label-driven contract and capability JSON.
5. `intent-cli intent status --domain {domain} --format json` — current baseline / WIP / queued / clarifications.
6. `intent-cli intent search --domain {domain} --format json` — locate related intents across the domain.
7. `intent-cli intent next-slice --dry-run --domain {domain} --target-repo {targetRepo} --format json` — surface recommended continuation candidates.

Packet preload steps:
1. Confirm cwd is the parent host repo root.
2. From the dry-run output, identify the next 3–4 continuation candidates as standalone contracts.
3. For each candidate, preview: `intent-cli packet draft --execution-unit <id> --target-repo {targetRepo} --dry-run --format markdown`.
4. Present each preview to the operator. Accept or reject each independently.
5. For accepted candidates: `intent-cli packet draft --execution-unit <id> --target-repo {targetRepo} --format json` to write the packet files locally.
6. After all packets are written, commit and push the parent state (packet files only, no GitHub issue yet).
7. Publish at most one GitHub issue: use `intent-cli issue publish-flow <id> --repo {targetRepo} --write --format json` for the first accepted candidate only, then `intent-cli automation issue-publish --repo {targetRepo} --issue <n> --write --format json` after parent state is pushed.
8. Mark remaining preloaded packets as `queued` in queue-state without linked issues; the next-slice loop will publish them in future wakes.

Hard rules:
- Do not read `intents/rules/**`, local skill files, or copied prompt files. Use `intent-cli guide ...` and `intent-cli intent ...` instead.
- Do not call `intent-cli run`. `run` is advanced runtime, not the intent-work path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- All label transitions go through installed `intent-cli automation` commands.
- `intent-target` is the host-owned publish boundary; apply it only via `intent-cli automation issue-publish` after parent state is pushed.
- Publish at most one GitHub issue per wake even when multiple packets are preloaded.
- Stop on Hard Clarification rather than guessing when source-of-truth is ambiguous.";

        return new GuideIntentWorkSetupResult
        {
            Kind = KindPacketPreload,
            Domain = domain,
            TargetRepo = targetRepo,
            Prompt = prompt,
            FirstCalls = new[]
            {
                "intent-cli guide model --format json",
                "intent-cli guide onboarding --format json",
                "intent-cli guide commands list --format json",
                "intent-cli automation summary --format json",
                $"intent-cli intent status --domain {domain} --format json",
                $"intent-cli intent search --domain {domain} --format json",
                $"intent-cli intent next-slice --dry-run --domain {domain} --target-repo {targetRepo} --format json"
            },
            ForbiddenSources = new[]
            {
                "intents/rules/**",
                "local skill files",
                "copied prompt files"
            },
            ClarificationFormat = "background, question, options, pros/cons, and recommendation",
            IssuePublishBoundary = "Publish at most one GitHub issue per wake even when multiple packets are preloaded. Use `intent-cli issue publish-flow` then `intent-cli automation issue-publish` after parent durable state is committed and pushed.",
            WorktreeFriendly = "The prompt names the parent host repo worktree root as cwd and references domain/target-repo from arguments; no operator-specific paths are hard-coded, so it works across host-side worktrees."
        };
    }

    private static GuideIntentWorkSetupResult BuildClarification(string domain, string targetRepo)
    {
        var prompt =
$@"Draft a clarification question for domain `{domain}` against `{targetRepo}`. Each clarification question must include: background, question, options, pros/cons, and recommendation.

First-call sequence (read-only; required before any mutation):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — surface `primary` / `support` / `advanced` / `experimental` buckets.
4. `intent-cli automation summary --format json` — canonical label-driven contract and capability JSON.
5. `intent-cli intent status --domain {domain} --format json` — current baseline / WIP / queued / clarifications.
6. `intent-cli intent search --domain {domain} --format json` — locate related intents across the domain.
7. `intent-cli intent next-slice --dry-run --domain {domain} --target-repo {targetRepo} --format json` — surface current clarification gates.

Clarification steps:
1. Confirm cwd is the parent host repo root.
2. Read the clarification gate from the dry-run output and any existing open questions in the clarifications file.
3. For each unclear decision, draft one question with this required structure:
   - **Background**: what accepted parent state is known; what the ambiguity is.
   - **Question**: one focused, answerable question.
   - **Options**: 2–4 concrete options with short labels.
   - **Pros / Cons**: for each option, 1–2 bullet pros and 1–2 bullet cons.
   - **Recommendation**: the agent's recommended option with a single-sentence rationale.
4. Present the draft to the operator for acceptance.
5. After acceptance: record the clarification entry via `intent-cli clarify record --write` (if available) or append the entry to `intents/{domain}/clarifications/open.md` under `## Open Questions`.
6. Commit and push the parent state.
7. Do not publish a GitHub issue in this wake; clarifications must be resolved first.

Hard rules:
- Do not read `intents/rules/**`, local skill files, or copied prompt files. Use `intent-cli guide ...` and `intent-cli intent ...` instead.
- Do not call `intent-cli run`. `run` is advanced runtime, not the intent-work path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- Every clarification question must include: background, question, options, pros/cons, and recommendation.
- Do not guess a product or spec decision when source-of-truth is ambiguous; surface it as a clarification instead.
- Do not publish a GitHub issue in this wake.";

        return new GuideIntentWorkSetupResult
        {
            Kind = KindClarification,
            Domain = domain,
            TargetRepo = targetRepo,
            Prompt = prompt,
            FirstCalls = new[]
            {
                "intent-cli guide model --format json",
                "intent-cli guide onboarding --format json",
                "intent-cli guide commands list --format json",
                "intent-cli automation summary --format json",
                $"intent-cli intent status --domain {domain} --format json",
                $"intent-cli intent search --domain {domain} --format json",
                $"intent-cli intent next-slice --dry-run --domain {domain} --target-repo {targetRepo} --format json"
            },
            ForbiddenSources = new[]
            {
                "intents/rules/**",
                "local skill files",
                "copied prompt files"
            },
            ClarificationFormat = "background, question, options, pros/cons, and recommendation",
            IssuePublishBoundary = "Do not publish a GitHub issue in this wake. Clarifications must be resolved before issue publication.",
            WorktreeFriendly = "The prompt names the parent host repo worktree root as cwd and references domain/target-repo from arguments; no operator-specific paths are hard-coded, so it works across host-side worktrees."
        };
    }

    private static GuideIntentWorkSetupResult BuildIntentShape(string domain, string targetRepo)
    {
        var prompt =
$@"Shape and adjust intents for domain `{domain}` against `{targetRepo}`. Conduct clarification interviews with the product owner as needed, preload future packets, and publish at most one GitHub issue per wake.

First-call sequence (read-only; required before any mutation):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — surface `primary` / `support` / `advanced` / `experimental` buckets.
4. `intent-cli automation summary --format json` — canonical label-driven contract and capability JSON.
5. `intent-cli intent status --domain {domain} --format json` — current baseline / WIP / queued / clarifications.
6. `intent-cli intent search --domain {domain} --format json` — locate related intents across the domain.
7. `intent-cli intent next-slice --dry-run --domain {domain} --target-repo {targetRepo} --format json` — verify WIP cap and clarification gates.

For each intent needing deeper review:
- `intent-cli intent explain <id> --format json` — surface the full intent tree, dependencies, and current execution state.

If PO input is needed for a clarification:
- `intent-cli interview <flow> [--format json]` — run a structured intake interview to record PO answers. Use the result to update the clarification record before proceeding.

Each clarification question must include:
- **Background**: what accepted parent state is known; what the ambiguity is.
- **Question**: one focused, answerable question.
- **Options**: 2–4 concrete options with short labels.
- **Pros / Cons**: for each option, 1–2 bullet pros and 1–2 bullet cons.
- **Recommendation**: the agent's recommended option with a single-sentence rationale.

Intent-shaping steps:
1. Confirm cwd is the parent host repo root.
2. Read `intent status` and `intent search` output to map accepted intents, execution units, open clarifications, and WIP.
3. For intents needing adjustment: run `intent explain <id>` to get full context before proposing any change.
4. Identify: intents to merge, split, re-scope, or retire; clarifications that must be resolved first.
5. Present the proposed adjustments to the operator for acceptance. Do not write anything until accepted.
6. After acceptance: apply only the operator-approved adjustments to the intent tree. Commit and push before packet work.

Packet preload (optional, after intent shaping is accepted):
1. For each accepted continuation candidate: `intent-cli packet draft --execution-unit <id> --target-repo {targetRepo} --dry-run --format markdown`.
2. Present each preview to the operator. Accept or reject independently.
3. For accepted packets: `intent-cli packet draft --execution-unit <id> --target-repo {targetRepo} --format json`.
4. Multiple packets may be preloaded; mark each as `queued` in queue-state without linked issues.

Issue publication (at most one per wake):
1. After parent durable state (intent files, packet files, queue-state) is committed and pushed:
   `intent-cli issue publish-flow <id> --repo {targetRepo} --write --format json`
2. After parent state is pushed: `intent-cli automation issue-publish --repo {targetRepo} --issue <n> --write --format json`.
3. Publish at most one GitHub issue per wake regardless of how many packets are preloaded.

Hard rules:
- Do not read `intents/rules/**`, local skill files, or copied prompt files. Use `intent-cli guide ...`, `intent-cli intent ...`, and `intent-cli interview ...` instead.
- Do not call `intent-cli run`. `run` is advanced runtime, not the intent-work path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- All label transitions go through installed `intent-cli automation` commands.
- `intent-target` is the host-owned publish boundary; apply it only via `intent-cli automation issue-publish` after parent state is pushed.
- Publish at most one GitHub issue per wake.
- Stop on Hard Clarification rather than guessing when source-of-truth is ambiguous.";

        return new GuideIntentWorkSetupResult
        {
            Kind = KindIntentShape,
            Domain = domain,
            TargetRepo = targetRepo,
            Prompt = prompt,
            FirstCalls = new[]
            {
                "intent-cli guide model --format json",
                "intent-cli guide onboarding --format json",
                "intent-cli guide commands list --format json",
                "intent-cli automation summary --format json",
                $"intent-cli intent status --domain {domain} --format json",
                $"intent-cli intent search --domain {domain} --format json",
                $"intent-cli intent next-slice --dry-run --domain {domain} --target-repo {targetRepo} --format json"
            },
            ForbiddenSources = new[]
            {
                "intents/rules/**",
                "local skill files",
                "copied prompt files"
            },
            ClarificationFormat = "background, question, options, pros/cons, and recommendation",
            IssuePublishBoundary = "Publish at most one GitHub issue per wake even when multiple packets are preloaded. Use `intent-cli issue publish-flow` then `intent-cli automation issue-publish` after parent durable state is committed and pushed.",
            WorktreeFriendly = "The prompt names the parent host repo worktree root as cwd and references domain/target-repo from arguments; no operator-specific paths are hard-coded, so it works across host-side worktrees."
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideIntentWorkSetupResult result)
    {
        writer.WriteLine($"# Guide intent-work setup — {result.Kind}");
        writer.WriteLine();
        writer.WriteLine($"- domain: {result.Domain}");
        writer.WriteLine($"- target repo: {result.TargetRepo}");
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

        writer.WriteLine("## Clarification format");
        writer.WriteLine();
        writer.WriteLine(result.ClarificationFormat);
        writer.WriteLine();

        writer.WriteLine("## Issue publish boundary");
        writer.WriteLine();
        writer.WriteLine(result.IssuePublishBoundary);
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
        out string? domain,
        out string? targetRepo,
        out string format,
        out string error)
    {
        kind = null;
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
        writer.WriteLine("guide intent-work setup");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only paste-ready prompts for parent-side intent work: domain organization, next-slice analysis, packet preload, clarification drafting, and intent shaping.");
        writer.WriteLine();
        writer.WriteLine("Supported kinds:");
        writer.WriteLine($"- {KindDomainOrganize}  (--domain, --target-repo required)");
        writer.WriteLine($"- {KindNextSlice}        (--domain, --target-repo required)");
        writer.WriteLine($"- {KindPacketPreload}    (--domain, --target-repo required)");
        writer.WriteLine($"- {KindClarification}   (--domain, --target-repo required)");
        writer.WriteLine($"- {KindIntentShape}     (--domain, --target-repo required)");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record GuideIntentWorkSetupResult
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("target_repo")]
    public required string TargetRepo { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    [JsonPropertyName("first_calls")]
    public required IReadOnlyList<string> FirstCalls { get; init; }

    [JsonPropertyName("forbidden_sources")]
    public required IReadOnlyList<string> ForbiddenSources { get; init; }

    [JsonPropertyName("clarification_format")]
    public required string ClarificationFormat { get; init; }

    [JsonPropertyName("issue_publish_boundary")]
    public required string IssuePublishBoundary { get; init; }

    [JsonPropertyName("worktree_friendly")]
    public required string WorktreeFriendly { get; init; }
}
