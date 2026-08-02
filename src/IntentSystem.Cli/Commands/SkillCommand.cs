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

        // PHASE 1 (a): validate every target/scope pair.
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

        // PHASE 1 (b): resolve every destination, read every embedded body, and
        // inspect every planned path — all BEFORE the first write. Inspecting
        // and writing in the same loop is not "validated before any write": an
        // earlier missing target would already be on disk by the time a later
        // drifted one was discovered, leaving a partial install behind an
        // exit-1 that claims nothing happened.
        var inspected = plan
            .Select(entry =>
            {
                var path = SkillTargets.ResolveSkillFilePath(entry.Target, entry.Scope, entry.Skill.Name, repoRoot, userHome);
                var embedded = SkillAssets.ReadBody(entry.Skill);
                return (entry.Target, entry.Scope, entry.Skill, Path: path, Embedded: embedded, State: InspectState(path, embedded, entry.Skill));
            })
            .ToList();

        var results = new List<SkillInstallResult>();

        // PHASE 2: a locally-modified destination anywhere in the plan aborts
        // the WHOLE run without touching the filesystem. A known stale shipped
        // version is safe to update without --force; only content outside the
        // lineage retains the edited-copy protection.
        if (!force && inspected.Any(entry => entry.State == SkillState.LocallyModified))
        {
            foreach (var entry in inspected)
            {
                results.Add(new SkillInstallResult
                {
                    Skill = entry.Skill.Name,
                    Target = entry.Target,
                    Scope = entry.Scope,
                    Path = entry.Path,
                    Outcome = entry.State == SkillState.LocallyModified ? "refused-drifted" : "skipped-plan-aborted",
                    Detail = entry.State == SkillState.LocallyModified
                        ? "the installed copy is locally modified and was NOT overwritten. "
                          + "Re-run with --force to replace it, or run `intent-cli skill diff` to see what differs."
                        : "not written: another destination in this plan has an edited copy, so the whole "
                          + "install was abandoned before any file was created or changed.",
                });
            }

            WriteInstallResults(writer, format, results);
            return 1;
        }

        // PHASE 3: every destination is now known to be writable, so the writes
        // that follow cannot be interrupted by a refusal.
        foreach (var entry in inspected)
        {
            if (entry.State == SkillState.Current)
            {
                results.Add(new SkillInstallResult
                {
                    Skill = entry.Skill.Name,
                    Target = entry.Target,
                    Scope = entry.Scope,
                    Path = entry.Path,
                    Outcome = "already-current",
                    Detail = "the installed copy already matches the embedded skill; nothing was written.",
                });
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(entry.Path)!);
            File.WriteAllText(entry.Path, entry.Embedded);
            results.Add(new SkillInstallResult
            {
                Skill = entry.Skill.Name,
                Target = entry.Target,
                Scope = entry.Scope,
                Path = entry.Path,
                Outcome = entry.State switch
                {
                    SkillState.NotInstalled => "installed",
                    SkillState.StaleShipped => "updated-stale",
                    _ => "overwritten",
                },
                Detail = entry.State == SkillState.NotInstalled
                    ? "written for the first time."
                    : entry.State == SkillState.StaleShipped
                        ? "a previous shipped version was updated to the current embedded skill without requiring --force."
                    : "an edited copy was replaced because --force was given.",
            });
        }

        WriteInstallResults(writer, format, results);
        return 0;
    }

    private static void WriteInstallResults(TextWriter writer, string format, IReadOnlyList<SkillInstallResult> results)
    {
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
                writer.WriteLine($"- comparison: {installation.Comparison}");
                writer.WriteLine($"- update available: {(installation.UpdateAvailable ? "yes" : "no")}");
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
                    var state = InspectState(path, embedded, skill);
                    installations.Add(new SkillInstallationState
                    {
                        Target = candidate,
                        Scope = candidateScope,
                        Path = path,
                        State = Describe(state),
                        UpdateAvailable = state == SkillState.StaleShipped,
                        Comparison = DescribeComparison(state),
                        Diff = state is SkillState.StaleShipped or SkillState.LocallyModified
                            ? BuildDiff(embedded, File.ReadAllText(path))
                            : null,
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

    private static SkillState InspectState(string path, string embedded, EmbeddedSkill skill)
    {
        if (!File.Exists(path))
        {
            return SkillState.NotInstalled;
        }

        var installed = File.ReadAllText(path);
        if (SkillAssets.Normalize(installed) == SkillAssets.Normalize(embedded))
        {
            return SkillState.Current;
        }

        var installedHash = SkillAssets.ComputeNormalizedHash(installed);
        return SkillAssets.ReadLineage(skill)
            .Contains(installedHash, StringComparer.Ordinal)
            ? SkillState.StaleShipped
            : SkillState.LocallyModified;
    }

    private static string Describe(SkillState state) => state switch
    {
        SkillState.NotInstalled => "not-installed",
        SkillState.Current => "current",
        SkillState.StaleShipped => "stale-shipped",
        _ => "locally-modified",
    };

    private static string DescribeComparison(SkillState state) => state switch
    {
        SkillState.NotInstalled => "not installed; no comparison",
        SkillState.Current => "installed copy matches the current embedded version",
        SkillState.StaleShipped => "previous shipped version → current embedded version",
        _ => "locally modified copy → current embedded version",
    };

    /// <summary>
    /// A minimal line-level diff: enough for an operator to see WHAT differs
    /// without pulling in a diff library for a file this small.
    /// </summary>
    private static IReadOnlyList<string> BuildDiff(string embedded, string installed)
    {
        var embeddedLines = SkillAssets.Normalize(embedded).Split('\n');
        var installedLines = SkillAssets.Normalize(installed).Split('\n');
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
                if (installation.UpdateAvailable)
                {
                    writer.WriteLine("  update available: yes (previous shipped version)");
                }
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
        StaleShipped,
        LocallyModified,
    }

    internal static SkillUpdateLocation? FindStaleShippedInstall(CliContext context)
    {
        try
        {
            var userHome = SkillTargets.ResolveUserHome();
            foreach (var skill in SkillAssets.All)
            {
                var embedded = SkillAssets.ReadBody(skill);
                foreach (var target in SkillTargets.Installable)
                {
                    foreach (var scope in SkillTargets.SupportedScopes(target))
                    {
                        var path = SkillTargets.ResolveSkillFilePath(
                            target, scope, skill.Name, context.RepoRoot, userHome);
                        try
                        {
                            if (InspectState(path, embedded, skill) == SkillState.StaleShipped)
                            {
                                return new SkillUpdateLocation(skill.Name, target, scope, path);
                            }
                        }
                        catch (Exception exception) when (
                            exception is IOException or UnauthorizedAccessException)
                        {
                            // One unreadable known location does not prevent
                            // checking the remaining local locations.
                        }
                    }
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            // Guide output is authoritative and must never fail or block on
            // this best-effort local-only check.
        }

        return null;
    }
}

internal sealed record SkillUpdateLocation(string Skill, string Target, string Scope, string Path);

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

    /// <summary>One of <c>not-installed</c>, <c>current</c>, <c>stale-shipped</c>, <c>locally-modified</c>.</summary>
    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("update_available")]
    public required bool UpdateAvailable { get; init; }

    [JsonPropertyName("comparison")]
    public required string Comparison { get; init; }

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

    /// <summary>
    /// One of <c>installed</c>, <c>updated-stale</c>, <c>overwritten</c>, <c>already-current</c>,
    /// <c>refused-drifted</c>, or <c>skipped-plan-aborted</c> — the last one
    /// meaning this destination was writable but a sibling in the same plan was
    /// drifted, so nothing at all was written.
    /// </summary>
    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("detail")]
    public required string Detail { get; init; }
}
