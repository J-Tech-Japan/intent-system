using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G691 canonical writer/reader for the durable team shape. Like
/// session-layer set, writes are explicit and idempotent, and every change is
/// retained as a transition so delivery ↔ authoring-only is auditable.
/// </summary>
internal static class TeamModeCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string Usage =
        "Usage: intent-cli team-mode show|set|validate --domain <name> [--team <name>] [--mode delivery|authoring-only] "
        + "[--dry-run|--write] [--format markdown|json]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Test seam for deterministic transition trails.</summary>
    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 0 || (args.Length == 1 && args[0] == "--help"))
        {
            writer.WriteLine(Usage);
            return args.Length == 0 ? 1 : 0;
        }

        return args[0] switch
        {
            "show" => ExecuteShow(context, args[1..], writer),
            "set" => ExecuteSet(context, args[1..], writer),
            "validate" => ExecuteValidate(context, args[1..], writer),
            _ => Unknown(args[0], writer),
        };
    }

    public static int ExecuteShow(CliContext context, string[] args, TextWriter writer)
    {
        if (IsHelp(args))
        {
            writer.WriteLine(Usage);
            return 0;
        }

        if (!TryParse(args, requireMode: false, out var options, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(Usage);
            return 1;
        }

        try
        {
            var resolution = TeamModeStore.Resolve(context.RepoRoot, options.Domain!, options.Team);
            var result = BuildResult(options.Domain!, options.Team, resolution, "show", false, false) with
            {
                Summary = Describe(options.Domain!, options.Team, resolution),
            };
            Emit(writer, options.Format, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine($"team-mode-unreadable: {exception.Message}");
            return 1;
        }
    }

    public static int ExecuteSet(CliContext context, string[] args, TextWriter writer)
    {
        if (IsHelp(args))
        {
            writer.WriteLine(Usage);
            return 0;
        }

        if (!TryParse(args, requireMode: true, out var options, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(Usage);
            return 1;
        }

        try
        {
            var state = TeamModeStore.TryRead(context.RepoRoot);
            var before = TeamModeStore.Resolve(state, options.Domain!, options.Team);
            var entries = state?.Entries.ToList() ?? [];
            var existingIndex = entries.FindIndex(entry =>
                string.Equals(entry.Domain, options.Domain, StringComparison.Ordinal)
                && string.Equals(entry.Team, options.Team, StringComparison.Ordinal));
            var existing = existingIndex >= 0 ? entries[existingIndex] : null;
            var alreadyRecorded = existing is not null
                && string.Equals(existing.Mode, options.Mode, StringComparison.Ordinal);
            var changed = !alreadyRecorded;
            var applied = false;

            if (options.Write && changed)
            {
                var now = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
                var transitions = existing?.Transitions.ToList() ?? [];
                transitions.Add(new TeamModeTransition
                {
                    From = existing?.Mode ?? TeamMode.Default,
                    To = options.Mode!,
                    At = now,
                });

                var updated = new TeamModeEntry
                {
                    Domain = options.Domain!,
                    Team = options.Team,
                    Mode = options.Mode!,
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

                TeamModeStore.Write(context.RepoRoot, new TeamModeState
                {
                    SchemaVersion = TeamModeStore.SchemaVersion,
                    Entries = entries
                        .OrderBy(entry => entry.Domain, StringComparer.Ordinal)
                        .ThenBy(entry => entry.Team ?? string.Empty, StringComparer.Ordinal)
                        .ToArray(),
                });
                applied = true;
            }

            var after = applied
                ? TeamModeStore.Resolve(context.RepoRoot, options.Domain!, options.Team)
                : before;
            var result = BuildResult(
                options.Domain!,
                options.Team,
                after,
                options.Write ? "write" : "dry-run",
                applied,
                changed) with
            {
                RequestedMode = options.Mode,
                PreviousMode = existing?.Mode ?? TeamMode.Default,
                AlreadyRecorded = alreadyRecorded,
                Summary = options.Write && applied
                    ? $"Recorded team mode '{after.Mode}' for {Scope(options.Domain!, options.Team)}."
                    : options.Write
                        ? $"Team mode '{after.Mode}' is already recorded for {Scope(options.Domain!, options.Team)}; no transition was appended."
                        : $"Previewed team mode '{options.Mode}' for {Scope(options.Domain!, options.Team)} without writing.",
            };
            Emit(writer, options.Format, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine($"team-mode-write-refused: {exception.Message}");
            return 1;
        }
        catch (IOException exception)
        {
            writer.WriteLine($"team-mode-write-failed: {exception.Message}");
            return 1;
        }
    }

    public static int ExecuteValidate(CliContext context, string[] args, TextWriter writer)
    {
        if (IsHelp(args))
        {
            writer.WriteLine(Usage);
            return 0;
        }

        if (!TryParse(args, requireMode: false, out var options, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(Usage);
            return 1;
        }

        try
        {
            var resolution = TeamModeStore.Resolve(context.RepoRoot, options.Domain!, options.Team);
            var result = new TeamModeValidationResult
            {
                Domain = options.Domain!,
                Team = options.Team,
                PreviewStatus = "preview-through-1.x",
                Valid = true,
                Mode = resolution.Mode,
                Source = SourceName(resolution.Source),
                RecordPath = TeamModeStore.RelativePath,
                Findings = [],
                Summary = $"Team mode '{resolution.Mode}' is valid for {Scope(options.Domain!, options.Team)}.",
            };
            Emit(writer, options.Format, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            var result = new TeamModeValidationResult
            {
                Domain = options.Domain!,
                Team = options.Team,
                PreviewStatus = "preview-through-1.x",
                Valid = false,
                Mode = null,
                Source = null,
                RecordPath = TeamModeStore.RelativePath,
                Findings = [exception.Message],
                Summary = "Team mode validation failed closed; the record was not trusted.",
            };
            Emit(writer, options.Format, result);
            return 1;
        }
    }

    private static TeamModeCommandResult BuildResult(
        string domain,
        string? team,
        TeamModeResolution resolution,
        string commandMode,
        bool applied,
        bool changed) => new()
        {
            Domain = domain,
            Team = team,
            PreviewStatus = "preview-through-1.x",
            Mode = resolution.Mode,
            Source = SourceName(resolution.Source),
            CommandMode = commandMode,
            Applied = applied,
            Changed = changed,
            RequestedMode = null,
            PreviousMode = null,
            AlreadyRecorded = false,
            Transitions = resolution.Entry?.Transitions ?? [],
            RecordPath = TeamModeStore.RelativePath,
            Summary = Describe(domain, team, resolution),
        };

    private static string Describe(string domain, string? team, TeamModeResolution resolution) =>
        resolution.Source == TeamModeSource.Recorded
            ? $"{Scope(domain, team)}: team mode is '{resolution.Mode}' (recorded)."
            : $"{Scope(domain, team)}: no team mode is recorded, so the default '{TeamMode.Default}' is in force.";

    private static string Scope(string domain, string? team) =>
        team is null ? $"domain '{domain}'" : $"team '{team}' in domain '{domain}'";

    private static string SourceName(TeamModeSource source) =>
        source == TeamModeSource.Recorded ? "recorded" : "default";

    private static bool TryParse(string[] args, bool requireMode, out TeamModeOptions options, out string error)
    {
        string? domain = null;
        string? team = null;
        string? mode = null;
        var write = false;
        var format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (!TryReadValue(args, ref index, "--domain", out domain, out error))
                    {
                        options = default!;
                        return false;
                    }
                    break;
                case "--team":
                    if (!TryReadValue(args, ref index, "--team", out team, out error))
                    {
                        options = default!;
                        return false;
                    }
                    break;
                case "--mode":
                    if (!TryReadValue(args, ref index, "--mode", out mode, out error))
                    {
                        options = default!;
                        return false;
                    }
                    break;
                case "--write":
                    write = true;
                    break;
                case "--dry-run":
                    write = false;
                    break;
                case "--format":
                    if (!TryReadValue(args, ref index, "--format", out var requestedFormat, out error)
                        || requestedFormat is not (FormatJson or FormatMarkdown))
                    {
                        error = string.IsNullOrEmpty(error)
                            ? $"--format must be '{FormatMarkdown}' or '{FormatJson}'."
                            : error;
                        options = default!;
                        return false;
                    }
                    format = requestedFormat!;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    options = default!;
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            error = "--domain is required.";
            options = default!;
            return false;
        }

        if (requireMode && !TeamMode.IsKnown(mode))
        {
            error = "--mode must be 'delivery' or 'authoring-only'.";
            options = default!;
            return false;
        }

        options = new TeamModeOptions(domain.Trim(), string.IsNullOrWhiteSpace(team) ? null : team.Trim(), mode, write, format);
        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, string option, out string? value, out string error)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            value = null;
            error = $"{option} requires a value.";
            return false;
        }

        value = args[++index].Trim();
        error = string.Empty;
        return true;
    }

    private static bool IsHelp(string[] args) => args.Length == 1 && args[0] == "--help";

    private static int Unknown(string command, TextWriter writer)
    {
        writer.WriteLine($"Unknown team-mode subcommand '{command}'.");
        writer.WriteLine(Usage);
        return 1;
    }

    private static void Emit(TextWriter writer, string format, object result)
    {
        if (format == FormatJson)
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        switch (result)
        {
            case TeamModeCommandResult command:
                writer.WriteLine($"# Team mode — {command.Domain}{(command.Team is null ? "" : $" / {command.Team}")}");
                writer.WriteLine($"preview_status: {command.PreviewStatus}");
                writer.WriteLine($"mode: {command.Mode}");
                writer.WriteLine($"source: {command.Source}");
                writer.WriteLine($"command_mode: {command.CommandMode}");
                writer.WriteLine($"applied: {command.Applied.ToString().ToLowerInvariant()}");
                writer.WriteLine($"changed: {command.Changed.ToString().ToLowerInvariant()}");
                writer.WriteLine(command.Summary);
                break;
            case TeamModeValidationResult validation:
                writer.WriteLine($"# Team mode validation — {validation.Domain}");
                writer.WriteLine($"preview_status: {validation.PreviewStatus}");
                writer.WriteLine($"valid: {validation.Valid.ToString().ToLowerInvariant()}");
                writer.WriteLine($"mode: {validation.Mode ?? "unresolved"}");
                writer.WriteLine(validation.Summary);
                foreach (var finding in validation.Findings) writer.WriteLine($"- {finding}");
                break;
        }
    }

    private readonly record struct TeamModeOptions(
        string Domain,
        string? Team,
        string? Mode,
        bool Write,
        string Format);
}

internal sealed record TeamModeCommandResult
{
    public required string Domain { get; init; }
    public string? Team { get; init; }
    public required string PreviewStatus { get; init; }
    public required string Mode { get; init; }
    public required string Source { get; init; }
    public required string CommandMode { get; init; }
    public required bool Applied { get; init; }
    public required bool Changed { get; init; }
    public string? RequestedMode { get; init; }
    public string? PreviousMode { get; init; }
    public required bool AlreadyRecorded { get; init; }
    public required IReadOnlyList<TeamModeTransition> Transitions { get; init; }
    public required string RecordPath { get; init; }
    public required string Summary { get; init; }
}

internal sealed record TeamModeValidationResult
{
    public required string Domain { get; init; }
    public string? Team { get; init; }
    public required string PreviewStatus { get; init; }
    public required bool Valid { get; init; }
    public string? Mode { get; init; }
    public string? Source { get; init; }
    public required string RecordPath { get; init; }
    public required IReadOnlyList<string> Findings { get; init; }
    public required string Summary { get; init; }
}
