using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G274: Read-only <c>intent-cli guide automation local-loop</c>. Returns a
/// paste-ready, agent-specific local recurring loop setup guide for Claude Code
/// (<c>/loop</c>), Codex local automation/heartbeat, or a generic unknown-agent
/// variant that requires clarification before scheduling. Covers frequency
/// policy (5m high-frequency, ~20m low-frequency), same-thread-only rule for
/// local-path workflows, local rules/skills prohibition, and
/// intent-cli-as-guide-source rule. Never mutates state. Never launches an AI
/// provider.
/// </summary>
internal static class GuideAutomationLocalLoopCommand
{
    private const string AgentClaude = "claude";
    private const string AgentCodex = "codex";
    private const string AgentGeneric = "generic";
    private const string AgentUnknown = "unknown";

    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide automation local-loop [--agent claude|codex|generic] [--repo <owner/repo>] [--format markdown|json]";

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

        if (!TryParseArguments(args, out var agent, out var repo, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var result = BuildLocalLoop(agent ?? AgentUnknown, repo);
        return EmitResult(writer, format, result);
    }

    private static int EmitResult(TextWriter writer, string format, GuideAutomationLocalLoopResult result)
    {
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

    private static GuideAutomationLocalLoopResult BuildLocalLoop(string agent, string? repo)
    {
        var repoLabel = string.IsNullOrWhiteSpace(repo) ? "the repo in the current worktree" : $"`{repo}`";

        var (prompt, clarificationRequired, schedulerGuidance) = agent switch
        {
            AgentClaude => BuildClaudeVariant(repoLabel),
            AgentCodex => BuildCodexVariant(repoLabel),
            AgentGeneric => BuildGenericVariant(repoLabel),
            _ => BuildUnknownVariant(repoLabel)
        };

        return new GuideAutomationLocalLoopResult
        {
            Kind = "local-loop",
            Agent = agent,
            Repo = string.IsNullOrWhiteSpace(repo) ? null : repo,
            Prompt = prompt,
            SchedulerGuidance = schedulerGuidance,
            FrequencyPolicy = "High-frequency local loops (active development): 5 minutes. Low-frequency local loops (background / idle polling): about 20 minutes. Ask the operator for the desired frequency before scheduling if it is not supplied.",
            ClarificationRequired = clarificationRequired,
            ForbiddenSources = new[]
            {
                "intents/rules/**",
                DispatcherSkillCarveOut.ForbiddenSourceItemWithExamples,
                "copied prompt files",
                "cloud or new-thread schedulers when the workflow depends on local paths or local .intent-cli packages"
            },
            WorktreeFriendly = "The prompt derives the repo from the current child worktree and does not hard-code operator-specific paths; the same guide works across worktrees."
        };
    }

    private static (string Prompt, bool ClarificationRequired, string SchedulerGuidance) BuildClaudeVariant(string repoLabel)
    {
        var prompt =
$@"Set up a Claude Code local recurring loop for {repoLabel}. Use same-thread `/loop` semantics; do not create any cloud routine, remote scheduler, or new-thread automation.

Scheduler setup:
- High-frequency (active development): type `/loop 5m <task-prompt>` in this Claude Code thread.
- Low-frequency (background / idle polling): type `/loop 20m <task-prompt>`.
- Ask the operator for the desired frequency if it was not supplied.
- Never create a cloud schedule, CronCreate job, or remote CCR session for a workflow that depends on local paths or local `.intent-cli` packages; those schedulers cannot access local state.

Task prompt construction:
1. Run `intent-cli guide automation setup --kind child-implement [--repo <OWNER/REPO>] --format json` to obtain the full child-implement loop prompt.
2. Use the emitted `prompt` field verbatim as the `/loop` task prompt so the agent receives complete, up-to-date guidance each wake.

Hard rules:
- Do not read `intents/rules/**`, local skill files, or copied prompt files. Use `intent-cli guide ...` instead.
- {DispatcherSkillCarveOut.Sentence}
- Do not call `intent-cli run`; it is advanced runtime, not the chat-first path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- Use same-thread `/loop`; never launch a cloud or new-thread scheduler for local-path workflows.
- Ask for frequency before scheduling if the operator has not supplied it.";

        var schedulerGuidance = "Claude Code same-thread `/loop 5m` or `/loop 20m`. Never cloud/new-thread for local-path workflows.";
        return (prompt, false, schedulerGuidance);
    }

    private static (string Prompt, bool ClarificationRequired, string SchedulerGuidance) BuildCodexVariant(string repoLabel)
    {
        var prompt =
$@"Set up a Codex local automation/heartbeat loop for {repoLabel}. Attach the recurring automation to the current thread; do not create any cloud routine, remote scheduler, or new-thread session.

Scheduler setup:
- High-frequency (active development): attach a local automation/heartbeat to the current Codex thread with 5-minute recurrence.
- Low-frequency (background / idle polling): attach with about 20-minute recurrence.
- Ask the operator for the desired frequency if it was not supplied.
- Never create a cloud schedule or new-thread scheduler for a workflow that depends on local paths or local `.intent-cli` packages; those schedulers cannot access local state.

Task prompt construction:
1. Run `intent-cli guide automation setup --kind child-implement [--repo <OWNER/REPO>] --format json` to obtain the full child-implement loop prompt.
2. Use the emitted `prompt` field verbatim as the automation/heartbeat task prompt so the agent receives complete, up-to-date guidance each wake.

Hard rules:
- Do not read `intents/rules/**`, local skill files, or copied prompt files. Use `intent-cli guide ...` instead.
- {DispatcherSkillCarveOut.Sentence}
- Do not call `intent-cli run`; it is advanced runtime, not the chat-first path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- Use local automation/heartbeat attached to the current thread; never launch a cloud or new-thread scheduler for local-path workflows.
- Ask for frequency before scheduling if the operator has not supplied it.";

        var schedulerGuidance = "Codex local automation/heartbeat attached to current thread at 5m or 20m. Never cloud/new-thread for local-path workflows.";
        return (prompt, false, schedulerGuidance);
    }

    private static (string Prompt, bool ClarificationRequired, string SchedulerGuidance) BuildGenericVariant(string repoLabel)
    {
        var prompt =
$@"Set up a local recurring loop for {repoLabel} using a provider-neutral approach.

Before scheduling, confirm the agent runtime with the operator:
- ""Are you using Claude Code (uses same-thread `/loop`), Codex local automation/heartbeat, or another agent runner?""
- Do not create any scheduler until the operator confirms the agent type.

Once the agent type is confirmed:
- Claude Code: use same-thread `/loop 5m` or `/loop 20m`.
- Codex: attach a local automation/heartbeat to the current thread at 5m or ~20m recurrence.
- Other: use the agent's native same-thread recurring mechanism.
- Ask for frequency if not supplied before scheduling.

Frequency defaults:
- High-frequency (active development): 5 minutes.
- Low-frequency (background / idle polling): about 20 minutes.

Task prompt construction:
1. Run `intent-cli guide automation setup --kind child-implement [--repo <OWNER/REPO>] --format json` to obtain the full child-implement loop prompt.
2. Use the emitted `prompt` field verbatim as the task prompt.

Hard rules:
- Do not read `intents/rules/**`, local skill files, or copied prompt files. Use `intent-cli guide ...` instead.
- {DispatcherSkillCarveOut.Sentence}
- Do not call `intent-cli run`.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- Never create a cloud or new-thread scheduler for workflows that depend on local paths or local `.intent-cli` packages.
- Ask for agent type and frequency if they were not supplied before scheduling.";

        var schedulerGuidance = "Generic: confirm agent type (Claude `/loop`, Codex heartbeat, or other) and frequency before scheduling. Never cloud/new-thread for local-path workflows.";
        return (prompt, true, schedulerGuidance);
    }

    private static (string Prompt, bool ClarificationRequired, string SchedulerGuidance) BuildUnknownVariant(string repoLabel)
    {
        var prompt =
$@"Set up a local recurring loop for {repoLabel}. The agent type is not known yet.

Before scheduling anything, ask the operator:
""Are you using Claude Code (uses same-thread `/loop`), Codex local automation/heartbeat, or another agent runner?""

Do not create any cron job, scheduler, automation, or recurring wakeup until the operator confirms the agent type and the desired frequency.

Once confirmed, re-run `intent-cli guide automation local-loop --agent <claude|codex|generic> [--repo <OWNER/REPO>] --format json` to get the agent-specific setup guidance.

Hard rules:
- Do not guess the agent type from environment variables alone.
- Do not create any scheduler before the agent type is confirmed.
- Do not read `intents/rules/**`, local skill files, or copied prompt files. Use `intent-cli guide ...` instead.
- {DispatcherSkillCarveOut.Sentence}
- Never create a cloud or new-thread scheduler for workflows that depend on local paths or local `.intent-cli` packages.";

        var schedulerGuidance = "Unknown agent: ask the operator for agent type (claude, codex, or generic) and frequency before scheduling.";
        return (prompt, true, schedulerGuidance);
    }

    private static void WriteMarkdown(TextWriter writer, GuideAutomationLocalLoopResult result)
    {
        writer.WriteLine($"# Guide automation local-loop — agent: {result.Agent}");
        writer.WriteLine();
        if (!string.IsNullOrWhiteSpace(result.Repo))
        {
            writer.WriteLine($"- repo: {result.Repo}");
        }
        writer.WriteLine($"- clarification required: {result.ClarificationRequired}");
        writer.WriteLine();

        writer.WriteLine("## Scheduler guidance");
        writer.WriteLine();
        writer.WriteLine(result.SchedulerGuidance);
        writer.WriteLine();

        writer.WriteLine("## Frequency policy");
        writer.WriteLine();
        writer.WriteLine(result.FrequencyPolicy);
        writer.WriteLine();

        writer.WriteLine("## Forbidden sources");
        foreach (var src in result.ForbiddenSources)
        {
            writer.WriteLine($"- {src}");
        }
        writer.WriteLine();

        writer.WriteLine("## Worktree-friendly assumption");
        writer.WriteLine();
        writer.WriteLine(result.WorktreeFriendly);
        writer.WriteLine();

        writer.WriteLine("## Prompt");
        writer.WriteLine();
        writer.WriteLine("```text");
        writer.WriteLine(result.Prompt);
        writer.WriteLine("```");
    }

    private static bool TryParseArguments(
        string[] args,
        out string? agent,
        out string? repo,
        out string format,
        out string error)
    {
        agent = null;
        repo = null;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--agent":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--agent requires a value (claude, codex, or generic).";
                        return false;
                    }

                    var requestedAgent = args[index + 1];
                    if (!string.Equals(requestedAgent, AgentClaude, StringComparison.Ordinal)
                        && !string.Equals(requestedAgent, AgentCodex, StringComparison.Ordinal)
                        && !string.Equals(requestedAgent, AgentGeneric, StringComparison.Ordinal))
                    {
                        error = $"--agent must be 'claude', 'codex', or 'generic' (got '{requestedAgent}').";
                        return false;
                    }

                    agent = requestedAgent;
                    index++;
                    break;

                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value.";
                        return false;
                    }

                    repo = args[index + 1];
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }

