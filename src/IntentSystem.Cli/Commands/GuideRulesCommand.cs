using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G252: Read-only <c>intent-cli guide rules --topic &lt;topic&gt;</c>. Emits
/// the canonical operator-facing rule summary for a supported topic
/// (label-ownership, child-issue-contract, clarification, review-closeout,
/// intake-interview), with source references and installed-command
/// hints, so AI agents can retrieve current rules without opening
/// <c>intents/rules</c> files. Never mutates state. Never launches an AI
/// provider.
/// </summary>
internal static class GuideRulesCommand
{
    private const string FormatMarkdown = "markdown";
    private const string FormatJson = "json";

    private const string TopicLabelOwnership = "label-ownership";
    private const string TopicChildIssueContract = "child-issue-contract";
    private const string TopicClarification = "clarification";
    private const string TopicReviewCloseout = "review-closeout";
    private const string TopicIntakeInterview = "intake-interview";

    private const string UsageLine =
        "Usage: intent-cli guide rules --topic <label-ownership|child-issue-contract|clarification|review-closeout|intake-interview> [--format markdown|json]";

    private static readonly IReadOnlyDictionary<string, RulesTopic> Topics = new Dictionary<string, RulesTopic>(StringComparer.Ordinal)
    {
        [TopicLabelOwnership] = new RulesTopic
        {
            Topic = TopicLabelOwnership,
            Title = "Label ownership",
            Summary = new[]
            {
                "`intent-target` is the host-owned publish boundary. The child loop never adds or removes it.",
                "`intent-pr-created` is an issue-side completion marker; never apply it to a PR.",
                "`intent-issue-in-progress` and `intent-pr-update-in-progress` are exclusive work markers; only one item per lane at a time.",
                "Label transitions for installed surfaces (`issue-publish`, `pr-transition.review-start|request-update|approved`) go through `intent-cli automation` commands; raw `gh ... edit --label` is not the normal path."
            },
            SourceReferences = new[]
            {
                "intents/rules/label-driven-automation.md",
                "docs/automation-templates/intent-automation-label-state-machine.md",
                "intents/rules/intent-target-publish-boundary.md"
            },
            InstalledCommandHints = new[]
            {
                "intent-cli automation summary --format json — current label contract, capability JSON, schema version.",
                "intent-cli automation issue-publish --repo <r> --issue <n> --write — supported issue-side `intent-target` apply.",
                "intent-cli automation pr-transition --repo <r> --pr <n> --transition <review-start|request-update|approved> --write — supported PR transitions.",
                "intent-cli worker claim / worker complete — child-loop label ownership; do not invent transitions in prompts."
            }
        },
        [TopicChildIssueContract] = new RulesTopic
        {
            Topic = TopicChildIssueContract,
            Title = "Child Issue Contract",
            Summary = new[]
            {
                "A child issue body is the standalone implementation contract; the implementer reads only the body.",
                "Required sections: Goal, Why This Slice Exists Now, Current Observed State, Accepted Baseline You May Assume, Target Repo / Path / Part, In Scope, Out Of Scope, Acceptance Criteria, Verification, Related Links.",
                "Parent file paths may appear in Related Links as audit references only; they do not satisfy missing body content.",
                "An incomplete contract must be repaired (or the issue declined) before implementation begins."
            },
            SourceReferences = new[]
            {
                "intents/rules/issue-template-and-review-context.md",
                "docs/automation-templates/coding-automation-loop.md",
                ".intent-cli/issues/<execution-unit>/github-body.md"
            },
            InstalledCommandHints = new[]
            {
                "intent-cli packet draft --execution-unit <id> --target-repo <r> --dry-run — scaffold all required sections in github-body.md.",
                "intent-cli intent next-slice --dry-run --domain <d> — verify current candidate's contract completeness before drafting.",
                "intent-cli intent explain <execution-unit> --domain <d> — surface the existing packet body and queue state."
            }
        },
        [TopicClarification] = new RulesTopic
        {
            Topic = TopicClarification,
            Title = "Hard Clarification rule",
            Summary = new[]
            {
                "Clarification is the hardest stop. When source-of-truth is ambiguous, contradictory, or under-specified, do not guess.",
                "Stop and report background, the question, options, pros/cons, and a recommendation. Status: `clarification-required`.",
                "Open clarification blockers (per-domain `intents/<domain>/clarifications/open.md`) override WIP cap and next-slice cut.",
                "Clarification is the only outcome that overrides best-effort completion."
            },
            SourceReferences = new[]
            {
                "intents/rules/interview-and-clarification-semantics.md",
                "intents/<domain>/clarifications/open.md"
            },
            InstalledCommandHints = new[]
            {
                "intent-cli intent status --domain <d> — surfaces `clarification_open: yes` if a blocker is currently active.",
                "intent-cli intent next-slice --dry-run --domain <d> — recommended outcome `clarification-required` when the file has open blockers or contract sections are missing.",
                "intent-cli automation clarification-stop — host-side clarification stop summary command."
            }
        },
        [TopicReviewCloseout] = new RulesTopic
        {
            Topic = TopicReviewCloseout,
            Title = "Review and closeout",
            Summary = new[]
            {
                "Review is read-only; do not push commits or mutate labels from inside the review loop.",
                "Stage 1 is review/closeout, Stage 2 is next-slice; both run in one wake but never inline next-slice work into review.",
                "Closeout (queue completed → submodule sync → linked issue close → continuation classification) runs only after review acceptance.",
                "`intent-target` PR transitions for review-start / request-update / approved go through `intent-cli automation pr-transition`.",
                "Submodule pointer updates remain a manual step in the current closeout slice."
            },
            SourceReferences = new[]
            {
                "intents/rules/automations/host-review-loop.md",
                "intents/rules/skills/intent-review-closeout.md",
                "docs/automation-templates/host-review-loop.md"
            },
            InstalledCommandHints = new[]
            {
                "intent-cli review closeout-plan --pr <n> --repo <r> --format json — read-only closeout plan with gaps.",
                "intent-cli guide review --pr <n> --repo <r> --format json — review checklist, boundaries, validation suggestions.",
                "intent-cli closeout pr --pr <n> --repo <r> --write — queue/runs closeout once review is accepted."
            }
        },
        [TopicIntakeInterview] = new RulesTopic
        {
            Topic = TopicIntakeInterview,
            Title = "Intake and interview",
            Summary = new[]
            {
                "Intake is bounded: intent-cli guides and records, the AI agent interviews and summarizes, and the operator decides.",
                "Durable interview state lives at `intents/<domain>/interviews/<session>.json`. Recording an answer requires `--write`; new questions must be added with `--prompt`.",
                "Drafts compiled from accepted answers live at `intents/<domain>/drafts/<session>.md`; drafts are not published GitHub issues.",
                "Operator acceptance is required before any source-of-truth mutation; `intent-target` apply happens only after parent durable state is pushed."
            },
            SourceReferences = new[]
            {
                "intents/rules/feature-request-intake-and-autostart.md",
                "intents/intent-cli/specs/04-concept-intake-and-interview.md",
                "intents/intent-cli/specs/06-interview-and-clarification-artifact-contract.md"
            },
            InstalledCommandHints = new[]
            {
                "intent-cli guide collaborate --kind feature-intake --domain <d> — boundaries, suggested command sequence, interview/draft rules.",
                "intent-cli interview next-question --session <id> --domain <d> — first pending question.",
                "intent-cli interview record-answer --session <id> --question <q> --from-file <path> --write — record an answer or append a new question with --prompt.",
                "intent-cli interview compile --session <id> --domain <d> — accepted baseline + open questions summary.",
                "intent-cli intent draft-from-interview --session <id> --domain <d> --write — write a draft markdown for operator review."
            }
        }
    };

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

