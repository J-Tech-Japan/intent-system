using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Reconciles or uninstalls supervisor artifacts that were explicitly created
/// by intent-cli. The command is intentionally separate from install: install
/// authors an artifact, while this explicit operator command performs the
/// bounded launchctl unload/removal needed to repair drift.
/// </summary>
internal static class NotifySuperviseReconcileCommand
{
    public const string ReconcileOperation = "reconcile";
    public const string UninstallOperation = "uninstall";
    public const string Usage =
        "Usage: intent-cli notify supervise reconcile|uninstall [--domain <d> --team <t>] "
        + "[--label-prefix intent-cli.supervise.<prefix>.] [--platform macos] "
        + "[--launchctl-target gui/<uid>|user/<uid>] [--routing-root <host-root>] "
        + "[--dry-run|--write] [--format markdown|json]";

    private const string MacOs = "macos";
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";
    private const string LabelPrefix = "intent-cli.supervise.";

    // Tests use a fake process runner to exercise the reconciliation state
    // machine on non-macOS CI hosts. Production keeps the real OS detector.
    internal static Func<bool> MacOsDetector { get; set; } = OperatingSystem.IsMacOS;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Execute(
        CliContext context,
        string[] args,
        TextWriter writer,
        string operation)
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
            EmitFailure(writer, operation, error);
            writer.WriteLine(Usage);
            return 1;
        }

        string artifactRoot;
        try
        {
            _ = Path.GetFullPath(options.RoutingRoot ?? context.RepoRoot);
            artifactRoot = context.ResolveSupervisionArtifactRootPath();
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            EmitFailure(writer, operation, $"invalid-routing-root: {exception.Message}");
            return 1;
        }

        var scope = options.Domain is null
            ? options.LabelPrefix
            : LabelPrefix + options.Domain + "." + options.Team;
        var artifactsBefore = NotifySuperviseArtifactInventory.FindManagedArtifacts(
            artifactRoot,
            scope,
            NotifySuperviseArtifactInventory.UserProfileDirectoryFactory());

        if (!string.Equals(options.Platform, MacOs, StringComparison.Ordinal)
            || !MacOsDetector())
        {
            EmitFailure(
                writer,
                operation,
                $"unsupported-platform: lifecycle reconciliation is executable only on macOS; requested '{options.Platform}', current '{CurrentPlatform()}'. Artifacts were not changed."
                    + $" Inspect '{artifactRoot}' and use the platform's explicit session cleanup command.");
            return 1;
        }

        var runner = NotifyCommand.ProcessRunnerFactory?.Invoke() ?? new NotifyProcessRunner();
        var uid = ResolveUserId(runner, out var uidError);
        if (uid is null)
        {
            EmitFailure(writer, operation, $"user-id-unavailable: {uidError}");
            return 1;
        }

        var launchctlTarget = options.LaunchctlTarget ?? $"gui/{uid}";
        if (!IsValidLaunchctlTarget(launchctlTarget, uid))
        {
            EmitFailure(
                writer,
                operation,
                $"invalid-launchctl-target: '{launchctlTarget}' must be gui/{uid} or user/{uid}; artifacts were not changed.");
            return 1;
        }

        var loadedBeforeResult = RunList(runner, launchctlTarget);
        if (loadedBeforeResult.ExitCode != 0)
        {
            EmitFailure(
                writer,
                operation,
                $"launchctl-list-failed: {TrimProcessError(loadedBeforeResult)}");
            return 1;
        }

        var loadedBefore = FilterLabels(
            ParseLaunchctlLabels(loadedBeforeResult.StandardOutput),
            scope);
        var unloaded = new List<string>();
        var errors = new List<string>();

        if (options.Write)
        {
            foreach (var label in loadedBefore)
            {
                var bootout = runner.Run("launchctl", ["bootout", $"{launchctlTarget}/{label}"]);
                if (bootout.ExitCode == 0 || IsAlreadyGone(bootout))
                {
                    unloaded.Add(label);
                }
                else
                {
                    errors.Add($"could not unload '{label}': {TrimProcessError(bootout)}");
                }
            }

            foreach (var artifact in artifactsBefore)
            {
                try
                {
                    File.Delete(artifact);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"could not remove artifact '{artifact}': {exception.Message}");
                }
            }
        }

        var loadedAfterResult = RunList(runner, launchctlTarget);
        if (loadedAfterResult.ExitCode != 0)
        {
            errors.Add($"could not verify launchctl after-state: {TrimProcessError(loadedAfterResult)}");
        }

        var loadedAfter = loadedAfterResult.ExitCode == 0
            ? FilterLabels(ParseLaunchctlLabels(loadedAfterResult.StandardOutput), scope)
            : [];
        var artifactsAfter = NotifySuperviseArtifactInventory.FindManagedArtifacts(
            artifactRoot,
            scope,
            NotifySuperviseArtifactInventory.UserProfileDirectoryFactory());
        var success = errors.Count == 0
            && (!options.Write || (loadedAfter.Count == 0 && artifactsAfter.Count == 0));
        var result = new ReconcileResult
        {
            Operation = "supervise-" + operation,
            Success = success,
            CommandMode = options.Write ? "write" : "dry-run",
            Platform = MacOs,
            LaunchctlTarget = launchctlTarget,
            Lifetime = "current GUI session only; no LaunchAgents login auto-load and no reboot persistence",
            Scope = scope,
            ArtifactRoot = artifactRoot,
            LoadedBefore = loadedBefore,
            Unloaded = unloaded,
            WouldUnload = options.Write ? [] : loadedBefore,
            ArtifactsBefore = artifactsBefore,
            RemovedArtifacts = options.Write ? artifactsBefore.Where(path => !File.Exists(path)).ToArray() : [],
            WouldRemoveArtifacts = options.Write ? [] : artifactsBefore,
            LoadedAfter = loadedAfter,
            ArtifactsAfter = artifactsAfter,
            Errors = errors,
            Summary = BuildSummary(operation, options.Write, loadedBefore, unloaded, artifactsBefore, artifactsAfter, errors),
        };
        Emit(writer, result, options.Format);
        return success ? 0 : 1;
    }

    private static string? ResolveUserId(INotifyProcessRunner runner, out string error)
    {
        try
        {
            var result = runner.Run("id", ["-u"]);
            var value = result.StandardOutput.Trim();
            if (result.ExitCode == 0 && int.TryParse(value, out var uid) && uid >= 0)
            {
                error = string.Empty;
                return value;
            }

            error = TrimProcessError(result);
            return null;
        }
        catch (InvalidOperationException exception)
        {
            error = exception.Message;
            return null;
        }
    }

    private static NotifyProcessResult RunList(INotifyProcessRunner runner, string launchctlTarget) =>
        launchctlTarget.StartsWith("user/", StringComparison.Ordinal)
            ? runner.Run("launchctl", ["print", launchctlTarget])
            : runner.Run("launchctl", ["list"]);

    private static IReadOnlyList<string> ParseLaunchctlLabels(string output) =>
        output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(columns => columns.Length >= 3)
            .Select(columns => columns[^1])
            .Where(label => label.StartsWith(LabelPrefix, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> FilterLabels(IReadOnlyList<string> labels, string? scope) =>
        scope is null
            ? labels
            : scope.EndsWith(".", StringComparison.Ordinal)
                ? labels.Where(label => label.StartsWith(scope, StringComparison.Ordinal)).ToArray()
                : labels.Where(label => string.Equals(label, scope, StringComparison.Ordinal)).ToArray();

    private static bool IsAlreadyGone(NotifyProcessResult result) =>
        (result.StandardError + result.StandardOutput).Contains("Could not find service", StringComparison.OrdinalIgnoreCase)
        || (result.StandardError + result.StandardOutput).Contains("No such process", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidLaunchctlTarget(string value, string uid) =>
        string.Equals(value, $"gui/{uid}", StringComparison.Ordinal)
        || string.Equals(value, $"user/{uid}", StringComparison.Ordinal);

    private static string TrimProcessError(NotifyProcessResult result)
    {
        var text = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        return string.IsNullOrWhiteSpace(text) ? $"exit code {result.ExitCode}" : text.Trim();
    }

    private static string BuildSummary(
        string operation,
        bool write,
        IReadOnlyList<string> loadedBefore,
        IReadOnlyList<string> unloaded,
        IReadOnlyList<string> artifactsBefore,
        IReadOnlyList<string> artifactsAfter,
        IReadOnlyList<string> errors)
    {
        if (errors.Count > 0)
        {
            return $"{operation} failed: {string.Join("; ", errors)}";
        }

        if (!write)
        {
            return $"Dry-run found {loadedBefore.Count} loaded supervise job(s) and {artifactsBefore.Count} artifact(s); no launchctl or filesystem mutation was performed. Use --write to unload and remove them.";
        }

        return $"{operation} unloaded {unloaded.Count} supervise job(s) and removed {artifactsBefore.Count - artifactsAfter.Count} artifact(s). Remaining loaded jobs: {loadedBefore.Count - unloaded.Count}; remaining artifacts: {artifactsAfter.Count}.";
    }

    private static string CurrentPlatform() =>
        OperatingSystem.IsMacOS() ? MacOs : OperatingSystem.IsWindows() ? "windows" : "linux";

    private static bool TryParse(string[] args, out ReconcileOptions options, out string error)
    {
        string? domain = null;
        string? team = null;
        string? routingRoot = null;
        string? launchctlTarget = null;
        string? labelPrefix = null;
        var platform = CurrentPlatform();
        var write = false;
        var format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--domain": if (!ReadValue(args, ref index, argument, out domain, out error)) return Fail(out options); break;
                case "--team": if (!ReadValue(args, ref index, argument, out team, out error)) return Fail(out options); break;
                case "--label-prefix": if (!ReadValue(args, ref index, argument, out labelPrefix, out error)) return Fail(out options); break;
                case "--routing-root": if (!ReadValue(args, ref index, argument, out routingRoot, out error)) return Fail(out options); break;
                case "--launchctl-target": if (!ReadValue(args, ref index, argument, out launchctlTarget, out error)) return Fail(out options); break;
                case "--platform": if (!ReadValue(args, ref index, argument, out platform, out error)) return Fail(out options); break;
                case "--write": write = true; break;
                case "--dry-run": write = false; break;
                case "--format":
                    if (!ReadValue(args, ref index, argument, out format, out error)) return Fail(out options);
                    if (format is not FormatJson and not FormatMarkdown)
                    {
                        error = "--format must be markdown or json.";
                        return Fail(out options);
                    }
                    break;
                default:
                    error = $"Unknown argument '{argument}'.";
                    return Fail(out options);
            }
        }

        if ((domain is null) != (team is null))
        {
            error = "--domain and --team must be supplied together when scoping reconciliation.";
            return Fail(out options);
        }
        if (domain is not null && labelPrefix is not null)
        {
            error = "--label-prefix cannot be combined with --domain/--team.";
            return Fail(out options);
        }
        if (labelPrefix is not null
            && (!labelPrefix.StartsWith(LabelPrefix, StringComparison.Ordinal)
                || !labelPrefix.EndsWith(".", StringComparison.Ordinal)
                || !IsSafeIdentity(labelPrefix[LabelPrefix.Length..^1])))
        {
            error = "--label-prefix must be intent-cli.supervise.<safe-prefix>.";
            return Fail(out options);
        }
        if (domain is not null && (!IsSafeIdentity(domain) || !IsSafeIdentity(team)))
        {
            error = "--domain and --team must be safe identity values.";
            return Fail(out options);
        }
        if (team is not null && !NotifyEventWriter.TryValidateTeam(team, out error))
        {
            return Fail(out options);
        }
        if (platform is not MacOs)
        {
            error = "--platform must be macos for lifecycle reconciliation.";
            return Fail(out options);
        }

        options = new ReconcileOptions
        {
            Domain = domain,
            Team = team,
            LabelPrefix = labelPrefix,
            RoutingRoot = routingRoot,
            LaunchctlTarget = launchctlTarget,
            Platform = platform,
            Write = write,
            Format = format!,
        };
        return true;
    }

    private static bool ReadValue(string[] args, ref int index, string option, out string? value, out string error)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            value = null;
            error = $"{option} requires a value.";
            return false;
        }

        value = args[++index];
        error = string.Empty;
        return true;
    }

    private static bool IsSafeIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-');

    private static bool Fail(out ReconcileOptions options)
    {
        options = null!;
        return false;
    }

    private static void EmitFailure(TextWriter writer, string operation, string error)
    {
        writer.WriteLine(JsonSerializer.Serialize(new
        {
            operation = "supervise-" + operation,
            success = false,
            error,
        }, JsonOptions));
    }

    private static void Emit(TextWriter writer, ReconcileResult result, string format)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        writer.WriteLine($"# notify supervise {result.Operation.Replace("supervise-", string.Empty, StringComparison.Ordinal)}");
        writer.WriteLine();
        writer.WriteLine($"- success: {result.Success.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- command mode: {result.CommandMode}");
        writer.WriteLine($"- lifetime: {result.Lifetime}");
        writer.WriteLine($"- launchctl target: `{result.LaunchctlTarget}`");
        writer.WriteLine($"- artifact root: `{result.ArtifactRoot}`");
        writer.WriteLine($"- loaded before: {string.Join(", ", result.LoadedBefore.DefaultIfEmpty("none"))}");
        writer.WriteLine($"- unloaded: {string.Join(", ", result.Unloaded.DefaultIfEmpty("none"))}");
        writer.WriteLine($"- removed artifacts: {string.Join(", ", result.RemovedArtifacts.DefaultIfEmpty("none"))}");
        writer.WriteLine($"- loaded after: {string.Join(", ", result.LoadedAfter.DefaultIfEmpty("none"))}");
        writer.WriteLine($"- artifacts after: {string.Join(", ", result.ArtifactsAfter.DefaultIfEmpty("none"))}");
        if (result.Errors.Count > 0)
        {
            writer.WriteLine($"- errors: {string.Join("; ", result.Errors)}");
        }
        writer.WriteLine();
        writer.WriteLine(result.Summary);
    }

    private sealed record ReconcileOptions
    {
        public string? Domain { get; init; }
        public string? Team { get; init; }
        public string? LabelPrefix { get; init; }
        public string? RoutingRoot { get; init; }
        public string? LaunchctlTarget { get; init; }
        public required string Platform { get; init; }
        public required bool Write { get; init; }
        public required string Format { get; init; }
    }

    private sealed record ReconcileResult
    {
        [JsonPropertyName("operation")] public required string Operation { get; init; }
        [JsonPropertyName("success")] public required bool Success { get; init; }
        [JsonPropertyName("command_mode")] public required string CommandMode { get; init; }
        [JsonPropertyName("platform")] public required string Platform { get; init; }
        [JsonPropertyName("launchctl_target")] public required string LaunchctlTarget { get; init; }
        [JsonPropertyName("lifetime")] public required string Lifetime { get; init; }
        [JsonPropertyName("scope")] public string? Scope { get; init; }
        [JsonPropertyName("artifact_root")] public required string ArtifactRoot { get; init; }
        [JsonPropertyName("loaded_before")] public required IReadOnlyList<string> LoadedBefore { get; init; }
        [JsonPropertyName("unloaded")] public required IReadOnlyList<string> Unloaded { get; init; }
        [JsonPropertyName("would_unload")] public required IReadOnlyList<string> WouldUnload { get; init; }
        [JsonPropertyName("artifacts_before")] public required IReadOnlyList<string> ArtifactsBefore { get; init; }
        [JsonPropertyName("removed_artifacts")] public required IReadOnlyList<string> RemovedArtifacts { get; init; }
        [JsonPropertyName("would_remove_artifacts")] public required IReadOnlyList<string> WouldRemoveArtifacts { get; init; }
        [JsonPropertyName("artifacts_after")] public required IReadOnlyList<string> ArtifactsAfter { get; init; }
        [JsonPropertyName("loaded_after")] public required IReadOnlyList<string> LoadedAfter { get; init; }
        [JsonPropertyName("errors")] public required IReadOnlyList<string> Errors { get; init; }
        [JsonPropertyName("summary")] public required string Summary { get; init; }
    }
}

