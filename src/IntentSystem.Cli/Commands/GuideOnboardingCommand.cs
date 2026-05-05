using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G259: Read-only <c>intent-cli guide onboarding</c>. Emits the canonical
/// first-call sequence an AI agent should run when no local skill files
/// or copied rules folders are assumed: model discovery, rules topic
/// discovery, workflow suggestion, then interview/status/check
/// follow-up. Each step is read-only by default. Names the host git
/// repository data boundary. Never mutates state. Never launches an
/// AI provider.
/// </summary>
internal static class GuideOnboardingCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide onboarding [--format markdown|json]";

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

        if (!TryParseArguments(args, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var result = BuildResult();

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

    internal static GuideOnboardingResult BuildResult()
    {
        return new GuideOnboardingResult
        {
            Summary = "AI-agent onboarding smoke: the first calls a fresh agent should make to learn the collaboration model from intent-cli itself, without reading local skill files or copied rules folders.",
            FirstCallSequence = new[]
            {
                new GuideOnboardingStep
                {
                    Order = 1,
                    Command = "intent-cli guide model --format json",
                    Purpose = "Learn the chat-first / CLI-internal collaboration model: roles (human / AI agent / intent-cli / host repo), primary data paths, optional advanced runtime, hard rules.",
                    NoMutation = "Pure read; emits a static description and never touches the file system."
                },
                new GuideOnboardingStep
                {
                    Order = 2,
                    Command = "intent-cli guide rules list --format json",
                    Purpose = "Discover supported `guide rules --topic` ids and their categories (automation / issue-contract / interview / review).",
                    NoMutation = "Pure read; lists a static registry."
                },
                new GuideOnboardingStep
                {
                    Order = 3,
                    Command = "intent-cli guide commands list --format json",
                    Purpose = "Inspect every command group with its lifecycle classification (primary / support / advanced / experimental) so the agent does not misread legacy surfaces such as `run` as the default path.",
                    NoMutation = "Pure read; static classification table."
                },
                new GuideOnboardingStep
                {
                    Order = 4,
                    Command = "intent-cli guide workflow suggest --goal \"<operator goal>\" --format json",
                    Purpose = "Classify the operator's stated goal into one of the canonical workflows (feature-intake / next-slice-planning / review / child-implementation / clarification) and receive the recommended command sequence.",
                    NoMutation = "Pure read; classification is keyword-based and emits no state. Default output excludes advanced-runtime suggestions; opt in with `--include-advanced-runtime`."
                },
                new GuideOnboardingStep
                {
                    Order = 5,
                    Command = "intent-cli intent status --domain <domain> --format json",
                    Purpose = "Surface current baseline, in-flight WIP, queued packets, and open clarification state for the target domain.",
                    NoMutation = "Pure read of `.intent-cli/queue-state.json` and `intents/<domain>/clarifications/open.md`."
                },
                new GuideOnboardingStep
                {
                    Order = 6,
                    Command = "intent-cli intent next-slice --dry-run --domain <domain> --target-repo <owner/repo> --format json",
                    Purpose = "Verify WIP cap, clarification gates, and candidate readiness before drafting or planning further state-mutating work.",
                    NoMutation = "Pure read; `--dry-run` is required and the command is implemented as read-only."
                },
                new GuideOnboardingStep
                {
                    Order = 7,
                    Command = "intent-cli interview next-question --session <id> --domain <domain> --format json",
                    Purpose = "Once an active interview session exists, surface the next pending question without recording any answer.",
                    NoMutation = "Pure read of `intents/<domain>/interviews/<session>.json`. `record-answer --write` is the only mutation in the chat-first flow and is excluded from onboarding."
                },
                new GuideOnboardingStep
                {
                    Order = 8,
                    Command = "intent-cli automation summary --format json",
                    Purpose = "Read the canonical label-driven automation contract and capability JSON for the host repo.",
                    NoMutation = "Pure read; the operator owns the runtime contract that this command surfaces."
                }
            },
            HostDataBoundary = new GuideOnboardingHostDataBoundary
            {
                CanonicalRoots = new[]
                {
                    "intents/<domain>/clarifications/open.md — open Hard Clarification blockers.",
                    "intents/<domain>/interviews/<session>.json — durable interview Q/A artifacts.",
                    "intents/<domain>/drafts/<session>.md — accepted-answer drafts before promotion.",
                    ".intent-cli/queue-state.json — execution-unit lifecycle state.",
                    ".intent-cli/runs.jsonl — append-only event log.",
                    ".intent-cli/issues/<execution-unit>/ — per-unit packets."
                },
                Boundaries = new[]
                {
                    "Canonical state lives in the host git repository; `intent-cli` reads it and surfaces structured views.",
                    "Onboarding does not modify any canonical file; mutating subcommands (`record-answer`, `draft-from-interview --write`, `worker complete --write`, `closeout pr --write`, `automation issue-publish --write`, `automation pr-transition --write`) are out of scope for this smoke.",
                    "intent-cli does not launch Codex/Claude or any AI provider; the AI agent runs separately and consumes intent-cli JSON/markdown.",
                    "Promotion to a published GitHub issue happens via `intent-cli packet draft` then `intent-cli issue publish-flow` followed by host `intent-cli automation issue-publish --write`; the onboarding smoke ends well before that boundary."
                }
            },
            HardRules = new[]
            {
                "intent-cli must not launch Codex/Claude or any AI provider during onboarding.",
                "No local skill files (`gh-issue-to-pr`, `gh-fix-pr-comment`, etc.) or copied prompts from `intents/rules/*.md` are required.",
                "Operator acceptance is required before any mutating call (out of scope for this onboarding smoke).",
                "`intent-target` and `intent-pr-created` label transitions stay behind explicit publish/closeout commands; onboarding never touches labels."
            }
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideOnboardingResult result)
    {
        writer.WriteLine("# Guide onboarding — zero-local-rules smoke path");
        writer.WriteLine();
        writer.WriteLine(result.Summary);
        writer.WriteLine();

        writer.WriteLine("## First-call sequence");
        writer.WriteLine();
        foreach (var step in result.FirstCallSequence)
        {
            writer.WriteLine($"### {step.Order}. `{step.Command}`");
            writer.WriteLine();
            writer.WriteLine($"- purpose: {step.Purpose}");
            writer.WriteLine($"- no-mutation: {step.NoMutation}");
            writer.WriteLine();
        }

        writer.WriteLine("## Host git repository data boundary");
        writer.WriteLine();
        writer.WriteLine("Canonical roots:");
        foreach (var root in result.HostDataBoundary.CanonicalRoots)
        {
            writer.WriteLine($"- {root}");
        }
        writer.WriteLine();
        writer.WriteLine("Boundaries:");
        foreach (var boundary in result.HostDataBoundary.Boundaries)
        {
            writer.WriteLine($"- {boundary}");
        }
        writer.WriteLine();

        writer.WriteLine("## Hard rules");
        foreach (var rule in result.HardRules)
        {
            writer.WriteLine($"- {rule}");
        }
    }

    private static bool TryParseArguments(string[] args, out string format, out string error)
    {
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
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
        writer.WriteLine("guide onboarding");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only first-call sequence for an AI agent with no local skill files or copied rules.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };
}

internal sealed record GuideOnboardingResult
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("first_call_sequence")]
    public required IReadOnlyList<GuideOnboardingStep> FirstCallSequence { get; init; }

    [JsonPropertyName("host_data_boundary")]
    public required GuideOnboardingHostDataBoundary HostDataBoundary { get; init; }

    [JsonPropertyName("hard_rules")]
    public required IReadOnlyList<string> HardRules { get; init; }
}

internal sealed record GuideOnboardingStep
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

internal sealed record GuideOnboardingHostDataBoundary
{
    [JsonPropertyName("canonical_roots")]
    public required IReadOnlyList<string> CanonicalRoots { get; init; }

    [JsonPropertyName("boundaries")]
    public required IReadOnlyList<string> Boundaries { get; init; }
}
