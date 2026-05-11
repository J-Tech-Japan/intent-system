using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G323: structured prohibition surfaced by every generated guidance
/// contract (setup contracts and one-shot task planners). Pairs a
/// stable identifier with a human-readable description so controllers
/// can dispatch on the <c>id</c> without reading prose, while operators
/// reading the prompt see the same enforcement.
/// </summary>
internal sealed record GuidanceProhibition
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }
}

/// <summary>
/// G323: single source of truth for the prohibitions every generated
/// guidance contract MUST advertise. Centralizing the catalog means
/// <see cref="GuideAutomationSetupCommand"/>, <see cref="TaskCommand"/>,
/// and any future planner produce the same structured prohibition list
/// with the same canonical IDs — tests can assert the IDs exist on each
/// output and the lint surface
/// (<see cref="GuideAutomationContractLinter"/>) can keep its prose
/// patterns aligned with the structured IDs.
/// </summary>
internal static class GuidanceProhibitionCatalog
{
    public const string LocalRulesFallbackForbidden = "local-rules-fallback-forbidden";
    public const string SkillFallbackForbidden = "skill-fallback-forbidden";
    public const string CopiedPromptForbidden = "copied-prompt-forbidden";
    public const string StaleCliFallbackForbidden = "stale-cli-fallback-forbidden";
    public const string MissingCommandSurfaceForbidden = "missing-command-surface-forbidden";
    public const string DotnetRunFallbackForbidden = "dotnet-run-fallback-forbidden";
    public const string RawGhLabelMutationForbidden = "raw-gh-label-mutation-forbidden";
    public const string ProviderLaunchForbidden = "provider-launch-forbidden";
    public const string IntentCliRunForbidden = "intent-cli-run-forbidden";
    public const string StaleMemoryFallbackForbidden = "stale-memory-fallback-forbidden";

    /// <summary>
    /// G323: canonical prohibition list for setup contracts AND task
    /// planners. Order is the order operators see in markdown rendering;
    /// JSON output mirrors the same order for stable diffs.
    /// </summary>
    public static IReadOnlyList<GuidanceProhibition> All { get; } = new GuidanceProhibition[]
    {
        new()
        {
            Id = LocalRulesFallbackForbidden,
            Description = "Do not read `intents/rules/**` for routine collaboration; the installed `intent-cli guide` is the canonical guidance source."
        },
        new()
        {
            Id = SkillFallbackForbidden,
            Description = "Do not dispatch local skill files (`gh-issue-to-pr`, `gh-fix-pr-comment`, etc.) for routine work; use `intent-cli` worker / automation commands instead."
        },
        new()
        {
            Id = CopiedPromptForbidden,
            Description = "Do not consume copied prompt files for routine operation; regenerate via `intent-cli guide automation setup` to capture current contract text."
        },
        new()
        {
            Id = StaleMemoryFallbackForbidden,
            Description = "Do not rely on stale model memory or prior conversation patterns when installed `intent-cli` guidance is available; re-run `guide model` / `guide onboarding` / `automation summary` to refresh the current contract."
        },
        new()
        {
            Id = StaleCliFallbackForbidden,
            Description = "If `intent-cli automation doctor` reports `stale-host-cli`, abort the wake before any mutation and refresh the installed CLI; never fall back to direct DLL invocation."
        },
        new()
        {
            Id = MissingCommandSurfaceForbidden,
            Description = "If a required automation command surface is missing per `automation doctor`, abort and surface the gap; do not silently fall back to ordinary GitHub review or raw label mutation."
        },
        new()
        {
            Id = DotnetRunFallbackForbidden,
            Description = "Do not run `dotnet run` as a fallback for `intent-cli`; the installed CLI on `PATH` (or the cwd-local shim / explicit override) is the only supported invocation surface."
        },
        new()
        {
            Id = RawGhLabelMutationForbidden,
            Description = "Do not perform raw `gh ... edit --add-label` / `--remove-label` workflow-label mutations; route every workflow label transition through `intent-cli automation` / `intent-cli worker` commands."
        },
        new()
        {
            Id = ProviderLaunchForbidden,
            Description = "Do not ask `intent-cli` to launch Claude/Codex or any AI provider; this CLI is text-only and never spawns model providers."
        },
        new()
        {
            Id = IntentCliRunForbidden,
            Description = "Do not call `intent-cli run` from chat-first loops; `run` is advanced runtime (integration smoke / replay / dogfooding) and is not part of the routine collaboration path."
        }
    };
}
