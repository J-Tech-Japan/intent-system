namespace IntentSystem.Cli.Commands;

/// <summary>
/// G249: Read-only <c>intent-cli guide collaborate</c> command. Emits the
/// canonical operator-facing collaboration prompt for early product-owner
/// feature intake. The output names explicit responsibility boundaries
/// (intent-cli guides/records, the AI agent interviews/summarizes, the
/// operator decides) and a suggested command sequence so an AI agent can
/// drive the conversation without reading local skill files or
/// <c>intents/rules</c> prompts. Never mutates state. Never launches an
/// AI provider.
/// </summary>
internal static class GuideCollaborateCommand
{
    private const string KindFeatureIntake = "feature-intake";

    private const string FormatMarkdown = "markdown";
    private const string FormatJson = "json";

    private const string UsageLine =
        "Usage: intent-cli guide collaborate --kind feature-intake [--domain <name>] [--format markdown|json]";

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

        if (!TryParseArguments(args, out var kind, out var domainOverride, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (!string.Equals(kind, KindFeatureIntake, StringComparison.Ordinal))
        {
            writer.WriteLine($"Unsupported --kind '{kind}'. Supported: {KindFeatureIntake}.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (!string.Equals(format, FormatMarkdown, StringComparison.Ordinal)
            && !string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine($"--format must be 'markdown' or 'json' (got '{format}').");
            writer.WriteLine(UsageLine);
            return 1;
        }

        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride!;

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            WriteJson(writer, domain);
        }
        else
        {
            WriteMarkdown(writer, domain);
        }

        return 0;
    }

    private static void WriteMarkdown(TextWriter writer, string domain)
    {
        writer.WriteLine($"# Collaborate — feature-intake — {domain}");
        writer.WriteLine();
        writer.WriteLine("Use this guide when an operator opens a session with a request like “intent-cli に以下の機能を追加したいから一緒に作業して” and an AI agent must run the early product-owner feature-intake conversation without reading local skill files.");
        writer.WriteLine();

        writer.WriteLine("## Responsibility boundaries");
        foreach (var line in ResponsibilityBoundaries)
        {
            writer.WriteLine($"- {line}");
        }
        writer.WriteLine();

        writer.WriteLine("## Suggested command sequence");
        foreach (var line in SuggestedCommandSequence(domain))
        {
            writer.WriteLine($"- {line}");
        }
        writer.WriteLine();

        writer.WriteLine("## Interview rules");
        foreach (var line in InterviewRules)
        {
            writer.WriteLine($"- {line}");
        }
        writer.WriteLine();

        writer.WriteLine("## Draft handoff rules");
        foreach (var line in DraftHandoffRules)
        {
            writer.WriteLine($"- {line}");
        }
    }

    private static void WriteJson(TextWriter writer, string domain)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            new
            {
                domain,
                kind = KindFeatureIntake,
                responsibility_boundaries = ResponsibilityBoundaries,
                suggested_command_sequence = SuggestedCommandSequence(domain),
                interview_rules = InterviewRules,
                draft_handoff_rules = DraftHandoffRules
            },
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = true
            });
        writer.Write(json);
        writer.WriteLine();
    }

    private static IReadOnlyList<string> ResponsibilityBoundaries => new[]
    {
        "intent-cli guides and records: provides discovery, status, search, draft, and validation commands; never decides product scope.",
        "AI agent interviews and summarizes: asks clarifying questions, surfaces tradeoffs, drafts contract content for operator acceptance; never mutates canonical state without explicit operator decision.",
        "Operator decides: chooses scope, accepts drafts, authorizes the publish boundary; canonical source-of-truth mutation requires this decision.",
        "intent-cli must not launch AI providers; the AI agent runs separately and consumes intent-cli JSON/markdown.",
        "Routine collaboration must not require reading `intents/rules` or local skill files that restate workflow; rely on the commands listed below. "
            + DispatcherSkillCarveOut.Sentence
    };

    private static IReadOnlyList<string> SuggestedCommandSequence(string domain) => new[]
    {
        $"`intent-cli intent status --domain {domain} --format json` — current baseline, WIP, queued packets, open clarifications.",
        $"`intent-cli intent search --domain {domain} --query <keyword> --format json` — discover related intents, packets, prior art.",
        $"`intent-cli intent explain <execution-unit> --domain {domain} --format json` — summarize an existing execution unit before suggesting overlap.",
        $"`intent-cli intent next-slice --dry-run --domain {domain} --format json` — verify WIP cap and clarification blockers before drafting.",
        "`intent-cli packet draft --execution-unit <id> --target-repo <owner/repo> --dry-run` — preview the canonical packet skeleton; only run without --dry-run after operator acceptance."
    };

    private static IReadOnlyList<string> InterviewRules => new[]
    {
        "Open with the operator's stated request verbatim and confirm scope before asking deeper questions.",
        "Ask one focused question at a time; carry durable answers into the eventual packet draft.",
        "Surface tradeoffs (effort, scope, dependency, sequencing) instead of recommending a single direction.",
        "Stop and report `clarification-required` rather than guessing when the operator's intent is ambiguous.",
        "Treat existing intents and packets surfaced by `intent search` / `intent explain` as authoritative prior art."
    };

    private static IReadOnlyList<string> DraftHandoffRules => new[]
    {
        "All drafts go through `intent-cli packet draft --dry-run` for preview before any write.",
        "Write only after explicit operator acceptance of the drafted Goal, Acceptance Criteria, and Verification.",
        "Never apply `intent-target`; that is the host-owned publish boundary.",
        "Never apply `intent-pr-created`; that is an issue-side completion marker, not a draft signal.",
        "Hand off to `issue publish-flow` only after parent durable state is authoritative."
    };

    private static bool TryParseArguments(
        string[] args,
        out string? kind,
        out string? domainOverride,
        out string format,
        out string error)
    {
        kind = null;
        domainOverride = null;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--kind":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--kind requires a value.";
                        return false;
                    }

                    kind = args[index + 1];
                    index++;
                    break;

                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }

                    domainOverride = args[index + 1];
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }

                    format = args[index + 1];
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(kind))
        {
            error = "--kind is required.";
            return false;
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide collaborate");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only operator-facing collaboration guide for early product-owner feature intake.");
    }
}
