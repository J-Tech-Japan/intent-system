using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G750: migrate supervision cycle history to the directory-local ignore
/// owned by the CLI. The files remain on disk and the shared supervision
/// policy/manifest files stay trackable.
/// </summary>
internal static class NotifySuperviseRepairCycleHistoryCommand
{
    public const string Operation = "repair-cycle-history";
    public const string Usage =
        "Usage: intent-cli notify supervise repair-cycle-history --domain <d> --team <t> "
        + "[--dry-run|--write] [--format markdown|json]";

    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly IReadOnlyList<string> LegacyRootIgnoreLines =
    [
        ".intent-cli/supervision/**/cycles.jsonl",
        ".intent-cli/supervision/**/stalls.jsonl",
    ];

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
            writer.WriteLine(Usage);
            return 0;
        }

        if (!TryParse(args, out var options, out var error))
        {
            EmitFailure(writer, error);
            writer.WriteLine(Usage);
            return 1;
        }

        string artifactRoot;
        string teamDirectory;
        try
        {
            artifactRoot = context.ResolveSupervisionArtifactRootPath();
            teamDirectory = NotifySupervisionStore.ResolveDirectory(
                artifactRoot,
                options.Domain,
                options.Team);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            EmitFailure(writer, $"supervision-artifact-root-unavailable: {exception.Message}");
            return 1;
        }

        if (!TryGetRepositoryRelativePath(context.RepoRoot, teamDirectory, out var teamRelativePath, out error))
        {
            EmitFailure(writer, error);
            return 1;
        }

        var rootIgnorePath = Path.Combine(context.RepoRoot, ".gitignore");
        var legacy = InspectLegacyRootIgnore(rootIgnorePath);

        var trackedResult = GitProcessRunner.Run(
            context.RepoRoot,
            ["ls-files", "-z", "--", teamRelativePath],
            timeout: TimeSpan.FromSeconds(10),
            nonInteractive: true);
        if (trackedResult.ExitCode != 0)
        {
            EmitFailure(
                writer,
                $"git-index-unavailable: {FirstNonEmpty(trackedResult.StdErr, trackedResult.StdOut, "git ls-files failed")}");
            return 1;
        }

        var ignore = NotifySupervisionStore.EnsureCycleHistoryIgnore(artifactRoot, options.Write);
        if (ignore.Error is not null)
        {
            EmitFailure(writer, $"cycle-history-ignore-unavailable: {ignore.Error}");
            return 1;
        }

        var trackedCyclePaths = ParseNulSeparatedPaths(trackedResult.StdOut)
            .Where(path => IsCycleHistoryPath(path, teamRelativePath))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var wouldChange = ignore.WouldChange || legacy.RemovedLines.Count > 0 || trackedCyclePaths.Length > 0;
        var removedFromIndex = Array.Empty<string>();
        var legacyRemoved = Array.Empty<string>();
        var applied = false;
        var commandError = (string?)null;

        if (options.Write && wouldChange)
        {
            if (legacy.RemovedLines.Count > 0)
            {
                try
                {
                    WriteLegacyRootIgnore(rootIgnorePath, legacy);
                    legacyRemoved = legacy.RemovedLines.ToArray();
                    applied = true;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    commandError = $"legacy-root-ignore-update-failed: {exception.Message}";
                }
            }

            if (commandError is null && trackedCyclePaths.Length > 0)
            {
                var removeArguments = new List<string>
                {
                    "rm",
                    "--cached",
                    "--ignore-unmatch",
                    "--",
                };
                removeArguments.AddRange(trackedCyclePaths);
                var removeResult = GitProcessRunner.Run(
                    context.RepoRoot,
                    removeArguments,
                    timeout: TimeSpan.FromSeconds(10),
                    nonInteractive: true);
                if (removeResult.ExitCode != 0)
                {
                    commandError =
                        $"cycle-history-index-repair-failed: {FirstNonEmpty(removeResult.StdErr, removeResult.StdOut, "git rm --cached failed")}";
                }
                else
                {
                    removedFromIndex = trackedCyclePaths;
                    applied = true;
                }
            }

            if (commandError is null && ignore.Applied)
            {
                applied = true;
            }
        }

        var result = new RepairCycleHistoryResult
        {
            Domain = options.Domain,
            Team = options.Team,
            CommandMode = options.Write ? "write" : "dry-run",
            Applied = applied,
            WouldChange = wouldChange,
            ArtifactRoot = artifactRoot,
            TeamDirectory = teamDirectory,
            IgnorePath = ignore.Path,
            IgnoreRules = NotifySupervisionStore.CycleHistoryIgnoreLines,
            TrackedCycleHistoryBefore = trackedCyclePaths,
            RemovedFromIndex = removedFromIndex,
            PreservedCyclePaths = trackedCyclePaths,
            LegacyRootRulesBefore = legacy.ExistingLines,
            LegacyRootRulesRemoved = options.Write ? legacyRemoved : legacy.RemovedLines.ToArray(),
            Error = commandError,
        };
        Emit(writer, result, options.Format);
        return result.Error is null ? 0 : 1;
    }

    private static void EmitFailure(TextWriter writer, string error) =>
        writer.WriteLine($"supervise-repair-cycle-history-failed: {error}");

    private static void Emit(TextWriter writer, RepairCycleHistoryResult result, string format)
    {
        var payload = new
        {
            operation = Operation,
            domain = result.Domain,
            team = result.Team,
            command_mode = result.CommandMode,
            applied = result.Applied,
            would_change = result.WouldChange,
            artifact_root = result.ArtifactRoot,
            team_directory = result.TeamDirectory,
            ignore_path = result.IgnorePath,
            ignore_rules = result.IgnoreRules,
            tracked_cycle_history_before = result.TrackedCycleHistoryBefore,
            removed_from_index = result.RemovedFromIndex,
            preserved_cycle_paths = result.PreservedCyclePaths,
            legacy_root_rules_before = result.LegacyRootRulesBefore,
            legacy_root_rules_removed = result.LegacyRootRulesRemoved,
            preserved_files = true,
            shared_policy_state = "trackable",
            behavior_changed = false,
            error = result.Error,
            summary = BuildSummary(result),
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
            return;
        }

        writer.WriteLine("# notify supervise repair-cycle-history");
        writer.WriteLine();
        writer.WriteLine($"- command mode: {result.CommandMode}");
        writer.WriteLine($"- directory-local ignore: `{result.IgnorePath}`");
        writer.WriteLine($"- ignore rules: {string.Join(", ", result.IgnoreRules.Select(line => $"`{line}`"))}");
        writer.WriteLine($"- tracked cycle history before: {FormatPaths(result.TrackedCycleHistoryBefore)}");
        writer.WriteLine($"- removed from index: {FormatPaths(result.RemovedFromIndex)}");
        writer.WriteLine($"- preserved cycle files: {FormatPaths(result.PreservedCyclePaths)}");
        writer.WriteLine($"- legacy root rules removed: {FormatPaths(result.LegacyRootRulesRemoved)}");
        writer.WriteLine("- shared supervision state: trackable (stalls and policy/manifest files were not ignored)");
        writer.WriteLine($"- summary: {BuildSummary(result)}");
        if (result.Error is not null)
        {
            writer.WriteLine($"- error: {result.Error}");
        }
    }

    private static string BuildSummary(RepairCycleHistoryResult result) => result.Error is not null
        ? $"The canonical cycle-history repair did not finish: {result.Error}"
        : result.CommandMode == "write"
            ? result.Applied
                ? $"Added the directory-local cycle-history ignore and removed {result.RemovedFromIndex.Count} tracked cycle-history path(s) from the index without deleting files; shared policy state remains trackable."
                : "Cycle-history ownership was already repaired; no files or index entries changed."
            : result.WouldChange
                ? $"Dry-run would add the directory-local cycle-history ignore and remove {result.TrackedCycleHistoryBefore.Count} tracked cycle-history path(s) from the index while preserving files."
                : "Dry-run found canonical cycle-history ownership already in place; no files or index entries would change.";

    private static bool TryGetRepositoryRelativePath(
        string repoRoot,
        string targetPath,
        out string relativePath,
        out string error)
    {
        var fullRepoRoot = Path.GetFullPath(repoRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullTargetPath = Path.GetFullPath(targetPath);
        relativePath = Path.GetRelativePath(fullRepoRoot, fullTargetPath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        if (Path.IsPathRooted(relativePath)
            || relativePath == ".."
            || relativePath.StartsWith("../", StringComparison.Ordinal))
        {
            error = $"supervision-team-outside-repository: '{fullTargetPath}' is not below '{fullRepoRoot}'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsCycleHistoryPath(string path, string teamRelativePath)
    {
        var normalizedPath = path.Replace('\\', '/');
        var normalizedTeam = teamRelativePath.TrimEnd('/');
        if (!normalizedPath.StartsWith(normalizedTeam + "/", StringComparison.Ordinal))
        {
            return false;
        }

        var relative = normalizedPath[(normalizedTeam.Length + 1)..];
        return string.Equals(relative, NotifySupervisionStore.CycleFileName, StringComparison.Ordinal)
            || (relative.StartsWith(NotifySupervisionStore.CycleArchiveDirectoryName + "/", StringComparison.Ordinal)
                && relative.EndsWith(".jsonl", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> ParseNulSeparatedPaths(string output) =>
        output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Replace('\\', '/'))
            .ToArray();

    private static LegacyRootIgnoreInspection InspectLegacyRootIgnore(string path)
    {
        var text = File.Exists(path) ? File.ReadAllText(path, Utf8NoBom) : string.Empty;
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var existingLines = LegacyRootIgnoreLines
            .Where(required => normalized.Split('\n').Any(line => string.Equals(line.Trim(), required, StringComparison.Ordinal)))
            .ToArray();
        return new LegacyRootIgnoreInspection(path, text, existingLines);
    }

    private static void WriteLegacyRootIgnore(string path, LegacyRootIgnoreInspection inspection)
    {
        var normalized = inspection.OriginalText.Replace("\r\n", "\n", StringComparison.Ordinal);
        var newline = inspection.OriginalText.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";
        var hadTrailingNewline = normalized.EndsWith('\n');
        var retained = normalized
            .Split('\n', StringSplitOptions.None)
            .Where(line => !inspection.RemovedLines.Contains(line.Trim(), StringComparer.Ordinal))
            .ToArray();
        var rewritten = string.Join('\n', retained);
        if (hadTrailingNewline && !rewritten.EndsWith('\n'))
        {
            rewritten += '\n';
        }

        File.WriteAllText(path, rewritten.Replace("\n", newline, StringComparison.Ordinal), Utf8NoBom);
    }

    private static string FormatPaths(IReadOnlyList<string> paths) =>
        paths.Count == 0 ? "<none>" : string.Join(", ", paths.Select(path => $"`{path}`"));

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
        ?? "unknown git error";

    private static bool TryParse(string[] args, out RepairOptions options, out string error)
    {
        string? domain = null;
        string? team = null;
        var write = false;
        var format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (!ReadValue(args, ref index, "--domain", out domain, out error)) return Fail(out options);
                    break;
                case "--team":
                    if (!ReadValue(args, ref index, "--team", out team, out error)) return Fail(out options);
                    break;
                case "--write": write = true; break;
                case "--dry-run": write = false; break;
                case "--format":
                    if (!ReadValue(args, ref index, "--format", out format, out error)) return Fail(out options);
                    if (format is not FormatJson and not FormatMarkdown)
                    {
                        error = "--format must be markdown or json.";
                        return Fail(out options);
                    }
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return Fail(out options);
            }
        }

        if (!IsSafeIdentity(domain) || !IsSafeIdentity(team))
        {
            error = "--domain and --team are required safe identity values.";
            return Fail(out options);
        }

        options = new RepairOptions(domain!, team!, write, format!);
        return true;
    }

    private static bool ReadValue(
        string[] args,
        ref int index,
        string argument,
        out string? value,
        out string error)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            value = null;
            error = $"{argument} requires a value.";
            return false;
        }

        value = args[index];
        error = string.Empty;
        return true;
    }

    private static bool IsSafeIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or ':' or '-');

    private static bool Fail(out RepairOptions options)
    {
        options = null!;
        return false;
    }

    private sealed record RepairOptions(string Domain, string Team, bool Write, string Format);

    private sealed record LegacyRootIgnoreInspection(
        string Path,
        string OriginalText,
        IReadOnlyList<string> ExistingLines)
    {
        public IReadOnlyList<string> RemovedLines => ExistingLines;
    }

    private sealed record RepairCycleHistoryResult
    {
        public required string Domain { get; init; }
        public required string Team { get; init; }
        public required string CommandMode { get; init; }
        public required bool Applied { get; init; }
        public required bool WouldChange { get; init; }
        public required string ArtifactRoot { get; init; }
        public required string TeamDirectory { get; init; }
        public required string IgnorePath { get; init; }
        public required IReadOnlyList<string> IgnoreRules { get; init; }
        public required IReadOnlyList<string> TrackedCycleHistoryBefore { get; init; }
        public required IReadOnlyList<string> RemovedFromIndex { get; init; }
        public required IReadOnlyList<string> PreservedCyclePaths { get; init; }
        public required IReadOnlyList<string> LegacyRootRulesBefore { get; init; }
        public required IReadOnlyList<string> LegacyRootRulesRemoved { get; init; }
        public string? Error { get; init; }
    }
}