        // G254: nested `guide rules list` discovery sub-subcommand.
        if (args.Length >= 1 && string.Equals(args[0], "list", StringComparison.Ordinal))
        {
            return GuideRulesListCommand.Execute(context, args[1..], writer);
        }

        if (!TryParseArguments(args, out var topic, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (!Topics.TryGetValue(topic!, out var resolved))
        {
            writer.WriteLine($"Unsupported --topic '{topic}'. Supported: {string.Join(", ", Topics.Keys)}.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(resolved, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, resolved);
        }

        return 0;
    }

    private static void WriteMarkdown(TextWriter writer, RulesTopic topic)
    {
        writer.WriteLine($"# Guide rules — {topic.Topic}");
        writer.WriteLine();
        writer.WriteLine($"**{topic.Title}**");
        writer.WriteLine();

        writer.WriteLine("## Summary");
        foreach (var line in topic.Summary)
        {
            writer.WriteLine($"- {line}");
        }
        writer.WriteLine();

        writer.WriteLine("## Source references");
        foreach (var line in topic.SourceReferences)
        {
            writer.WriteLine($"- {line}");
        }
        writer.WriteLine();

        writer.WriteLine("## Installed command hints");
        foreach (var line in topic.InstalledCommandHints)
        {
            writer.WriteLine($"- {line}");
        }
    }

    private static bool TryParseArguments(string[] args, out string? topic, out string format, out string error)
    {
        topic = null;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--topic":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--topic requires a value.";
                        return false;
                    }

                    topic = args[index + 1];
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

        if (string.IsNullOrWhiteSpace(topic))
        {
            error = "--topic is required.";
            return false;
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide rules");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only operator-facing rule summaries for a supported topic.");
        writer.WriteLine();
        writer.WriteLine("Supported topics:");
        foreach (var name in Topics.Keys)
        {
            writer.WriteLine($"- {name}");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    private sealed record RulesTopic
    {
        public required string Topic { get; init; }

        public required string Title { get; init; }

        public required IReadOnlyList<string> Summary { get; init; }

        public required IReadOnlyList<string> SourceReferences { get; init; }

        public required IReadOnlyList<string> InstalledCommandHints { get; init; }
    }
}
