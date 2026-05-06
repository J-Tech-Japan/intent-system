using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G266: Read-only <c>intent-cli guide worker pr-comment-fix</c>. Returns a
/// paste-ready, skill-free PR comment fix prompt so AI agents can repair a PR
/// branch without depending on local <c>gh-fix-pr-comment</c> skill files or
/// copied prompt files. The prompt covers selecting unresolved actionable
/// comments, checking out the existing PR branch, applying a narrow fix,
/// focused validation, push, worker result-summary, and worker complete.
/// Never mutates state. Never launches an AI provider.
/// </summary>
internal static class GuideWorkerPrCommentFixCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide worker pr-comment-fix [--repo <owner/repo>] [--domain <name>] [--format markdown|json]";

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

        if (!TryParseArguments(args, out var repo, out var domain, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var result = BuildPrCommentFix(repo, domain);
        return EmitResult(writer, format, result);
    }

    private static int EmitResult(TextWriter writer, string format, GuideWorkerPrCommentFixResult result)
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

    private static GuideWorkerPrCommentFixResult BuildPrCommentFix(string? repo, string? domain)
    {
        var repoLabel = string.IsNullOrWhiteSpace(repo) ? "the repo in the current worktree" : $"`{repo}`";
        var domainPlaceholder = string.IsNullOrWhiteSpace(domain) ? "<DOMAIN>" : domain;

        var prompt =
$@"Repair the PR branch for {repoLabel} based on review comments. The PR URL returned by `intent-cli worker next-action` (or supplied directly by the operator) is the authoritative work input for this turn; do not inspect parent host queue-state or linked PR fields to decide what to repair. Do not use the `gh-fix-pr-comment` skill file, local skill files, or copied prompt files.

First-call sequence (read-only; required before any code work):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — primary vs support vs advanced vs experimental classification.
4. `intent-cli automation summary --domain {domainPlaceholder} --format json` — canonical label-driven contract and capability JSON.

Comment triage (required before checkout or code changes):
Read review comments with `gh pr view <n> --repo <OWNER>/<REPO> --comments` and `gh api repos/<OWNER>/<REPO>/pulls/<n>/reviews`. Identify all unresolved, actionable review comments.
- If there are no unresolved actionable comments, stop. Set outcome to `no-actionable-comments`.
- If a comment is ambiguous and cannot be resolved without guessing, stop. Set outcome to `clarification-required`. Report which comment and why.
- If the requested change is already applied in the existing branch, stop. Set outcome to `already-resolved`.

Repair steps:
1. Claim the PR: `intent-cli worker claim --kind pr --number <n> --repo <OWNER>/<REPO> --write --format json`.
2. Check out the existing PR branch: `gh pr checkout <n> --repo <OWNER>/<REPO>`. Do not create a new branch.
3. Apply only the narrow change requested in the review comments. Do not add unrequested refactors or opportunistic cleanups.
4. Run the most relevant targeted tests. Report the command and the result.
5. Push the repaired branch: `git push`. Do not force-push unless the PR branch explicitly requires it.
6. From the parent host root, run `intent-cli worker result-summary --kind pr-comment-fix --pr <n> --repo <OWNER>/<REPO> --format json`, then `intent-cli worker complete --kind pr --number <n> --repo <OWNER>/<REPO> --outcome repair-pushed --write --format json`.

Outcome classification:
- `repair-pushed` — narrow fix pushed to the existing PR branch.
- `no-actionable-comments` — no unresolved actionable review comments; nothing to fix.
- `already-resolved` — the requested change is already applied in the branch.
- `clarification-required` — ambiguous comment or blocker found; cannot proceed without operator input.
- `failed` — fix failed (build/test failure, unresolvable conflict).
- `label-cleanup-required` — stale labels prevent clean claim/complete flow.

Hard rules:
- Do not use the `gh-fix-pr-comment` skill file or any local skill file.
- Do not create a new branch. Check out and repair the existing PR branch.
- Repair only the narrow change requested in review comments. Do not widen scope.
- Do not read `intents/rules/**`, local skill files, or copied prompt files.
- All label transitions go through `intent-cli worker claim` / `intent-cli worker complete`. No manual `gh ... edit --add-label` / `--remove-label` fallback for workflow labels.
- Do not call `intent-cli run`. `run` is for integration smoke/replay/dogfooding, not the chat-first repair path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- Do not add `intent-target` to the PR; it is host-owned.
- Do not add `intent-pr-created` to the PR; it is an issue-side completion marker.
- Do not edit `queue-state.json`, `linked_issue`, or `linked_pr`; those are host-owned durable bookkeeping and must not be touched during a PR comment fix turn.
- Do not run `intent-cli automation issue-publish`; that command is for publishing child issues, not for resolving PR comment repairs.";

        return new GuideWorkerPrCommentFixResult
        {
            Kind = "pr-comment-fix",
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
            OutcomeClassification = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["repair-pushed"] = "Narrow fix pushed to the existing PR branch.",
                ["no-actionable-comments"] = "No unresolved actionable review comments; nothing to fix.",
                ["already-resolved"] = "The requested change is already applied in the branch.",
                ["clarification-required"] = "Ambiguous comment or blocker found; cannot proceed without operator input.",
                ["failed"] = "Fix failed (build/test failure, unresolvable conflict).",
                ["label-cleanup-required"] = "Stale labels prevent clean claim/complete flow."
            },
            ForbiddenSources = new[]
            {
                "gh-fix-pr-comment skill file",
                "local skill files",
                "copied prompt files",
                "intents/rules/**"
            },
            LabelOwnership = "All label transitions delegated to installed intent-cli worker claim / worker complete. Manual `gh ... edit --label` fallback is forbidden.",
            WorktreeFriendly = "The prompt resolves the repo from the current worktree's `gh` / `git remote` and runs worker commands from the parent host root with --repo; no hard-coded paths."
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideWorkerPrCommentFixResult result)
    {
        writer.WriteLine($"# Guide worker — {result.Kind}");
        writer.WriteLine();
        if (!string.IsNullOrWhiteSpace(result.Repo))
        {
            writer.WriteLine($"- repo: {result.Repo}");
        }
        if (!string.IsNullOrWhiteSpace(result.Domain))
        {
            writer.WriteLine($"- domain: {result.Domain}");
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
        out string? repo,
        out string? domain,
        out string format,
        out string error)
    {
        repo = null;
        domain = null;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
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
        writer.WriteLine("guide worker pr-comment-fix");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only paste-ready skill-free PR comment fix prompt for AI agents.");
        writer.WriteLine();
        writer.WriteLine("  --repo is optional; omit to derive the repo from the current child worktree.");
        writer.WriteLine("  --domain is optional; omit to emit a <DOMAIN> placeholder in the generated prompt.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record GuideWorkerPrCommentFixResult
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("repo")]
    public string? Repo { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    [JsonPropertyName("first_calls")]
    public required IReadOnlyList<string> FirstCalls { get; init; }

    [JsonPropertyName("outcome_classification")]
    public required IReadOnlyDictionary<string, string> OutcomeClassification { get; init; }

    [JsonPropertyName("forbidden_sources")]
    public required IReadOnlyList<string> ForbiddenSources { get; init; }

    [JsonPropertyName("label_ownership")]
    public required string LabelOwnership { get; init; }

    [JsonPropertyName("worktree_friendly")]
    public required string WorktreeFriendly { get; init; }
}
