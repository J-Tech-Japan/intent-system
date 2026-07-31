using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G559: <c>intent-cli skill list | install | diff</c> — installs the embedded
/// single-source SKILL.md into each platform's own skill location.
///
/// The installer exists because the format is already shared and only the
/// LOCATION differs: Claude Code, Codex, and Copilot all read the same
/// SKILL.md. Copying by hand is what produced the drifted copies this slice
/// replaces, so the commands here always compare against the embedded source
/// and never overwrite an edited copy without <c>--force</c>.
/// </summary>
internal static class SkillCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private const string ListUsage =
        "Usage: intent-cli skill list [--format text|json]";

    private const string InstallUsage =
        "Usage: intent-cli skill install --target claude|codex|copilot|all [--scope user|repo] [--skill <name>] [--force] [--format text|json]";

    private const string DiffUsage =
        "Usage: intent-cli skill diff [--target claude|codex|copilot|all] [--scope user|repo] [--skill <name>] [--format text|json]";

    public static int ExecuteList(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (IsHelp(args))
        {
            writer.WriteLine(ListUsage);
            return 0;
        }

        if (!TryParseFormat(args, ListUsage, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var report = BuildReport(context, skillFilter: null, targetFilter: null, scopeOverride: null, out var reportError);
        if (report is null)
        {
            writer.WriteLine(reportError);
            return 1;
        }

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
            return 0;
        }

        WriteListText(writer, report);
        return 0;
    }

    public static int ExecuteInstall(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (IsHelp(args))
        {
            writer.WriteLine(InstallUsage);
            return 0;
        }

        if (!TryParseInstallArguments(args, out var target, out var scope, out var skillName, out var force, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(InstallUsage);
            return 1;
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            writer.WriteLine("--target is required (claude, codex, copilot, or all).");
            writer.WriteLine(InstallUsage);
            return 1;
        }

        var skills = ResolveSkills(skillName, out var skillError);
        if (skills is null)
        {
            writer.WriteLine(skillError);
            return 1;
        }

        var targets = string.Equals(target, SkillTargets.All, StringComparison.Ordinal)
            ? SkillTargets.Installable
            : [target!];

        // Validate every target/scope pair BEFORE writing anything: a partial
        // install that stops halfway leaves the operator guessing which
        // platforms are current.
        var plan = new List<(string Target, string Scope, EmbeddedSkill Skill)>();
        foreach (var candidate in targets)
        {
            if (!SkillTargets.TryValidate(candidate, scope, out var resolvedScope, out var validationError))
            {
                // An explicit --scope that a platform does not define is an
                // error even under --target all: silently skipping it would
                // report success for an install that never happened.
                writer.WriteLine(validationError);
                return 1;
            }

            foreach (var skill in skills)
            {
                plan.Add((candidate, resolvedScope, skill));
            }
        }

        var repoRoot = context.RepoRoot;
        var userHome = SkillTargets.ResolveUserHome();
        var results = new List<SkillInstallResult>();
        var blocked = false;

        foreach (var (candidateTarget, candidateScope, skill) in plan)
        {
            var path = SkillTargets.ResolveSkillFilePath(candidateTarget, candidateScope, skill.Name, repoRoot, userHome);
            var embedded = SkillAssets.ReadBody(skill);
            var state = InspectState(path, embedded);

            if (state == SkillState.Drifted && !force)
            {
                results.Add(new SkillInstallResult
                {
                    Skill = skill.Name,
                    Target = candidateTarget,
                    Scope = candidateScope,
                    Path = path,
                    Outcome = "refused-drifted",
                    Detail =
                        "the installed copy differs from the embedded skill and was NOT overwritten. "
                        + "Re-run with --force to replace it, or run `intent-cli skill diff` to see what differs.",
                });
                blocked = true;
                continue;
            }

            if (state == SkillState.Current)
            {
                results.Add(new SkillInstallResult
                {
                    Skill = skill.Name,
                    Target = candidateTarget,
                    Scope = candidateScope,
                    Path = path,
                    Outcome = "already-current",
                    Detail = "the installed copy already matches the embedded skill; nothing was written.",
                });
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, embedded);
            results.Add(new SkillInstallResult
            {
                Skill = skill.Name,
                Target = candidateTarget,
                Scope = candidateScope,
                Path = path,
                Outcome = state == SkillState.NotInstalled ? "installed" : "overwritten",
                Detail = state == SkillState.NotInstalled
                    ? "written for the first time."
                    : "an edited copy was replaced because --force was given.",
            });
        }

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(new SkillInstallReport { Results = results }, JsonOptions));
        }
        else
        {
            foreach (var result in results)
            {
                writer.WriteLine($"- {result.Skill} → {result.Target} ({result.Scope}): {result.Outcome} — {result.Path}");
                writer.WriteLine($"  {result.Detail}");
            }
        }

        // A refusal is a non-zero exit: the caller asked for an install that
        // did not fully happen, and a script must be able to notice.
        return blocked ? 1 : 0;
    }

    public static int ExecuteDiff(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (IsHelp(args))
        {
            writer.WriteLine(DiffUsage);
            return 0;
        }

        if (!TryParseInstallArguments(args, out var target, out var scope, out var skillName, out _, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(DiffUsage);
            return 1;
        }

        var report = BuildReport(context, skillName, target, scope, out var reportError);
        if (report is null)
        {
            writer.WriteLine(reportError);
            return 1;
        }

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
            return 0;
        }

        foreach (var skill in report.Skills)
        {
            foreach (var installation in skill.Installations)
            {
                writer.WriteLine($"## {skill.Name} → {installation.Target} ({installation.Scope}) — {installation.State}");
                writer.WriteLine($"- path: {installation.Path}");
                if (installation.Diff is { Count: > 0 })
                {
                    foreach (var line in installation.Diff)
                    {
                        writer.WriteLine(line);
                    }
                }
                writer.WriteLine();
            }
        }

        return 0;
    }

    private static SkillReport? BuildReport(
        CliContext context, string? skillFilter, string? targetFilter, string? scopeOverride, out string error)
    {
        error = string.Empty;

        var skills = ResolveSkills(skillFilter, out var skillError);
        if (skills is null)
        {
            error = skillError;
            return null;
        }

        var targets = string.IsNullOrWhiteSpace(targetFilter) || string.Equals(targetFilter, SkillTargets.All, StringComparison.Ordinal)
            ? SkillTargets.Installable
            : [targetFilter!];

        var repoRoot = context.RepoRoot;
        var userHome = SkillTargets.ResolveUserHome();
        var entries = new List<SkillReportEntry>();

        foreach (var skill in skills)
        {
            var embedded = SkillAssets.ReadBody(skill);
            var installations = new List<SkillInstallationState>();

            foreach (var candidate in targets)
            {
                if (!SkillTargets.TryValidate(candidate, scopeOverride, out var resolvedScope, out var validationError))
                {
                    error = validationError;
                    return null;
                }

                // Report every scope the platform defines unless one was named,
                // so `skill list` shows a repo-scoped Claude install and a
                // user-scoped one as the distinct things they are.
                var scopes = string.IsNullOrWhiteSpace(scopeOverride)
                    ? SkillTargets.SupportedScopes(candidate)
                    : [resolvedScope];

                foreach (var candidateScope in scopes)
                {
                    var path = SkillTargets.ResolveSkillFilePath(candidate, candidateScope, skill.Name, repoRoot, userHome);
                    var state = InspectState(path, embedded);
                    installations.Add(new SkillInstallationState
                    {
                        Target = candidate,
                        Scope = candidateScope,
                        Path = path,
                        State = Describe(state),
                        Diff = state == SkillState.Drifted ? BuildDiff(embedded, File.ReadAllText(path)) : null,
                    });
                }
            }

            entries.Add(new SkillReportEntry
            {
                Name = skill.Name,
                Summary = skill.Summary,
                Installations = installations,
            });
        }

        return new SkillReport { Skills = entries };
    }

    private static IReadOnlyList<EmbeddedSkill>? ResolveSkills(string? name, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            return SkillAssets.All;
        }

        var skill = SkillAssets.Find(name!);
        if (skill is null)
        {
            error = $"unknown skill '{name}'. Embedded skill(s): {string.Join(", ", SkillAssets.All.Select(s => s.Name))}.";
            return null;
        }

        return [skill];
    }

    private static SkillState InspectState(string path, string embedded)
    {
        if (!File.Exists(path))
        {
            return SkillState.NotInstalled;
        }

        var installed = File.ReadAllText(path);
        return Normalize(installed) == Normalize(embedded) ? SkillState.Current : SkillState.Drifted;
    }

    /// <summary>
    /// Line-ending differences are not drift — a checkout on Windows would
    /// otherwise report every install as edited.
    /// </summary>
    private static string Normalize(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

    private static string Describe(SkillState state) => state switch
    {
        SkillState.NotInstalled => "not-installed",
        SkillState.Current => "installed",
        _ => "drifted",
    };

    /// <summary>
    /// A minimal line-level diff: enough for an operator to see WHAT differs
    /// without pulling in a diff library for a file this small.
    /// </summary>
    private static IReadOnlyList<string> BuildDiff(string embedded, string installed)
    {
        var embeddedLines = Normalize(embedded).Split('\n');
        var installedLines = Normalize(installed).Split('\n');
        var lines = new List<string>();

        for (var index = 0; index < Math.Max(embeddedLines.Length, installedLines.Length); index++)
        {
            var left = index < embeddedLines.Length ? embeddedLines[index] : null;
            var right = index < installedLines.Length ? installedLines[index] : null;

            if (string.Equals(left, right, StringComparison.Ordinal))
            {
                continue;
            }

            if (right is not null)
            {
                lines.Add($"- installed:{index + 1}: {right}");
            }

            if (left is not null)
            {
                lines.Add($"+ embedded:{index + 1}: {left}");
            }
        }

        return lines;
    }

    private static void WriteListText(TextWriter writer, SkillReport report)
    {
        foreach (var skill in report.Skills)
        {
            writer.WriteLine($"# {skill.Name}");
            writer.WriteLine($"- summary: {skill.Summary}");
            foreach (var installation in skill.Installations)
            {
                writer.WriteLine($"- {installation.Target} ({installation.Scope}): {installation.State} — {installation.Path}");
            }

            writer.WriteLine();
        }
    }

    private static bool IsHelp(string[] args) =>
        args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal);

    private static bool TryParseFormat(string[] args, string usage, out string format, out string error)
    {
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], "--format", StringComparison.Ordinal))
            {
                error = $"Unknown argument '{args[index]}'. {usage}";
                return false;
            }

            if (index + 1 >= args.Length)
            {
                error = "--format requires a value (text or json).";
                return false;
            }

            format = args[index + 1];
            index++;

            if (!string.Equals(format, FormatText, StringComparison.Ordinal)
                && !string.Equals(format, FormatJson, StringComparison.Ordinal))
            {
                error = $"unknown format '{format}'. Supported: text, json.";
                return false;
            }
        }

        return true;
    }

    private static bool TryParseInstallArguments(
        string[] args,
        out string? target,
        out string? scope,
        out string? skill,
        out bool force,
        out string format,
        out string error)
    {
        target = null;
        scope = null;
        skill = null;
        force = false;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--force":
                    force = true;
                    break;

                case "--target":
                case "--scope":
                case "--skill":
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = $"{argument} requires a value.";
                        return false;
                    }

                    var value = args[index + 1];
                    index++;
                    switch (argument)
                    {
                        case "--target":
                            target = value;
                            break;
                        case "--scope":
                            scope = value;
                            break;
                        case "--skill":
                            skill = value;
                            break;
                        default:
                            format = value;
                            break;
                    }

                    break;

                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        if (!string.Equals(format, FormatText, StringComparison.Ordinal)
            && !string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            error = $"unknown format '{format}'. Supported: text, json.";
            return false;
        }

        if (scope is not null
            && !string.Equals(scope, SkillTargets.ScopeUser, StringComparison.Ordinal)
            && !string.Equals(scope, SkillTargets.ScopeRepo, StringComparison.Ordinal))
        {
            error = $"unknown scope '{scope}'. Supported: {SkillTargets.ScopeUser}, {SkillTargets.ScopeRepo}.";
            return false;
        }

        return true;
    }

    private enum SkillState
    {
        NotInstalled,
        Current,
        Drifted,
    }
}

internal sealed record SkillReport
{
    [JsonPropertyName("skills")]
    public required IReadOnlyList<SkillReportEntry> Skills { get; init; }
}

internal sealed record SkillReportEntry
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("installations")]
    public required IReadOnlyList<SkillInstallationState> Installations { get; init; }
}

internal sealed record SkillInstallationState
{
    [JsonPropertyName("target")]
    public required string Target { get; init; }

    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>One of <c>not-installed</c>, <c>installed</c>, <c>drifted</c>.</summary>
    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("diff")]
    public IReadOnlyList<string>? Diff { get; init; }
}

internal sealed record SkillInstallReport
{
    [JsonPropertyName("results")]
    public required IReadOnlyList<SkillInstallResult> Results { get; init; }
}

internal sealed record SkillInstallResult
{
    [JsonPropertyName("skill")]
    public required string Skill { get; init; }

    [JsonPropertyName("target")]
    public required string Target { get; init; }

    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>One of <c>installed</c>, <c>overwritten</c>, <c>already-current</c>, <c>refused-drifted</c>.</summary>
    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("detail")]
    public required string Detail { get; init; }
}
