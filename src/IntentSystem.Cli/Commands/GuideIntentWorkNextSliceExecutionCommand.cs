using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G269: Read-only <c>intent-cli guide intent-work next-slice-execution</c>.
/// Returns a paste-ready, worktree-friendly next-slice execution prompt for AI
/// agents, replacing routine dependence on local <c>intent-next-slice</c>
/// skills. The prompt covers intent status/search/dry-run before mutation,
/// clarification question format (background, question, options, pros/cons,
/// recommendation), multiple future packet preloads, at-most-one GitHub issue
/// publication per wake, packet draft, issue publish-flow, parent durable
/// commit/push, and automation issue-publish for the publish boundary. Never
/// mutates state. Never launches an AI provider.
/// </summary>
internal static class GuideIntentWorkNextSliceExecutionCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide intent-work next-slice-execution --domain <domain> --target-repo <owner/repo> [--format markdown|json]";

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

        var result = BuildNextSliceExecution(domain!, targetRepo!);
        return EmitResult(writer, format, result);
    }

    private static int EmitResult(TextWriter writer, string format, GuideIntentWorkNextSliceExecutionResult result)
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

    private static GuideIntentWorkNextSliceExecutionResult BuildNextSliceExecution(string domain, string targetRepo)
    {
        var prompt =
$@"Execute the next slice for domain `{domain}` against `{targetRepo}`. Do not use the `intent-next-slice` skill file, local skill files, or copied prompt files.

First-call sequence (read-only; required before any mutation):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — surface `primary` / `support` / `advanced` / `experimental` buckets.
4. `intent-cli automation summary --format json` — canonical label-driven contract and capability JSON.
5. `intent-cli intent status --domain {domain} --format json` — current baseline / WIP / queued / clarifications.
6. `intent-cli intent search --domain {domain} --format json` — locate related intents across the domain.
7. `intent-cli intent next-slice --dry-run --domain {domain} --target-repo {targetRepo} --format json` — verify WIP cap and clarification gates; surface recommended next execution unit.

Pre-execution gate (evaluate dry-run before any mutation):
Read the dry-run output. If `recommended_outcome` is not `issue-cut-ready`, surface the blocker and stop. Do not cut an issue, write a packet, or publish anything until the gate passes.

If open clarifications are present, draft each question with this required structure:
- **Background**: what accepted parent state is known; what the ambiguity is.
- **Question**: one focused, answerable question.
- **Options**: 2–4 concrete options with short labels.
- **Pros / Cons**: for each option, 1–2 bullet pros and 1–2 bullet cons.
- **Recommendation**: the agent's recommended option with a single-sentence rationale.
Record the clarification via `intent-cli clarify record --write` (if available) or append to `intents/{domain}/clarifications/open.md` under `## Open Questions`, commit and push, then stop. Do not publish a GitHub issue while clarifications are open.

Execution steps (only when dry-run gate passes):
1. Confirm cwd is the parent host repo root.
2. Preview the recommended execution unit packet: `intent-cli packet draft --execution-unit <id> --target-repo {targetRepo} --dry-run --format markdown`.
3. Present the packet preview to the operator for acceptance.
4. After acceptance:
   a. Write the packet: `intent-cli packet draft --execution-unit <id> --target-repo {targetRepo} --format json`.
   b. Run `intent-cli issue publish-flow <id> --repo {targetRepo} --write --format json` to commit parent state and prepare the issue body.
   c. After parent durable state is pushed: `intent-cli automation issue-publish --repo {targetRepo} --issue <n> --write --format json`.
5. Multiple future packets may be preloaded (dry-run only) when parent state supports them. Mark preloaded packets as `queued` in queue-state without linked issues; the next-slice loop will publish them in future wakes.
6. Publish at most one GitHub issue per wake regardless of how many packets are preloaded.

Parent state commit and push:
After writing packet files and before calling `intent-cli automation issue-publish`, commit and push only the relevant parent durable files for the selected execution unit:
```
git add .intent-cli/queue-state.json .intent-cli/runs.jsonl .intent-cli/issues/<EU>/ intents/{domain}/issues/<EU>/ && git commit -m ""G<n>: <title>"" && git push
```
Also stage the child submodule pointer when applicable: `git add submodules/<child-repo-name>`.
Do NOT use `git add -A`; staging unrelated local changes into a publish commit corrupts the durable parent state.
This is the durable publish boundary. `intent-target` is applied only after parent state is durably committed.

Hard rules:
- Do not use the `intent-next-slice` skill file, local skill files, or copied prompt files. Use `intent-cli guide ...` and `intent-cli intent ...` instead.
- Do not read `intents/rules/**`.
- Do not call `intent-cli run`. `run` is advanced runtime, not the intent-work path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- All label transitions go through installed `intent-cli automation` commands.
- `intent-target` is the host-owned publish boundary; apply it only via `intent-cli automation issue-publish` after parent state is pushed.
- Publish at most one GitHub issue per wake.
- Do not use `git add -A` for the parent state commit; always stage only the relevant durable files explicitly.
- Stop on Hard Clarification rather than guessing when source-of-truth is ambiguous.";

        return new GuideIntentWorkNextSliceExecutionResult
        {
            Kind = "next-slice-execution",
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
                "intent-next-slice skill file",
                "local skill files",
                "copied prompt files",
                "intents/rules/**"
            },
            ClarificationFormat = "background, question, options, pros/cons, and recommendation",
            IssuePublishBoundary = "Publish at most one GitHub issue per wake. Use `intent-cli issue publish-flow` then `intent-cli automation issue-publish` after parent durable state is committed and pushed.",
            WorktreeFriendly = "The prompt names the parent host repo worktree root as cwd and references domain/target-repo from arguments; no operator-specific paths are hard-coded, so it works across host-side worktrees."
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideIntentWorkNextSliceExecutionResult result)
    {
        writer.WriteLine($"# Guide intent-work next-slice-execution");
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

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide intent-work next-slice-execution");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only paste-ready skill-free next-slice execution prompt for AI agents.");
        writer.WriteLine("Replaces routine dependence on local intent-next-slice skill files.");
        writer.WriteLine();
        writer.WriteLine("  --domain is required.");
        writer.WriteLine("  --target-repo is required.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record GuideIntentWorkNextSliceExecutionResult
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
