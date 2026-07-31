using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G295: Read-only <c>intent-cli guide intent-work audit</c>. Emits a
/// concise audit/report template that an AI agent (Codex / Claude / Copilot)
/// must include in its final summary for an intent-organize / clarification /
/// packet-preload / intent-shape session, so the operator can confirm the
/// session was driven through <c>intent-cli</c> surfaces — not through
/// copied prompt files, <c>intents/rules/**</c>, or local skills.
///
/// The template explicitly distinguishes read-only guidance/status/search
/// calls (noisy by default and not tracked in <c>runs.jsonl</c>) from
/// mutation calls (which DO produce durable state). Skipped commands are
/// expected to come with a one-line reason (e.g. "no clarification
/// required"), so a missing call is auditable rather than silent.
///
/// Pure read of static template data. Never mutates state. Never launches
/// an AI provider.
/// </summary>
internal static class GuideIntentWorkAuditCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide intent-work audit [--domain <name>] [--target-repo <owner/repo>] [--format markdown|json]";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(UsageLine);
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

    private static GuideIntentWorkAuditResult BuildResult(string? domain, string? targetRepo)
    {
        var domainPlaceholder = string.IsNullOrWhiteSpace(domain) ? "<DOMAIN>" : domain!;
        var targetRepoPlaceholder = string.IsNullOrWhiteSpace(targetRepo) ? "<TARGET-REPO>" : targetRepo!;

        return new GuideIntentWorkAuditResult
        {
            Summary =
                "Audit template for an intent-work session. Include this trace in your final operator summary so the operator can verify the session ran through intent-cli surfaces (and not through copied prompt files, intents/rules/**, or local skills).",
            Domain = domainPlaceholder,
            TargetRepo = targetRepoPlaceholder,
            ReadOnlyCallExpectations =
            [
                new GuideIntentWorkAuditExpectation
                {
                    Order = 1,
                    Command = "intent-cli guide model --format json",
                    Purpose = "Confirm chat-first / CLI-internal collaboration model.",
                    NoMutation = "Pure read; no durable state."
                },
                new GuideIntentWorkAuditExpectation
                {
                    Order = 2,
                    Command = "intent-cli guide onboarding --format json",
                    Purpose = "First-call sequence for a fresh agent.",
                    NoMutation = "Pure read; no durable state."
                },
                new GuideIntentWorkAuditExpectation
                {
                    Order = 3,
                    Command = "intent-cli guide commands list --format json",
                    Purpose = "Distinguish primary / support / advanced / experimental command groups.",
                    NoMutation = "Pure read; no durable state."
                },
                new GuideIntentWorkAuditExpectation
                {
                    Order = 4,
                    Command = $"intent-cli intent status --domain {domainPlaceholder} --format json",
                    Purpose = "Current baseline / WIP / queued packets / open clarifications for the domain.",
                    NoMutation = "Pure read; no durable state."
                },
                new GuideIntentWorkAuditExpectation
                {
                    Order = 5,
                    Command = $"intent-cli intent search --domain {domainPlaceholder} <query> --format json",
                    Purpose = "Locate relevant intent-tree / clarification / packet artifacts before mutating.",
                    NoMutation = "Pure read; no durable state."
                },
                new GuideIntentWorkAuditExpectation
                {
                    Order = 6,
                    Command = $"intent-cli intent next-slice --dry-run --domain {domainPlaceholder} --target-repo {targetRepoPlaceholder} --format json",
                    Purpose = "Preview the recommended next slice and verify WIP cap / clarification gates.",
                    NoMutation = "Dry-run; --write is the mutation boundary."
                }
            ],
            MutationCallBoundaries =
            [
                new GuideIntentWorkAuditExpectation
                {
                    Order = 1,
                    Command = $"intent-cli interview record-answer --domain {domainPlaceholder} --question <id> --answer <text> --write",
                    Purpose = "Durably record an operator-accepted answer to a clarification question.",
                    NoMutation = "Mutation: writes the interview artifact only after operator acceptance."
                },
                new GuideIntentWorkAuditExpectation
                {
                    Order = 2,
                    Command = $"intent-cli intent draft-from-interview --domain {domainPlaceholder} --write",
                    Purpose = "Promote interview answers into the durable intent shape draft.",
                    NoMutation = "Mutation: writes the intent draft."
                },
                new GuideIntentWorkAuditExpectation
                {
                    Order = 3,
                    Command = $"intent-cli packet draft --execution-unit <id> --target-repo {targetRepoPlaceholder} --write",
                    Purpose = "Materialize the canonical packet directory for a publishable slice.",
                    NoMutation = "Mutation: writes packet.yaml / implementation.md / review-context.md / github-body.md."
                },
                new GuideIntentWorkAuditExpectation
                {
                    Order = 4,
                    Command = $"intent-cli issue publish-flow <id> --repo {targetRepoPlaceholder} --write",
                    Purpose = "Publish the prepared packet as a GitHub issue and update durable state.",
                    NoMutation = "Mutation: validates packet → creates GitHub issue → updates queue-state."
                }
            ],
            FinalReportSections =
            [
                "Read-only commands invoked (with arguments)",
                "Mutation commands invoked (with arguments and resulting artifact paths or URLs)",
                "Files changed (paths + 1-line summary)",
                "Issue URLs created or updated",
                "Clarification questions opened, answered, or deferred",
                "Skipped commands with a one-line reason (e.g. 'no clarification required', 'WIP cap blocked')",
                "Forbidden sources NOT consulted: intents/rules/**, local skill files, copied prompt files"
            ],
            ForbiddenSources =
            [
                "intents/rules/**",
                DispatcherSkillCarveOut.ForbiddenSourceItemWithExamples,
                "copied prompt files"
            ],
            HardRules =
            [
                "intent-cli must not launch Codex/Claude or any AI provider during intent work.",
                "Mutation calls (--write) require explicit operator acceptance; never silent.",
                "Read-only guidance/status/search calls do NOT need to be queued through `runs.jsonl`; the audit trace in the final summary is the operator's verification surface.",
                "Skipped commands must come with a one-line reason; a silent omission is a failure of the audit, not an acceptable shortcut.",
                "Final summary must include this audit template — copy it, fill it in, and surface it to the operator at the end of the session."
            ]
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideIntentWorkAuditResult result)
    {
        writer.WriteLine("# Intent-work audit — final-report template");
        writer.WriteLine();
        writer.WriteLine(result.Summary);
        writer.WriteLine();
        writer.WriteLine($"- Domain: `{result.Domain}`");
        writer.WriteLine($"- Target repo: `{result.TargetRepo}`");
        writer.WriteLine();

        writer.WriteLine("## Read-only call expectations (no durable state)");
        foreach (var expectation in result.ReadOnlyCallExpectations)
        {
            writer.WriteLine($"{expectation.Order}. `{expectation.Command}`");
            writer.WriteLine($"   - Purpose: {expectation.Purpose}");
            writer.WriteLine($"   - {expectation.NoMutation}");
        }
        writer.WriteLine();

        writer.WriteLine("## Mutation call boundaries (require operator acceptance)");
        foreach (var expectation in result.MutationCallBoundaries)
        {
            writer.WriteLine($"{expectation.Order}. `{expectation.Command}`");
            writer.WriteLine($"   - Purpose: {expectation.Purpose}");
            writer.WriteLine($"   - {expectation.NoMutation}");
        }
        writer.WriteLine();

        writer.WriteLine("## Final-report sections to fill in");
        foreach (var section in result.FinalReportSections)
        {
            writer.WriteLine($"- {section}");
        }
        writer.WriteLine();

        writer.WriteLine("## Forbidden sources (must NOT be consulted as routine source of truth)");
        foreach (var src in result.ForbiddenSources)
        {
            writer.WriteLine($"- {src}");
        }
        writer.WriteLine();

        writer.WriteLine("## Hard rules");
        foreach (var rule in result.HardRules)
        {
            writer.WriteLine($"- {rule}");
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? domain,
        out string? targetRepo,
        out string format,
        out string error)
    {
        domain = null;
        targetRepo = null;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }
                    domain = args[++index].Trim();
                    break;
                case "--target-repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--target-repo requires a value.";
                        return false;
                    }
                    targetRepo = args[++index].Trim();
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requestedFormat = args[++index].Trim();
                    if (!string.Equals(requestedFormat, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requestedFormat}').";
                        return false;
                    }
                    format = requestedFormat;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        return true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}

internal sealed record GuideIntentWorkAuditResult
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("target_repo")]
    public required string TargetRepo { get; init; }

    [JsonPropertyName("read_only_call_expectations")]
    public required IReadOnlyList<GuideIntentWorkAuditExpectation> ReadOnlyCallExpectations { get; init; }

    [JsonPropertyName("mutation_call_boundaries")]
    public required IReadOnlyList<GuideIntentWorkAuditExpectation> MutationCallBoundaries { get; init; }

    [JsonPropertyName("final_report_sections")]
    public required IReadOnlyList<string> FinalReportSections { get; init; }

    [JsonPropertyName("forbidden_sources")]
    public required IReadOnlyList<string> ForbiddenSources { get; init; }

    [JsonPropertyName("hard_rules")]
    public required IReadOnlyList<string> HardRules { get; init; }
}

internal sealed record GuideIntentWorkAuditExpectation
{
    [JsonPropertyName("order")]
    public required int Order { get; init; }

    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    [JsonPropertyName("no_mutation")]
    public required string NoMutation { get; init; }
}
