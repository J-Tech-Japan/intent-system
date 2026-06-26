using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G488: read-only guide surface that renders a THIN, portable agent
/// skill/prompt bootstrap (ADR-013 / spec-27) for Claude/Codex-style agents.
/// One generic <c>intent-cli</c> skill body with role sections (design,
/// implement, review, orchestrator, generic). It teaches the Intent CLI mental
/// model, the thread-role model, and the fixed cwd/worktree/domain/repo/branch
/// safety boundaries, then sends the agent back to installed
/// <c>intent-cli guide</c> / <c>automation</c> for current workflow details —
/// the generated skill is explicitly NOT a workflow source of truth and must
/// not become a stale runbook. Host-state-free; performs NO filesystem writes
/// (the install plan is dry-run only); never launches an AI provider; never
/// embeds raw label edits, queue-state hand edits, hard-coded issue/PR numbers,
/// or copied full runbooks. Role-specific fixed conditions are surfaced from
/// the resolved config when available, but the reusable skill body never
/// hard-codes a machine-specific cwd path.
/// </summary>
internal static class GuideSkillPackCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide skill-pack [--domain <name>] [--target-repo <owner/repo>] [--format markdown|json]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
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

        if (!TryParseArguments(args, out var format, out var domainOverride, out var repoOverride, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var pack = BuildPack(context, domainOverride, repoOverride);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(pack, JsonOptions));
            writer.WriteLine();
            return 0;
        }

        WriteMarkdown(writer, pack);
        return 0;
    }

    private static AgentSkillPack BuildPack(CliContext context, string? domainOverride, string? repoOverride)
    {
        var project = context.Config?.Project;
        var domain = !string.IsNullOrWhiteSpace(domainOverride)
            ? domainOverride!
            : (string.IsNullOrWhiteSpace(project?.Domain) ? "<domain>" : project!.Domain);
        var repo = !string.IsNullOrWhiteSpace(repoOverride) ? repoOverride! : "<owner/repo>";

        var baseBranchPolicy = string.IsNullOrWhiteSpace(project?.BaseBranchPolicy)
            ? CliRuntimeContracts.DefaultBaseBranchPolicy
            : project!.BaseBranchPolicy;
        var implementationBaseBranch = !string.IsNullOrWhiteSpace(project?.ImplementationBaseBranch)
            ? project!.ImplementationBaseBranch
            : BaseBranchPolicyContract.ResolveExpectedBaseBranch(baseBranchPolicy);
        var metadataBranch = ResolveMetadataBranch(project);
        var sameRepoTopology = project?.SameRepoTopology == true;

        return new AgentSkillPack
        {
            SkillName = "intent-cli",
            Summary =
                "Thin, portable bootstrap for an Intent CLI agent (ADR-013 / spec-27). It teaches the mental model, "
                + "thread roles, and the fixed cwd/worktree/domain/repo/branch safety boundaries, then requires the "
                + "agent to fetch installed `intent-cli guide` / `automation` guidance before any mutation. It is a "
                + "concept primer and activation hint, NOT a workflow source of truth or a runbook copy.",
            AuthorityBoundary =
                "Installed `intent-cli guide` / `automation` output is authoritative over this skill, copied prompts, "
                + "local skill files, repository-local historical notes (`intents/rules/**`), and conversation memory. "
                + "When this skill disagrees with installed guidance, the installed guidance wins. This skill never "
                + "carries current workflow steps, label rules, or queue-state mechanics — it points at the commands "
                + "that do.",
            ConceptModel = new[]
            {
                Concept("intent tree", "The hierarchy of ends/means that records WHY work exists; host-owned design state."),
                Concept("ADR / decision", "A recorded, hard-to-reverse design decision (with rejected alternatives)."),
                Concept("packet / backlog", "A prepared unit of work (the four packet files) staged before publishing a GitHub issue."),
                Concept("GitHub issue", "The published, standalone Child Issue Contract an implementation agent acts on."),
                Concept("implementation PR", "The child PR that implements one issue and closes it with `Closes #<issue>`."),
                Concept("review", "Intent/packet-aware semantic review of a PR through intent-cli review surfaces; tests-pass is necessary, not sufficient."),
                Concept("closeout", "Merging an approved PR and recording the completion in durable host state."),
                Concept("inspect", "Evidence-backed observation of the current state before acting."),
                Concept("improve", "Design-thread reflection that catches drift the packet-time checks missed."),
                Concept("grill", "Persistent interview-mode questioning to harden an intent or decision."),
                Concept("stack", "Backlog creation: drafting/stacking prepared packets for future slices."),
            },
            FixedConditions = new[]
            {
                FixedCondition("cwd / worktree", "<cwd> / <worktree>", "Host vs child cwd decides what an agent may read/write; a child implementation cwd is GitHub-contract-only and must not read host `.intent-cli/**` / `intents/**`. Never assume a worktree; confirm it."),
                FixedCondition("domain", domain, "Selects the intent domain whose bindings, queue, and packets apply. The wrong domain resolves the wrong regex/queue."),
                FixedCondition("target repo", repo, "The GitHub repo the workflow acts on; issue/PR numbers come from `worker next-action`, never from prompt memory."),
                FixedCondition("implementation base branch", implementationBaseBranch, "The branch child PRs target. Assuming `main` when the policy says otherwise produces a wrong-based PR that review rejects."),
                FixedCondition("PR base branch", implementationBaseBranch, "Review validates a merge against the same base the implementation used; a mismatch is a fail-closed stop."),
                FixedCondition("metadata branch", metadataBranch, "In same-repo topology, host metadata lives on a separate branch; reading the wrong branch yields stale queue-state."),
                FixedCondition("same-repo topology", sameRepoTopology ? "true" : "false", "When true, code and metadata share one repo on different branches; the `[project] same_repo_topology` / `metadata_source_branch` / `metadata_write_branch` keys must be honored."),
            },
            FirstCallSequence = new[]
            {
                "intent-cli guide model --format json — confirm the chat-first / CLI-internal collaboration model.",
                "intent-cli guide onboarding --format json — the first-call sequence for a fresh agent.",
                "intent-cli guide commands list --format json — primary / support / advanced / experimental command buckets.",
                $"intent-cli automation summary --domain {domain} --format json — canonical label/contract/branch facts for the active domain.",
            },
            Roles = new[]
            {
                Role("design",
                    "Shape intents/decisions/packets through interview / clarification / packet-draft; never mutate child implementation code.",
                    "Run in the HOST repo cwd. Ask `intent-cli guide workflow task intent-interview` / `packet-draft`; record only after approval."),
                Role("implement",
                    "Implement exactly one selected issue and open a ready-for-review PR that `Closes #<issue>`.",
                    $"Run in the child implementation cwd (GitHub-contract-only). Get the issue/PR from `worker next-action --repo {repo} --github-only`; open PRs against `{implementationBaseBranch}`; all label transitions via `intent-cli worker`/`automation`."),
                Role("review",
                    "Run intent/packet-aware review + closeout for one PR through intent-cli surfaces; approve/request-update with evidence.",
                    $"Run in the HOST review cwd. Use `review closeout-plan` / `guide review` / `automation pr-transition` / `closeout pr`; perform semantic review only when you are the packet `review_role`; validate the merge against `{implementationBaseBranch}`."),
                Role("orchestrator",
                    "Coordinate implementation/review threads (optionally over agmsg) without mutating workflow state.",
                    $"See `intent-cli guide orchestrator-thread --domain {domain} --target-repo {repo}`. Send at most one delegation/repair/escalation per wake; do NOT also launch implement/review timers for the same domain/repo."),
                Role("generic",
                    "Any Intent CLI agent: orient first, then act through installed guidance.",
                    "Confirm cwd/domain/target-repo/base-branch, run the first-call sequence, then ask the specific `intent-cli guide ...` surface for the current step."),
            },
            FailClosed = new[]
            {
                "intent-cli missing from PATH or `automation doctor` reports `stale-host-cli` / a missing surface → stop; refresh the installed CLI; never fall back to raw `gh` label mutation.",
                "Wrong or unconfirmed cwd / worktree → stop with `wrong-worktree`; do not guess a path.",
                "Target repo mismatch (resolved repo ≠ requested) → stop and surface the gap; never act on the wrong repo.",
                "Base branch mismatch (PR base ≠ implementation base) → stop; do not approve/merge against the wrong branch.",
                "Conflicting mode (timer-loop AND orchestrator-message for the same domain/repo) → stop; pick one driver.",
                "Any request to hand-edit labels, queue-state, runs.jsonl, or other durable host state when an intent-cli command exists → refuse and use the command instead.",
            },
            InstallPlan = new AgentSkillInstallPlan
            {
                Mode = "dry-run",
                Note =
                    "This command writes NO files. To use the skill, copy the rendered body into your agent's personal "
                    + "skill/prompt location yourself. The reusable body intentionally contains no machine-specific cwd "
                    + "path — supply cwd/worktree per session.",
                SuggestedDestinations = new[]
                {
                    "Claude Code: a personal skill under your skills directory (the agent decides activation).",
                    "Codex / other agents: a personal prompt/instruction file the agent loads at session start.",
                },
            },
            Guardrails = new[]
            {
                "No raw gh label-mutation flags; all label transitions go through intent-cli worker/automation.",
                "No hand-editing queue-state, runs.jsonl, packets, or other host metadata.",
                "No hard-coded issue/PR numbers; they come from `worker next-action`.",
                "No provider launch commands (never ask intent-cli to launch Claude/Codex/Copilot).",
                "No copied full runbooks (`intents/rules/**`); fetch current behavior from installed guide/automation.",
            },
        };
    }

    private static string ResolveMetadataBranch(ProjectConfig? project)
    {
        if (project is null)
        {
            return "<metadata-branch>";
        }

        if (!string.IsNullOrWhiteSpace(project.MetadataSourceBranch))
        {
            return project.MetadataSourceBranch;
        }

        return string.IsNullOrWhiteSpace(project.MetadataBranch) ? "<metadata-branch>" : project.MetadataBranch;
    }

    private static AgentSkillConcept Concept(string term, string meaning) => new() { Term = term, Meaning = meaning };

    private static AgentSkillFixedCondition FixedCondition(string field, string value, string whyItMatters) =>
        new() { Field = field, Value = value, WhyItMatters = whyItMatters };

    private static AgentSkillRole Role(string name, string responsibility, string fixedConditionRequirement) =>
        new() { Name = name, Responsibility = responsibility, FixedConditionRequirement = fixedConditionRequirement };

    private static bool TryParseArguments(
        string[] args,
        out string format,
        out string? domain,
        out string? repo,
        out string error)
    {
        format = FormatMarkdown;
        domain = null;
        repo = null;
        error = string.Empty;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!RequiresValue(arg))
            {
                error = $"Unknown argument '{arg}'.";
                return false;
            }

            if (i + 1 >= args.Length)
            {
                error = $"{arg} requires a value.";
                return false;
            }

            var value = args[++i];
            switch (arg)
            {
                case "--format":
                    format = value;
                    break;
                case "--domain":
                    domain = value;
                    break;
                case "--target-repo":
                    repo = value;
                    break;
            }
        }

        if (!string.Equals(format, FormatMarkdown, StringComparison.Ordinal)
            && !string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            error = $"Unknown --format '{format}'. Supported: markdown, json.";
            return false;
        }

        return true;
    }

    private static bool RequiresValue(string arg) =>
        string.Equals(arg, "--format", StringComparison.Ordinal)
        || string.Equals(arg, "--domain", StringComparison.Ordinal)
        || string.Equals(arg, "--target-repo", StringComparison.Ordinal);

    private static void WriteMarkdown(TextWriter writer, AgentSkillPack pack)
    {
        writer.WriteLine($"# Agent skill pack — `{pack.SkillName}` (G488)");
        writer.WriteLine();
        writer.WriteLine(pack.Summary);
        writer.WriteLine();

        writer.WriteLine("## Authority boundary");
        writer.WriteLine();
        writer.WriteLine(pack.AuthorityBoundary);
        writer.WriteLine();

        writer.WriteLine("## Concept model");
        writer.WriteLine();
        foreach (var concept in pack.ConceptModel)
        {
            writer.WriteLine($"- **{concept.Term}** — {concept.Meaning}");
        }
        writer.WriteLine();

        writer.WriteLine("## Fixed conditions (safety boundaries)");
        writer.WriteLine();
        foreach (var fixedCondition in pack.FixedConditions)
        {
            writer.WriteLine($"- **{fixedCondition.Field}** = `{fixedCondition.Value}` — {fixedCondition.WhyItMatters}");
        }
        writer.WriteLine();

        writer.WriteLine("## First-call sequence (read-only)");
        writer.WriteLine();
        foreach (var call in pack.FirstCallSequence)
        {
            writer.WriteLine($"1. {call}");
        }
        writer.WriteLine();

        writer.WriteLine("## Roles");
        foreach (var role in pack.Roles)
        {
            writer.WriteLine();
            writer.WriteLine($"### {role.Name}");
            writer.WriteLine();
            writer.WriteLine($"- responsibility: {role.Responsibility}");
            writer.WriteLine($"- fixed-condition requirement: {role.FixedConditionRequirement}");
        }
        writer.WriteLine();

        writer.WriteLine("## Fail closed");
        writer.WriteLine();
        foreach (var rule in pack.FailClosed)
        {
            writer.WriteLine($"- {rule}");
        }
        writer.WriteLine();

        writer.WriteLine("## Install plan (dry-run — no files written)");
        writer.WriteLine();
        writer.WriteLine($"- mode: {pack.InstallPlan.Mode}");
        writer.WriteLine($"- note: {pack.InstallPlan.Note}");
        foreach (var destination in pack.InstallPlan.SuggestedDestinations)
        {
            writer.WriteLine($"- suggested destination: {destination}");
        }
        writer.WriteLine();

        writer.WriteLine("## Guardrails");
        writer.WriteLine();
        foreach (var guardrail in pack.Guardrails)
        {
            writer.WriteLine($"- {guardrail}");
        }
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide skill-pack");
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("Renders a thin, portable agent skill/prompt bootstrap for Claude/Codex-style agents: one");
        writer.WriteLine("generic `intent-cli` skill body with role sections. It teaches the mental model and the fixed");
        writer.WriteLine("cwd/worktree/domain/repo/branch safety boundaries, then points at installed `intent-cli guide` /");
        writer.WriteLine("`automation` as authoritative. Dry-run install plan only; writes no files; launches no provider.");
    }
}

