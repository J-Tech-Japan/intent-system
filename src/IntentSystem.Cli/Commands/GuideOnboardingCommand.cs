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
        "Usage: intent-cli guide onboarding [--role <role>] [--format markdown|json]";

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

        if (!TryParseArguments(args, out var role, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var result = BuildResult(role);

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
        => BuildResult(role: null);

    internal static GuideOnboardingResult BuildResult(string? role)
    {
        return new GuideOnboardingResult
        {
            InvokingRole = GuideRoleContractGuidance.Normalize(role),
            RoleContractFirst = GuideRoleContractGuidance.Resolve(role),
            Summary = "AI-agent onboarding smoke: the first calls a fresh agent should make to learn the collaboration model from intent-cli itself, without reading local skill files or copied rules folders.",
            FirstCallSequence = new[]
            {
                new GuideOnboardingStep
                {
                    Order = 1,
                    Command = "intent-cli guide model --format json",
                    Purpose = "Learn the chat-first / CLI-internal collaboration model (roles: human / AI agent / intent-cli / host repo, primary data paths, optional advanced runtime, hard rules) AND the PRIMARY execution orchestration model for autonomous multi-thread work (design / orchestrator / implementation / review over agmsg, message-driven steady state, timer-loop as the simpler alternative).",
                    NoMutation = "Pure read; emits a static description and never touches the file system."
                },
                new GuideOnboardingStep
                {
                    Order = 2,
                    Command = "intent-cli guide orchestrator-thread --domain <domain> --target-repo <owner/repo> --agent <agent> --format markdown",
                    Purpose = "G540: reach the full orchestrator-thread setup checklist directly from onboarding — the PRIMARY four-thread model's setup intake over the selected session transport (missing-inputs / setup-ready / blocked), role prompts, mode separation, role boundary and design↔orchestrator double-check rule, and safety-net guidance. Not required for a single-thread/timer-loop setup (see `guide prompt-matrix` instead).",
                    NoMutation = "Pure read; emits a static description and never touches the file system or agmsg state."
                },
                // G570: a fresh agent must learn WHICH TRANSPORT this team runs
                // before it reads transport-specific operating sections —
                // otherwise it follows agmsg registration steps in a herdr-only
                // team, or the reverse. This sits directly after the
                // orchestrator-thread step because that is the surface the mode
                // routes.
                new GuideOnboardingStep
                {
                    Order = 3,
                    Command = "intent-cli session-layer show --domain <domain> [--team <team>] --format json",
                    Purpose = "G624: learn which SESSION LAYER this team runs — prefer `herdr-only` for a collocated single-machine team because it has fewer dependencies, or choose supported, non-retired `agmsg` + herdr for a distributed team or an existing agmsg investment. "
                        + SessionLayerMode.TransportPreferenceSentence
                        + " Absent a record the mode is `agmsg`. The recorded mode selects which operating sections `guide orchestrator-thread` renders, so read it before following any transport-specific setup. Change it with `intent-cli session-layer set --domain <domain> --mode agmsg|herdr-only --write` — reversible in both directions.",
                    NoMutation = "Pure read; `show` never writes. Only `session-layer set --write` records a mode."
                },
                new GuideOnboardingStep
                {
                    Order = 4,
                    Command = "intent-cli guide rules list --format json",
                    Purpose = "Discover supported `guide rules --topic` ids and their categories (automation / issue-contract / interview / review).",
                    NoMutation = "Pure read; lists a static registry."
                },
                new GuideOnboardingStep
                {
                    Order = 5,
                    Command = "intent-cli guide commands list --format json",
                    Purpose = "Inspect every command group with its lifecycle classification (primary / support / advanced / experimental) so the agent does not misread legacy surfaces such as `run` as the default path.",
                    NoMutation = "Pure read; static classification table."
                },
                new GuideOnboardingStep
                {
                    Order = 6,
                    Command = "intent-cli guide workflow suggest --goal \"<operator goal>\" --format json",
                    Purpose = "Classify the operator's stated goal into one of the canonical workflows (feature-intake / next-slice-planning / review / child-implementation / clarification / orchestrator-setup) and receive the recommended command sequence.",
                    NoMutation = "Pure read; classification is keyword-based and emits no state. Default output excludes advanced-runtime suggestions; opt in with `--include-advanced-runtime`."
                },
                new GuideOnboardingStep
                {
                    Order = 7,
                    Command = "intent-cli intent status --domain <domain> --format json",
                    Purpose = "Surface current baseline, in-flight WIP, queued packets, and open clarification state for the target domain.",
                    NoMutation = "Pure read of `.intent-cli/queue-state.json` and `intents/<domain>/clarifications/open.md`."
                },
                new GuideOnboardingStep
                {
                    Order = 8,
                    Command = "intent-cli intent next-slice --dry-run --domain <domain> --target-repo <owner/repo> --format json",
                    Purpose = "Verify WIP cap, clarification gates, and candidate readiness before drafting or planning further state-mutating work.",
                    NoMutation = "Pure read; `--dry-run` is required and the command is implemented as read-only."
                },
                new GuideOnboardingStep
                {
                    Order = 9,
                    Command = "intent-cli interview next-question --session <id> --domain <domain> --format json",
                    Purpose = "Once an active interview session exists, surface the next pending question without recording any answer.",
                    NoMutation = "Pure read of `intents/<domain>/interviews/<session>.json`. `record-answer --write` is the only mutation in the chat-first flow and is excluded from onboarding."
                },
                new GuideOnboardingStep
                {
                    Order = 10,
                    Command = "intent-cli automation summary --format json",
                    Purpose = "Read the canonical label-driven automation contract and capability JSON for the host repo.",
                    NoMutation = "Pure read; the operator owns the runtime contract that this command surfaces."
                },
            },
            FeedbackGuidance = new GuideOnboardingFeedbackGuidance
            {
                JsonCommand = "intent-cli guide feedback --format json",
                MarkdownCommand = "intent-cli guide feedback --format markdown",
                Pointer = "Project feedback is a public GitHub issue channel on J-Tech-Japan/intent-system. Read the warning and let the design thread or operator deliberately file a reviewed report; an AI seat drafts only.",
                NoMutation = "The pointer and target guide are render-only: no gh issue create execution, API POST, network connection, subprocess, confirmation submission, or telemetry write/queue."
            },
            UpdateChannelGuidance = new GuideOnboardingUpdateChannelGuidance
            {
                JsonCommand = "intent-cli update --check --format json",
                MarkdownCommand = "intent-cli update --check --format markdown",
                Contract = "The channel is re-derived from the fully resolved executable real path on every run; no channel marker is persisted. Unknown or ambiguous paths fail closed with manual guidance for dotnet tool, npm global, npx, and standalone binary updates.",
                CheckSafety = "--check reports current_version, latest_version, and would_be_action with process_spawned=false and writes_performed=false."
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
                DispatcherSkillCarveOut.Sentence,
                "Operator acceptance is required before any mutating call (out of scope for this onboarding smoke).",
                "`intent-target` and `intent-pr-created` label transitions stay behind explicit publish/closeout commands; onboarding never touches labels."
            },
            MeasuredIncident = GuideRoleContractGuidance.MeasuredIncident,
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideOnboardingResult result)
    {
        writer.WriteLine("# Guide onboarding — zero-local-rules smoke path");
        writer.WriteLine();
        if (result.RoleContractFirst is { } roleContract)
        {
            writer.WriteLine("## Read your role contract first (G672 — preview-through-1.x)");
            writer.WriteLine();
            writer.WriteLine($"- role: `{roleContract.Role}`");
            writer.WriteLine($"- operating guide: `{roleContract.Guide}`");
            writer.WriteLine($"- {roleContract.Instruction}");
            writer.WriteLine();
        }
        writer.WriteLine(result.Summary);
        writer.WriteLine();
        writer.WriteLine("## Measured incident record (G672 — preview-through-1.x)");
        writer.WriteLine();
        writer.WriteLine(result.MeasuredIncident);
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

        writer.WriteLine("## Channel-aware update route (G703)");
        writer.WriteLine();
        writer.WriteLine($"- JSON: `{result.UpdateChannelGuidance.JsonCommand}`");
        writer.WriteLine($"- Markdown: `{result.UpdateChannelGuidance.MarkdownCommand}`");
        writer.WriteLine($"- contract: {result.UpdateChannelGuidance.Contract}");
        writer.WriteLine($"- check safety: {result.UpdateChannelGuidance.CheckSafety}");
        writer.WriteLine();

        writer.WriteLine("## Project feedback route (G705)");
        writer.WriteLine();
        writer.WriteLine($"- JSON: `{result.FeedbackGuidance.JsonCommand}`");
        writer.WriteLine($"- Markdown: `{result.FeedbackGuidance.MarkdownCommand}`");
        writer.WriteLine($"- pointer: {result.FeedbackGuidance.Pointer}");
        writer.WriteLine($"- no-mutation: {result.FeedbackGuidance.NoMutation}");
        writer.WriteLine();

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

    private static bool TryParseArguments(string[] args, out string? role, out string format, out string error)
    {
        role = null;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--role":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--role requires a value.";
                        return false;
                    }

                    role = args[index + 1].Trim();
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
        writer.WriteLine("guide onboarding");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only first-call sequence for an AI agent with no local skill files or copied rules. With --role, a role that has an installed operating contract receives that pointer before the sequence; roles without a contract receive no invented instruction.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };
}

internal sealed record GuideOnboardingResult
{
    [JsonPropertyName("invoking_role")]
    public string? InvokingRole { get; init; }

    [JsonPropertyName("role_contract_first")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GuideRoleContractPointer? RoleContractFirst { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("measured_incident")]
    public required string MeasuredIncident { get; init; }

    [JsonPropertyName("first_call_sequence")]
    public required IReadOnlyList<GuideOnboardingStep> FirstCallSequence { get; init; }

    [JsonPropertyName("feedback_guidance")]
    public required GuideOnboardingFeedbackGuidance FeedbackGuidance { get; init; }

    [JsonPropertyName("update_channel_guidance")]
    public required GuideOnboardingUpdateChannelGuidance UpdateChannelGuidance { get; init; }

    [JsonPropertyName("host_data_boundary")]
    public required GuideOnboardingHostDataBoundary HostDataBoundary { get; init; }

    [JsonPropertyName("hard_rules")]
    public required IReadOnlyList<string> HardRules { get; init; }
}

internal sealed record GuideOnboardingFeedbackGuidance
{
    [JsonPropertyName("json_command")]
    public required string JsonCommand { get; init; }

    [JsonPropertyName("markdown_command")]
    public required string MarkdownCommand { get; init; }

    [JsonPropertyName("pointer")]
    public required string Pointer { get; init; }

    [JsonPropertyName("no_mutation")]
    public required string NoMutation { get; init; }
}

internal sealed record GuideOnboardingUpdateChannelGuidance
{
    [JsonPropertyName("json_command")]
    public required string JsonCommand { get; init; }

    [JsonPropertyName("markdown_command")]
    public required string MarkdownCommand { get; init; }

    [JsonPropertyName("contract")]
    public required string Contract { get; init; }

    [JsonPropertyName("check_safety")]
    public required string CheckSafety { get; init; }
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
