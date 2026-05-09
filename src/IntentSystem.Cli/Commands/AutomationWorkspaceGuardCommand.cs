using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G306: <c>intent-cli automation workspace-guard --mode plan|begin|end</c>
/// — host-only safe-stash lane that lets a host review/next-slice wake
/// proceed through unrelated dirty worktree changes (an unrelated
/// submodule pointer, a tracked-file edit, an untracked scratch file)
/// without losing them and without allowing automation to mutate over
/// them. Durable host-state files (<c>.intent-cli/**</c>, <c>intents/**</c>)
/// are NEVER stashed by this lane; if any of those are dirty the lane
/// refuses begin and the operator must reconcile them through the host
/// loop's normal fail-closed path (G304).
///
/// Three modes:
/// <list type="bullet">
/// <item><c>plan</c> — read-only scan of <c>git status --porcelain</c>;
/// returns the deterministic safe-stash plan (paths to stash + the
/// stash message intent-cli would create).</item>
/// <item><c>begin --write</c> — actually creates the stash via
/// <c>git stash push --include-untracked -m "intent-cli/G306 ..." -- &lt;paths&gt;</c>
/// and writes <c>.intent-cli/workspace-guard.json</c> recording the
/// stash ref + the stashed paths so <c>end</c> knows what to restore.</item>
/// <item><c>end --write</c> — reads the state file, runs
/// <c>git stash pop &lt;ref&gt;</c>, reports a structured conflict on
/// pop failure (no claim of successful completion), and removes the
/// state file on clean restore.</item>
/// </list>
///
/// Tests inject a fake <see cref="IGitRunner"/> through
/// <see cref="GitRunnerFactory"/>; the default implementation shells
/// out to <c>git</c> in the host repo's working directory.
/// </summary>
internal static class AutomationWorkspaceGuardCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";
    private const string ModePlan = "plan";
    private const string ModeBegin = "begin";
    private const string ModeEnd = "end";

    private const string StateFileRelativePath = ".intent-cli/workspace-guard.json";

    /// <summary>Test seam — replaces the default git command runner.</summary>
    public static Func<string, IGitRunner>? GitRunnerFactory { get; set; }

    /// <summary>Test seam — replaces the default UTC timestamp source.</summary>
    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var mode, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var runner = GitRunnerFactory?.Invoke(context.RepoRoot) ?? new ShellGitRunner(context.RepoRoot);
        var statePath = Path.Combine(context.RepoRoot, StateFileRelativePath.Replace('/', Path.DirectorySeparatorChar));

        return mode switch
        {
            ModePlan => ExecutePlan(runner, context, format, writer),
            ModeBegin => ExecuteBegin(runner, context, statePath, write, format, writer),
            ModeEnd => ExecuteEnd(runner, statePath, write, format, writer),
            _ => throw new InvalidOperationException($"unreachable mode '{mode}'")
        };
    }

    private static int ExecutePlan(IGitRunner runner, CliContext context, string format, TextWriter writer)
    {
        var entries = ScanWorkingTree(runner);
        var safeStash = entries.Where(e => !IsDurableStatePath(e.Path)).ToArray();
        var durable = entries.Where(e => IsDurableStatePath(e.Path)).ToArray();
        var stashMessage = BuildStashMessage();

        var result = new WorkspaceGuardResult
        {
            Mode = ModePlan,
            ProceedAllowed = durable.Length == 0,
            SafeStashPaths = safeStash,
            DirtyDurableStatePaths = durable,
            StashMessage = stashMessage,
            StashRef = null,
            ConflictPaths = Array.Empty<string>(),
            StateFilePath = Path.Combine(context.RepoRoot, StateFileRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            Summary = durable.Length > 0
                ? $"workspace-guard plan: {durable.Length} dirty durable-state path(s) present; safe-stash refused. Reconcile durable host-state through the G304 fail-closed path before re-running."
                : safeStash.Length > 0
                    ? $"workspace-guard plan: {safeStash.Length} unrelated dirty path(s) would be stashed under message '{stashMessage}' on `--mode begin --write`."
                    : "workspace-guard plan: working tree is clean; no stash required."
        };

        EmitResult(writer, format, result);
        return durable.Length > 0 ? 1 : 0;
    }

    private static int ExecuteBegin(IGitRunner runner, CliContext context, string statePath, bool write, string format, TextWriter writer)
    {
        var entries = ScanWorkingTree(runner);
        var safeStash = entries.Where(e => !IsDurableStatePath(e.Path)).ToArray();
        var durable = entries.Where(e => IsDurableStatePath(e.Path)).ToArray();

        if (durable.Length > 0)
        {
            var refusal = new WorkspaceGuardResult
            {
                Mode = ModeBegin,
                ProceedAllowed = false,
                SafeStashPaths = safeStash,
                DirtyDurableStatePaths = durable,
                StashMessage = null,
                StashRef = null,
                ConflictPaths = Array.Empty<string>(),
                StateFilePath = statePath,
                Summary = $"workspace-guard begin refused: {durable.Length} dirty durable-state path(s) cannot be safe-stashed (G306). Reconcile via the G304 fail-closed path first."
            };
            EmitResult(writer, format, refusal);
            return 1;
        }

        if (safeStash.Length == 0)
        {
            var noop = new WorkspaceGuardResult
            {
                Mode = ModeBegin,
                ProceedAllowed = true,
                SafeStashPaths = Array.Empty<HostSyncWorkingTreeEntry>(),
                DirtyDurableStatePaths = Array.Empty<HostSyncWorkingTreeEntry>(),
                StashMessage = null,
                StashRef = null,
                ConflictPaths = Array.Empty<string>(),
                StateFilePath = statePath,
                Summary = "workspace-guard begin: working tree is clean; no stash created."
            };
            EmitResult(writer, format, noop);
            return 0;
        }

        var message = BuildStashMessage();
        if (!write)
        {
            var dryRun = new WorkspaceGuardResult
            {
                Mode = ModeBegin,
                ProceedAllowed = true,
                SafeStashPaths = safeStash,
                DirtyDurableStatePaths = Array.Empty<HostSyncWorkingTreeEntry>(),
                StashMessage = message,
                StashRef = null,
                ConflictPaths = Array.Empty<string>(),
                StateFilePath = statePath,
                Summary = $"workspace-guard begin (dry-run): would stash {safeStash.Length} path(s) under '{message}'. Re-run with --write to apply."
            };
            EmitResult(writer, format, dryRun);
            return 0;
        }

        var pushArgs = new List<string> { "stash", "push", "--include-untracked", "-m", message, "--" };
        pushArgs.AddRange(safeStash.Select(e => e.Path));
        var pushResult = runner.Run(pushArgs);
        if (pushResult.ExitCode != 0)
        {
            var failure = new WorkspaceGuardResult
            {
                Mode = ModeBegin,
                ProceedAllowed = false,
                SafeStashPaths = safeStash,
                DirtyDurableStatePaths = Array.Empty<HostSyncWorkingTreeEntry>(),
                StashMessage = message,
                StashRef = null,
                ConflictPaths = Array.Empty<string>(),
                StateFilePath = statePath,
                Summary = $"workspace-guard begin failed: `git stash push` exited {pushResult.ExitCode}: {pushResult.StandardError.Trim()}"
            };
            EmitResult(writer, format, failure);
            return 1;
        }

        // Stash list lookup: find the most recent stash matching our message.
        var listResult = runner.Run(new[] { "stash", "list", "--format=%gd %s" });
        var stashRef = ParseStashRef(listResult.StandardOutput, message);
        if (listResult.ExitCode != 0 || stashRef is null)
        {
            var failure = new WorkspaceGuardResult
            {
                Mode = ModeBegin,
                ProceedAllowed = false,
                SafeStashPaths = safeStash,
                DirtyDurableStatePaths = Array.Empty<HostSyncWorkingTreeEntry>(),
                StashMessage = message,
                StashRef = null,
                ConflictPaths = Array.Empty<string>(),
                StateFilePath = statePath,
                Summary = listResult.ExitCode != 0
                    ? $"workspace-guard begin failed after stash push: `git stash list` exited {listResult.ExitCode}: {listResult.StandardError.Trim()}. Inspect `git stash list` and restore the stash with message '{message}' before continuing."
                    : $"workspace-guard begin failed after stash push: could not identify a stash entry with message '{message}'. Inspect `git stash list` and restore that stash before continuing."
            };
            EmitResult(writer, format, failure);
            return 1;
        }

        var state = new WorkspaceGuardState
        {
            StashRef = stashRef,
            StashMessage = message,
            CreatedAt = ResolveNow(),
            StashedPaths = safeStash.Select(e => e.Path).ToArray()
        };
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        File.WriteAllText(statePath, JsonSerializer.Serialize(state, JsonOptions));

        var result = new WorkspaceGuardResult
        {
            Mode = ModeBegin,
            ProceedAllowed = true,
            SafeStashPaths = safeStash,
            DirtyDurableStatePaths = Array.Empty<HostSyncWorkingTreeEntry>(),
            StashMessage = message,
            StashRef = stashRef,
            ConflictPaths = Array.Empty<string>(),
            StateFilePath = statePath,
            Summary = $"workspace-guard begin: stashed {safeStash.Length} path(s) at `{stashRef}` (state file: `{statePath}`). Run `automation workspace-guard --mode end --write` after the wake commits/pushes durable host-state."
        };
        EmitResult(writer, format, result);
        return 0;
    }

    private static int ExecuteEnd(IGitRunner runner, string statePath, bool write, string format, TextWriter writer)
    {
        if (!File.Exists(statePath))
        {
            var noState = new WorkspaceGuardResult
            {
                Mode = ModeEnd,
                ProceedAllowed = true,
                SafeStashPaths = Array.Empty<HostSyncWorkingTreeEntry>(),
                DirtyDurableStatePaths = Array.Empty<HostSyncWorkingTreeEntry>(),
                StashMessage = null,
                StashRef = null,
                ConflictPaths = Array.Empty<string>(),
                StateFilePath = statePath,
                Summary = $"workspace-guard end: no state file at `{statePath}`; nothing to restore (no-op)."
            };
            EmitResult(writer, format, noState);
            return 0;
        }

        WorkspaceGuardState state;
        try
        {
            state = JsonSerializer.Deserialize<WorkspaceGuardState>(File.ReadAllText(statePath), JsonOptions)
                ?? throw new InvalidOperationException("state file deserialized to null");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            writer.WriteLine($"workspace-guard end failed: state file at `{statePath}` could not be parsed: {exception.Message}");
            return 1;
        }

        if (!write)
        {
            var dryRun = new WorkspaceGuardResult
            {
                Mode = ModeEnd,
                ProceedAllowed = true,
                SafeStashPaths = state.StashedPaths.Select(p => new HostSyncWorkingTreeEntry { Path = p, Status = "  " }).ToArray(),
                DirtyDurableStatePaths = Array.Empty<HostSyncWorkingTreeEntry>(),
                StashMessage = state.StashMessage,
                StashRef = state.StashRef,
                ConflictPaths = Array.Empty<string>(),
                StateFilePath = statePath,
                Summary = $"workspace-guard end (dry-run): would restore stash `{state.StashRef}` ({state.StashedPaths.Count} path(s)). Re-run with --write to apply."
            };
            EmitResult(writer, format, dryRun);
            return 0;
        }

        var popResult = runner.Run(new[] { "stash", "pop", state.StashRef });
        if (popResult.ExitCode != 0)
        {
            var conflict = new WorkspaceGuardResult
            {
                Mode = ModeEnd,
                ProceedAllowed = false,
                SafeStashPaths = state.StashedPaths.Select(p => new HostSyncWorkingTreeEntry { Path = p, Status = "  " }).ToArray(),
                DirtyDurableStatePaths = Array.Empty<HostSyncWorkingTreeEntry>(),
                StashMessage = state.StashMessage,
                StashRef = state.StashRef,
                ConflictPaths = ParseConflictPaths(popResult.StandardOutput + "\n" + popResult.StandardError),
                StateFilePath = statePath,
                Summary = $"workspace-guard end CONFLICT: `git stash pop {state.StashRef}` exited {popResult.ExitCode}. Resolve conflicts manually then `git stash drop {state.StashRef}`. State file preserved at `{statePath}`. Do NOT claim the wake completed cleanly."
            };
            EmitResult(writer, format, conflict);
            return 1;
        }

        File.Delete(statePath);
        var result = new WorkspaceGuardResult
        {
            Mode = ModeEnd,
            ProceedAllowed = true,
            SafeStashPaths = state.StashedPaths.Select(p => new HostSyncWorkingTreeEntry { Path = p, Status = "  " }).ToArray(),
            DirtyDurableStatePaths = Array.Empty<HostSyncWorkingTreeEntry>(),
            StashMessage = state.StashMessage,
            StashRef = state.StashRef,
            ConflictPaths = Array.Empty<string>(),
            StateFilePath = statePath,
            Summary = $"workspace-guard end: restored {state.StashedPaths.Count} path(s) from `{state.StashRef}` and removed the state file."
        };
        EmitResult(writer, format, result);
        return 0;
    }

    private static IReadOnlyList<HostSyncWorkingTreeEntry> ScanWorkingTree(IGitRunner runner)
    {
        var statusResult = runner.Run(new[] { "status", "--porcelain" });
        var entries = new List<HostSyncWorkingTreeEntry>();
        foreach (var rawLine in statusResult.StandardOutput.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawLine.Length < 4) continue;
            var status = rawLine[..2];
            var path = rawLine[3..].Trim();
            var arrowIndex = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrowIndex >= 0)
            {
                path = path[(arrowIndex + 4)..].Trim();
            }
            entries.Add(new HostSyncWorkingTreeEntry { Path = path, Status = status });
        }
        return entries;
    }

    private static bool IsDurableStatePath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        return normalized.StartsWith(".intent-cli/", StringComparison.Ordinal)
            || normalized.StartsWith("intents/", StringComparison.Ordinal);
    }

    private static string BuildStashMessage()
    {
        var iso = ResolveNow().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
        return $"intent-cli/G306 workspace-guard {iso}";
    }

    private static DateTimeOffset ResolveNow() =>
        (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();

    private static string? ParseStashRef(string stashListOutput, string message)
    {
        // `stash list --format=%gd %s` produces: stash@{0} <message>
        foreach (var rawLine in stashListOutput.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var spaceIndex = rawLine.IndexOf(' ');
            if (spaceIndex <= 0) continue;
            var head = rawLine[..spaceIndex];
            var tail = rawLine[(spaceIndex + 1)..].Trim();
            if (tail.Equals(message, StringComparison.Ordinal)
                || tail.StartsWith("On ", StringComparison.Ordinal) && tail.Contains(message, StringComparison.Ordinal))
            {
                return head;
            }
        }
        return null;
    }

    private static IReadOnlyList<string> ParseConflictPaths(string output)
    {
        var paths = new List<string>();
        foreach (var rawLine in output.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("CONFLICT", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("error:", StringComparison.OrdinalIgnoreCase)
                || line.Contains("would be overwritten", StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(line);
            }
        }
        return paths;
    }

    private static void EmitResult(TextWriter writer, string format, WorkspaceGuardResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }
    }

    private static void WriteMarkdown(TextWriter writer, WorkspaceGuardResult result)
    {
        writer.WriteLine($"# automation workspace-guard (G306) — mode `{result.Mode}`");
        writer.WriteLine();
        writer.WriteLine($"- proceed_allowed: {(result.ProceedAllowed ? "yes" : "**no**")}");
        if (result.StashRef is not null)
        {
            writer.WriteLine($"- stash_ref: `{result.StashRef}`");
        }
        if (result.StashMessage is not null)
        {
            writer.WriteLine($"- stash_message: `{result.StashMessage}`");
        }
        writer.WriteLine($"- safe_stash_paths: {result.SafeStashPaths.Count}");
        writer.WriteLine($"- dirty_durable_state_paths: {result.DirtyDurableStatePaths.Count}");
        if (result.ConflictPaths.Count > 0)
        {
            writer.WriteLine($"- conflicts: {result.ConflictPaths.Count}");
        }
        writer.WriteLine();
        writer.WriteLine(result.Summary);
        if (result.SafeStashPaths.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Safe-stash paths");
            foreach (var entry in result.SafeStashPaths)
            {
                writer.WriteLine($"- `{entry.Status}` `{entry.Path}`");
            }
        }
        if (result.DirtyDurableStatePaths.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Dirty durable-state paths (refused)");
            foreach (var entry in result.DirtyDurableStatePaths)
            {
                writer.WriteLine($"- `{entry.Status}` `{entry.Path}`");
            }
        }
        if (result.ConflictPaths.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Conflicts");
            foreach (var path in result.ConflictPaths)
            {
                writer.WriteLine($"- {path}");
            }
        }
    }

    private static bool TryParseArguments(string[] args, out string mode, out bool write, out string format, out string error)
    {
        mode = string.Empty;
        write = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--mode":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--mode requires a value (plan, begin, or end).";
                        return false;
                    }
                    var requestedMode = args[++index].Trim();
                    if (!string.Equals(requestedMode, ModePlan, StringComparison.Ordinal)
                        && !string.Equals(requestedMode, ModeBegin, StringComparison.Ordinal)
                        && !string.Equals(requestedMode, ModeEnd, StringComparison.Ordinal))
                    {
                        error = $"--mode must be 'plan', 'begin', or 'end' (got '{requestedMode}').";
                        return false;
                    }
                    mode = requestedMode;
                    break;
                case "--write":
                    write = true;
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requestedFormat = args[++index].Trim();
                    if (!string.Equals(requestedFormat, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requestedFormat}').";
                        return false;
                    }
                    format = requestedFormat;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        if (string.IsNullOrEmpty(mode))
        {
            error = "automation workspace-guard requires '--mode plan|begin|end'.";
            return false;
        }
        return true;
    }
}

