using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G267: Read-only <c>intent-cli guide review run</c>. Returns a paste-ready,
/// skill-free deterministic PR review prompt for AI agents, replacing routine
/// dependence on local <c>intent-review</c> skill files. The prompt uses
/// <c>automation host-review-preflight</c>, <c>guide review</c>, <c>review
/// closeout-plan</c>, PR diff, linked issue, and packet refs to drive a
/// findings-first review, then delegates all label transitions to installed
/// <c>intent-cli automation pr-transition</c> commands.
/// Never mutates state. Never launches an AI provider.
/// </summary>
internal static class GuideReviewRunCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide review run [--domain <name>] [--target-repo <owner/repo>] [--format markdown|json]";

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

        var result = BuildReviewRun(domain, targetRepo);
        return EmitResult(writer, format, result);
    }

    private static int EmitResult(TextWriter writer, string format, GuideReviewRunResult result)
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

    private static GuideReviewRunResult BuildReviewRun(string? domain, string? targetRepo)
    {
        var domainPlaceholder = string.IsNullOrWhiteSpace(domain) ? "<DOMAIN>" : domain;
        var targetRepoPlaceholder = string.IsNullOrWhiteSpace(targetRepo) ? "<TARGET_REPO>" : targetRepo;

        var prompt =
$@"Run one PR review wake for domain `{domainPlaceholder}` against `{targetRepoPlaceholder}`. Do not use the `intent-review` skill file, local skill files, or copied prompt files.

First-call sequence (read-only; required before any review work):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — primary vs support vs advanced vs experimental classification.
4. `intent-cli automation summary --domain {domainPlaceholder} --format json` — canonical label-driven contract and capability JSON.

Stage 1 — preflight (required before reading any PR):
1. `intent-cli automation host-review-preflight --repo {targetRepoPlaceholder} --format json` → selects an eligible PR.
   - If the result has no eligible PR, stop with `idle`. Do not proceed.
2. Note the selected PR number from the preflight result.

Stage 2 — collect review inputs (all read-only):
1. `intent-cli guide review --pr <n> --repo {targetRepoPlaceholder} --domain {domainPlaceholder} --format json` → review guidance, packet refs, review checklist, and review boundaries.
2. `gh pr view <n> --repo {targetRepoPlaceholder}` → PR title, body, linked issue number.
3. `gh pr diff <n> --repo {targetRepoPlaceholder}` → full diff.
4. `gh issue view <LINKED_ISSUE> --repo {targetRepoPlaceholder}` → linked issue body (Acceptance Criteria, In Scope, Out Of Scope, Verification).
5. `intent-cli review closeout-plan --pr <n> --repo {targetRepoPlaceholder} --domain {domainPlaceholder} --format json` → closeout plan and any blocking items.

Stage 3 — review (findings-first; performed by the AI agent):
- Read the diff against the linked issue's Acceptance Criteria and In Scope / Out Of Scope sections.
- Read the PR against any packet refs returned by `guide review`.
- Use a findings-first approach: enumerate concrete findings before rendering a verdict.
- A finding is a concrete, actionable observation tied to a specific file/line or a specific Acceptance Criterion.
- If all findings are minor (style, nit) and Acceptance Criteria are met: verdict is `approved`.
- If any finding blocks an Acceptance Criterion or violates an Out Of Scope boundary: verdict is `request-update`.
- Render the review as a structured report: findings list, verdict, next action.

Stage 4 — label transition (required; delegate to intent-cli):
- If verdict is `approved`:
  `intent-cli automation pr-transition --transition approved --repo {targetRepoPlaceholder} --pr <n> --write --format json`
- If verdict is `request-update`:
  1. Post an actionable PR comment: `gh pr comment <n> --repo {targetRepoPlaceholder} --body ""...""`  listing each finding with file/line, description, and a concrete suggested fix.
  2. `intent-cli automation pr-transition --transition request-update --repo {targetRepoPlaceholder} --pr <n> --write --format json`

Hard rules:
- Do not use the `intent-review` skill file or any local skill file.
- Do not merge the PR. Merging happens via the host closeout path after `approved`.
- Do not read `intents/rules/**`, local skill files, or copied prompt files.
- All label transitions go through installed `intent-cli automation pr-transition`. No manual `gh ... edit --add-label` / `--remove-label` fallback for workflow labels.
- Do not call `intent-cli run`. `run` is for integration smoke/replay/dogfooding, not the host review path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- Process at most one PR review per wake.";

        return new GuideReviewRunResult
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
                "intent-review skill file",
                "local skill files",
                "copied prompt files",
                "intents/rules/**"
            },
            LabelOwnership = "All review label transitions delegated to installed `intent-cli automation pr-transition`. Manual `gh ... edit --label` fallback is forbidden.",
            WorktreeFriendly = "The prompt resolves domain and target-repo from CLI args; no hard-coded local paths."
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideReviewRunResult result)
    {
        writer.WriteLine($"# Guide review — {result.Kind}");
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
        writer.WriteLine("guide review run");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only paste-ready skill-free PR review prompt for AI agents.");
        writer.WriteLine();
        writer.WriteLine("  --domain is optional; omit to emit a <DOMAIN> placeholder.");
        writer.WriteLine("  --target-repo is optional; omit to emit a <TARGET_REPO> placeholder.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record GuideReviewRunResult
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
