using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G570: the session-layer transport a team's threads use to talk to each other.
///
/// Operator ruling (2026-08-01, host node 08): the session layer is SELECTABLE
/// rather than agmsg-replaced. <see cref="Agmsg"/> stays the practiced, primary
/// transport; <see cref="HerdrOnly"/> is a preview alternative for a setup where
/// every agent is herdr-resident on one machine.
///
/// The qualifier matters and is deliberately narrow: PREVIEW attaches to the
/// SESSION TRANSPORT, never to the four-thread model itself. G540 ruled that
/// model unqualified — design / orchestrator / implementation / review is the
/// primary model in BOTH transports — and this vocabulary must never be read as
/// re-qualifying it.
/// </summary>
internal static class SessionLayerMode
{
    public const string Agmsg = "agmsg";
    public const string HerdrOnly = "herdr-only";

    /// <summary>The mode in force when nothing has been recorded.</summary>
    public const string Default = Agmsg;

    public static readonly IReadOnlyList<string> All = [Agmsg, HerdrOnly];

    /// <summary>
    /// The sentence every surface that says PREVIEW must carry. It exists so a
    /// reader cannot mistake the preview qualifier for a qualifier on the
    /// four-thread model — which is exactly the confusion G540 ruled out.
    /// </summary>
    public const string PreviewScopingSentence =
        "PREVIEW here scopes the SESSION TRANSPORT only — how the four threads exchange messages. The four-thread "
        + "model itself (design / orchestrator / implementation / review) is PRIMARY and unqualified in both modes, "
        + "exactly as G540 ruled; choosing a transport never makes the model provisional.";

    /// <summary>One team runs one mode; mixed delivery is a contract violation.</summary>
    public const string ExclusivitySentence =
        "A team runs exactly ONE session-layer mode at a time. Mixing agmsg and herdr-only delivery inside one team "
        + "is a contract violation, not a fallback: two transports mean two views of who was told what.";

    public static bool IsKnown(string? mode) =>
        mode is not null && All.Contains(mode, StringComparer.Ordinal);

    /// <summary>Human-facing label, including the qualifier where one applies.</summary>
    public static string Describe(string mode) => mode switch
    {
        Agmsg => "agmsg (PRIMARY)",
        HerdrOnly => "herdr-only (PREVIEW — session transport only)",
        _ => mode,
    };
}

/// <summary>
/// G570: how a resolved mode was arrived at — a recorded selection, or the
/// default because nothing was recorded. Callers that render guidance need the
/// difference: "you chose this" and "nobody has chosen" warrant different text.
/// </summary>
internal enum SessionLayerModeSource
{
    Default,
    Recorded,
}

internal sealed record SessionLayerModeResolution
{
    public required string Mode { get; init; }

    public required SessionLayerModeSource Source { get; init; }

    /// <summary>The recorded entry this resolved to, when one exists.</summary>
    public SessionLayerModeEntry? Entry { get; init; }

    public bool IsHerdrOnly => string.Equals(Mode, SessionLayerMode.HerdrOnly, StringComparison.Ordinal);
}

internal sealed record SessionLayerModeTransition
{
    [JsonPropertyName("from")]
    public required string From { get; init; }

    [JsonPropertyName("to")]
    public required string To { get; init; }

    [JsonPropertyName("at")]
    public required DateTimeOffset At { get; init; }
}

internal sealed record SessionLayerModeEntry
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    /// <summary>Null when the selection applies to the domain as a whole.</summary>
    [JsonPropertyName("team")]
    public string? Team { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("updated_at")]
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Every transition this entry has been through, oldest first. Reversibility
    /// is a first-class property of this ruling — going back to agmsg must be as
    /// ordinary as going to herdr-only — so the record keeps the whole path
    /// rather than only the current value.
    /// </summary>
    [JsonPropertyName("transitions")]
    public required IReadOnlyList<SessionLayerModeTransition> Transitions { get; init; }
}

internal sealed record SessionLayerModeState
{
    [JsonPropertyName("schema_version")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("entries")]
    public required IReadOnlyList<SessionLayerModeEntry> Entries { get; init; }
}

/// <summary>
/// G570: durable host-side persistence for the selected mode, at
/// <c>.intent-cli/session-layer-mode.json</c>.
///
/// Written ONLY by <see cref="SessionLayerCommand"/> (G548 lineage: durable
/// state changes through canonical commands, never by hand), and read by every
/// surface that routes on the mode.
/// </summary>
internal static class SessionLayerModeStore
{
    public const string RelativePath = ".intent-cli/session-layer-mode.json";

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

    /// <summary>
    /// Reads the recorded state. Returns <see langword="null"/> when no file
    /// exists — absence is the default, not an error. Throws
    /// <see cref="InvalidOperationException"/> when the file exists but cannot
    /// be read: an unreadable record must never silently become "the default",
    /// because that would quietly move a team back onto agmsg.
    /// </summary>
    public static SessionLayerModeState? TryRead(string repoRoot)
    {
        var path = ResolvePath(repoRoot);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<SessionLayerModeState>(File.ReadAllText(path), Options)
                ?? throw new InvalidOperationException("session-layer mode state deserialized to null.");
            foreach (var entry in state.Entries)
            {
                if (!SessionLayerMode.IsKnown(entry.Mode))
                {
                    throw new InvalidOperationException(
                        $"session-layer mode state records unknown mode '{entry.Mode}' for domain '{entry.Domain}'.");
                }
            }

            return state;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new InvalidOperationException(
                $"session-layer mode state at `{path}` could not be read: {exception.Message}");
        }
    }

    /// <summary>
    /// Resolves the mode for a (domain, team) pair. A team-scoped entry wins
    /// over a domain-wide one — a team is the narrower statement — and absence
    /// resolves to <see cref="SessionLayerMode.Default"/>.
    /// </summary>
    public static SessionLayerModeResolution Resolve(string repoRoot, string domain, string? team)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        SessionLayerModeState? state;
        try
        {
            state = TryRead(repoRoot);
        }
        catch (InvalidOperationException)
        {
            // A guidance surface must still render. The command surface is what
            // reports an unreadable record loudly; here the safe reading is the
            // primary transport, which is also what the reader sees today.
            return new SessionLayerModeResolution { Mode = SessionLayerMode.Default, Source = SessionLayerModeSource.Default };
        }

        if (state is null)
        {
            return new SessionLayerModeResolution { Mode = SessionLayerMode.Default, Source = SessionLayerModeSource.Default };
        }

        var teamScoped = team is null
            ? null
            : state.Entries.FirstOrDefault(entry =>
                string.Equals(entry.Domain, domain, StringComparison.Ordinal)
                && string.Equals(entry.Team, team, StringComparison.Ordinal));

        var domainWide = state.Entries.FirstOrDefault(entry =>
            string.Equals(entry.Domain, domain, StringComparison.Ordinal) && entry.Team is null);

        var match = teamScoped ?? domainWide;
        return match is null
            ? new SessionLayerModeResolution { Mode = SessionLayerMode.Default, Source = SessionLayerModeSource.Default }
            : new SessionLayerModeResolution
            {
                Mode = match.Mode,
                Source = SessionLayerModeSource.Recorded,
                Entry = match,
            };
    }

    public static string Serialize(SessionLayerModeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonSerializer.Serialize(state, Options);
    }

    public static void Write(string repoRoot, SessionLayerModeState state)
    {
        var path = ResolvePath(repoRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Serialize(state));
    }
}