internal sealed record AgentSkillPack
{
    [JsonPropertyName("skill_name")]
    public required string SkillName { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("authority_boundary")]
    public required string AuthorityBoundary { get; init; }

    [JsonPropertyName("concept_model")]
    public required IReadOnlyList<AgentSkillConcept> ConceptModel { get; init; }

    [JsonPropertyName("fixed_conditions")]
    public required IReadOnlyList<AgentSkillFixedCondition> FixedConditions { get; init; }

    [JsonPropertyName("first_call_sequence")]
    public required IReadOnlyList<string> FirstCallSequence { get; init; }

    [JsonPropertyName("roles")]
    public required IReadOnlyList<AgentSkillRole> Roles { get; init; }

    [JsonPropertyName("fail_closed")]
    public required IReadOnlyList<string> FailClosed { get; init; }

    [JsonPropertyName("install_plan")]
    public required AgentSkillInstallPlan InstallPlan { get; init; }

    [JsonPropertyName("guardrails")]
    public required IReadOnlyList<string> Guardrails { get; init; }
}

internal sealed record AgentSkillConcept
{
    [JsonPropertyName("term")]
    public required string Term { get; init; }

    [JsonPropertyName("meaning")]
    public required string Meaning { get; init; }
}

internal sealed record AgentSkillFixedCondition
{
    [JsonPropertyName("field")]
    public required string Field { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }

    [JsonPropertyName("why_it_matters")]
    public required string WhyItMatters { get; init; }
}

internal sealed record AgentSkillRole
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("responsibility")]
    public required string Responsibility { get; init; }

    [JsonPropertyName("fixed_condition_requirement")]
    public required string FixedConditionRequirement { get; init; }
}

internal sealed record AgentSkillInstallPlan
{
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("note")]
    public required string Note { get; init; }

    [JsonPropertyName("suggested_destinations")]
    public required IReadOnlyList<string> SuggestedDestinations { get; init; }
}
