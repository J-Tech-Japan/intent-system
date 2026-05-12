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
            Command = "intent-cli issue publish-flow <execution-unit> --repo <owner/repo> [--domain <name>] [--write] [--format json|markdown]",
            Purpose = "End-to-end publish: validate packet → create issue → advance durable state. Without `--write` this is a dry run that surfaces missing contract sections.",
            Boundary = "Stage boundary: durable host state is now advanced (queue-state, packet publish.yaml, runs.jsonl). The issue is on GitHub but the `intent-target` LABEL has not been applied yet.",
            FailsOpen = "If validation surfaces missing contract sections (Goal / AC / OOS / Verification / Closes ref etc.), STOP and repair the packet. `publish-flow` without `--write` is the canonical preflight."
        },
        new PublishStage
        {
            Stage = "automation issue-publish",
            Command = "intent-cli automation issue-publish --repo <owner/repo> --issue <n> --write [--dry-run] [--format text|json]",
            Purpose = "FINAL publish boundary: apply the `intent-target` label so the child implementation loop picks the issue up next. This is the ONE place `intent-target` is added.",
            Boundary = "Stage boundary: `intent-target` is now on the GitHub issue. The child loop's `worker next-action` selector will return this issue as `action: issue-to-pr` on its next wake.",
            FailsOpen = "Raw `gh issue edit --add-label intent-target` is FORBIDDEN — it skips the issue-publish capability check, the publish.yaml advance, and the host audit trail. If `automation issue-publish` refuses (missing publish.yaml, mismatched repo, host-sync-preflight not clean), repair the gap; never bypass."
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
        "draft → create → publish-flow → automation issue-publish is a sequence, not a synonym set. Skipping a stage breaks host audit trail (no publish.yaml entry, no runs.jsonl event) and can deadlock the host-loop's WIP-cap check (G288).",
        "`issue publish-flow` without `--write` is the canonical preflight: it surfaces missing contract sections (Goal / Why / AC / OOS / Verification / Closes ref) BEFORE any GitHub mutation. Always run the dry-run first.",
        "WIP cap (G288) — only one `intent-target` issue/PR at a time per domain by default. If an issue/PR is already `intent-target` and the operator wants to publish another, pass `--allow-wip-cap-override` explicitly; the publish surface refuses silently otherwise.",
        "Prefer intent-cli-backed metadata mutation over hand-editing. Routine automation MUST NOT directly edit queue-state, runs logs, publish artifacts, workflow labels, or runtime metadata by hand when a supported intent-cli command exists.",
        "Child implementation loops MUST NOT inspect or mutate parent host queue-state, runs logs, packet directories, intent tree, review-runtime state, local rules, or local skills (G300 / G330 / G333). The publish surfaces are host-owned; child agents only ever see the issue once `intent-target` is applied by the host.",
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
        "Run `intent-cli issue publish-flow <execution-unit> --repo <r> --format json` (no `--write`) FIRST. If it reports `errors[]` naming missing contract sections, stop and repair the packet — `intent-cli guide workflow task packet-draft --format json` lists the required sections.",
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
