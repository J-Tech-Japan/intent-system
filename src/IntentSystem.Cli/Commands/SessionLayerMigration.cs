using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G602: mode switches name operator-owned follow-up without attempting to
/// rewrite it. Residue discovery is intentionally limited to declared project
/// locations; it is advisory evidence and never a source of mode truth.
/// </summary>
internal static class SessionLayerMigration
{
    public const string ProjectHooksPath = ".codex/hooks.json";

    public const string ResidueCause = "other-mode-residue";

    public static IReadOnlyList<SessionLayerMigrationPlanItem> Plan(
        string domain,
        string? team,
        string previousMode,
        string requestedMode) =>
    [
        new()
        {
            Artifact = "other-mode-session-hooks",
            OtherMode = previousMode,
            Action = $"Review project-level session hooks in `{ProjectHooksPath}` for '{previousMode}' and manually remove or disable only the hooks that no longer belong to this team.",
        },
        new()
        {
            Artifact = "other-mode-inbox-watchers-monitors",
            OtherMode = previousMode,
            Action = $"Review inbox watchers and monitors for '{previousMode}' and stop or reconfigure only the user-managed processes that no longer belong to this team.",
        },
        new()
        {
            Artifact = "g601-visibility-marker",
            OtherMode = previousMode,
            Action = $"Regenerate the visibility marker for the recorded '{requestedMode}' mode with `intent-cli session-layer marker generate --domain {domain}"
                + (team is null ? string.Empty : $" --team {team}")
                + " --file <AGENTS.md|CLAUDE.md> --write`.",
        },
    ];

    public static IReadOnlyList<SessionLayerResidue> Discover(string repoRoot, string recordedMode)
    {
        var otherMode = string.Equals(recordedMode, SessionLayerMode.HerdrOnly, StringComparison.Ordinal)
            ? SessionLayerMode.Agmsg
            : string.Equals(recordedMode, SessionLayerMode.Agmsg, StringComparison.Ordinal)
                ? SessionLayerMode.HerdrOnly
                : null;
        if (otherMode is null)
        {
            return [];
        }

        var path = Path.Combine(repoRoot, ProjectHooksPath);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var content = File.ReadAllText(path);
            return HasSessionHookForMode(content, otherMode)
                ? [new(ProjectHooksPath, otherMode, "project-level-session-hooks")]
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // User configuration is not a record of mode truth. A file we
            // cannot read cannot support a residue conclusion, and this slice
            // must neither repair it nor turn it into a mode decision.
            return [];
        }
    }

    private static bool HasSessionHookForMode(string content, string mode) =>
        content.Contains(
            string.Equals(mode, SessionLayerMode.HerdrOnly, StringComparison.Ordinal) ? "herdr" : mode,
            StringComparison.OrdinalIgnoreCase)
        && (content.Contains("session-start", StringComparison.OrdinalIgnoreCase)
            || content.Contains("session-end", StringComparison.OrdinalIgnoreCase)
            || content.Contains("sessionstart", StringComparison.OrdinalIgnoreCase)
            || content.Contains("sessionend", StringComparison.OrdinalIgnoreCase));
}

internal sealed record SessionLayerMigrationPlanItem
{
    [JsonPropertyName("artifact")]
    public required string Artifact { get; init; }

    [JsonPropertyName("other_mode")]
    public required string OtherMode { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }
}

internal sealed record SessionLayerResidue(string Path, string OwningMode, string Artifact);
