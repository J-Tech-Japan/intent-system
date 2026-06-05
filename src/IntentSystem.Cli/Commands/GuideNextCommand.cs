using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G465: Read-only <c>intent-cli next</c> / <c>intent-cli guide next</c>.
/// The design-side <em>action advisor</em>: answers "what should I do next?" by
/// laying out the catalog of design-side processes (grill, stack, improve,
/// inspect, issue-publish, review, recovery, idle), the evidence to check
/// before choosing, and the recommendation output shape the agent fills in.
///
/// The intended user experience is a single natural-language ask:
/// <c>intent-cli に聞いて、次に何をしたらいいか教えてください。</c>
///
/// next is READ-ONLY by default: it recommends a process, it does not secretly
/// mutate packets, issues, labels, or queue state, and it never launches an AI
/// provider. Host-state-free: works from any cwd.
/// </summary>
internal static class GuideNextCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli next [--domain <name>] [--target-repo <owner/repo>] [--format markdown|json]  (alias: intent-cli guide next)";

    /// <summary>The shortest natural-language ask that triggers the advisor.</summary>
    public const string ShortPrompt = "intent-cli に聞いて、次に何をしたらいいか教えてください。";

    // Design-side actions in the decision set.
    public const string ActionGrill = "grill";
    public const string ActionStack = "stack";
    public const string ActionImprove = "improve";
    public const string ActionInspect = "inspect";
    public const string ActionIssuePublish = "issue-publish";
    public const string ActionReview = "review";
    public const string ActionRecovery = "recovery";
    public const string ActionIdle = "idle";

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

        var result = BuildResult(domain, targetRepo);
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

    internal static GuideNextResult BuildResult(string? domain, string? targetRepo)
    {
        var domainArg = string.IsNullOrWhiteSpace(domain) ? "<domain>" : domain!;
        var repoArg = string.IsNullOrWhiteSpace(targetRepo) ? "<owner/repo>" : targetRepo!;

        var prompt =
$@"Advise the design thread on what to do next for `{domainArg}` ({repoArg}). This is READ-ONLY: recommend ONE design-side process, do not mutate packets / issues / labels / queue state, and never launch an AI provider.

1. Check the evidence (below) — current intents, open questions, packet backlog, open PRs / review state, and CLI / queue health — before recommending.
2. Match the situation to exactly one action in the decision set (grill / stack / improve / inspect / issue-publish / review / recovery / idle).
3. Return the recommendation output shape: the recommended action, the reason tied to the evidence you actually checked, the evidence checked, a paste-ready suggested prompt for that action, and the safety boundary.
4. Stop there — the user decides whether to run the suggested prompt. next never auto-executes the chosen action.";

        var evidenceToCheck = new[]
        {
            $"Current intents — `intents/{domainArg}/` MVV / product goal / intent-tree: is the intent clear, or are there open questions to extract?",
            $"Open questions / clarifications — `intents/{domainArg}/clarifications/`: unresolved blocking questions push toward grill or clarification.",
            "Packet backlog — recent `.intent-cli/issues/<unit>/` packets: is there ready work to stack, or is the backlog already drafted?",
            $"Open PRs and review state — `intent-cli guide review` inputs / GitHub PR labels for {repoArg}: a PR awaiting review pushes toward review; a request-update pushes toward recovery/comment-fix.",
            "CLI / queue health — `intent-cli automation doctor`: a stale CLI or dirty queue pushes toward recovery before anything else.",
            "Drift / short-term-loop signals — repeated corrective packets on the same surface push toward improve.",
        };

        var decisionSet = new[]
        {
            new GuideNextAction
            {
                Action = ActionGrill,
                WhenToChoose = "The intent is still fuzzy — there are open product / technical / operational / verification questions to extract before any packet is cut. Persistent interview mode (G463).",
                SuggestedPrompt = $"intent-cli で <topic> を grill してください。（`intent-cli grill --domain {domainArg} --format markdown`）",
            },
            new GuideNextAction
            {
                Action = ActionStack,
                WhenToChoose = "The intent is clear and there is ready work to package — create an ordered packet backlog and publish the first issue. Forward planning (G464).",
                SuggestedPrompt = $"intent-cli で stack を実行してください。（`intent-cli stack --domain {domainArg} --target-repo {repoArg} --format markdown`）",
            },
            new GuideNextAction
            {
                Action = ActionImprove,
                WhenToChoose = "Recent work has drifted from MVV / ADR / intent tree, or a short-term-loop / repeated-patch pattern is showing. Retrospective realignment (G456).",
                SuggestedPrompt = $"intent-cli で improve プロセスを実行してください。（`intent-cli improve --domain {domainArg} --format markdown`）",
            },
            new GuideNextAction
            {
                Action = ActionInspect,
                WhenToChoose = "You need to observe what the product ACTUALLY does before deciding — evidence-backed observation of real app / CLI / UI / log / test behavior, separating observed evidence from inference and turning gaps into packet candidates (G466). This is NOT status / next-slice checking; for a quick read-only state summary use `intent-cli status brief` / `intent intent status` directly.",
                SuggestedPrompt = $"intent-cli で <target> を inspect してください。（`intent-cli inspect --domain {domainArg} --target-repo {repoArg} --format markdown`, alias `intent-cli guide inspect`）",
            },
            new GuideNextAction
            {
                Action = ActionIssuePublish,
                WhenToChoose = "A reviewed, contract-complete packet is ready to become a GitHub issue — publish through the normal boundary (host applies intent-target).",
                SuggestedPrompt = $"`intent-cli issue publish-flow <id> --repo {repoArg} --write --format json` then `intent-cli automation issue-publish --write`",
            },
            new GuideNextAction
            {
                Action = ActionReview,
                WhenToChoose = "An implementation PR is open and awaiting design/host review against its packet and intent.",
                SuggestedPrompt = $"`intent-cli guide review --pr <n> --repo {repoArg} --domain {domainArg} --format json`",
            },
            new GuideNextAction
            {
                Action = ActionRecovery,
                WhenToChoose = "The CLI is stale, the queue/labels are inconsistent, or a publish/closeout is stuck — repair operational state before design work.",
                SuggestedPrompt = $"`intent-cli automation doctor --format json` then `intent-cli automation reconcile` / `intent-cli automation publish-recovery` as indicated.",
            },
            new GuideNextAction
            {
                Action = ActionIdle,
                WhenToChoose = "Nothing is actionable on the design side right now — the backlog is drained, PRs are with the host, and no drift or open question is pending. Stop and wait.",
                SuggestedPrompt = "（no action — report idle and wait for the next design input or host hand-off）",
            },
        };

        return new GuideNextResult
        {
            Process = "design-action-next-advisor",
            Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
            TargetRepo = string.IsNullOrWhiteSpace(targetRepo) ? null : targetRepo,
            ShortPrompt = ShortPrompt,
            ReadOnly = true,
            Summary =
                "next is the design-side action advisor: ask it what to do next and it lays out the catalog of design-side "
                + "processes (grill, stack, improve, inspect, issue-publish, review, recovery, idle), the evidence to check first, "
                + "and the recommendation output shape. It recommends ONE process tied to the evidence; it is read-only by default "
                + "and never auto-executes the chosen action — the user decides whether to run the suggested prompt.",
            NotThis = new[]
            {
                "next does NOT auto-execute the selected action — it recommends; the user runs the suggested prompt.",
                "next is READ-ONLY by default — it does not mutate packets, issues, labels, or queue state.",
                "next does NOT replace the host / review / worker loops — it advises the design thread, it does not drive operational automation.",
                "intent-cli does NOT launch Claude/Codex/Copilot or any AI provider; the AI agent owns the semantic decision and conversation.",
            },
            DoNotSubstitute = new[]
            {
                "When the user asks what to do next, run `intent-cli next` (or `intent-cli guide next`) and recommend ONE design-side process — do NOT silently start one.",
                "Do NOT turn next into a generic AI planner unrelated to intent-cli state — every recommendation must tie to the evidence checked.",
                "If a first-class next surface is not found in the installed CLI (e.g. `intent-cli next --help` fails), report `next advisor unavailable` and request a CLI update — do NOT silently substitute another workflow.",
            },
            EvidenceToCheck = evidenceToCheck,
            DecisionSet = decisionSet,
            RecommendationOutputShape = new[]
            {
                new GuideNextOutputField { Field = "recommended_action", Meaning = "Exactly one action id from the decision set (grill / stack / improve / inspect / issue-publish / review / recovery / idle)." },
                new GuideNextOutputField { Field = "reason", Meaning = "Why this action, tied to the specific evidence checked (cite the intent / packet / PR / health signal that drove it)." },
                new GuideNextOutputField { Field = "evidence_checked", Meaning = "The evidence actually inspected this run, so the recommendation is auditable." },
                new GuideNextOutputField { Field = "suggested_prompt", Meaning = "The paste-ready prompt / command for the recommended action that the user can run as-is." },
                new GuideNextOutputField { Field = "safety_boundary", Meaning = "The read-only / no-auto-execute boundary: next recommended, the user decides whether to run it." },
            },
            SafetyBoundary = new[]
            {
                "Read-only by default: next inspects evidence and recommends; it does not mutate packets, issues, labels, or queue state.",
                "No auto-execute: the recommended action runs only when the user chooses to run the suggested prompt.",
                "Never hand-edit workflow labels, queue-state, or publish metadata from next — those stay in the operational intent-cli surfaces.",
            },
            Prompt = prompt,
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideNextResult result)
    {
        writer.WriteLine("# Guide next — design-side action advisor");
        writer.WriteLine();
        writer.WriteLine($"_Ask it:_ **{result.ShortPrompt}**");
        writer.WriteLine();
        if (!string.IsNullOrWhiteSpace(result.Domain))
        {
            writer.WriteLine($"- domain: {result.Domain}");
        }
        if (!string.IsNullOrWhiteSpace(result.TargetRepo))
        {
            writer.WriteLine($"- target repo: {result.TargetRepo}");
        }
        writer.WriteLine($"- read-only: {(result.ReadOnly ? "yes" : "no")}");
        writer.WriteLine();
        writer.WriteLine(result.Summary);
        writer.WriteLine();

        writer.WriteLine("## Procedure");
        writer.WriteLine();
        writer.WriteLine("```text");
        writer.WriteLine(result.Prompt);
        writer.WriteLine("```");
        writer.WriteLine();

        writer.WriteLine("## What this is NOT");
        foreach (var item in result.NotThis)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Do not substitute another workflow");
        foreach (var item in result.DoNotSubstitute)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Evidence to check before recommending");
        foreach (var item in result.EvidenceToCheck)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Decision set — when to choose each design-side process");
        foreach (var action in result.DecisionSet)
        {
            writer.WriteLine($"- **{action.Action}** — {action.WhenToChoose}");
            writer.WriteLine($"  - suggested prompt: {action.SuggestedPrompt}");
        }
        writer.WriteLine();

        writer.WriteLine("## Recommendation output shape");
        foreach (var field in result.RecommendationOutputShape)
        {
            writer.WriteLine($"- `{field.Field}`: {field.Meaning}");
        }
        writer.WriteLine();

        writer.WriteLine("## Safety boundary");
        foreach (var item in result.SafetyBoundary)
        {
            writer.WriteLine($"- {item}");
        }
    }

    private static bool TryParseArguments(string[] args, out string? domain, out string? targetRepo, out string format, out string error)
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
                    domain = args[index + 1].Trim();
                    index++;
                    break;

                case "--target-repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--target-repo requires a value.";
                        return false;
                    }
                    targetRepo = args[index + 1].Trim();
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
        writer.WriteLine("next (alias: guide next)");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only: design-side action advisor. Lays out the design-side process catalog (grill / stack / improve / inspect / issue-publish / review / recovery / idle), the evidence to check, and the recommendation output shape so an AI agent can answer what to do next. Read-only by default; never auto-executes; never launches an AI provider.");
        writer.WriteLine("Ask it: " + ShortPrompt);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed record GuideNextResult
{
    [JsonPropertyName("process")]
    public required string Process { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("target_repo")]
    public string? TargetRepo { get; init; }

    [JsonPropertyName("short_prompt")]
    public required string ShortPrompt { get; init; }

    [JsonPropertyName("read_only")]
    public required bool ReadOnly { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("not_this")]
    public required IReadOnlyList<string> NotThis { get; init; }

    [JsonPropertyName("do_not_substitute")]
    public required IReadOnlyList<string> DoNotSubstitute { get; init; }

    [JsonPropertyName("evidence_to_check")]
    public required IReadOnlyList<string> EvidenceToCheck { get; init; }

    [JsonPropertyName("decision_set")]
    public required IReadOnlyList<GuideNextAction> DecisionSet { get; init; }

    [JsonPropertyName("recommendation_output_shape")]
    public required IReadOnlyList<GuideNextOutputField> RecommendationOutputShape { get; init; }

    [JsonPropertyName("safety_boundary")]
    public required IReadOnlyList<string> SafetyBoundary { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }
}

internal sealed record GuideNextAction
{
    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("when_to_choose")]
    public required string WhenToChoose { get; init; }

    [JsonPropertyName("suggested_prompt")]
    public required string SuggestedPrompt { get; init; }
}

internal sealed record GuideNextOutputField
{
    [JsonPropertyName("field")]
    public required string Field { get; init; }

    [JsonPropertyName("meaning")]
    public required string Meaning { get; init; }
}
