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
        var nonDurable = entries.Where(e => !DurableHostStatePathClassifier.IsDurableHostStatePath(e.Path)).ToArray();
        var durable = entries.Where(e => DurableHostStatePathClassifier.IsDurableHostStatePath(e.Path)).ToArray();

        // G352: classify non-durable entries to surface gitlink vs regular paths in the plan.
        var (gitlinkOnly, submoduleInternalDirty, nestedPointerDrift, regular) = ClassifyNonDurableEntries(runner, nonDurable);

        var stashMessage = regular.Count > 0 ? BuildStashMessage() : null;
        var totalNonDurable = nonDurable.Length;

        var proceedAllowed = durable.Length == 0 && submoduleInternalDirty.Count == 0;
        string summary;
        if (durable.Length > 0)
            summary = $"workspace-guard plan: {durable.Length} dirty durable-state path(s) present; safe-stash refused. Reconcile durable host-state through the G304 fail-closed path before re-running.";
        else if (submoduleInternalDirty.Count > 0)
            summary = $"workspace-guard plan: {submoduleInternalDirty.Count} submodule(s) have internal uncommitted changes ({string.Join(", ", submoduleInternalDirty.Select(e => e.Path))}); checkout-lane refused. Manually reconcile submodule internal changes before re-running.";
        else if (nestedPointerDrift.Count > 0 && regular.Count > 0)
            summary = $"workspace-guard plan: {nestedPointerDrift.Count} aligned host gitlink(s) contain only clean nested-pointer drift and {regular.Count} regular path(s). Leave the owning and nested submodule paths untouched; only the regular paths use the stash lane on `--mode begin --write`.";
        else if (nestedPointerDrift.Count > 0)
            summary = $"workspace-guard plan: {nestedPointerDrift.Count} aligned host gitlink(s) contain only clean nested-pointer drift; leave the owning and nested submodule paths untouched and proceed without stash or checkout.";
        else if (gitlinkOnly.Count > 0 && regular.Count > 0)
            summary = $"workspace-guard plan: {gitlinkOnly.Count} gitlink-only path(s) (checkout lane) + {regular.Count} regular path(s) (stash lane) on `--mode begin --write`.";
        else if (gitlinkOnly.Count > 0)
            summary = $"workspace-guard plan: {gitlinkOnly.Count} gitlink-only path(s) would be preserved via checkout lane on `--mode begin --write`.";
        else if (regular.Count > 0)
            summary = $"workspace-guard plan: {regular.Count} unrelated dirty path(s) would be stashed under message '{stashMessage}' on `--mode begin --write`.";
        else
            summary = "workspace-guard plan: working tree is clean; no stash required.";

        var result = new WorkspaceGuardResult
        {
            Mode = ModePlan,
            ProceedAllowed = proceedAllowed,
            SafeStashPaths = regular.ToArray(),
            GitlinkPaths = gitlinkOnly.Select(t => t.Entry).ToArray(),
            NestedPointerDriftSubmodules = nestedPointerDrift,
            DirtyDurableStatePaths = durable,
            StashMessage = stashMessage,
            StashRef = null,
            ConflictPaths = Array.Empty<string>(),
            StateFilePath = Path.Combine(context.RepoRoot, StateFileRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            Summary = summary
        };

        EmitResult(writer, format, result);
        return (durable.Length > 0 || submoduleInternalDirty.Count > 0) ? 1 : 0;
    }

    private static int ExecuteBegin(IGitRunner runner, CliContext context, string statePath, bool write, string format, TextWriter writer)
    {
        var entries = ScanWorkingTree(runner);
        var nonDurable = entries.Where(e => !DurableHostStatePathClassifier.IsDurableHostStatePath(e.Path)).ToArray();
        var durable = entries.Where(e => DurableHostStatePathClassifier.IsDurableHostStatePath(e.Path)).ToArray();

        if (durable.Length > 0)
        {
            var refusal = new WorkspaceGuardResult
            {
                Mode = ModeBegin,
                ProceedAllowed = false,
                SafeStashPaths = nonDurable,
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

        // G352: classify non-durable entries: gitlink-only, submodule-internal-dirty, regular.
        var (gitlinkOnly, submoduleInternalDirty, nestedPointerDrift, regularStash) = ClassifyNonDurableEntries(runner, nonDurable);

        // G352: refuse if any submodule has internal uncommitted changes — we cannot safely auto-reset those.
        if (submoduleInternalDirty.Count > 0)
        {
            var paths = string.Join(", ", submoduleInternalDirty.Select(e => e.Path));
            var refusal = new WorkspaceGuardResult
            {
                Mode = ModeBegin,
                ProceedAllowed = false,
                SafeStashPaths = regularStash.ToArray(),
                GitlinkPaths = submoduleInternalDirty.ToArray(),
                DirtyDurableStatePaths = Array.Empty<HostSyncWorkingTreeEntry>(),
                StashMessage = null,
                StashRef = null,
                ConflictPaths = Array.Empty<string>(),
                StateFilePath = statePath,
                Summary = $"workspace-guard begin refused: {submoduleInternalDirty.Count} submodule(s) have internal uncommitted changes ({paths}). " +
                          "Cannot auto-handle; commit or stash the changes inside each submodule manually before re-running. Do NOT claim the wake can continue."
            };
            EmitResult(writer, format, refusal);
            return 1;
        }

        if (gitlinkOnly.Count == 0 && regularStash.Count == 0)
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
                NestedPointerDriftSubmodules = nestedPointerDrift,
                Summary = nestedPointerDrift.Count == 0
                    ? "workspace-guard begin: working tree is clean; no stash created."
                    : $"workspace-guard begin: {nestedPointerDrift.Count} aligned host gitlink(s) have only clean nested-pointer drift; no stash or checkout was run and the foreign paths remain untouched."
            };
            EmitResult(writer, format, noop);
            return 0;
        }

        var message = regularStash.Count > 0 ? BuildStashMessage() : null;
        if (!write)
        {
            var dryRun = new WorkspaceGuardResult
            {
                Mode = ModeBegin,
                ProceedAllowed = true,
                SafeStashPaths = regularStash.ToArray(),
                GitlinkPaths = gitlinkOnly.Select(t => t.Entry).ToArray(),
                NestedPointerDriftSubmodules = nestedPointerDrift,
                DirtyDurableStatePaths = Array.Empty<HostSyncWorkingTreeEntry>(),
                StashMessage = message,
                StashRef = null,
                ConflictPaths = Array.Empty<string>(),
                StateFilePath = statePath,
                Summary = BuildBeginDryRunSummary(gitlinkOnly.Count, nestedPointerDrift.Count, regularStash.Count, message)
            };
            EmitResult(writer, format, dryRun);
            return 0;
        }

        // --- Write mode ---

        // G352: Step 1 — collect original submodule HEADs (read-only, no mutations yet).
        // Separating this from the checkout loop makes the preliminary state write below safe.
        var gitlinkRestoreEntries = new List<GitlinkRestoreEntry>();
        foreach (var (entry, _) in gitlinkOnly)
        {
            var originalHead = GetCurrentSubmoduleHead(runner, entry.Path);
            if (originalHead is null)
            {
                var failure = new WorkspaceGuardResult
                {
                    Mode = ModeBegin,
                    ProceedAllowed = false,
                    SafeStashPaths = regularStash.ToArray(),
                    GitlinkPaths = gitlinkOnly.Select(t => t.Entry).ToArray(),
                    NestedPointerDriftSubmodules = nestedPointerDrift,
                    DirtyDurableStatePaths = Array.Empty<HostSyncWorkingTreeEntry>(),
                    StashMessage = message,
                    StashRef = null,
                    ConflictPaths = Array.Empty<string>(),
                    StateFilePath = statePath,
                    Summary = $"workspace-guard begin failed: could not read HEAD of submodule `{entry.Path}`. Inspect the submodule and retry."
                };
                EmitResult(writer, format, failure);
                return 1;
            }
            gitlinkRestoreEntries.Add(new GitlinkRestoreEntry { Path = entry.Path, OriginalHead = originalHead });
        }

        // G352: Step 2 — for mixed lanes (gitlink + stash), write a preliminary state file
        // with the gitlink restore entries BEFORE any repo mutation. If the stash lane fails
        // after gitlinks are checked out, this guarantees the operator has a durable restore
        // instruction and can run `workspace-guard --mode end --write` to recover.
        bool hasMixedLanes = gitlinkOnly.Count > 0 && regularStash.Count > 0;
        if (hasMixedLanes)
        {
            var prelimState = new WorkspaceGuardState
            {
                StashRef = null,
                StashMessage = null,
                CreatedAt = ResolveNow(),
                StashedPaths = Array.Empty<string>(),
                GitlinkRestorePaths = gitlinkRestoreEntries
            };
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            File.WriteAllText(statePath, JsonSerializer.Serialize(prelimState, JsonOptions));
        }

        // G352: Step 3 — checkout each gitlink to the parent-recorded commit.
        // The preliminary state file (step 2) ensures any failure here is recoverable via `end`.
        for (var i = 0; i < gitlinkOnly.Count; i++)
        {
            var (entry, parentCommit) = gitlinkOnly[i];
            var checkoutResult = runner.Run(new[] { "-C", entry.Path, "checkout", parentCommit });
            if (checkoutResult.ExitCode != 0)
            {
                var recoverySuffix = hasMixedLanes
                    ? $" Gitlink restore state is at `{statePath}`; run `automation workspace-guard --mode end --write` to restore already-checked-out gitlinks."
                    : $" Original submodule HEAD: {gitlinkRestoreEntries[i].OriginalHead}. No state file was written; retry after resolving the submodule.";
                var failure = new WorkspaceGuardResult
                {
                    Mode = ModeBegin,
                    ProceedAllowed = false,
                    SafeStashPaths = regularStash.ToArray(),
                    GitlinkPaths = gitlinkOnly.Select(t => t.Entry).ToArray(),
                    DirtyDurableStatePaths = Array.Empty<HostSyncWorkingTreeEntry>(),
                    StashMessage = message,
                    StashRef = null,
                    ConflictPaths = Array.Empty<string>(),
                    StateFilePath = statePath,
                    Summary = $"workspace-guard begin failed: `git -C {entry.Path} checkout {parentCommit}` exited {checkoutResult.ExitCode}: {checkoutResult.StandardError.Trim()}.{recoverySuffix}"
                };
                EmitResult(writer, format, failure);
                return 1;
            }
        }

        // Step 4 — stash regular dirty files.
        string? stashRef = null;
        if (regularStash.Count > 0)
        {
            var pushArgs = new List<string> { "stash", "push", "--include-untracked", "-m", message!, "--" };
            pushArgs.AddRange(regularStash.Select(e => e.Path));
            var pushResult = runner.Run(pushArgs);
            if (pushResult.ExitCode != 0)
            {
                var recoverySuffix = hasMixedLanes
                    ? $" Gitlink restore state is at `{statePath}`; run `automation workspace-guard --mode end --write` to restore the checked-out gitlinks."
                    : string.Empty;
                var failure = new WorkspaceGuardResult
                {
                    Mode = ModeBegin,
                    ProceedAllowed = false,
                    SafeStashPaths = regularStash.ToArray(),
                    GitlinkPaths = gitlinkOnly.Select(t => t.Entry).ToArray(),
                    DirtyDurableStatePaths = Array.Empty<HostSyncWorkingTreeEntry>(),
                    StashMessage = message,
                    StashRef = null,
                    ConflictPaths = Array.Empty<string>(),
                    StateFilePath = statePath,
                    Summary = $"workspace-guard begin failed: `git stash push` exited {pushResult.ExitCode}: {pushResult.StandardError.Trim()}.{recoverySuffix}"
                };
                EmitResult(writer, format, failure);
                return 1;
            }

            // Stash list lookup: find the most recent stash matching our message.
            var listResult = runner.Run(new[] { "stash", "list", "--format=%gd %s" });
            stashRef = ParseStashRef(listResult.StandardOutput, message!);
            if (listResult.ExitCode != 0 || stashRef is null)
            {
                var recoverySuffix = hasMixedLanes
                    ? $" Gitlink restore state is at `{statePath}`; run `automation workspace-guard --mode end --write` to restore gitlinks."
                    : string.Empty;
                var failure = new WorkspaceGuardResult
                {
                    Mode = ModeBegin,
                    ProceedAllowed = false,
                    SafeStashPaths = regularStash.ToArray(),
                    GitlinkPaths = gitlinkOnly.Select(t => t.Entry).ToArray(),
                    DirtyDurableStatePaths = Array.Empty<HostSyncWorkingTreeEntry>(),
                    StashMessage = message,
                    StashRef = null,
                    ConflictPaths = Array.Empty<string>(),
                    StateFilePath = statePath,
                    Summary = (listResult.ExitCode != 0
                        ? $"workspace-guard begin failed after stash push: `git stash list` exited {listResult.ExitCode}: {listResult.StandardError.Trim()}. Inspect `git stash list` and restore the stash with message '{message}' before continuing."
                        : $"workspace-guard begin failed after stash push: could not identify a stash entry with message '{message}'. Inspect `git stash list` and restore that stash before continuing.") + recoverySuffix
                };
                EmitResult(writer, format, failure);
                return 1;
            }
        }

        // Step 5 — write final state file (overwrites preliminary if hasMixedLanes).
        var state = new WorkspaceGuardState
        {
            StashRef = stashRef,
            StashMessage = message,
            CreatedAt = ResolveNow(),
            StashedPaths = regularStash.Select(e => e.Path).ToArray(),
            GitlinkRestorePaths = gitlinkRestoreEntries
        };
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        File.WriteAllText(statePath, JsonSerializer.Serialize(state, JsonOptions));

        var successResult = new WorkspaceGuardResult
        {
            Mode = ModeBegin,
            ProceedAllowed = true,
            SafeStashPaths = regularStash.ToArray(),
            GitlinkPaths = gitlinkOnly.Select(t => t.Entry).ToArray(),
            NestedPointerDriftSubmodules = nestedPointerDrift,
            DirtyDurableStatePaths = Array.Empty<HostSyncWorkingTreeEntry>(),
            StashMessage = message,
            StashRef = stashRef,
            ConflictPaths = Array.Empty<string>(),
            StateFilePath = statePath,
            Summary = BuildBeginSuccessSummary(gitlinkRestoreEntries.Count, regularStash.Count, stashRef, statePath)
        };
        EmitResult(writer, format, successResult);
        return 0;
    }

    private static string BuildBeginDryRunSummary(int gitlinkCount, int nestedPointerDriftCount, int regularCount, string? message)
    {
        var nestedSuffix = nestedPointerDriftCount > 0
            ? $" Leave {nestedPointerDriftCount} clean nested-pointer-drift submodule(s) untouched."
            : string.Empty;
        if (gitlinkCount > 0 && regularCount > 0)
            return $"workspace-guard begin (dry-run): would preserve {gitlinkCount} gitlink path(s) via checkout lane and stash {regularCount} regular path(s) under '{message}'.{nestedSuffix} Re-run with --write to apply.";
        if (gitlinkCount > 0)
            return $"workspace-guard begin (dry-run): would preserve {gitlinkCount} gitlink-only path(s) via checkout lane.{nestedSuffix} Re-run with --write to apply.";
        return $"workspace-guard begin (dry-run): would stash {regularCount} path(s) under '{message}'.{nestedSuffix} Re-run with --write to apply.";
    }

    private static string BuildBeginSuccessSummary(int gitlinkCount, int regularCount, string? stashRef, string statePath)
    {
        if (gitlinkCount > 0 && regularCount > 0)
            return $"workspace-guard begin: preserved {gitlinkCount} gitlink path(s) via checkout lane and stashed {regularCount} regular path(s) at `{stashRef}` (state file: `{statePath}`). Run `automation workspace-guard --mode end --write` after the wake commits/pushes durable host-state.";
        if (gitlinkCount > 0)
            return $"workspace-guard begin: preserved {gitlinkCount} gitlink-only path(s) via checkout lane (state file: `{statePath}`). Run `automation workspace-guard --mode end --write` after the wake commits/pushes durable host-state.";
        return $"workspace-guard begin: stashed {regularCount} path(s) at `{stashRef}` (state file: `{statePath}`). Run `automation workspace-guard --mode end --write` after the wake commits/pushes durable host-state.";
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
            var dryRunSummary = BuildEndDryRunSummary(state);
            var dryRun = new WorkspaceGuardResult
            {
                Mode = ModeEnd,
                ProceedAllowed = true,
                SafeStashPaths = state.StashedPaths.Select(p => new HostSyncWorkingTreeEntry { Path = p, Status = "  " }).ToArray(),
                GitlinkPaths = state.GitlinkRestorePaths.Select(e => new HostSyncWorkingTreeEntry { Path = e.Path, Status = "  " }).ToArray(),
                DirtyDurableStatePaths = Array.Empty<HostSyncWorkingTreeEntry>(),
                StashMessage = state.StashMessage,
                StashRef = state.StashRef,
                ConflictPaths = Array.Empty<string>(),
                StateFilePath = statePath,
                Summary = dryRunSummary
            };
            EmitResult(writer, format, dryRun);
            return 0;
        }

        // --- Write mode ---

        // Stash lane: pop the stash if one was created.
        if (state.StashRef is not null)
        {
            var popResult = runner.Run(new[] { "stash", "pop", state.StashRef });
            if (popResult.ExitCode != 0)
            {
                var conflict = new WorkspaceGuardResult
                {
                    Mode = ModeEnd,
                    ProceedAllowed = false,
                    SafeStashPaths = state.StashedPaths.Select(p => new HostSyncWorkingTreeEntry { Path = p, Status = "  " }).ToArray(),
                    GitlinkPaths = state.GitlinkRestorePaths.Select(e => new HostSyncWorkingTreeEntry { Path = e.Path, Status = "  " }).ToArray(),
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
        }

        // G352: gitlink restore lane — return each submodule to its original HEAD.
        foreach (var gitlinkEntry in state.GitlinkRestorePaths)
        {
            var restoreResult = runner.Run(new[] { "-C", gitlinkEntry.Path, "checkout", gitlinkEntry.OriginalHead });
            if (restoreResult.ExitCode != 0)
            {
                var failure = new WorkspaceGuardResult
                {
                    Mode = ModeEnd,
                    ProceedAllowed = false,
                    SafeStashPaths = state.StashedPaths.Select(p => new HostSyncWorkingTreeEntry { Path = p, Status = "  " }).ToArray(),
                    GitlinkPaths = state.GitlinkRestorePaths.Select(e => new HostSyncWorkingTreeEntry { Path = e.Path, Status = "  " }).ToArray(),
                    DirtyDurableStatePaths = Array.Empty<HostSyncWorkingTreeEntry>(),
                    StashMessage = state.StashMessage,
                    StashRef = state.StashRef,
                    ConflictPaths = new[] { $"git -C {gitlinkEntry.Path} checkout {gitlinkEntry.OriginalHead}: exit {restoreResult.ExitCode}" },
                    StateFilePath = statePath,
                    Summary = $"workspace-guard end CONFLICT: `git -C {gitlinkEntry.Path} checkout {gitlinkEntry.OriginalHead}` exited {restoreResult.ExitCode}. Restore the submodule manually to commit `{gitlinkEntry.OriginalHead}`. State file preserved at `{statePath}`. Do NOT claim the wake completed cleanly."
                };
                EmitResult(writer, format, failure);
                return 1;
            }
        }

        File.Delete(statePath);
        var result = new WorkspaceGuardResult
        {
            Mode = ModeEnd,
            ProceedAllowed = true,
            SafeStashPaths = state.StashedPaths.Select(p => new HostSyncWorkingTreeEntry { Path = p, Status = "  " }).ToArray(),
            GitlinkPaths = state.GitlinkRestorePaths.Select(e => new HostSyncWorkingTreeEntry { Path = e.Path, Status = "  " }).ToArray(),
            DirtyDurableStatePaths = Array.Empty<HostSyncWorkingTreeEntry>(),
            StashMessage = state.StashMessage,
            StashRef = state.StashRef,
            ConflictPaths = Array.Empty<string>(),
            StateFilePath = statePath,
            Summary = BuildEndSuccessSummary(state)
        };
        EmitResult(writer, format, result);
        return 0;
    }

    private static string BuildEndDryRunSummary(WorkspaceGuardState state)
    {
        var parts = new List<string>();
        if (state.StashRef is not null)
            parts.Add($"restore stash `{state.StashRef}` ({state.StashedPaths.Count} path(s))");
        if (state.GitlinkRestorePaths.Count > 0)
            parts.Add($"restore {state.GitlinkRestorePaths.Count} gitlink path(s) to original HEAD(s)");
        return parts.Count == 0
            ? "workspace-guard end (dry-run): state file present but nothing recorded to restore. Re-run with --write to apply."
            : $"workspace-guard end (dry-run): would {string.Join(" and ", parts)}. Re-run with --write to apply.";
    }

    private static string BuildEndSuccessSummary(WorkspaceGuardState state)
    {
        var parts = new List<string>();
        if (state.StashedPaths.Count > 0)
            parts.Add($"restored {state.StashedPaths.Count} path(s) from `{state.StashRef}`");
        if (state.GitlinkRestorePaths.Count > 0)
            parts.Add($"restored {state.GitlinkRestorePaths.Count} gitlink path(s) to original HEAD(s)");
        var body = parts.Count > 0 ? string.Join(" and ", parts) : "nothing to restore";
        return $"workspace-guard end: {body} and removed the state file.";
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
        writer.WriteLine($"- gitlink_paths: {result.GitlinkPaths.Count}");
        writer.WriteLine($"- nested_pointer_drift_submodules: {result.NestedPointerDriftSubmodules.Count}");
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
        if (result.GitlinkPaths.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Gitlink paths (checkout lane)");
            foreach (var entry in result.GitlinkPaths)
            {
                writer.WriteLine($"- `{entry.Status}` `{entry.Path}`");
            }
        }
        if (result.NestedPointerDriftSubmodules.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Clean nested-pointer drift (left untouched)");
            foreach (var drift in result.NestedPointerDriftSubmodules)
            {
                writer.WriteLine($"- owning submodule `{drift.OwningSubmodulePath}` (parent-recorded `{drift.ParentRecordedCommit}`)");
                foreach (var nestedPath in drift.UntouchedNestedPaths)
                {
                    writer.WriteLine($"  - untouched nested path `{nestedPath}`");
                }
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

    // ------------------------------------------------------------------ G352 helpers

    /// <summary>G352: returns the current HEAD sha of the submodule, or null on failure.</summary>
    private static string? GetCurrentSubmoduleHead(IGitRunner runner, string submodulePath)
    {
        var result = runner.Run(new[] { "-C", submodulePath, "rev-parse", "HEAD" });
        return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
    }

    /// <summary>
    /// G791: classifies non-durable dirty entries into gitlink-only,
    /// clean nested-pointer drift, submodule-internal-dirty, and regular buckets.
    /// </summary>
    private static (
        IReadOnlyList<(HostSyncWorkingTreeEntry Entry, string ParentCommit)> GitlinkOnly,
        IReadOnlyList<HostSyncWorkingTreeEntry> SubmoduleInternalDirty,
        IReadOnlyList<NestedPointerDriftSubmodule> NestedPointerDrift,
        IReadOnlyList<HostSyncWorkingTreeEntry> Regular)
    ClassifyNonDurableEntries(IGitRunner runner, IReadOnlyList<HostSyncWorkingTreeEntry> nonDurableEntries)
    {
        var gitlinkOnly = new List<(HostSyncWorkingTreeEntry, string)>();
        var submoduleInternalDirty = new List<HostSyncWorkingTreeEntry>();
        var nestedPointerDrift = new List<NestedPointerDriftSubmodule>();
        var regular = new List<HostSyncWorkingTreeEntry>();

        foreach (var entry in nonDurableEntries)
        {
            if (NestedSubmodulePointerDriftDetector.TryGetParentRecordedGitlinkCommit(runner, entry.Path, out var parentCommit))
            {
                var submoduleStatus = runner.Run(["-C", entry.Path, "status", "--porcelain"]);
                if (submoduleStatus.ExitCode == 0 && string.IsNullOrWhiteSpace(submoduleStatus.StandardOutput))
                    gitlinkOnly.Add((entry, parentCommit));
                else if (submoduleStatus.ExitCode == 0
                    && NestedSubmodulePointerDriftDetector.TryDetect(
                        runner,
                        entry.Path,
                        parentCommit,
                        submoduleStatus.StandardOutput,
                        out var drift))
                    nestedPointerDrift.Add(drift);
                else
                    submoduleInternalDirty.Add(entry);
            }
            else
            {
                regular.Add(entry);
            }
        }
        return (gitlinkOnly, submoduleInternalDirty, nestedPointerDrift, regular);
    }

    // ------------------------------------------------------------------ arg parse

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
        process.StartInfo.StandardOutputEncoding = ProcessOutputEncoding.Utf8NoBom;
        process.StartInfo.StandardErrorEncoding = ProcessOutputEncoding.Utf8NoBom;
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

    /// <summary>
    /// G352: gitlink-only submodule paths that the workspace-guard handles
    /// via the checkout-based preservation lane (not git stash).
    /// </summary>
    [JsonPropertyName("gitlink_paths")]
    public IReadOnlyList<HostSyncWorkingTreeEntry> GitlinkPaths { get; init; } = Array.Empty<HostSyncWorkingTreeEntry>();

    /// <summary>
    /// G791: host gitlink is aligned and the only dirt is clean nested-submodule
    /// pointer drift. These foreign paths deliberately remain untouched.
    /// </summary>
    [JsonPropertyName("nested_pointer_drift_submodules")]
    public IReadOnlyList<NestedPointerDriftSubmodule> NestedPointerDriftSubmodules { get; init; } = Array.Empty<NestedPointerDriftSubmodule>();
}

/// <summary>G352: gitlink restore entry recording the original submodule HEAD so
/// <c>workspace-guard end</c> can return the submodule to the operator's commit.</summary>
internal sealed record GitlinkRestoreEntry
{
    [JsonPropertyName("path")] public required string Path { get; init; }
    [JsonPropertyName("original_head")] public required string OriginalHead { get; init; }
}

internal sealed record WorkspaceGuardState
{
    /// <summary>Stash ref created by <c>git stash push</c>; null when only gitlink paths were preserved.</summary>
    [JsonPropertyName("stash_ref")] public string? StashRef { get; init; }
    /// <summary>Stash message used during <c>git stash push</c>; null when only gitlink paths were preserved.</summary>
    [JsonPropertyName("stash_message")] public string? StashMessage { get; init; }
    [JsonPropertyName("created_at")] public required DateTimeOffset CreatedAt { get; init; }
    /// <summary>Paths that were placed into the git stash (regular files / untracked).</summary>
    [JsonPropertyName("stashed_paths")] public IReadOnlyList<string> StashedPaths { get; init; } = Array.Empty<string>();
    /// <summary>G352: submodule gitlink paths preserved via checkout lane; restored on <c>end</c>.</summary>
    [JsonPropertyName("gitlink_restore_paths")]
    public IReadOnlyList<GitlinkRestoreEntry> GitlinkRestorePaths { get; init; } = Array.Empty<GitlinkRestoreEntry>();
}
