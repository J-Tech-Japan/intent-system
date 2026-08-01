using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G570: <c>intent-cli session-layer show|set</c> — the canonical surface for
/// the session-layer transport a team runs.
///
/// The operator ruling (2026-08-01, host node 08) is that this is a CHOICE, not
/// a migration: a setup can ask for herdr-only at first contact, have that
/// remembered, and come back to agmsg at will. So the surface is deliberately
/// ordinary — show and set, per domain, team-scoped where teams are modeled,
/// with the whole transition path kept so "we tried it and went back" is a
/// readable fact rather than an absence.
///
/// <c>set</c> is <c>--dry-run</c> by default and idempotent: re-recording the
/// mode already in force changes nothing and records no transition, so a setup
/// script can assert the mode without accumulating noise in the trail.
/// </summary>
internal static class SessionLayerCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    /// <summary>
    /// G570: the guide section a switch must be followed by. Its CONTENT ships
    /// in G571 — this slice makes the mode exist, persist, and route — so the
    /// pointer names the section without claiming the content is already there.
    /// </summary>
    public const string SwitchChecklistSection =
        "`intent-cli guide orchestrator-thread --domain <domain> --target-repo <owner/repo> --agent <agent>` → "
        + "\"Session-layer switch checklist\" (herdr-only operating content ships in G571)";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const string ShowUsage =
        "Usage: intent-cli session-layer show --domain <name> [--team <name>] [--format markdown|json]";

    private const string SetUsage =
        "Usage: intent-cli session-layer set --domain <name> [--team <name>] --mode agmsg|herdr-only [--dry-run|--write] [--format markdown|json]";

    /// <summary>Test seam: deterministic transition timestamps.</summary>
    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    public static int ExecuteShow(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(ShowUsage);
            return 0;
        }

        if (!TryParseArguments(args, requireMode: false, out var domain, out var team, out _, out _, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(ShowUsage);
            return 1;
        }

        SessionLayerModeResolution resolution;
        try
        {
            // Deliberately NOT the tolerant Resolve() the guide surfaces use: the
            // command exists to tell the operator the truth about the record, so
            // an unreadable one is an error here even though guidance still
            // renders under the default.
            var state = SessionLayerModeStore.TryRead(context.RepoRoot);
            resolution = SessionLayerModeStore.Resolve(context.RepoRoot, domain!, team);
            _ = state;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }

        Emit(writer, format, BuildResult(domain!, team, resolution, mode: "show", applied: false, changed: false));
        return 0;
    }

    public static int ExecuteSet(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(SetUsage);
            return 0;
        }

        if (!TryParseArguments(args, requireMode: true, out var domain, out var team, out var requested, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(SetUsage);
            return 1;
        }

        SessionLayerModeState? state;
        try
        {
            state = SessionLayerModeStore.TryRead(context.RepoRoot);
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine($"{exception.Message} Refusing to overwrite a record this command cannot read.");
            return 1;
        }

        var before = SessionLayerModeStore.Resolve(context.RepoRoot, domain!, team);
        var entries = state?.Entries.ToList() ?? [];
        var existingIndex = entries.FindIndex(entry =>
            string.Equals(entry.Domain, domain, StringComparison.Ordinal)
            && string.Equals(entry.Team, team, StringComparison.Ordinal));
        var existing = existingIndex >= 0 ? entries[existingIndex] : null;

        // Idempotent: the mode already recorded at THIS scope is a no-op. Note
        // this compares the entry at the same scope, not the resolved mode — a
        // team-scoped set that happens to match the domain-wide mode is still a
        // real, recordable narrowing.
        var alreadyRecorded = existing is not null
            && string.Equals(existing.Mode, requested, StringComparison.Ordinal);

        var now = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var changed = !alreadyRecorded;
        var applied = false;

        if (write && changed)
        {
            var previous = existing?.Mode ?? SessionLayerMode.Default;
            var transitions = existing?.Transitions.ToList() ?? [];
            transitions.Add(new SessionLayerModeTransition { From = previous, To = requested!, At = now });

            var updated = new SessionLayerModeEntry
            {
                Domain = domain!,
                Team = team,
                Mode = requested!,
                UpdatedAt = now,
                Transitions = transitions,
            };

            if (existingIndex >= 0)
            {
                entries[existingIndex] = updated;
            }
            else
            {
                entries.Add(updated);
            }

            SessionLayerModeStore.Write(context.RepoRoot, new SessionLayerModeState
            {
                SchemaVersion = SessionLayerModeStore.SchemaVersion,
                Entries = entries
                    .OrderBy(entry => entry.Domain, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Team ?? string.Empty, StringComparer.Ordinal)
                    .ToArray(),
            });
            applied = true;
        }

        var after = applied
            ? SessionLayerModeStore.Resolve(context.RepoRoot, domain!, team)
            : before;

        var result = BuildResult(domain!, team, after, mode: write ? "write" : "dry-run", applied, changed) with
        {
            RequestedMode = requested,
            PreviousMode = existing?.Mode ?? SessionLayerMode.Default,
            AlreadyRecorded = alreadyRecorded,
        };

        Emit(writer, format, result);
        return 0;
    }

    private static SessionLayerResult BuildResult(
        string domain,
        string? team,
        SessionLayerModeResolution resolution,
        string mode,
        bool applied,
        bool changed)
    {
        var scope = team is null ? $"domain `{domain}`" : $"team `{team}` in domain `{domain}`";
        var summary = resolution.Source == SessionLayerModeSource.Recorded
            ? $"{scope}: session layer is {SessionLayerMode.Describe(resolution.Mode)} (recorded)."
            : $"{scope}: no session layer recorded, so the default {SessionLayerMode.Describe(SessionLayerMode.Default)} is in force.";

        return new SessionLayerResult
        {
            Domain = domain,
            Team = team,
            Mode = resolution.Mode,
            Source = resolution.Source == SessionLayerModeSource.Recorded ? "recorded" : "default",
            CommandMode = mode,
            Applied = applied,
            Changed = changed,
            RequestedMode = null,
            PreviousMode = null,
            AlreadyRecorded = false,
            Transitions = resolution.Entry?.Transitions ?? Array.Empty<SessionLayerModeTransition>(),
            RecordPath = SessionLayerModeStore.RelativePath,
            SwitchChecklist = SwitchChecklistSection,
            Exclusivity = SessionLayerMode.ExclusivitySentence,
            PreviewScoping = SessionLayerMode.PreviewScopingSentence,
            Summary = summary,
        };
    }

    private static void Emit(TextWriter writer, string format, SessionLayerResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
            return;
        }

        writer.WriteLine($"# Session layer — {(result.Team is null ? result.Domain : $"{result.Domain} / {result.Team}")}");
        writer.WriteLine();
        writer.WriteLine($"- mode: {SessionLayerMode.Describe(result.Mode)}");
        writer.WriteLine($"- source: {result.Source}");
        writer.WriteLine($"- record: `{result.RecordPath}`");
        if (result.RequestedMode is not null)
        {
            writer.WriteLine($"- requested: {result.RequestedMode}");
            writer.WriteLine($"- previous: {result.PreviousMode}");
            writer.WriteLine($"- command mode: {result.CommandMode}");
            writer.WriteLine($"- applied: {(result.Applied ? "true" : "false")}");
            if (result.AlreadyRecorded)
            {
                writer.WriteLine("- already recorded: true (idempotent no-op; no transition recorded)");
            }
        }

        writer.WriteLine();
        writer.WriteLine(result.Summary);
        writer.WriteLine();
        writer.WriteLine($"- {result.Exclusivity}");
        writer.WriteLine($"- {result.PreviewScoping}");
        writer.WriteLine();
        writer.WriteLine("## After a switch");
        writer.WriteLine();
        writer.WriteLine($"Follow {result.SwitchChecklist}.");

        if (result.Transitions.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Transition trail");
            writer.WriteLine();
            foreach (var transition in result.Transitions)
            {
                writer.WriteLine($"- {transition.At:O}: {transition.From} → {transition.To}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        bool requireMode,
        out string? domain,
        out string? team,
        out string? requestedMode,
        out bool write,
        out string format,
        out string error)
    {
        domain = null;
        team = null;
        requestedMode = null;
        write = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }
                    domain = args[++index].Trim();
                    break;

                case "--team":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--team requires a value.";
                        return false;
                    }
                    team = args[++index].Trim();
                    break;

                case "--mode":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = $"--mode requires a value ({string.Join(" or ", SessionLayerMode.All)}).";
                        return false;
                    }
                    requestedMode = args[++index].Trim();
                    break;

                case "--write":
                    write = true;
                    break;

                case "--dry-run":
                    write = false;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    format = args[++index].Trim();
                    if (!string.Equals(format, FormatJson, StringComparison.Ordinal)
                        && !string.Equals(format, FormatMarkdown, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{format}').";
                        return false;
                    }
                    break;

                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            error = "--domain is required.";
            return false;
        }

        if (requireMode)
        {
            if (string.IsNullOrWhiteSpace(requestedMode))
            {
                error = $"--mode is required ({string.Join(" or ", SessionLayerMode.All)}).";
                return false;
            }

            if (!SessionLayerMode.IsKnown(requestedMode))
            {
                error =
                    $"--mode '{requestedMode}' is not a session layer. Supported: {string.Join(", ", SessionLayerMode.All)}. "
                    + "An unrecognised value is refused rather than recorded — a mode nothing can route on is worse "
                    + "than no record at all.";
                return false;
            }
        }

        return true;
    }
}

internal sealed record SessionLayerResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("team")]
    public required string? Team { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("command_mode")]
    public required string CommandMode { get; init; }

    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }

    [JsonPropertyName("changed")]
    public required bool Changed { get; init; }

    [JsonPropertyName("requested_mode")]
    public string? RequestedMode { get; init; }

    [JsonPropertyName("previous_mode")]
    public string? PreviousMode { get; init; }

    [JsonPropertyName("already_recorded")]
    public bool AlreadyRecorded { get; init; }

    [JsonPropertyName("transitions")]
    public required IReadOnlyList<SessionLayerModeTransition> Transitions { get; init; }

    [JsonPropertyName("record_path")]
    public required string RecordPath { get; init; }

    [JsonPropertyName("switch_checklist")]
    public required string SwitchChecklist { get; init; }

    [JsonPropertyName("exclusivity")]
    public required string Exclusivity { get; init; }

    [JsonPropertyName("preview_scoping")]
    public required string PreviewScoping { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}
