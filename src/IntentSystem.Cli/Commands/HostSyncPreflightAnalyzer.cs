using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G304: pure analyzer for the host-side pre-wake sync preflight. The host
/// review / next-slice loop must NOT proceed when the parent host repo is
/// behind <c>origin/main</c> or has uncommitted changes to durable
/// host-state files (<c>.intent-cli/queue-state.json</c>, <c>runs.jsonl</c>,
/// <c>.intent-cli/issues/&lt;unit&gt;/publish.yaml</c>, <c>intents/&lt;domain&gt;/**</c>),
/// because in either case the wake will see stale or partial state. Pure
/// analyzer keeps the logic deterministic and unit-testable; the command
/// layer captures the actual git output and feeds it in.
///
/// Classifications (terminal):
/// <list type="bullet">
/// <item><c>clean</c> — branch is up to date with origin and no durable-state changes are pending.</item>
/// <item><c>behind-origin</c> — branch is behind origin/main; the wake must pull before reading state.</item>
/// <item><c>dirty-host-durable-state</c> — uncommitted changes touch host durable-state files; refuse to proceed.</item>
/// <item><c>dirty-unrelated-submodule</c> — only a submodule pointer / non-durable-state file is dirty; reported separately so the operator can decide without silent overwrite.</item>
/// <item><c>dirty-mixed</c> — both dirty durable-state AND dirty unrelated changes are present; refuse to proceed and surface both buckets.</item>
/// </list>
/// </summary>
internal static class HostSyncPreflightAnalyzer
{
    public const string ClassificationClean = "clean";
    public const string ClassificationBehindOrigin = "behind-origin";
    public const string ClassificationDirtyDurableState = "dirty-host-durable-state";
    public const string ClassificationDirtyUnrelatedSubmodule = "dirty-unrelated-submodule";
    public const string ClassificationDirtyMixed = "dirty-mixed";

    private static readonly string[] DurableStatePathPrefixes =
    [
        ".intent-cli/queue-state.json",
        ".intent-cli/runs.jsonl",
        ".intent-cli/issues/",
        "intents/"
    ];

    public static HostSyncPreflightResult Analyze(
        string branch,
        int behindOriginCommits,
        IReadOnlyList<HostSyncWorkingTreeEntry> workingTreeEntries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        ArgumentNullException.ThrowIfNull(workingTreeEntries);
        if (behindOriginCommits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(behindOriginCommits), "behind-origin count must be non-negative.");
        }

        var dirtyDurable = new List<HostSyncWorkingTreeEntry>();
        var dirtyOther = new List<HostSyncWorkingTreeEntry>();
        foreach (var entry in workingTreeEntries)
        {
            if (IsDurableStatePath(entry.Path))
            {
                dirtyDurable.Add(entry);
            }
            else
            {
                dirtyOther.Add(entry);
            }
        }

        var clean = behindOriginCommits == 0 && dirtyDurable.Count == 0 && dirtyOther.Count == 0;
        var classification = ClassifyTerminal(behindOriginCommits, dirtyDurable.Count, dirtyOther.Count);

        // G306: when ONLY unrelated dirty paths are present (no dirty
        // durable host-state, no `dirty-mixed`), the host loop can proceed
        // through the safe-stash lane: stash the unrelated paths via
        // `automation workspace-guard --mode begin --write`, run the wake,
        // then restore via `--mode end --write`. Dirty durable host-state
        // and `dirty-mixed` remain hard stops because automation cannot
        // safely stash files it might also be about to mutate.
        var safeStashAllowed = dirtyDurable.Count == 0 && dirtyOther.Count > 0;
        var proceedAllowed = clean || safeStashAllowed;

        var nextSteps = BuildNextSteps(classification, behindOriginCommits, dirtyDurable, dirtyOther);

