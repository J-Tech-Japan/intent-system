using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G464: Read-only <c>intent-cli stack</c> / <c>intent-cli guide stack</c>.
/// Emits the canonical <em>stack</em> process: forward planning that creates an
/// ordered packet backlog from the CURRENT intents and, by default, publishes
/// only the FIRST GitHub issue after durable state is committed and pushed.
///
/// stack matches タスクを積む — stacking up the available work. It is distinct
/// from <c>improve</c> (retrospective drift / loop realignment), <c>grill</c>
/// (persistent open-question interview), <c>clarification</c> (blocker
/// resolution), and runtime <c>queue</c> transitions. Host-state-free: works
/// from any cwd, never reads parent queue state, never launches an AI provider,
/// and never publishes more than one issue by default.
/// </summary>
internal static class GuideStackCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli stack [--domain <name>] [--target-repo <owner/repo>] [--format markdown|json]  (alias: intent-cli guide stack)";

    /// <summary>The shortest natural-language ask that starts the stack process.</summary>
    public const string ShortPrompt = "intent-cli で stack を実行してください（packet を積んで最初の issue を publish）。";

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

    internal static GuideStackResult BuildResult(string? domain, string? targetRepo)
    {
        var domainArg = string.IsNullOrWhiteSpace(domain) ? "<domain>" : domain!;
        var repoArg = string.IsNullOrWhiteSpace(targetRepo) ? "<owner/repo>" : targetRepo!;

        var prompt =
$@"Run the stack process for `{domainArg}` against `{repoArg}`: stack up the currently-available work as an ordered packet backlog and publish ONLY the first issue. You call intent-cli internally and never launch an AI provider.

1. Research current intents: read `intents/{domainArg}/` MVV, product goal, and intent-tree nodes, plus recent packet history, to find the slices that are ready to cut NOW. Do not start a broad intent-tree redesign unless a real blocker surfaces.
2. Respect open questions and WIP: skip topics with unresolved open questions (route those to `grill` / `clarification`) and slices already in progress; stack only what is actually ready.
3. Create an ordered packet backlog: draft the available packets (often around ten) in dependency order via `intent-cli packet draft`, honoring the G461 packet-time intent-maintenance metadata (intent placement, ADR / diagram / docs candidates, closeout writeback). Classify any host-only packet and do NOT publish it as a child implementation issue.
4. Commit and push durable state FIRST: commit and push the created packet files before any GitHub mutation, so the published issue references durable packets.
5. Publish AT MOST the first issue: publish only the first backlog packet as a GitHub issue (through the normal issue publish-flow / `automation issue-publish` boundary, which applies `intent-target`). Leave the rest as a deferred backlog unless the operator explicitly asks for more.
6. Report the output shape (below): created packets, the recommended first issue, the published issue, and the deferred items.";

        var inspectionSources = new[]
        {
            $"Current intents — `intents/{domainArg}/` MVV, product goal, and intent-tree nodes that have ready-to-cut slices.",
            $"Recent packet history — `.intent-cli/issues/<unit>/` packets, to avoid re-stacking work already drafted or in progress.",
            "Open questions — unresolved questions / clarifications: stack defers these to grill / clarification rather than guessing a packet.",
            "Work in progress — slices already claimed or with an open PR are skipped; stack only adds genuinely-available work.",
        };

        var backlogCreation = new[]
        {
            "Derive ready slices from the current intents — the work that can be cut NOW, not a speculative roadmap.",
            "Order by dependency: blocking packets first, so the first issue is the right one to publish.",
            "Draft each packet via `intent-cli packet draft`, honoring the G461 packet-time intent-maintenance metadata (intent placement, ADR / diagram / docs candidates, closeout writeback).",
            "Classify host-only packets (target paths entirely under host-owned `intents/**` / `.intent-cli/**`) and keep them out of the child implementation-issue publish set.",
        };

        var boundary = new[]
        {
            "Publish AT MOST the first GitHub issue by default — never publish the whole backlog at once unless the operator explicitly asks for more.",
            "Commit and push durable packet state BEFORE issue-publish, so the issue references durable packets rather than uncommitted files.",
            "Respect open questions and WIP: do not stack slices that depend on unresolved questions or that are already in progress.",
            "Host-only packets are not published as child implementation issues — surface them for the host instead.",
            "`intent-target` is applied only by the host publish boundary (`automation issue-publish`), never hand-applied; stack proposes the first issue and lets that boundary finalize it.",
        };

        var distinctions = new[]
        {
            "vs improve — improve (G456) is retrospective realignment that starts from a drift / short-term-loop crisis; stack is forward planning that starts from ready intents, with no crisis assumed.",
            "vs grill — grill (G463) is a persistent open-question interview; stack does not primarily generate questions, it stacks ready packets and skips topics that still have open questions.",
            "vs clarification — clarification resolves a blocking ambiguity; stack defers blockers to clarification rather than packaging around them.",
            "vs queue transitions — `queue` is runtime durable state advanced by `automation` / `worker`; stack is a design-time backlog-creation process and never hand-edits queue-state, labels, or publish metadata.",
        };

        return new GuideStackResult
        {
            Process = "task-stack",
            Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
            TargetRepo = string.IsNullOrWhiteSpace(targetRepo) ? null : targetRepo,
            ShortPrompt = ShortPrompt,
            Summary =
                "stack is the forward packet-backlog-creation process (matches タスクを積む). It reads the current intents, creates an "
                + "ordered backlog of the packets that are ready NOW (often around ten), commits and pushes that durable state, and by "
                + "default publishes ONLY the first GitHub issue — leaving the rest as a deferred backlog. It is NOT improve "
                + "(retrospective realignment), NOT grill (persistent open-question interview), NOT clarification (blocker resolution), "
                + "and NOT a runtime queue transition.",
            NotThis = new[]
            {
                "stack does NOT publish multiple issues by default — it publishes at most the first and defers the rest.",
                "stack does NOT replace improve (retrospective realignment), grill / interview (open-question extraction), or clarification (blocker resolution).",
                "stack does NOT do a broad intent-tree redesign — it stacks ready slices; redesign is only triggered if a real blocker is found.",
                "intent-cli does NOT launch Claude/Codex/Copilot or any AI provider; the agent owns the semantic packet shaping.",
            },
            DoNotSubstitute = new[]
            {
                "When the operator asks to create the available packets and publish the first issue, run `intent-cli stack` (or `intent-cli guide stack`) — do NOT improvise an ad-hoc multi-issue publish.",
                "Do NOT route stack to `improve` — improve assumes a drift / loop crisis; stack assumes ready intents.",
                "Do NOT route stack to `grill` — grill generates open questions; stack stacks ready packets and defers open questions.",
                "If a first-class stack surface is not found in the installed CLI (e.g. `intent-cli stack --help` fails), report `stack guidance unavailable` and request a CLI update — do NOT silently substitute another workflow.",
            },
            InspectionSources = inspectionSources,
            BacklogCreation = backlogCreation,
            Boundary = boundary,
            Distinctions = distinctions,
            OutputShape = new[]
            {
                new GuideStackOutputField { Field = "created_packets", Meaning = "The ordered list of packet candidates drafted this run (execution-unit id + title), in dependency order." },
                new GuideStackOutputField { Field = "recommended_first_issue", Meaning = "The first backlog packet — the one issue recommended for publish this run." },
                new GuideStackOutputField { Field = "published_issue", Meaning = "The GitHub issue actually published (number + url), or none if the operator deferred publish." },
                new GuideStackOutputField { Field = "deferred_items", Meaning = "The remaining created packets left as a backlog (including any host-only packets surfaced for the host), not published this run." },
            },
            MutationBoundary = new[]
            {
                "Create packets through `intent-cli packet draft`; commit and push the durable packet state before any GitHub mutation.",
                "Publish at most one issue through the normal issue publish-flow / `automation issue-publish` boundary; do not hand-apply `intent-target`.",
                "Never hand-edit workflow labels, queue-state, or publish metadata from stack — those transitions stay in the operational intent-cli surfaces.",
            },
            Prompt = prompt,
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideStackResult result)
    {
        writer.WriteLine("# Guide stack — packet backlog creation + first issue publish");
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

        writer.WriteLine("## Inspect before stacking");
        foreach (var item in result.InspectionSources)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Ordered packet backlog creation");
        foreach (var item in result.BacklogCreation)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Boundary (at most one first issue)");
        foreach (var item in result.Boundary)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## How stack differs");
        foreach (var item in result.Distinctions)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Output shape");
        foreach (var field in result.OutputShape)
        {
            writer.WriteLine($"- `{field.Field}`: {field.Meaning}");
        }
        writer.WriteLine();

        writer.WriteLine("## Mutation boundary");
        foreach (var item in result.MutationBoundary)
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
        writer.WriteLine("stack (alias: guide stack)");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only: stack process. Creates an ordered packet backlog from the current intents and, by default, publishes at most the first GitHub issue after durable state is committed/pushed. Distinct from improve / grill / clarification / queue; never auto-publishes the whole backlog; never launches an AI provider.");
        writer.WriteLine("Run it by asking: " + ShortPrompt);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed record GuideStackResult
{
    [JsonPropertyName("process")]
    public required string Process { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("target_repo")]
    public string? TargetRepo { get; init; }

    [JsonPropertyName("short_prompt")]
    public required string ShortPrompt { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("not_this")]
    public required IReadOnlyList<string> NotThis { get; init; }

    [JsonPropertyName("do_not_substitute")]
    public required IReadOnlyList<string> DoNotSubstitute { get; init; }

    [JsonPropertyName("inspection_sources")]
    public required IReadOnlyList<string> InspectionSources { get; init; }

    [JsonPropertyName("backlog_creation")]
    public required IReadOnlyList<string> BacklogCreation { get; init; }

    [JsonPropertyName("boundary")]
    public required IReadOnlyList<string> Boundary { get; init; }

    [JsonPropertyName("distinctions")]
    public required IReadOnlyList<string> Distinctions { get; init; }

    [JsonPropertyName("output_shape")]
    public required IReadOnlyList<GuideStackOutputField> OutputShape { get; init; }

    [JsonPropertyName("mutation_boundary")]
    public required IReadOnlyList<string> MutationBoundary { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }
}

internal sealed record GuideStackOutputField
{
    [JsonPropertyName("field")]
    public required string Field { get; init; }

    [JsonPropertyName("meaning")]
    public required string Meaning { get; init; }
}