                    var requestedFormat = args[index + 1];
                    if (!string.Equals(requestedFormat, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requestedFormat}').";
                        return false;
                    }

                    format = requestedFormat;
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
        writer.WriteLine("guide automation local-loop");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only agent-specific local recurring loop setup guide.");
        writer.WriteLine();
        writer.WriteLine("  --agent claude   Same-thread /loop semantics (Claude Code).");
        writer.WriteLine("  --agent codex    Local automation/heartbeat attached to current thread (Codex).");
        writer.WriteLine("  --agent generic  Provider-neutral with required operator clarification.");
        writer.WriteLine("  --agent omitted  Unknown agent: emits clarification-required guidance.");
        writer.WriteLine("  --repo           Optional. Omit to derive the repo from the current worktree.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record GuideAutomationLocalLoopResult
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("agent")]
    public required string Agent { get; init; }

    [JsonPropertyName("repo")]
    public string? Repo { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    [JsonPropertyName("scheduler_guidance")]
    public required string SchedulerGuidance { get; init; }

    [JsonPropertyName("frequency_policy")]
    public required string FrequencyPolicy { get; init; }

    [JsonPropertyName("clarification_required")]
    public required bool ClarificationRequired { get; init; }

    [JsonPropertyName("forbidden_sources")]
    public required IReadOnlyList<string> ForbiddenSources { get; init; }

    [JsonPropertyName("worktree_friendly")]
    public required string WorktreeFriendly { get; init; }
}
