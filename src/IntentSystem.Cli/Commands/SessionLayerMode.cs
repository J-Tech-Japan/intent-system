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

    /// <summary>
    /// Team-scoped entries in the requested domain when the caller omitted a
    /// team. They do not change resolution: they are disclosure evidence for
    /// surfaces that would otherwise make those records disappear silently.
    /// </summary>
    public IReadOnlyList<SessionLayerModeEntry> TeamScopedEntriesInDomain { get; init; } = [];

    public bool IsHerdrOnly => string.Equals(Mode, SessionLayerMode.HerdrOnly, StringComparison.Ordinal);
}

internal sealed record SessionLayerTeamCorrection
{
    [JsonPropertyName("team")]
    public required string Team { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("command")]
    public required string Command { get; init; }
}

/// <summary>
/// G585: the structured disclosure emitted when team-scoped records exist but
/// a mode-reading caller omitted --team. Resolution remains deterministic; the
/// disclosure makes its scope and the one-step correction impossible to miss.
/// </summary>
internal sealed record SessionLayerTeamOmissionDisclosure
{
    public const string MarkdownHeading = "TEAM NOT SUPPLIED — ROUTING DISCLOSURE";

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("mode_in_force")]
    public required string ModeInForce { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("corrective_commands")]
    public required IReadOnlyList<SessionLayerTeamCorrection> CorrectiveCommands { get; init; }

    public static SessionLayerTeamOmissionDisclosure? Create(
        SessionLayerModeResolution resolution,
        string domain,
        Func<SessionLayerModeEntry, string> correctiveCommand)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(correctiveCommand);

        if (resolution.TeamScopedEntriesInDomain.Count == 0)
        {
            return null;
        }

        var corrections = resolution.TeamScopedEntriesInDomain
            .Select(entry => new SessionLayerTeamCorrection
            {
                Team = entry.Team!,
                Mode = entry.Mode,
                Command = correctiveCommand(entry),
            })
            .ToArray();
        var recordedTeams = string.Join(
            ", ",
            corrections.Select(correction =>
                $"`{correction.Team}` = {SessionLayerMode.Describe(correction.Mode)}"));
        var source = resolution.Source == SessionLayerModeSource.Recorded ? "recorded" : "default";

        return new SessionLayerTeamOmissionDisclosure
        {
            Summary =
                $"`--team` was not supplied. This invocation therefore uses "
                + $"{SessionLayerMode.Describe(resolution.Mode)} ({source}). Team-scoped session-layer records also "
                + $"exist in domain `{domain}`: {recordedTeams}. Run the command for the intended team below to "
                + "correct this invocation in one step.",
            ModeInForce = resolution.Mode,
            Source = source,
            CorrectiveCommands = corrections,
        };
    }
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
            // G570 third repair: the ENVELOPE is command-produced state too.
            // Validation stopped at entries, so changing only `schema_version`
            // to something the writer never emits left the record accepted and
            // still routing — a command-impossible record that changed
            // behaviour. The writer emits exactly one schema version; anything
            // else did not come from `set --write`.
            if (!string.Equals(state.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"session-layer mode state at `{path}` declares schema_version '{state.SchemaVersion}', but "
                    + $"`session-layer set --write` only ever writes '{SchemaVersion}'. The record was not produced "
                    + "by the command, so it is refused rather than trusted.");
            }

            // G570 fourth repair: the reader now audits the ENVELOPE against
            // every invariant the writer holds, not only the three named
            // mutations. `set --write` emits entries sorted by (domain, team),
            // one per scope, each with UpdatedAt equal to its last transition
            // — so a file violating any of those is command-impossible, and a
            // command-impossible record must never change routing.
            var scopes = new HashSet<(string Domain, string? Team)>();
            foreach (var entry in state.Entries)
            {
                if (!scopes.Add((entry.Domain, entry.Team)))
                {
                    throw new InvalidOperationException(
                        $"session-layer mode state at `{path}` holds MORE THAN ONE record for domain "
                        + $"'{entry.Domain}'{(entry.Team is null ? " (domain-wide)" : $" / team '{entry.Team}'")}. "
                        + "`session-layer set --write` keeps exactly one record per scope, so duplicates are "
                        + "command-impossible — and 'the first one wins' would silently pick a mode nobody chose.");
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
                        $"session-layer mode state at `{path}` is not ordered by (domain, team). The writer always "
                        + "emits sorted entries, so an out-of-order file was not produced by "
                        + "`session-layer set --write`.");
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
    /// G570 review repair: validates one entry against the command-only
    /// persistence contract. An unknown mode, or a trail that disagrees with
    /// the current mode, means the file was hand-edited or corrupted — and a
    /// record nobody can trust must never be read as a mode.
    ///
    /// The trail is the integrity check: <c>set --write</c> appends exactly one
    /// transition per change and always leaves the last <c>to</c> equal to the
    /// entry's mode, so a mismatch is proof the record did not come from the
    /// command.
    /// </summary>
    private static void Validate(SessionLayerModeEntry entry, string path)
    {
        if (!SessionLayerMode.IsKnown(entry.Mode))
        {
            throw new InvalidOperationException(
                $"session-layer mode state at `{path}` records unknown mode '{entry.Mode}' for domain "
                + $"'{entry.Domain}'. Refusing to route on a mode nothing can act on.");
        }

        // G570 rereview repair: an EMPTY trail is not "no history" — `set
        // --write` always appends exactly one transition when it creates or
        // changes an entry, so a record with a mode but no transitions cannot
        // have come from the command. The previous version made the
        // final-target check conditional on Count > 0, which let exactly that
        // hand edit through and route every surface to a mode nothing recorded.
        if (entry.Transitions.Count == 0)
        {
            throw new InvalidOperationException(
                $"session-layer mode state at `{path}` records mode '{entry.Mode}' for domain '{entry.Domain}' with "
                + "an EMPTY transition trail. `session-layer set --write` always records the transition that created "
                + "the entry, so a trail-less record was not written by the command and is refused rather than "
                + "trusted.");
        }

        for (var index = 0; index < entry.Transitions.Count; index++)
        {
            var transition = entry.Transitions[index];
            if (!SessionLayerMode.IsKnown(transition.From) || !SessionLayerMode.IsKnown(transition.To))
            {
                throw new InvalidOperationException(
                    $"session-layer mode state at `{path}` has a transition to/from an unknown mode for domain "
                    + $"'{entry.Domain}' ('{transition.From}' → '{transition.To}').");
            }

            var expectedFrom = index == 0 ? SessionLayerMode.Default : entry.Transitions[index - 1].To;
            if (!string.Equals(transition.From, expectedFrom, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    index == 0
                        ? $"session-layer mode state at `{path}` starts domain '{entry.Domain}' at "
                            + $"'{transition.From}'. The first transition a command-written record can hold always "
                            + $"starts from the default '{SessionLayerMode.Default}', so this record was hand-edited."
                        : $"session-layer mode state at `{path}` has a broken transition chain for domain "
                            + $"'{entry.Domain}': transition {index} starts at '{transition.From}' but the previous "
                            + $"one ended at '{expectedFrom}'. Only `session-layer set --write` may write this file.");
            }
        }

        // G570 fifth repair: `set --write` is a no-op when the requested mode
        // is already recorded at that scope, so it never appends a same-mode
        // transition to an EXISTING record. (The first transition may be
        // same-mode: creating a record for the default mode legitimately
        // records agmsg → agmsg.) A later same-mode step is therefore
        // command-impossible.
        for (var index = 1; index < entry.Transitions.Count; index++)
        {
            if (string.Equals(entry.Transitions[index].From, entry.Transitions[index].To, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"session-layer mode state at `{path}` records a same-mode transition "
                    + $"('{entry.Transitions[index].From}' → '{entry.Transitions[index].To}') at position {index} for "
                    + $"domain '{entry.Domain}'. `session-layer set --write` is a no-op when the mode is already "
                    + "recorded, so it never appends one — this record was hand-edited.");
            }

            if (entry.Transitions[index].At < entry.Transitions[index - 1].At)
            {
                throw new InvalidOperationException(
                    $"session-layer mode state at `{path}` has transitions going backwards in time for domain "
                    + $"'{entry.Domain}'. The writer appends in order, so this record was hand-edited.");
            }
        }

        if (entry.UpdatedAt != entry.Transitions[^1].At)
        {
            throw new InvalidOperationException(
                $"session-layer mode state at `{path}` records updated_at {entry.UpdatedAt:O} for domain "
                + $"'{entry.Domain}' but its last transition happened at {entry.Transitions[^1].At:O}. The writer "
                + "stamps both from the same instant, so they cannot legitimately differ.");
        }

        if (!string.Equals(entry.Mode, entry.Transitions[^1].To, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"session-layer mode state at `{path}` records mode '{entry.Mode}' for domain '{entry.Domain}' but "
                + $"its last transition ended at '{entry.Transitions[^1].To}'. The record was not written by "
                + "`session-layer set --write`, so it is refused rather than trusted.");
        }
    }

    /// <summary>
    /// Resolves the mode for a (domain, team) pair. A team-scoped entry wins
    /// over a domain-wide one — a team is the narrower statement — and ABSENCE
    /// resolves to <see cref="SessionLayerMode.Default"/>.
    ///
    /// G570 review repair: an INVALID present record is not absence. This used
    /// to catch the read failure and return agmsg so guidance would always
    /// render — which meant a corrupted or hand-edited record silently routed
    /// every guide and setup surface through the wrong transport. It now
    /// throws, and every mode-dependent surface fails closed with a named
    /// error instead of rendering guidance for a mode nobody chose.
    /// </summary>
    public static SessionLayerModeResolution Resolve(string repoRoot, string domain, string? team)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        var state = TryRead(repoRoot);

        return Resolve(state, domain, team);
    }

    /// <summary>
    /// Resolves from an already validated snapshot. G594's shared preflight
    /// reads the record once, then gives every consumer the same verdict and
    /// resolution without re-reading or independently defaulting.
    /// </summary>
    internal static SessionLayerModeResolution Resolve(
        SessionLayerModeState? state,
        string domain,
        string? team)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        if (state is null)
        {
            return new SessionLayerModeResolution { Mode = SessionLayerMode.Default, Source = SessionLayerModeSource.Default };
        }

        var teamScopedEntriesInDomain = team is null
            ? state.Entries
                .Where(entry =>
                    string.Equals(entry.Domain, domain, StringComparison.Ordinal)
                    && entry.Team is not null)
                .OrderBy(entry => entry.Team, StringComparer.Ordinal)
                .ToArray()
            : [];

        var teamScoped = team is null
            ? null
            : state.Entries.FirstOrDefault(entry =>
                string.Equals(entry.Domain, domain, StringComparison.Ordinal)
                && string.Equals(entry.Team, team, StringComparison.Ordinal));

        var domainWide = state.Entries.FirstOrDefault(entry =>
            string.Equals(entry.Domain, domain, StringComparison.Ordinal) && entry.Team is null);

        var match = teamScoped ?? domainWide;
        return match is null
            ? new SessionLayerModeResolution
            {
                Mode = SessionLayerMode.Default,
                Source = SessionLayerModeSource.Default,
                TeamScopedEntriesInDomain = teamScopedEntriesInDomain,
            }
            : new SessionLayerModeResolution
            {
                Mode = match.Mode,
                Source = SessionLayerModeSource.Recorded,
                Entry = match,
                TeamScopedEntriesInDomain = teamScopedEntriesInDomain,
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
