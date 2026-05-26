using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G393: Read-only <c>intent-cli guide start</c> — the single, obvious
/// guide-first entrypoint. Any agent family (Codex, Claude, Copilot, Cursor,
/// OpenCode, Antigravity, …) runs this one command first to discover which
/// <c>intent-cli guide …</c> command to use for the phase of work it is about
/// to start, learn the guide-first rule, and see the host/design vs.
/// child-implementation role split. Detailed, drift-prone workflow rules are
/// intentionally NOT duplicated here — they live behind the per-phase guide
/// commands this entrypoint points at. Also emits short, ready-to-paste
/// AGENTS.md / CLAUDE.md snippets so a repository can carry the same
/// "ask intent-cli first" rule without copying a full workflow spec. Never
/// mutates state; never launches an AI provider.
/// </summary>
internal static class GuideStartCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide start [--format markdown|json]";

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

    internal static GuideStartResult BuildResult()
    {
        return new GuideStartResult
        {
            Summary =
                "Guide-first entrypoint. Before doing intent / packet / issue / review / loop work, ask intent-cli "
                + "which guide command to run — do not start from memory, copied prompts, repo files, or ordinary "
                + "GitHub behavior. intent-cli is the workflow and metadata authority.",
            GuideFirstRule = new[]
            {
                "Ask intent-cli first: run the per-phase `intent-cli guide …` command below before acting.",
                "Use intent-cli-supported commands for transitions; do not hand-edit queue-state, workflow labels, "
                    + "packet/publish metadata, or other host artifacts when an `intent-cli automation` / "
                    + "`intent-cli worker` command owns that transition.",
                "Detailed, drift-prone rules stay in intent-cli guidance output — not copied into repo files or "
                    + "agent prompts. Re-ask the guide command when you resume work; it reflects the installed CLI.",
                "You can ask an AI agent (Claude, Codex, Copilot, etc.) to run intent-cli commands on your behalf "
                    + "— intent-cli is deterministic, provider-free tooling the agent invokes internally; it does not "
                    + "launch AI providers itself. `intent-cli run` is not the production orchestrator.",
            },
            WorkflowPhases = new[]
            {
                new GuideStartPhase
                {
                    Phase = "design-and-intent",
                    When = "Capturing intent, interviewing the owner, clarifying scope before any packet/issue.",
                    GuideCommand = "intent-cli guide workflow task intent-interview --format json",
                    Side = "host/design",
                    Note = "Free-form design chats most often skip intent-cli — ask here first.",
                },
                new GuideStartPhase
                {
                    Phase = "packet-draft",
                    When = "Scaffolding the canonical packet (packet.yaml / implementation.md / review-context.md / github-body.md).",
                    GuideCommand = "intent-cli guide workflow task packet-draft --format json",
                    Side = "host/design",
                    Note = "Packet authoring is host/design work; the child loop never hand-writes packets.",
                },
                new GuideStartPhase
                {
                    Phase = "issue-publish",
                    When = "Publishing a reviewed Standalone Child Issue Contract to GitHub.",
                    GuideCommand = "intent-cli guide workflow task issue-publish --format json",
                    Side = "host/design",
                    Note = "`intent-target` is applied by the publish boundary command, never by hand.",
                },
                new GuideStartPhase
                {
                    Phase = "implementation-loop",
                    When = "Turning an intent-target issue into a PR, or repairing a PR from review feedback.",
                    GuideCommand = "intent-cli guide oneshot --kind child-implement-or-update --repo <owner>/<repo>",
                    Side = "child-implementation",
                    Note = "GitHub-contract-only & metadata-free: issue/PR + repo code are the source of truth.",
                },
                new GuideStartPhase
                {
                    Phase = "review-next-slice-loop",
                    When = "Reviewing PRs against the packet/intent contract, approving/merging, and cutting the next slice.",
                    GuideCommand = "intent-cli guide oneshot --kind host-review-next-slice --repo <owner>/<repo>",
                    Side = "host/design",
                    Note = "Host-side review owns label transitions via `intent-cli automation`.",
                },
                new GuideStartPhase
                {
                    Phase = "recovery",
                    When = "A loop looks wrong, stuck, or you are unsure whether a fix is in scope.",
                    GuideCommand = "intent-cli worker pr-comment-preflight --repo <owner>/<repo> --pr <n> --format json  # or issue-preflight / automation doctor",
                    Side = "either",
                    Note = "Ask the read-only preflight/doctor surfaces to classify a safe repair instead of hand-fixing state.",
                },
            },
            AgentRoles = new[]
            {
                new GuideStartRole
                {
                    Role = "child-implementation",
                    SourceOfTruth = "the GitHub issue/PR + repo-local code",
                    Rule =
                        "Implementation-side agents are GitHub-contract-only and metadata-free: they MUST NOT read or "
                        + "mutate host `.intent-cli/`, queue-state, metadata branches, or `intents/**`. Treat the issue "
                        + "body as the standalone contract and record outcomes via `intent-cli worker`.",
                },
                new GuideStartRole
                {
                    Role = "host/design",
                    SourceOfTruth = "parent host `.intent-cli/` state + intent tree",
                    Rule =
                        "Host/design-side agents may operate on metadata, but MUST ask intent-cli for the current "
                        + "command/guidance first and prefer intent-cli-supported transitions before hand-editing "
                        + "metadata or labels.",
                },
            },
            OnboardingGuidance = new GuideStartOnboarding
            {
                InstallAndVerify =
                    "Install the `intent-cli` .NET tool, then verify with `intent-cli --version` and "
                    + "`intent-cli automation doctor --format json` (reports CLI freshness / host-state). SDK and tool "
                    + "install steps for macOS / Windows / Linux: `docs/en/01-install.md` (`docs/ja/01-install.md`).",
                ProjectStart =
                    "Stand up a new host project / domain with `intent-cli guide workflow task init-host --format json`; "
                    + "for the zero-local-rules first-call sequence run `intent-cli guide onboarding --format json`.",
                AskIntentCliFirst =
                    "After install, ask intent-cli for each next step — either by pasting a design-thread prompt "
                    + "to an AI agent (Claude, Codex, Copilot, etc.) that runs intent-cli internally, or by running "
                    + "`intent-cli guide start` yourself. Either way: re-ask the per-phase guide command when you resume; "
                    + "it reflects the installed CLI, not memory or copied prompts.",
            },
            AskIntentCliTemplates = new[]
            {
                new GuideStartTemplate
                {
                    Name = "implementation-loop",
                    Side = "child-implementation",
                    When = "Run a recurring loop that turns intent-target issues into PRs and applies PR-comment fixes.",
                    Template =
                        "Run the intent-cli child implementation loop for `<owner>/<repo>` (domain `<domain>`, implementation "
                        + "PR base `<base-branch>`) using agent `<agent>` at `<frequency>` cadence, from child worktree cwd "
                        + "`<cwd>`. Each wake, process at most one action:\n"
                        + "1. Confirm `<cwd>` is the worktree root and the repo is `<owner>/<repo>`; `git fetch --all --prune`.\n"
                        + "2. Run `intent-cli guide oneshot --kind child-implement-or-update --repo <owner>/<repo>` and "
                        + "follow it verbatim.\n"
                        + "3. Choose work ONLY via `intent-cli worker next-action --repo <owner>/<repo> --github-only "
                        + "--format json`; start implementation from `<base-branch>`; make every workflow-label transition "
                        + "through `intent-cli worker` / `intent-cli automation`, never raw `gh` label edits.\n"
                        + "4. Open PRs ready-for-review (non-draft) by DEFAULT; only pass `--draft` when the operator "
                        + "explicitly requests it, and report the actual draft state via `worker result-summary "
                        + "--pr-draft true|false`.\n"
                        + "Child implementation agents are GitHub-contract-only and do NOT need host metadata: the issue/PR "
                        + "body and repo-local code are the only source of truth; never read or mutate host `.intent-cli/`, "
                        + "queue-state, or `intents/**`.",
                },
                new GuideStartTemplate
                {
                    Name = "review-next-slice-loop",
                    Side = "host/design",
                    When = "Run a recurring loop that reviews PRs against the contract, approves/merges, and cuts the next slice.",
                    Template =
                        "Run the intent-cli host review / next-slice loop for `<owner>/<repo>` (domain `<domain>`, "
                        + "implementation PR base `<base-branch>`) using agent `<agent>` at `<frequency>` cadence, from host "
                        + "worktree cwd `<cwd>`. Each wake, process at most one action:\n"
                        + "1. Run `intent-cli guide oneshot --kind host-review-next-slice --repo <owner>/<repo>` and follow "
                        + "it verbatim.\n"
                        + "2. Review the open PR against the packet/intent contract with `intent-cli guide review --pr <n> "
                        + "--repo <owner>/<repo> --format json`.\n"
                        + "3. Apply review label transitions ONLY via `intent-cli automation pr-transition`; approve on "
                        + "packet/intent evidence (green tests are necessary but not sufficient), then cut the next slice "
                        + "once merged.\n"
                        + "4. Draft-aware review: a selected draft PR is still review-eligible — draft state alone is NOT an "
                        + "operator stop and NOT a reason to skip intent/packet-aware review. Review eligibility is distinct "
                        + "from merge eligibility: run the review now, then ready the PR via the sanctioned path before merge "
                        + "(or request-update / surface the gap); never report a normal hold solely because the PR is draft.\n"
                        + "Host/review agents may touch metadata, but ask intent-cli for the current supported transition "
                        + "before hand-editing labels or queue-state.",
                },
            },
            MultiAgentNote =
                "Every agent family — Codex, Claude, Copilot, Cursor, OpenCode, Antigravity, and other local/host "
                + "agents — uses this same `intent-cli guide start` entrypoint. Repositories carry only a short "
                + "guide-first rule (see the snippets below); they do not embed a full workflow spec that can drift. "
                + "Fill the `<domain>` / `<agent>` / `<frequency>` / `<cwd>` / `<owner>/<repo>` / `<base-branch>` "
                + "placeholders in the ask-intent-cli templates from your local bindings before scheduling a loop.",
            RepositoryInstructionSnippets = new GuideStartSnippets
            {
                AgentsMd =
                    "## intent-cli is the workflow authority\n\n"
                    + "Before any intent / packet / issue / review / implementation-loop work, run "
                    + "`intent-cli guide start` and follow the per-phase `intent-cli guide …` command it points at. "
                    + "Do not start from memory or copied prompts. Use intent-cli-supported commands for label/metadata "
                    + "transitions — never hand-edit them. Implementation agents are GitHub-contract-only (issue/PR + "
                    + "repo code; no host metadata).",
                ClaudeMd =
                    "## Ask intent-cli first\n\n"
                    + "This repo's workflow is driven by `intent-cli`. Run `intent-cli guide start` to discover the "
                    + "right `intent-cli guide …` command for your current phase, then follow it. Don't guess "
                    + "metadata/label behavior or copy long rules here — intent-cli guidance is the source of truth, "
                    + "and implementation work stays GitHub-contract-only (no host `.intent-cli` metadata).",
            },
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideStartResult result)
    {
        writer.WriteLine("# Guide start — ask intent-cli first");
        writer.WriteLine();
        writer.WriteLine(result.Summary);
        writer.WriteLine();

        writer.WriteLine("## Guide-first rule");
        foreach (var rule in result.GuideFirstRule)
        {
            writer.WriteLine($"- {rule}");
        }
        writer.WriteLine();

        writer.WriteLine("## Which guide command for which phase");
        writer.WriteLine();
        foreach (var phase in result.WorkflowPhases)
        {
            writer.WriteLine($"### {phase.Phase} ({phase.Side})");
            writer.WriteLine($"- when: {phase.When}");
            writer.WriteLine($"- run: `{phase.GuideCommand}`");
            writer.WriteLine($"- note: {phase.Note}");
            writer.WriteLine();
        }

        writer.WriteLine("## Agent roles");
        foreach (var role in result.AgentRoles)
        {
            writer.WriteLine($"### {role.Role}");
            writer.WriteLine($"- source of truth: {role.SourceOfTruth}");
            writer.WriteLine($"- rule: {role.Rule}");
            writer.WriteLine();
        }

        writer.WriteLine("## Install & onboarding");
        writer.WriteLine($"- install / verify: {result.OnboardingGuidance.InstallAndVerify}");
        writer.WriteLine($"- project start: {result.OnboardingGuidance.ProjectStart}");
        writer.WriteLine($"- ask intent-cli first: {result.OnboardingGuidance.AskIntentCliFirst}");
        writer.WriteLine();

        writer.WriteLine("## Ask-intent-cli loop prompt templates");
        writer.WriteLine();
        writer.WriteLine("Fill the `<domain>` / `<agent>` / `<frequency>` / `<cwd>` / `<owner>/<repo>` placeholders before scheduling.");
        writer.WriteLine();
        foreach (var template in result.AskIntentCliTemplates)
        {
            writer.WriteLine($"### {template.Name} ({template.Side})");
            writer.WriteLine($"- when: {template.When}");
            writer.WriteLine();
            writer.WriteLine("```text");
            writer.WriteLine(template.Template);
            writer.WriteLine("```");
            writer.WriteLine();
        }

        writer.WriteLine("## Works the same across agents");
        writer.WriteLine(result.MultiAgentNote);
        writer.WriteLine();

        writer.WriteLine("## Repository instruction snippets");
        writer.WriteLine();
        writer.WriteLine("Paste into `AGENTS.md`:");
        writer.WriteLine();
        writer.WriteLine("```markdown");
        writer.WriteLine(result.RepositoryInstructionSnippets.AgentsMd);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine("Paste into `CLAUDE.md`:");
        writer.WriteLine();
        writer.WriteLine("```markdown");
        writer.WriteLine(result.RepositoryInstructionSnippets.ClaudeMd);
        writer.WriteLine("```");
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
        writer.WriteLine("guide start");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Guide-first entrypoint: the single command to run before intent/packet/issue/review/loop work; points at the per-phase guide command and emits AGENTS.md/CLAUDE.md snippets.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };
}