internal interface IGitRunner
{
    GitRunResult Run(IReadOnlyList<string> args);
}

internal sealed record GitRunResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
}

internal sealed class ShellGitRunner : IGitRunner
{
    private readonly string workingDirectory;
    public ShellGitRunner(string workingDirectory) => this.workingDirectory = workingDirectory;

    public GitRunResult Run(IReadOnlyList<string> args)
    {
        using var process = new Process();
        process.StartInfo.FileName = "git";
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new GitRunResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = stdout,
            StandardError = stderr
        };
    }
}

internal sealed record WorkspaceGuardResult
{
    [JsonPropertyName("mode")] public required string Mode { get; init; }
    [JsonPropertyName("proceed_allowed")] public required bool ProceedAllowed { get; init; }
    [JsonPropertyName("safe_stash_paths")] public required IReadOnlyList<HostSyncWorkingTreeEntry> SafeStashPaths { get; init; }
    [JsonPropertyName("dirty_durable_state_paths")] public required IReadOnlyList<HostSyncWorkingTreeEntry> DirtyDurableStatePaths { get; init; }
    [JsonPropertyName("stash_message")] public string? StashMessage { get; init; }
    [JsonPropertyName("stash_ref")] public string? StashRef { get; init; }
    [JsonPropertyName("conflict_paths")] public required IReadOnlyList<string> ConflictPaths { get; init; }
    [JsonPropertyName("state_file_path")] public required string StateFilePath { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}

internal sealed record WorkspaceGuardState
{
    [JsonPropertyName("stash_ref")] public required string StashRef { get; init; }
    [JsonPropertyName("stash_message")] public required string StashMessage { get; init; }
    [JsonPropertyName("created_at")] public required DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("stashed_paths")] public required IReadOnlyList<string> StashedPaths { get; init; }
}
