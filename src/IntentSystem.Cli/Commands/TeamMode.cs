using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G691: the durable team shape. Team mode is deliberately independent from
/// the session-layer transport: delivery remains the default and keeps the
/// existing four-thread/supervision contract, while authoring-only is the
/// zero-herdr front-door shape for teams that publish issues without running
/// delivery seats locally.
/// </summary>
internal static class TeamMode
{
    public const string Delivery = "delivery";
    public const string AuthoringOnly = "authoring-only";
    public const string Default = Delivery;

    public static readonly IReadOnlyList<string> All = [Delivery, AuthoringOnly];

    public static bool IsKnown(string? mode) =>
        mode is not null && All.Contains(mode, StringComparer.Ordinal);

    public static bool IsAuthoringOnly(string? mode) =>
        string.Equals(mode, AuthoringOnly, StringComparison.Ordinal);
}

internal enum TeamModeSource
{
    Default,
    Recorded,
}

internal sealed record TeamModeResolution
{
    public required string Mode { get; init; }
    public required TeamModeSource Source { get; init; }
    public TeamModeEntry? Entry { get; init; }

    public bool IsAuthoringOnly => TeamMode.IsAuthoringOnly(Mode);
}

internal sealed record TeamModeTransition
{
    [JsonPropertyName("from")]
    public required string From { get; init; }

    [JsonPropertyName("to")]
    public required string To { get; init; }

    [JsonPropertyName("at")]
    public required DateTimeOffset At { get; init; }
}

internal sealed record TeamModeEntry
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("team")]
    public string? Team { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("updated_at")]
    public required DateTimeOffset UpdatedAt { get; init; }

    [JsonPropertyName("transitions")]
    public required IReadOnlyList<TeamModeTransition> Transitions { get; init; }
}

internal sealed record TeamModeState
{
    [JsonPropertyName("schema_version")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("entries")]
    public required IReadOnlyList<TeamModeEntry> Entries { get; init; }
}

/// <summary>
/// Command-produced state only. An unreadable, malformed, duplicated, or
/// out-of-order record fails closed instead of silently reverting to delivery.
/// </summary>
internal static class TeamModeStore
{
    public const string RelativePath = ".intent-cli/team-mode.json";
    public const string SchemaVersion = "1";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ResolvePath(string repoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        return Path.GetFullPath(Path.Combine(repoRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    public static TeamModeState? TryRead(string repoRoot)
    {
        var path = ResolvePath(repoRoot);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<TeamModeState>(File.ReadAllText(path), Options)
                ?? throw new InvalidOperationException("team mode state deserialized to null.");
            if (!string.Equals(state.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"team mode state at `{path}` declares schema_version '{state.SchemaVersion}', but "
                    + $"`team-mode set --write` only writes '{SchemaVersion}'.");
            }

            var scopes = new HashSet<(string Domain, string? Team)>();
            foreach (var entry in state.Entries)
            {
                if (!scopes.Add((entry.Domain, entry.Team)))
                {
                    throw new InvalidOperationException(
                        $"team mode state at `{path}` contains duplicate scope for domain '{entry.Domain}'"
                        + (entry.Team is null ? " (domain-wide)." : $" / team '{entry.Team}'."));
                }

                Validate(entry, path);
            }

            var sorted = state.Entries
                .OrderBy(entry => entry.Domain, StringComparer.Ordinal)
                .ThenBy(entry => entry.Team ?? string.Empty, StringComparer.Ordinal)
                .ToArray();
            for (var index = 0; index < sorted.Length; index++)
            {
                if (!ReferenceEquals(sorted[index], state.Entries[index]))
                {
                    throw new InvalidOperationException(
                        $"team mode state at `{path}` is not ordered by (domain, team); refusing a record "
                        + "that could not have been emitted by `team-mode set --write`.");
                }
            }

            return state;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new InvalidOperationException(
                $"team mode state at `{path}` could not be read: {exception.Message}");
        }
    }

    public static TeamModeResolution Resolve(string repoRoot, string domain, string? team)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        return Resolve(TryRead(repoRoot), domain, team);
    }

    internal static TeamModeResolution Resolve(TeamModeState? state, string domain, string? team)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        if (state is null)
        {
            return new TeamModeResolution { Mode = TeamMode.Default, Source = TeamModeSource.Default };
        }

        var entry = team is null
            ? state.Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.Domain, domain, StringComparison.Ordinal) && candidate.Team is null)
            : state.Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.Domain, domain, StringComparison.Ordinal)
                && string.Equals(candidate.Team, team, StringComparison.Ordinal))
                ?? state.Entries.FirstOrDefault(candidate =>
                    string.Equals(candidate.Domain, domain, StringComparison.Ordinal) && candidate.Team is null);

        return entry is null
            ? new TeamModeResolution { Mode = TeamMode.Default, Source = TeamModeSource.Default }
            : new TeamModeResolution { Mode = entry.Mode, Source = TeamModeSource.Recorded, Entry = entry };
    }

    public static string Serialize(TeamModeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonSerializer.Serialize(state, Options);
    }

    public static void Write(string repoRoot, TeamModeState state)
    {
        var path = ResolvePath(repoRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Serialize(state));
    }

    private static void Validate(TeamModeEntry entry, string path)
    {
        if (string.IsNullOrWhiteSpace(entry.Domain))
        {
            throw new InvalidOperationException($"team mode state at `{path}` contains an entry with no domain.");
        }

        if (entry.Team is not null && string.IsNullOrWhiteSpace(entry.Team))
        {
            throw new InvalidOperationException($"team mode state at `{path}` contains a blank team scope.");
        }

        if (!TeamMode.IsKnown(entry.Mode))
        {
            throw new InvalidOperationException(
                $"team mode state at `{path}` records unknown mode '{entry.Mode}' for domain '{entry.Domain}'.");
        }

        if (entry.Transitions.Count == 0)
        {
            throw new InvalidOperationException(
                $"team mode state at `{path}` records '{entry.Mode}' without a transition trail; refusing it.");
        }

        for (var index = 0; index < entry.Transitions.Count; index++)
        {
            var transition = entry.Transitions[index];
            if (!TeamMode.IsKnown(transition.From) || !TeamMode.IsKnown(transition.To))
            {
                throw new InvalidOperationException(
                    $"team mode state at `{path}` has an unknown transition for domain '{entry.Domain}'.");
            }

            var expectedFrom = index == 0 ? TeamMode.Default : entry.Transitions[index - 1].To;
            if (!string.Equals(transition.From, expectedFrom, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"team mode state at `{path}` has a broken transition chain for domain '{entry.Domain}'.");
            }

            if (index > 0)
            {
                if (string.Equals(transition.From, transition.To, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"team mode state at `{path}` contains a repeated mode transition for domain '{entry.Domain}'.");
                }

                if (transition.At < entry.Transitions[index - 1].At)
                {
                    throw new InvalidOperationException(
                        $"team mode state at `{path}` contains transitions going backwards in time.");
                }
            }
        }

        if (entry.UpdatedAt != entry.Transitions[^1].At || !string.Equals(entry.Mode, entry.Transitions[^1].To, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"team mode state at `{path}` disagrees with its last transition; refusing a hand-edited record.");
        }
    }
}
