using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G265: Read-only <c>intent-cli guide worker issue-to-pr</c>. Returns a
/// paste-ready, skill-free issue-to-PR implementation prompt so AI agents can
/// implement issues without depending on local <c>gh-issue-to-pr</c> skill
/// files or copied prompt files. The prompt includes the standalone contract
/// gate, origin/main branch creation, minimal implementation, focused
/// validation, draft PR creation, and worker claim/complete handoff.
/// Never mutates state. Never launches an AI provider.
/// </summary>
internal static class GuideWorkerIssueToPrCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide worker issue-to-pr [--repo <owner/repo>] [--domain <name>] [--format markdown|json]";

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

        var result = BuildIssueToPr(repo, domain);
        return EmitResult(writer, format, result);
    }

    private static int EmitResult(TextWriter writer, string format, GuideWorkerIssueToPrResult result)
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

    private static GuideWorkerIssueToPrResult BuildIssueToPr(string? repo, string? domain)
    {
        var repoLabel = string.IsNullOrWhiteSpace(repo) ? "the repo in the current worktree" : $"`{repo}`";
        var domainPlaceholder = string.IsNullOrWhiteSpace(domain) ? "<DOMAIN>" : domain;

        var prompt =
$@"Implement the issue returned by `intent-cli worker next-action` as a draft PR for {repoLabel}. Do not use the `gh-issue-to-pr` skill file, local skill files, or copied prompt files.

First-call sequence (read-only; required before any code work):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — primary vs support vs advanced vs experimental classification.
4. `intent-cli automation summary --domain {domainPlaceholder} --format json` — canonical label-driven contract and capability JSON.

Standalone contract gate (required before branch creation or code changes):
Read the issue body with `gh issue view <n> --repo <OWNER>/<REPO>`. The body must contain all of:
- Goal
- Why This Slice Exists Now
- Current Observed State or Reproduction
- Accepted Baseline You May Assume
- Target Repo-Path-Part
- In Scope
- Out Of Scope
- Acceptance Criteria
- Verification

If any required section is missing or if the real contract lives only in linked parent files, decline before creating any branch or code change. Report the missing sections and stop. Set outcome to `declined-contract-incomplete`.

Implementation steps:
1. Claim the issue: `intent-cli worker claim --kind issue --number <n> --repo <OWNER>/<REPO> --write --format json`.
2. Fetch origin/main: `git fetch origin main`. Create a new branch `claude/<slug>` from `origin/main`. Never reuse an existing unrelated branch.
3. Read only the source files needed to implement the issue correctly. Match the repository's existing style and patterns.
4. Implement only the requested change. Do not widen scope. Do not add unrequested refactors.
5. Run the most relevant targeted tests. Report the command and the result. If broader validation is required by the repository contract or the touched area, run it too.
6. Push the branch and create a draft PR with `gh pr create --draft --title ""..."" --body ""..."" --repo <OWNER>/<REPO>`.
7. Do not add `intent-target` or `intent-pr-created` to the PR.
8. From the parent host root, run `intent-cli worker result-summary --kind issue-to-pr --issue <n> --pr <pr-n> --repo <OWNER>/<REPO> --format json`, then `intent-cli worker complete --kind issue --number <n> --repo <OWNER>/<REPO> --outcome pr-created --write --format json`.

Outcome classification:
- `pr-created` — draft PR created successfully.
- `declined-contract-incomplete` — issue body missing required sections; no branch or PR created.
- `clarification-required` — ambiguous contract or blocker found during implementation; no PR created.
- `already-resolved` — the issue is already fixed in origin/main; no PR created.
- `failed` — implementation failed (build/test failure, unresolvable merge conflict).
- `label-cleanup-required` — stale labels prevent clean claim/complete flow.

Hard rules:
- Use the issue body as the standalone contract. Do not read parent host packet files, `intents/rules/**`, local skill files, or copied prompt files to fill contract gaps.
- Do not use the `gh-issue-to-pr` skill file or any local skill file that restates workflow.
- {DispatcherSkillCarveOut.Sentence}
- Do not add `intent-target` to the PR; it is host-owned.
- Do not add `intent-pr-created` to the PR; it is an issue-side completion marker applied by `worker complete`.
- All label transitions go through `intent-cli worker claim` / `intent-cli worker complete`. No manual `gh ... edit --add-label` / `--remove-label` fallback for workflow labels.
- Do not call `intent-cli run`. `run` is for integration smoke/replay/dogfooding, not the chat-first implementation path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- If the issue body is a thin stub or points only to parent breadcrumbs for the real contract, decline before creating any branch.

Repeated-stall recovery (G408: when the same issue has stalled without progress for two or more consecutive wakes):
1. Do not retry the same action again without first re-reading the installed guidance:
   - `intent-cli guide model --format json`
   - `intent-cli guide onboarding --format json`
   - `intent-cli guide commands list --format json`
   - `intent-cli automation summary --domain {domainPlaceholder} --format json`
   - `intent-cli worker issue-preflight --repo <OWNER>/<REPO> --issue <n> --format json`
2. Determine whether the stall was caused by prior noncompliance with intent-cli guidance (e.g. missing worker claim, wrong branch base, missing `Closes #<n>` in PR body, host-owned label applied from child loop). If yes, apply the one safe current-lane repair that `intent-cli` explicitly marks as owned by the child loop (`child-selector-label-gap` category).
3. If the stall is caused by a host-owned or operator-owned gap (`host-artifact-repair-required`, `ambiguous-contract`, unsafe durable state, or operator policy decision), stop and emit a structured operator stop. Do not silently bypass or invent workarounds.
4. Apply at most one guided repair per recovery cycle. If the stall persists after the repair, escalate to an operator stop rather than retrying indefinitely.";

        return new GuideWorkerIssueToPrResult
        {
            Kind = "issue-to-pr",
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
            RepeatedStallRecovery = new RepeatedStallRecoveryGuidance
            {
                Trigger = "Same issue stalled without progress for two or more consecutive wakes.",
                ReReadGuidance = new[]
                {
                    "intent-cli guide model --format json",
                    "intent-cli guide onboarding --format json",
                    "intent-cli guide commands list --format json",
                    $"intent-cli automation summary --domain {domainPlaceholder} --format json",
                    "intent-cli worker issue-preflight --repo <OWNER>/<REPO> --issue <n> --format json"
                },
                SafeRepairCategory = "child-selector-label-gap",
                OperatorStopCategories = new[] { "host-artifact-repair-required", "ambiguous-contract", "unsafe-durable-state", "operator-policy-decision" },
                MaxRepairsPerCycle = 1
            },
            ContractGateSections = new[]
            {
                "Goal",
                "Why This Slice Exists Now",
                "Current Observed State or Reproduction",
                "Accepted Baseline You May Assume",
                "Target Repo-Path-Part",
                "In Scope",
                "Out Of Scope",
                "Acceptance Criteria",
                "Verification"
            },
            OutcomeClassification = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["pr-created"] = "Draft PR created successfully.",
                ["declined-contract-incomplete"] = "Issue body missing required sections; no branch or PR created.",
                ["clarification-required"] = "Ambiguous contract or blocker found during implementation; no PR created.",
                ["already-resolved"] = "The issue is already fixed in origin/main; no PR created.",
                ["failed"] = "Implementation failed (build/test failure, unresolvable merge conflict).",
                ["label-cleanup-required"] = "Stale labels prevent clean claim/complete flow."
            },
            ForbiddenSources = new[]
            {
                "gh-issue-to-pr skill file",
                DispatcherSkillCarveOut.ForbiddenSourceItem,
                "copied prompt files",
                "intents/rules/**",
                "parent host packet files (for filling contract gaps)"
            },
            LabelOwnership = "All label transitions delegated to installed intent-cli worker claim / worker complete. Manual `gh ... edit --label` fallback is forbidden.",
            WorktreeFriendly = "The prompt resolves the repo from the child worktree's `gh` / `git remote` and runs worker commands from the parent host root with --repo; no hard-coded paths."
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideWorkerIssueToPrResult result)
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

        writer.WriteLine("## Contract gate sections (required in issue body)");
        foreach (var section in result.ContractGateSections)
        {
            writer.WriteLine($"- {section}");
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

        writer.WriteLine("## Repeated-stall recovery (G408)");
        writer.WriteLine();
        writer.WriteLine($"Trigger: {result.RepeatedStallRecovery.Trigger}");
        writer.WriteLine();
        writer.WriteLine("Re-read guidance:");
        foreach (var call in result.RepeatedStallRecovery.ReReadGuidance)
        {
            writer.WriteLine($"- `{call}`");
        }
        writer.WriteLine();
        writer.WriteLine($"Safe repair category: `{result.RepeatedStallRecovery.SafeRepairCategory}`");
        writer.WriteLine($"Operator stop categories: {string.Join(", ", result.RepeatedStallRecovery.OperatorStopCategories.Select(c => $"`{c}`"))}");
        writer.WriteLine($"Max repairs per cycle: {result.RepeatedStallRecovery.MaxRepairsPerCycle}");
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
        writer.WriteLine("guide worker issue-to-pr");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only paste-ready skill-free issue-to-PR implementation prompt for AI agents.");
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

internal sealed record GuideWorkerIssueToPrResult
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

    [JsonPropertyName("contract_gate_sections")]
    public required IReadOnlyList<string> ContractGateSections { get; init; }

    [JsonPropertyName("outcome_classification")]
    public required IReadOnlyDictionary<string, string> OutcomeClassification { get; init; }

    [JsonPropertyName("forbidden_sources")]
    public required IReadOnlyList<string> ForbiddenSources { get; init; }

    [JsonPropertyName("label_ownership")]
    public required string LabelOwnership { get; init; }

    [JsonPropertyName("worktree_friendly")]
    public required string WorktreeFriendly { get; init; }

    [JsonPropertyName("repeated_stall_recovery")]
    public required RepeatedStallRecoveryGuidance RepeatedStallRecovery { get; init; }
}

/// <summary>
/// G408: Structured repeated-stall recovery guidance emitted by worker guide commands.
/// </summary>
internal sealed record RepeatedStallRecoveryGuidance
{
    [JsonPropertyName("trigger")]
    public required string Trigger { get; init; }

    [JsonPropertyName("re_read_guidance")]
    public required IReadOnlyList<string> ReReadGuidance { get; init; }

    [JsonPropertyName("safe_repair_category")]
    public required string SafeRepairCategory { get; init; }

    [JsonPropertyName("operator_stop_categories")]
    public required IReadOnlyList<string> OperatorStopCategories { get; init; }

    [JsonPropertyName("max_repairs_per_cycle")]
    public required int MaxRepairsPerCycle { get; init; }
}
