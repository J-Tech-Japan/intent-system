using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G255: Read-only <c>intent-cli guide workflow suggest</c>. Classifies a
/// broad operator goal into one of five canonical workflows
/// (feature-intake, next-slice-planning, review, child-implementation,
/// clarification) and returns the recommended <c>intent-cli</c> command
/// sequence plus relevant rule topics. No semantic product decisions and
/// no AI provider launch.
/// </summary>
internal static class GuideWorkflowSuggestCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string WorkflowFeatureIntake = "feature-intake";
    private const string WorkflowNextSlicePlanning = "next-slice-planning";
    private const string WorkflowReview = "review";
    private const string WorkflowChildImplementation = "child-implementation";
    private const string WorkflowClarification = "clarification";
    private const string WorkflowUnknown = "unknown";

    private const string UsageLine =
        "Usage: intent-cli guide workflow suggest [--domain <name>] (--goal <text> | --from-file <path>) [--format markdown|json]";

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

        if (!TryParseArguments(args, out var goalInline, out var goalFile, out var domainOverride, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (goalInline is not null && goalFile is not null)
        {
            writer.WriteLine("--goal and --from-file are mutually exclusive.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        string goalText;
        if (goalInline is not null)
        {
            goalText = goalInline;
        }
        else if (goalFile is not null)
        {
            if (!File.Exists(goalFile))
            {
                writer.WriteLine($"--from-file path not found: {goalFile}");
                writer.WriteLine(UsageLine);
                return 1;
            }

            goalText = File.ReadAllText(goalFile);
        }
        else
        {
            writer.WriteLine("either --goal <text> or --from-file <path> is required.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride!;

        var (workflow, matchedKeywords) = Classify(goalText);
        var recommendation = BuildRecommendation(workflow, domain, goalText, matchedKeywords);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(recommendation, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, recommendation);
        }

        return 0;
    }

    private static (string Workflow, IReadOnlyList<string> MatchedKeywords) Classify(string goalText)
    {
        var lower = goalText.ToLowerInvariant();
        var matched = new List<string>();

        bool ContainsAny(IEnumerable<string> needles)
        {
            var hit = false;
            foreach (var needle in needles)
            {
                if (goalText.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || lower.Contains(needle, StringComparison.Ordinal))
                {
                    matched.Add(needle);
                    hit = true;
                }
            }
            return hit;
        }

        if (ContainsAny(new[] { "clarif", "ambiguous", "blocker", "曖昧", "未決" }))
        {
            return (WorkflowClarification, matched);
        }

        if (ContainsAny(new[] { "review", "approve", "merge", "closeout", "request-update", "レビュー" }))
        {
            return (WorkflowReview, matched);
        }

        if (ContainsAny(new[] { "implement issue", "fix issue", "issue-to-pr", "implement #", "実装" }))
        {
            return (WorkflowChildImplementation, matched);
        }

        if (ContainsAny(new[] { "next slice", "next-slice", "plan", "candidate", "次のスライス", "次スライス" }))
        {
            return (WorkflowNextSlicePlanning, matched);
        }

        if (ContainsAny(new[] { "add", "新機能", "feature", "want to", "intake", "shape", "追加", "new feature", "新しい機能" }))
        {
            return (WorkflowFeatureIntake, matched);
        }

        return (WorkflowUnknown, matched);
    }

    private static GuideWorkflowSuggestion BuildRecommendation(string workflow, string domain, string goalText, IReadOnlyList<string> matchedKeywords)
    {
        var (commands, ruleTopics, summary) = workflow switch
        {
            WorkflowFeatureIntake => (
                new[]
                {
                    $"intent-cli guide collaborate --kind feature-intake --domain {domain} --format markdown",
                    $"intent-cli intent status --domain {domain} --format json",
                    $"intent-cli intent search --domain {domain} --query <keyword> --format json",
                    $"intent-cli interview record-answer --session <id> --question <q> --prompt <text> --from-file <path> --write --format json",
                    $"intent-cli intent draft-from-interview --session <id> --domain {domain} --format markdown"
                },
                new[] { "intake-interview", "child-issue-contract", "label-ownership" },
                "Feature intake — operator opens with a request to add functionality; the AI agent runs the interview-to-draft flow without publishing."),

            WorkflowNextSlicePlanning => (
                new[]
                {
                    $"intent-cli intent status --domain {domain} --format json",
                    $"intent-cli intent search --domain {domain} --query <keyword> --format json",
                    $"intent-cli intent next-slice --dry-run --domain {domain} --target-repo <owner/repo> --format json",
                    $"intent-cli packet draft --execution-unit <id> --target-repo <owner/repo> --dry-run --format markdown"
                },
                new[] { "child-issue-contract", "clarification", "label-ownership" },
                "Next-slice planning — verify WIP cap, clarification gates, and candidate readiness before promoting a draft."),

            WorkflowReview => (
                new[]
                {
                    $"intent-cli guide review --pr <n> --repo <owner/repo> --domain {domain} --format markdown",
                    $"intent-cli review closeout-plan --pr <n> --repo <owner/repo> --domain {domain} --format json",
                    "intent-cli automation host-review-preflight --repo <owner/repo> --format json",
                    "intent-cli closeout pr --pr <n> --repo <owner/repo> --write --format json"
                },
                new[] { "review-closeout", "label-ownership" },
                "Review and closeout — read-only checklist + closeout plan first; mutate state only via closeout pr after acceptance."),

            WorkflowChildImplementation => (
                new[]
                {
                    "intent-cli worker next-action --repo <owner/repo> --workdir $PWD --format json",
                    "intent-cli worker claim --kind issue --number <n> --write --format json",
                    "intent-cli worker result-summary --kind issue-to-pr --repo <owner/repo> --issue <n> --pr <m> --outcome <outcome> --format json",
                    "intent-cli worker complete --kind issue --number <n> --outcome <outcome> --write --format json"
                },
                new[] { "child-issue-contract", "label-ownership", "clarification" },
                "Child implementation — drive issue → draft PR through the worker selector and complete the queue/label transitions deterministically."),

            WorkflowClarification => (
                new[]
                {
                    $"intent-cli intent status --domain {domain} --format json",
                    $"intent-cli intent next-slice --dry-run --domain {domain} --target-repo <owner/repo> --format json",
                    "intent-cli automation clarification-stop"
                },
                new[] { "clarification", "child-issue-contract" },
                "Clarification — surface open blockers and stop with a clarification-required summary; do not guess past ambiguous source-of-truth."),

            _ => (
                new[]
                {
                    $"intent-cli guide collaborate --kind feature-intake --domain {domain} --format markdown",
                    $"intent-cli intent status --domain {domain} --format json",
                    $"intent-cli intent search --domain {domain} --query <keyword> --format json",
                    "intent-cli guide rules list --format markdown"
                },
                new[] { "intake-interview", "label-ownership" },
                "Goal did not match a known workflow shape; start with collaborate and search to disambiguate.")
        };

        return new GuideWorkflowSuggestion
        {
            Domain = domain,
            Workflow = workflow,
            Goal = goalText,
            MatchedKeywords = matchedKeywords,
            Summary = summary,
            RecommendedCommands = commands,
            RuleTopics = ruleTopics
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideWorkflowSuggestion result)
    {
        writer.WriteLine($"# Guide workflow suggest — {result.Workflow}");
        writer.WriteLine();
        writer.WriteLine($"- domain: {result.Domain}");
        writer.WriteLine($"- workflow: {result.Workflow}");
        if (result.MatchedKeywords.Count > 0)
        {
            writer.WriteLine($"- matched keywords: {string.Join(", ", result.MatchedKeywords)}");
        }
        writer.WriteLine();
        writer.WriteLine($"## Summary");
        writer.WriteLine();
        writer.WriteLine(result.Summary);
        writer.WriteLine();
        writer.WriteLine("## Recommended commands");
        foreach (var command in result.RecommendedCommands)
        {
            writer.WriteLine($"- `{command}`");
        }
        writer.WriteLine();
        writer.WriteLine("## Rule topics");
        foreach (var topic in result.RuleTopics)
        {
            writer.WriteLine($"- `intent-cli guide rules --topic {topic} --format markdown`");
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? goalInline,
        out string? goalFile,
        out string? domainOverride,
        out string format,
        out string error)
    {
        goalInline = null;
        goalFile = null;
        domainOverride = null;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--goal":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--goal requires a value.";
                        return false;
                    }

                    goalInline = args[index + 1];
                    index++;
                    break;

                case "--from-file":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--from-file requires a path.";
                        return false;
                    }

                    goalFile = args[index + 1];
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
        writer.WriteLine("guide workflow suggest");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only workflow recommendation for a broad operator goal. Returns the suggested intent-cli command sequence and rule topics.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };
}

internal sealed record GuideWorkflowSuggestion
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("workflow")]
    public required string Workflow { get; init; }

    [JsonPropertyName("goal")]
    public required string Goal { get; init; }

    [JsonPropertyName("matched_keywords")]
    public required IReadOnlyList<string> MatchedKeywords { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("recommended_commands")]
    public required IReadOnlyList<string> RecommendedCommands { get; init; }

    [JsonPropertyName("rule_topics")]
    public required IReadOnlyList<string> RuleTopics { get; init; }
}