        return new HostSyncPreflightResult
        {
            Branch = branch,
            BehindOriginCommits = behindOriginCommits,
            Classification = classification,
            ProceedAllowed = proceedAllowed,
            SafeStashRequired = safeStashAllowed,
            SafeStashPaths = dirtyOther,
            DirtyDurableStatePaths = dirtyDurable,
            DirtyUnrelatedPaths = dirtyOther,
            Summary = BuildSummary(classification, branch, behindOriginCommits, dirtyDurable.Count, dirtyOther.Count),
            NextSteps = nextSteps
        };
    }

    private static bool IsDurableStatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }
        var normalized = path.Replace('\\', '/').TrimStart('/');
        foreach (var prefix in DurableStatePathPrefixes)
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static string ClassifyTerminal(int behindOriginCommits, int dirtyDurableCount, int dirtyOtherCount)
    {
        if (dirtyDurableCount > 0 && dirtyOtherCount > 0)
        {
            return ClassificationDirtyMixed;
        }
        if (dirtyDurableCount > 0)
        {
            return ClassificationDirtyDurableState;
        }
        if (dirtyOtherCount > 0)
        {
            return ClassificationDirtyUnrelatedSubmodule;
        }
        if (behindOriginCommits > 0)
        {
            return ClassificationBehindOrigin;
        }
        return ClassificationClean;
    }

    private static string BuildSummary(string classification, string branch, int behindCount, int dirtyDurableCount, int dirtyOtherCount)
    {
        return classification switch
        {
            ClassificationClean =>
                $"Host repo on `{branch}` is clean and up to date with origin. Pre-wake sync boundary satisfied.",
            ClassificationBehindOrigin =>
                $"Host repo on `{branch}` is behind origin by {behindCount} commit(s). Run `git pull --ff-only` before any read-only preflight (G304).",
            ClassificationDirtyDurableState =>
                $"Host repo on `{branch}` has {dirtyDurableCount} uncommitted change(s) to durable host-state files. Refusing to proceed (G304); commit/push or revert before the wake continues.",
            ClassificationDirtyUnrelatedSubmodule =>
                $"Host repo on `{branch}` has {dirtyOtherCount} uncommitted change(s) outside durable host-state. Use the G306 safe-stash lane (`automation workspace-guard --mode begin --write` before the wake, `--mode end --write` after) to preserve and restore the unrelated changes; durable host-state remains untouched.",
            ClassificationDirtyMixed =>
                $"Host repo on `{branch}` has {dirtyDurableCount} dirty durable-state path(s) AND {dirtyOtherCount} unrelated dirty path(s). Refusing to proceed (G304); both buckets must be reconciled before the wake.",
            _ => throw new InvalidOperationException($"Unknown host-sync classification '{classification}'.")
        };
    }

    private static IReadOnlyList<string> BuildNextSteps(
        string classification,
        int behindCommits,
        IReadOnlyList<HostSyncWorkingTreeEntry> dirtyDurable,
        IReadOnlyList<HostSyncWorkingTreeEntry> dirtyOther)
    {
        return classification switch
        {
            ClassificationClean => Array.Empty<string>(),
            ClassificationBehindOrigin => new[]
            {
                $"Run `git pull --ff-only` to fast-forward (host repo is {behindCommits} commit(s) behind).",
                "Re-run the host-loop read-only preflight (`automation doctor`, `host-review-preflight`) after the pull lands."
            },
            ClassificationDirtyDurableState => BuildDirtyDurableSteps(dirtyDurable),
            ClassificationDirtyUnrelatedSubmodule => new[]
            {
                "Run `intent-cli automation workspace-guard --mode begin --write` to preserve the unrelated path(s) (G306). " +
                "Regular files are placed into a git stash; submodule gitlink-only changes are preserved via the checkout lane (records the current submodule HEAD and checks out the parent-recorded commit). Unrelated paths: " + string.Join(", ", dirtyOther.Select(e => "`" + e.Path + "`")),
                "Run the host wake (review/closeout/next-slice). Durable host-state stays untouched throughout.",
                "After durable host-state is committed/pushed, run `intent-cli automation workspace-guard --mode end --write` to restore the unrelated changes (stash pop for regular files, submodule HEAD restore for gitlinks).",
                "If `--mode end` reports a conflict, do NOT claim the wake completed; surface the structured recovery instruction to the operator.",
                "If workspace-guard begin returns `proceed_allowed: false` (e.g. submodule has internal uncommitted changes), follow the manual recovery instructions in the output before re-running."
            },
            ClassificationDirtyMixed => BuildDirtyDurableSteps(dirtyDurable).Concat(new[]
            {
                "Also reconcile the unrelated dirty paths: " + string.Join(", ", dirtyOther.Select(e => "`" + e.Path + "`"))
            }).ToArray(),
            _ => throw new InvalidOperationException($"Unknown host-sync classification '{classification}'.")
        };
    }

    private static IReadOnlyList<string> BuildDirtyDurableSteps(IReadOnlyList<HostSyncWorkingTreeEntry> dirtyDurable)
    {
        return new[]
        {
            "Inspect the dirty durable-state paths: " + string.Join(", ", dirtyDurable.Select(e => "`" + e.Path + "`")),
            "If the changes are wanted, commit and push them BEFORE the wake continues (host durable state is operator-visible).",
            "If the changes are stale local-only edits, revert them before the wake continues; do not let the loop overwrite parent state.",
            "Re-run the read-only preflight once the working tree is clean."
        };
    }
}

internal sealed record HostSyncWorkingTreeEntry
{
    /// <summary>git status --porcelain path (relative to repo root).</summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>2-character porcelain status code (e.g. " M", "??", "AM").</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }
}

internal sealed record HostSyncPreflightResult
{
    [JsonPropertyName("branch")]
    public required string Branch { get; init; }

    [JsonPropertyName("behind_origin_commits")]
    public required int BehindOriginCommits { get; init; }

    [JsonPropertyName("classification")]
    public required string Classification { get; init; }

    [JsonPropertyName("proceed_allowed")]
    public required bool ProceedAllowed { get; init; }

    /// <summary>
    /// G306: true when the wake must run through the safe-stash lane
    /// (`automation workspace-guard --mode begin --write` before the wake,
    /// `--mode end --write` after) before reading durable host-state.
    /// Implies <c>ProceedAllowed = true</c>.
    /// </summary>
    [JsonPropertyName("safe_stash_required")]
    public bool SafeStashRequired { get; init; }

    /// <summary>
    /// G306: paths the safe-stash lane will record into the workspace
    /// guard state file. Empty unless <see cref="SafeStashRequired"/> is
    /// true.
    /// </summary>
    [JsonPropertyName("safe_stash_paths")]
    public IReadOnlyList<HostSyncWorkingTreeEntry> SafeStashPaths { get; init; } = Array.Empty<HostSyncWorkingTreeEntry>();

    [JsonPropertyName("dirty_durable_state_paths")]
    public required IReadOnlyList<HostSyncWorkingTreeEntry> DirtyDurableStatePaths { get; init; }

    [JsonPropertyName("dirty_unrelated_paths")]
    public required IReadOnlyList<HostSyncWorkingTreeEntry> DirtyUnrelatedPaths { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("next_steps")]
    public required IReadOnlyList<string> NextSteps { get; init; }
}