internal sealed record GuideStartResult
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("guide_first_rule")]
    public required IReadOnlyList<string> GuideFirstRule { get; init; }

    [JsonPropertyName("workflow_phases")]
    public required IReadOnlyList<GuideStartPhase> WorkflowPhases { get; init; }

    [JsonPropertyName("agent_roles")]
    public required IReadOnlyList<GuideStartRole> AgentRoles { get; init; }

    [JsonPropertyName("onboarding_guidance")]
    public required GuideStartOnboarding OnboardingGuidance { get; init; }

    [JsonPropertyName("ask_intent_cli_templates")]
    public required IReadOnlyList<GuideStartTemplate> AskIntentCliTemplates { get; init; }

    [JsonPropertyName("multi_agent_note")]
    public required string MultiAgentNote { get; init; }

    [JsonPropertyName("repository_instruction_snippets")]
    public required GuideStartSnippets RepositoryInstructionSnippets { get; init; }
}

internal sealed record GuideStartPhase
{
    [JsonPropertyName("phase")]
    public required string Phase { get; init; }

    [JsonPropertyName("when")]
    public required string When { get; init; }

    [JsonPropertyName("guide_command")]
    public required string GuideCommand { get; init; }

    [JsonPropertyName("side")]
    public required string Side { get; init; }

    [JsonPropertyName("note")]
    public required string Note { get; init; }
}

internal sealed record GuideStartRole
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("source_of_truth")]
    public required string SourceOfTruth { get; init; }

    [JsonPropertyName("rule")]
    public required string Rule { get; init; }
}

internal sealed record GuideStartSnippets
{
    [JsonPropertyName("agents_md")]
    public required string AgentsMd { get; init; }

    [JsonPropertyName("claude_md")]
    public required string ClaudeMd { get; init; }
}

internal sealed record GuideStartOnboarding
{
    [JsonPropertyName("install_and_verify")]
    public required string InstallAndVerify { get; init; }

    [JsonPropertyName("project_start")]
    public required string ProjectStart { get; init; }

    [JsonPropertyName("ask_intent_cli_first")]
    public required string AskIntentCliFirst { get; init; }
}

internal sealed record GuideStartTemplate
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("side")]
    public required string Side { get; init; }

    [JsonPropertyName("when")]
    public required string When { get; init; }

    [JsonPropertyName("template")]
    public required string Template { get; init; }
}
