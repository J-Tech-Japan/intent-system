using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G466: Read-only <c>intent-cli inspect</c> / <c>intent-cli guide inspect</c>.
/// The evidence-backed <em>observation</em> process: observe the REAL
/// app / CLI / UI / log / test behavior with whatever tools are available,
/// strictly separate observed evidence from inference, and convert the gaps
/// between observed behavior and expected intent into packet candidates.
///
/// inspect is the named answer to "look at what the product actually does
/// before cutting tasks", so agents stop inventing packets from stale
/// assumptions. It is distinct from <c>grill</c> (open-question interview),
/// <c>stack</c> (packet backlog creation), and <c>improve</c> (retrospective
/// realignment).
///
/// The FIRST inspect pass is READ-ONLY by default: it observes and reports, it
/// does not run destructive interactions, publish issues, or mutate state, and
/// it never launches an AI provider. It guides how to use existing
/// browser / computer-use / log / test tooling — it does not replace it.
/// </summary>
internal static class GuideInspectCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli inspect [--domain <name>] [--target-repo <owner/repo>] [--format markdown|json]  (alias: intent-cli guide inspect)";

    /// <summary>The shortest natural-language ask that triggers the process.</summary>
    public const string ShortPrompt = "intent-cli で <target> を inspect して、観測した挙動から packet candidate を出してください。";

    // Where an inspect pass routes next.
    public const string RouteStack = "stack";
    public const string RouteGrill = "grill";
    public const string RouteImprove = "improve";
    public const string RouteRecovery = "recovery";
    public const string RouteNoAction = "no-action";

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

    internal static GuideInspectResult BuildResult(string? domain, string? targetRepo)
    {
        var domainArg = string.IsNullOrWhiteSpace(domain) ? "<domain>" : domain!;
        var repoArg = string.IsNullOrWhiteSpace(targetRepo) ? "<owner/repo>" : targetRepo!;

        var prompt =
$@"Run an evidence-backed inspect pass for `{domainArg}` ({repoArg}). The FIRST pass is READ-ONLY: observe the real behavior, do not run destructive interactions, do not publish issues, and never launch an AI provider.

1. Observe the real product — exercise the app / CLI / UI / logs / tests with the tools you actually have (browser / computer-use / shell / test runner). Capture concrete, reproducible evidence (commands run, outputs, screenshots, log lines, test names).
2. Separate observation from inference — write down ONLY what you observed as evidence; mark anything you are reasoning about as inference, never blur the two.
3. Compare observed behavior against expected intent — read `intents/{domainArg}/` and recent packets for what the product is SUPPOSED to do, and identify the gaps.
4. Rank gaps by risk / severity — note user impact and likelihood, so the most important gaps surface first.
5. Convert gaps into packet candidates — concrete, cuttable slices, each tied to the evidence and the intent it restores. Do not publish them; propose them.
6. Recommend the next action (below) — stack / grill / improve / recovery / no-action — and stop. The user decides.";

        var observationVsInference = new[]
        {
            "Evidence is what you directly observed: a command you ran and its output, a log line, a screenshot, a failing test name — reproducible and attributable.",
            "Inference is what you concluded from evidence: a suspected cause, a generalization, an expectation. Always label it as inference, never as observed fact.",
            "Never promote inference to evidence: if you did not observe it this pass, it is not evidence — go observe it or mark the gap as 'unobserved'.",
            "Every claimed gap must cite the specific evidence that shows it; a gap with no observed evidence is an open question for grill, not an inspect finding.",
        };

        var inspectionTargets = new[]
        {
            "App / UI behavior — exercise the running product with browser / computer-use tooling; capture what the user actually sees and does.",
            "CLI behavior — run the real commands and capture exit codes and output; compare against documented behavior.",
            "Logs — read runtime / build / CI logs for errors, warnings, and unexpected paths.",
            "Tests — run the focused tests for the area and capture pass/fail + the concrete failure, not a summary.",
            $"Intent baseline — `intents/{domainArg}/` and recent `.intent-cli/issues/<unit>/` packets for the expected behavior to compare against.",
        };

        var reportShape = new[]
        {
            new GuideInspectReportSection { Section = "observed_behavior", Meaning = "What the product actually did this pass — concrete and reproducible, no inference." },
            new GuideInspectReportSection { Section = "expected_intent", Meaning = "What the product is supposed to do, per the intents / packets / docs, with the source cited." },
            new GuideInspectReportSection { Section = "evidence", Meaning = "The raw evidence backing the observation: commands run, outputs, log lines, screenshots, test names." },
            new GuideInspectReportSection { Section = "gaps", Meaning = "The differences between observed behavior and expected intent, each tied to the evidence above." },
            new GuideInspectReportSection { Section = "risk_severity", Meaning = "User impact and likelihood for each gap, so the most important surface first." },
            new GuideInspectReportSection { Section = "recommended_next_action", Meaning = "One of stack / grill / improve / recovery / no-action, with the reason." },
            new GuideInspectReportSection { Section = "packet_candidates", Meaning = "Concrete, cuttable slices derived from the gaps — proposed, not published." },
        };

        var nextActionRouting = new[]
        {
            new GuideInspectRoute { Route = RouteStack, WhenToChoose = "The gaps are well understood and ready to package as work — hand the packet candidates to stack to create the backlog and publish the first issue." },
            new GuideInspectRoute { Route = RouteGrill, WhenToChoose = "Inspection surfaced open questions the evidence cannot answer — the intent itself is unclear; grill to extract it before cutting packets." },
            new GuideInspectRoute { Route = RouteImprove, WhenToChoose = "The gaps reveal systemic drift or a short-term-loop pattern, not a single fix — improve for retrospective realignment." },
            new GuideInspectRoute { Route = RouteRecovery, WhenToChoose = "Inspection found broken operational state (stale CLI, dirty queue, stuck publish/closeout) — recover before more design work." },
            new GuideInspectRoute { Route = RouteNoAction, WhenToChoose = "Observed behavior matches expected intent — no gap worth a packet; record the inspection and stop." },
        };

        return new GuideInspectResult
        {
            Process = "evidence-backed-inspect",
            Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
            TargetRepo = string.IsNullOrWhiteSpace(targetRepo) ? null : targetRepo,
            ShortPrompt = ShortPrompt,
            ReadOnly = true,
            Summary =
                "inspect is the evidence-backed observation process: observe the REAL app / CLI / UI / log / test behavior with the "
                + "tools you have, strictly separate observed evidence from inference, compare against expected intent, and turn the "
                + "gaps into packet candidates. The first pass is read-only by default; it never runs destructive interactions, never "
                + "auto-publishes, and guides how to use existing browser / computer-use / log / test tooling rather than replacing it. "
                + "It is distinct from grill (open-question interview), stack (packet backlog creation), and improve (retrospective "
                + "realignment).",
            NotThis = new[]
            {
                "inspect does NOT replace browser / computer-use / log / test tooling — it guides how to use that tooling and turn its output into evidence.",
                "inspect does NOT run destructive interactions by default — the first pass is read-only observation.",
                "inspect does NOT auto-publish issues — packet candidates are proposed; publishing is a separate, user-approved step (stack / issue-publish).",
                "inspect does NOT do a broad retrospective intent rewrite — that is improve; inspect reports observed gaps and proposes cuttable slices.",
                "intent-cli does NOT launch Claude/Codex/Copilot or any AI provider; the agent owns the observation and the semantic gap analysis.",
            },
            DoNotSubstitute = new[]
            {
                "When the work needs observing the real product before cutting tasks, run `intent-cli inspect` (or `intent-cli guide inspect`) — do NOT invent packets from stale assumptions.",
                "Do NOT route inspect to grill for things you can directly observe — observe them and record evidence; only genuinely unobservable questions go to grill.",
                "Do NOT route inspect to improve — improve is retrospective realignment, inspect is forward observation of real current behavior.",
                "If a first-class inspect surface is not found in the installed CLI (e.g. `intent-cli inspect --help` fails), report `inspect guidance unavailable` and request a CLI update — do NOT silently substitute another workflow.",
            },
            ObservationVsInference = observationVsInference,
            InspectionTargets = inspectionTargets,
            ReportShape = reportShape,
            NextActionRouting = nextActionRouting,
            SafetyBoundary = new[]
            {
                "First inspect pass is read-only by default: observe and report, do not mutate product, packets, issues, labels, or queue state.",
                "No destructive interactions by default: any state-changing interaction needs explicit user approval and is out of the first pass.",
                "No auto-publish: packet candidates are proposed; the user decides whether to stack / publish them.",
            },
            Prompt = prompt,
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideInspectResult result)
    {
        writer.WriteLine("# Guide inspect — evidence-backed observation");
        writer.WriteLine();
        writer.WriteLine($"_Run it by asking:_ **{result.ShortPrompt}**");
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

        writer.WriteLine("## Observation vs inference");
        foreach (var item in result.ObservationVsInference)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Inspection targets");
        foreach (var item in result.InspectionTargets)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Inspect Report sections");
        foreach (var section in result.ReportShape)
        {
            writer.WriteLine($"- `{section.Section}`: {section.Meaning}");
        }
        writer.WriteLine();

        writer.WriteLine("## Where an inspect pass routes next");
        foreach (var route in result.NextActionRouting)
        {
            writer.WriteLine($"- **{route.Route}** — {route.WhenToChoose}");
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
        writer.WriteLine("inspect (alias: guide inspect)");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only: evidence-backed observation process. Observe the real app / CLI / UI / log / test behavior, separate observed evidence from inference, compare against expected intent, and turn gaps into packet candidates. First pass read-only; never runs destructive interactions; never auto-publishes; never launches an AI provider.");
        writer.WriteLine("Run it by asking: " + ShortPrompt);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed record GuideInspectResult
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

    [JsonPropertyName("observation_vs_inference")]
    public required IReadOnlyList<string> ObservationVsInference { get; init; }

    [JsonPropertyName("inspection_targets")]
    public required IReadOnlyList<string> InspectionTargets { get; init; }

    [JsonPropertyName("report_shape")]
    public required IReadOnlyList<GuideInspectReportSection> ReportShape { get; init; }

    [JsonPropertyName("next_action_routing")]
    public required IReadOnlyList<GuideInspectRoute> NextActionRouting { get; init; }

    [JsonPropertyName("safety_boundary")]
    public required IReadOnlyList<string> SafetyBoundary { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }
}

internal sealed record GuideInspectReportSection
{
    [JsonPropertyName("section")]
    public required string Section { get; init; }

    [JsonPropertyName("meaning")]
    public required string Meaning { get; init; }
}

internal sealed record GuideInspectRoute
{
    [JsonPropertyName("route")]
    public required string Route { get; init; }

    [JsonPropertyName("when_to_choose")]
    public required string WhenToChoose { get; init; }
}
