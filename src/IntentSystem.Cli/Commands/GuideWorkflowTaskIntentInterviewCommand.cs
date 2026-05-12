using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G336: read-only <c>intent-cli guide workflow task intent-interview</c>.
/// Surfaces the canonical product-owner interview loop for an external
/// agent that does not already know intent-system. Two distinct
/// modes are advertised:
/// <list type="bullet">
///   <item><strong>interview</strong> — new concept shaping. The
///         durable artifact lives under
///         <c>intents/&lt;domain&gt;/interview/**</c> and is driven
///         by <c>intent-cli interview next-question</c> /
///         <c>record-answer</c> / <c>compile</c>.</item>
///   <item><strong>clarification</strong> — repair an existing
///         intent that has hit a Hard Clarification blocker. The
///         durable artifact lives under
///         <c>intents/&lt;domain&gt;/clarifications/**</c> and is
///         driven by <c>intent-cli clarification next</c> /
///         <c>answer</c>.</item>
/// </list>
///
/// Each question must follow the
/// <em>background → question → options → pros/cons → recommendation</em>
/// structure (acceptance criterion). The guide returns the canonical
/// outline plus a worked example so an AI agent can format every
/// question consistently without referencing local rules or skill
/// prompt files.
///
/// Pure read-only — never reads parent host queue-state, never calls
/// <c>gh</c>, never mutates state, never launches an AI provider.
/// </summary>
internal static class GuideWorkflowTaskIntentInterviewCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide workflow task intent-interview [--mode interview|clarification] [--format markdown|json]";

    internal const string ModeInterview = "interview";
    internal const string ModeClarification = "clarification";

    /// <summary>
    /// G336: structural outline every product-owner question must
    /// follow. Tests pin every section so future drift in tooling
    /// cannot silently relax the contract.
    /// </summary>
    internal static readonly IReadOnlyList<QuestionSection> QuestionStructure = new[]
    {
        new QuestionSection
        {
            Section = "background",
            Purpose = "One short paragraph the AI agent fills in BEFORE asking — names the intent slice, the durable artifact path, and what the agent already knows. Operators must not have to re-explain context.",
            ExampleLine = "We are shaping intent `intent-cli` slice G336 (guided intent interview). The current interview log lives under `intents/intent-cli/interview/2026-05-12-g336.md`. We have already recorded the slice goal and the in-scope statement."
        },
        new QuestionSection
        {
            Section = "question",
            Purpose = "Exactly one focused question the operator can answer in a single sentence or paragraph. NEVER bundle multiple decisions in one question — split them and ask the most blocking one first.",
            ExampleLine = "Should the durable interview artifact live under `intents/<domain>/interview/<date>-<slice>.md` (per-slice file) or `intents/<domain>/interview/log.md` (rolling log)?"
        },
        new QuestionSection
        {
            Section = "options",
            Purpose = "Bullet-listed candidate answers. Two or three is usual; one explicit `none-of-the-above-please-clarify` option keeps the operator from feeling boxed in.",
            ExampleLine = "Options:\n- per-slice file (one markdown per slice)\n- rolling log (single markdown appended forever)\n- none of the above — please describe the durable layout you prefer"
        },
        new QuestionSection
        {
            Section = "pros-cons",
            Purpose = "Concise pros/cons per option so the operator does not have to derive trade-offs from scratch. Three bullets per option max; surface the durability / discoverability trade-off explicitly.",
            ExampleLine = "Per-slice file pros: easy to grep by slice ID. Cons: more files in `intents/`.\nRolling log pros: single artifact, no naming bikeshed. Cons: harder to find slice boundaries during review."
        },
        new QuestionSection
        {
            Section = "recommendation",
            Purpose = "The AI agent's recommended pick + one-sentence reason, called out as a recommendation (not a decision). The operator stays the source of truth; the recommendation only saves them a turn.",
            ExampleLine = "Recommendation: per-slice file — it pairs naturally with the per-slice issue/PR cadence and grep-by-slice is the most common query."
        }
    };

    /// <summary>
    /// G336: mode-specific guidance. Tests pin each mode's commands,
    /// durable-artifact path, and stop conditions.
    /// </summary>
    internal static readonly IReadOnlyList<InterviewModeGuidance> Modes = new[]
    {
        new InterviewModeGuidance
        {
            Mode = ModeInterview,
            Purpose = "Shape a NEW concept into a durable intent. The conversation produces the intent tree update and unblocks `intent next-slice` for the first time.",
            DurableArtifactPath = "intents/<domain>/interview/<date>-<slice>.md (or the per-domain default — confirm via `intent-cli interview compile --session <session-id> [--domain <d>] --format json`).",
            // PR #776 reviewer fix: command examples MUST match the
            // real CLI signatures. The interview surface is
            // session-keyed (`--session <id>`), uses `--question` +
            // `--from-file` for record-answer, and `--session` for
            // compile / draft-from-interview. Domain is optional and
            // falls back to the host config.
            Commands = new[]
            {
                "intent-cli interview next-question --session <session-id> [--domain <domain-name>] --format json",
                "intent-cli interview record-answer --session <session-id> --question <question-id> --from-file <answer-path> [--domain <domain-name>] [--prompt <text>] --write --format json",
                "intent-cli interview compile --session <session-id> [--domain <domain-name>] --format json",
                "intent-cli intent draft-from-interview --session <session-id> [--domain <domain-name>] --write --format json"
            },
            StopConditions = new[]
            {
                "`interview compile` reports `status: complete` and an intent tree update is staged.",
                "Operator explicitly closes the interview (no further open questions, no Hard Clarification raised).",
                "Three consecutive answers are `none of the above — please describe`: stop and surface a Hard Clarification so the operator can re-shape the slice rather than continuing to guess."
            },
            FollowUp = "After compile completes, run `intent-cli intent next-slice --dry-run --domain <d> --format json` to confirm the slice is now `issue-cut-ready`. If `clarification-required`, switch to the `clarification` mode (below) to repair the blocker."
        },
        new InterviewModeGuidance
        {
            Mode = ModeClarification,
            Purpose = "REPAIR an existing intent that has hit a Hard Clarification blocker. The interview's job is to extract exactly enough operator input to unblock `intent next-slice`; it MUST NOT pretend to be a fresh-concept interview.",
            DurableArtifactPath = "intents/<domain>/clarifications/<clarification-id>.json (created by `clarify open` or `clarification next` — never hand-edited).",
            // PR #776 reviewer fix: `clarification answer` is a choice
            // / note flow (the prompt-matrix surface), not a file
            // upload. `clarify record` consumes a recorded decision
            // packet via `--from-file`, and `clarify draft` is keyed
            // on `--question` (the open question text), not a
            // clarification id.
            Commands = new[]
            {
                "intent-cli clarification status [--domain <domain-name>] --format json",
                "intent-cli clarification next [--domain <domain-name>] --format json",
                "intent-cli clarification answer --domain <domain-name> --id <clarification-id> --choice <option-id> [--note <text>] --write --format json",
                "intent-cli clarify draft --domain <domain-name> --question <open-question-text> --format markdown",
                "intent-cli clarify record --domain <domain-name> --from-file <decision-packet-path> [--dry-run]"
            },
            StopConditions = new[]
            {
                "`clarification status` reports the blocker as `answered`; `intent next-slice --dry-run` no longer returns `clarification-required`.",
                "Operator explicitly closes the clarification with a `won't-fix` or `out-of-scope` choice; the intent tree update is then optional, not blocking.",
                "Operator answer reveals the blocker is actually a fresh-concept gap (interview-class): stop the clarification cleanly (record the decision via `clarify record --from-file`), then start a new interview via `interview next-question --session <id>`."
            },
            FollowUp = "After answering, re-run `intent-cli intent next-slice --dry-run --domain <d> --format json`. If still blocked, the next clarification will be queued and `clarification next` returns it."
        }
    };

    /// <summary>
    /// G336: explicit invariants every interview/clarification surface
    /// must surface. Tests pin each invariant verbatim.
    /// </summary>
    internal static readonly IReadOnlyList<string> Invariants = new[]
    {
        "Interview is for shaping a NEW concept; clarification is for repairing an EXISTING intent. Do not run an interview to repair a Hard Clarification, and do not run a clarification to invent a new concept — surface the wrong-mode mismatch instead.",
        "Every question follows the background → question → options → pros/cons → recommendation structure. One focused question per turn. Recommendations are advisory; the operator is the source of truth.",
        "Generated questions are durable. Interview questions live under `intents/<domain>/interview/**`; clarification questions live under `intents/<domain>/clarifications/**`. Never hand-edit these files when a supported `intent-cli interview` or `intent-cli clarify` / `clarification` command exists.",
        "Prefer intent-cli-backed metadata mutation over hand-editing. Ask `intent-cli guide commands list --format json` or `intent-cli automation summary --domain <d> --format json` which command performs the transition, run that command, then validate the result.",
        "Child implementation loops MUST NOT inspect or mutate parent host queue-state, runs logs, packet directories, intent tree, review-runtime state, local rules, or local skills (G300 / G330 / G333). Interview / clarification artifacts are host-owned.",
        "Never launch AI providers (Claude / Codex / any LLM) from intent-cli. The chat-first model has the human agent driving the conversation."
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var mode, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var visibleModes = string.IsNullOrEmpty(mode)
            ? Modes
            : Modes.Where(m => string.Equals(m.Mode, mode, StringComparison.Ordinal)).ToArray();

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            var payload = new IntentInterviewGuidance
            {
                Usage = UsageLine,
                FocusMode = string.IsNullOrEmpty(mode) ? null : mode,
                QuestionStructure = QuestionStructure,
                Modes = visibleModes,
                Invariants = Invariants
            };
            writer.Write(JsonSerializer.Serialize(payload, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(visibleModes, writer);
        }

        return 0;
    }

    private static void WriteMarkdown(IReadOnlyList<InterviewModeGuidance> modes, TextWriter writer)
    {
        writer.WriteLine("# intent-cli — intent interview / clarification workflow guide");
        writer.WriteLine();
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("Two modes; pick the right one BEFORE asking any question.");
        writer.WriteLine();

        writer.WriteLine("## Question structure (mandatory)");
        writer.WriteLine();
        writer.WriteLine("Every question to the operator MUST include all five sections in order:");
        writer.WriteLine();
        writer.WriteLine("| section | purpose | example |");
        writer.WriteLine("|---------|---------|---------|");
        foreach (var s in QuestionStructure)
        {
            writer.WriteLine($"| `{s.Section}` | {s.Purpose} | {s.ExampleLine.Replace("\n", " ")} |");
        }
        writer.WriteLine();

        foreach (var m in modes)
        {
            writer.WriteLine($"## Mode: {m.Mode}");
            writer.WriteLine();
            writer.WriteLine($"- Purpose: {m.Purpose}");
            writer.WriteLine($"- Durable artifact: `{m.DurableArtifactPath}`");
            writer.WriteLine();
            writer.WriteLine("### Commands");
            foreach (var c in m.Commands)
            {
                writer.WriteLine($"- `{c}`");
            }
            writer.WriteLine();
            writer.WriteLine("### Stop conditions");
            foreach (var s in m.StopConditions)
            {
                writer.WriteLine($"- {s}");
            }
            writer.WriteLine();
            writer.WriteLine("### Follow up");
            writer.WriteLine($"- {m.FollowUp}");
            writer.WriteLine();
        }

        writer.WriteLine("## Invariants");
        foreach (var line in Invariants)
        {
            writer.WriteLine($"- {line}");
        }
    }

    private static bool TryParseArguments(string[] args, out string mode, out string format, out string error)
    {
        mode = string.Empty;
        format = FormatMarkdown;
        error = string.Empty;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help":
                    break;
                case "--mode":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = "--mode requires a value (interview or clarification).";
                        return false;
                    }
                    var raw = args[i + 1];
                    if (!string.Equals(raw, ModeInterview, StringComparison.Ordinal)
                        && !string.Equals(raw, ModeClarification, StringComparison.Ordinal))
                    {
                        error = $"--mode '{raw}' did not resolve (use interview or clarification).";
                        return false;
                    }
                    mode = raw;
                    i++;
                    break;
                case "--format":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var fmt = args[i + 1];
                    if (!string.Equals(fmt, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(fmt, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{fmt}').";
                        return false;
                    }
                    format = fmt;
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
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>
/// G336: one structural section of a product-owner question.
/// </summary>
internal sealed record QuestionSection
{
    [JsonPropertyName("section")]
    public required string Section { get; init; }

    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    [JsonPropertyName("example_line")]
    public required string ExampleLine { get; init; }
}

/// <summary>
/// G336: guidance for one interview mode (new concept vs blocker repair).
/// </summary>
internal sealed record InterviewModeGuidance
{
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    [JsonPropertyName("durable_artifact_path")]
    public required string DurableArtifactPath { get; init; }

    [JsonPropertyName("commands")]
    public required IReadOnlyList<string> Commands { get; init; }

    [JsonPropertyName("stop_conditions")]
    public required IReadOnlyList<string> StopConditions { get; init; }

    [JsonPropertyName("follow_up")]
    public required string FollowUp { get; init; }
}

/// <summary>
/// G336: full JSON payload.
/// </summary>
internal sealed record IntentInterviewGuidance
{
    [JsonPropertyName("usage")]
    public required string Usage { get; init; }

    [JsonPropertyName("focus_mode")]
    public string? FocusMode { get; init; }

    [JsonPropertyName("question_structure")]
    public required IReadOnlyList<QuestionSection> QuestionStructure { get; init; }

    [JsonPropertyName("modes")]
    public required IReadOnlyList<InterviewModeGuidance> Modes { get; init; }

    [JsonPropertyName("invariants")]
    public required IReadOnlyList<string> Invariants { get; init; }
}