internal static class NotifySuperviseArtifactInventory
{
    private const string LabelPrefix = "intent-cli.supervise.";

    internal static Func<string> UserProfileDirectoryFactory { get; set; } =
        () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static IReadOnlyList<string> FindManagedArtifacts(
        string artifactRoot,
        string? label,
        string userProfileDirectory)
    {
        var results = new List<string>();
        if (Directory.Exists(artifactRoot))
        {
            try
            {
                results.AddRange(
                    Directory.EnumerateFiles(artifactRoot, "*", SearchOption.AllDirectories)
                        .Where(path => IsManagedArtifact(path, label)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The caller will report the visible inventory it could read;
                // a later launchctl verification still prevents false success.
            }
        }

        var legacyDirectory = Path.Combine(userProfileDirectory, "Library", "LaunchAgents");
        if (Directory.Exists(legacyDirectory))
        {
            try
            {
                results.AddRange(
                    Directory.EnumerateFiles(legacyDirectory, "intent-cli.supervise.*.plist", SearchOption.TopDirectoryOnly)
                        .Where(path => IsManagedArtifact(path, label)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // As above, the explicit launchctl after-state remains the
                // authoritative success gate.
            }
        }

        return results.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<string> RemoveLegacyArtifacts(
        string label,
        string userProfileDirectory,
        out string? error)
    {
        error = null;
        var legacyDirectory = Path.Combine(userProfileDirectory, "Library", "LaunchAgents");
        if (!Directory.Exists(legacyDirectory))
        {
            return [];
        }

        IReadOnlyList<string> legacyPaths;
        try
        {
            legacyPaths = Directory.EnumerateFiles(
                    legacyDirectory,
                    "intent-cli.supervise.*.plist",
                    SearchOption.TopDirectoryOnly)
                .Where(path => IsManagedArtifact(path, label))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"Could not inspect legacy login-persistent artifacts in '{legacyDirectory}': {exception.Message}";
            return [];
        }

        var removed = new List<string>();
        foreach (var path in legacyPaths)
        {
            try
            {
                File.Delete(path);
                if (!File.Exists(path))
                {
                    removed.Add(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error = $"Could not remove legacy login-persistent artifact '{path}': {exception.Message}";
            }
        }

        return removed;
    }

    private static bool IsManagedArtifact(string path, string? label)
    {
        var fileName = Path.GetFileName(path);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (label is not null
            && (label.EndsWith(".", StringComparison.Ordinal)
                ? !stem.StartsWith(label, StringComparison.Ordinal)
                : !string.Equals(stem, label, StringComparison.Ordinal)))
        {
            return false;
        }

        return fileName.StartsWith(LabelPrefix, StringComparison.Ordinal)
            && (fileName.EndsWith(".plist", StringComparison.Ordinal)
                || fileName.EndsWith(".xml", StringComparison.Ordinal)
                || fileName.EndsWith(".service", StringComparison.Ordinal));
    }
}
