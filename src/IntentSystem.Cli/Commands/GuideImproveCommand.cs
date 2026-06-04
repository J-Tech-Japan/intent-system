using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G456: Read-only <c>intent-cli guide improve --domain &lt;domain&gt;</c>.
/// Emits the canonical design-thread <em>improve / realignment</em> process so a
/// design-thread AI agent can periodically step back and check whether recent
/// work still aligns with the original mission, vision, values, ADR/design
/// notes, and intent tree — reusing the accumulated intent artifacts instead of
/// letting fast issue/PR automation drift into short-term local fixes.
///
/// The intended user experience is a single natural-language ask:
/// <c>intent-cli で improve プロセスを実行してください。</c>
/// The agent then runs this guide to fetch the current improve guidance and
/// produces a structured design-thread report.
///
/// This is a DESIGN-THREAD periodic reflection process — NOT a scheduler, NOT a
/// provider launcher, and NOT a routine host-loop/worker-loop recovery
/// diagnostic. intent-cli owns deterministic guidance, discovery, artifact
/// locations, the report shape, and mutation boundaries; the AI agent owns
/// semantic analysis and conversation. Never mutates state. Never launches an
/// AI provider.
/// </summary>
internal static class GuideImproveCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide improve [--domain <name>] [--format markdown|json]";

    /// <summary>The shortest natural-language ask that triggers this process.</summary>
    public const string ShortPrompt = "intent-cli で improve プロセスを実行してください。";

    // Outcome classifications the design-thread report must use.
    public const string OutcomeAligned = "aligned";
    public const string OutcomeIntentStrengthening = "intent-strengthening-recommended";
    public const string OutcomeClarification = "clarification-recommended";
    public const string OutcomeCorrectivePacket = "corrective-packet-recommended";
    public const string OutcomeAdrUpdate = "adr-update-recommended";
    public const string OutcomeShortTermLoop = "short-term-loop-detected";
    public const string OutcomeOperatorPolicy = "operator-policy-required";

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

        if (!TryParseArguments(args, out var domain, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var result = BuildResult(domain);
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

    internal static GuideImproveResult BuildResult(string? domain)
    {
        var domainToken = string.IsNullOrWhiteSpace(domain) ? "<domain>" : domain!;
        return new GuideImproveResult
        {
            Process = "design-thread-improve",
            Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
            ShortPrompt = ShortPrompt,
            Summary =
                "Design-thread improve / realignment: periodically step back and check whether recent work still aligns "
                + "with the original mission, vision, values, ADR/design notes, and intent tree. Reuse the accumulated "
                + "intent artifacts (MVV, ADRs, packet history, clarification history) rather than letting fast issue/PR "
                + "automation drift into short-term local fixes.",
            NotThis = new[]
            {
                "This is a DESIGN-THREAD periodic reflection process — run it on demand when the operator asks, not on a 5m/20m schedule.",
                "intent-cli does NOT launch Claude/Codex/Copilot or any AI provider; the AI agent owns the semantic analysis and conversation.",
                "This is NOT a scheduler and NOT a routine host-loop / worker-loop recovery diagnostic — operational metadata/label/queue recovery stays in the existing operational surfaces (`automation reconcile`, `automation publish-recovery`, `automation host-review-diagnostics`, `review closeout-plan`).",
                "intent-cli does not make semantic product-direction decisions; it owns deterministic guidance, discovery, artifact locations, report shape, and mutation boundaries."
            },
            DoNotSubstitute = new[]
            {
                "When the operator asks to run the improve process, run `intent-cli improve` (or `intent-cli guide improve`) — do NOT substitute an operational recovery workflow.",
                "Do NOT route improve to `guide workflow task bug-to-intent-repair` — that is a bug → triage → intent/implementation-repair chain, not a design-thread realignment review.",
                "Do NOT route improve to host-loop recovery (`automation host-loop-wake` / `automation host-review-diagnostics` / `automation reconcile`) — those advance or repair the operational queue, not the intent alignment.",
                "Do NOT route improve to `automation state-doctor` or any dirty-state / workspace repair — improve never inspects or repairs queue-state, labels, or publish metadata.",
                "If a first-class improve surface is not found in the installed CLI (e.g. `intent-cli improve --help` fails), report `improve guidance unavailable` and request a CLI update — do NOT silently choose another workflow."
            },
            InspectionChecklist = new[]
            {
                $"Mission / Vision / Values and product goal — read `intents/{domainToken}/` MVV and product-goal artifacts; restate them in your own words and check recent work against them.",
                $"ADR / design notes — review architecture decision records and design notes for decisions recent work may have silently violated or superseded.",
                $"Intent tree structure — inspect `intents/{domainToken}/intent-tree/` for coherence: are new slices hanging off the right parent intents, or accreting as ad-hoc leaves?",
                $"Recent packet history — review the last several execution-unit packets (`.intent-cli/issues/<unit>/`) for theme: are they advancing intents or repeatedly patching the same surface?",
                $"Clarification history — review `intents/{domainToken}/clarifications/` for unresolved or repeatedly-reopened questions that signal an unsettled intent.",
                "Short-term loop signals — look for repeated corrective packets on the same area, fixes that re-fix prior fixes, or automation that advanced while the underlying intent stayed ambiguous.",
                "Intent strengthening opportunities — where an intent is implied by the work but never written down, note it as a candidate to make explicit."
            },
            ReportTemplate = new[]
            {
                "## Design-thread improve report",
                "- domain: <domain>",
                "- reviewed artifacts: <MVV / ADRs / intent-tree paths / packet units / clarification ids actually read>",
                "## Alignment findings",
                "- <finding> — <which MVV/ADR/intent it supports or contradicts, with the artifact path/clause cited>",
                "## Short-term-loop signals",
                "- <repeated-pattern observed, with the packet units / PRs that evidence it, or 'none observed'>",
                "## Recommended outcome",
                "- outcome: <one of the outcome classifications>",
                "- rationale: <why, tied to the cited artifacts>",
                "## Proposed next steps (propose first — apply only after operator agreement)",
                "- <intent-strengthening edit / clarification to open / corrective packet to draft / ADR update>, each via a supported intent-cli or repo path"
            },
            OutcomeClassifications = new[]
            {
                new GuideImproveOutcome { Id = OutcomeAligned, Meaning = "Recent work aligns with MVV / ADRs / intent tree; no realignment action needed beyond recording the check." },
                new GuideImproveOutcome { Id = OutcomeIntentStrengthening, Meaning = "An intent implied by recent work is not yet written down; propose making it explicit in the intent tree / specs." },
                new GuideImproveOutcome { Id = OutcomeClarification, Meaning = "An ambiguous source-of-truth surfaced; propose opening a Hard Clarification via `intent-cli clarification` rather than guessing." },
                new GuideImproveOutcome { Id = OutcomeCorrectivePacket, Meaning = "Recent work drifted from intent in a way that needs a corrective execution unit; propose drafting a packet through the normal next-slice/packet path." },
                new GuideImproveOutcome { Id = OutcomeAdrUpdate, Meaning = "An architecture decision was superseded or violated; propose updating the ADR / design notes." },
                new GuideImproveOutcome { Id = OutcomeShortTermLoop, Meaning = "A short-term local-fix loop is detected (repeated patches on the same surface); surface it so the operator can decide whether to invest in the underlying intent." },
                new GuideImproveOutcome { Id = OutcomeOperatorPolicy, Meaning = "A product-direction or policy decision is required that only the operator can make; surface it as an operator decision, do not decide it in intent-cli or the agent." }
            },
            MutationBoundary = new[]
            {
                "Propose mutations first; apply them only after explicit operator agreement, and only through supported intent-cli / repo paths.",
                "Intent strengthening / ADR updates land as ordinary repo edits the operator accepts; clarifications go through `intent-cli clarification`; corrective work goes through the normal packet / next-slice path.",
                "Never hand-edit workflow labels, queue-state, or publish metadata from this process — that repair stays routed through the existing operational intent-cli surfaces."
            }
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideImproveResult result)
    {
        writer.WriteLine("# Guide improve — design-thread realignment process");
        writer.WriteLine();
        writer.WriteLine($"_Run it by asking:_ **{result.ShortPrompt}**");
        writer.WriteLine();
        writer.WriteLine(result.Summary);
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

        writer.WriteLine("## Inspection checklist");
        foreach (var item in result.InspectionChecklist)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Structured report template");
        writer.WriteLine();
        foreach (var line in result.ReportTemplate)
        {
            writer.WriteLine(line);
        }
        writer.WriteLine();

        writer.WriteLine("## Outcome classifications");
        foreach (var outcome in result.OutcomeClassifications)
        {
            writer.WriteLine($"- `{outcome.Id}` — {outcome.Meaning}");
        }
        writer.WriteLine();

        writer.WriteLine("## Mutation boundary");
        foreach (var item in result.MutationBoundary)
        {
            writer.WriteLine($"- {item}");
        }
    }

    private static bool TryParseArguments(string[] args, out string? domain, out string format, out string error)
    {
        domain = null;
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
        writer.WriteLine("guide improve");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only design-thread improve / realignment process: review MVV, ADR/design notes, intent tree, packet history, and clarification history for drift and short-term loops. Run it by asking: " + ShortPrompt);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record GuideImproveResult
{
    [JsonPropertyName("process")]
    public required string Process { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("short_prompt")]
    public required string ShortPrompt { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("not_this")]
    public required IReadOnlyList<string> NotThis { get; init; }

    [JsonPropertyName("do_not_substitute")]
    public required IReadOnlyList<string> DoNotSubstitute { get; init; }

    [JsonPropertyName("inspection_checklist")]
    public required IReadOnlyList<string> InspectionChecklist { get; init; }

    [JsonPropertyName("report_template")]
    public required IReadOnlyList<string> ReportTemplate { get; init; }

    [JsonPropertyName("outcome_classifications")]
    public required IReadOnlyList<GuideImproveOutcome> OutcomeClassifications { get; init; }

    [JsonPropertyName("mutation_boundary")]
    public required IReadOnlyList<string> MutationBoundary { get; init; }
}

internal sealed record GuideImproveOutcome
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("meaning")]
    public required string Meaning { get; init; }
}
