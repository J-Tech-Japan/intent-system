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
/// <item><c>submodule-checkout-mismatch</c> — G357: parent gitlink has advanced (typically after git pull)
///   but the submodule working tree checkout is still at the old commit; the submodule itself is clean so
///   <c>git submodule update --init &lt;path&gt;</c> is the deterministic safe repair.</item>
/// </list>
/// </summary>
internal static class HostSyncPreflightAnalyzer
{
    public const string ClassificationClean = "clean";
    public const string ClassificationBehindOrigin = "behind-origin";

    /// <summary>
    /// G400: local <c>main</c> has a local-only commit AND <c>origin/main</c>
    /// has advanced — the branches have diverged, so <c>git pull --ff-only</c>
    /// is blocked. This is a PRE-MUTATION operator stop: no review/closeout/
    /// publish mutation occurred, so the wake must NOT append a durable abort
    /// summary to <c>.intent-cli/runs.jsonl</c>. Repeated wakes in the same
    /// diverged state are idempotent w.r.t. durable host-state. Resolution
    /// requires an operator decision (push, relocate, or explicitly authorize
    /// discard of the local-only commit) — automation never resets or discards
    /// the foreign commit.
    /// </summary>
    public const string ClassificationFfBlocked = "ff-blocked";

    public const string ClassificationDirtyDurableState = "dirty-host-durable-state";
    public const string ClassificationDirtyUnrelatedSubmodule = "dirty-unrelated-submodule";
    public const string ClassificationDirtyMixed = "dirty-mixed";

    /// <summary>
    /// G357: parent gitlink has advanced but submodule working tree checkout is stale.
    /// All paths in this classification have no local uncommitted changes inside the
    /// submodule, so <c>git submodule update --init &lt;path&gt;</c> is the safe repair.
    /// </summary>
    public const string ClassificationSubmoduleCheckoutMismatch = "submodule-checkout-mismatch";

    private static readonly string[] DurableStatePathPrefixes =
    [
        ".intent-cli/queue-state.json",
        ".intent-cli/runs.jsonl",
        ".intent-cli/issues/",
        "intents/"
    ];

    /// <param name="submoduleGitlinkMismatchPaths">
    /// G357: paths where the parent gitlink has advanced but the submodule working tree
    /// checkout is stale and the submodule itself is clean (so <c>git submodule update
    /// --init &lt;path&gt;</c> is the safe repair). When all dirty-other paths appear in
    /// this list and no dirty-durable paths exist, the classification is
    /// <c>submodule-checkout-mismatch</c> rather than <c>dirty-unrelated-submodule</c>.
    /// </param>
    public static HostSyncPreflightResult Analyze(
        string branch,
        int behindOriginCommits,
        IReadOnlyList<HostSyncWorkingTreeEntry> workingTreeEntries,
        IReadOnlyList<string>? submoduleGitlinkMismatchPaths = null,
        int aheadOriginCommits = 0,
        IReadOnlyList<HostSyncCommitInfo>? localOnlyCommits = null,
        IReadOnlyList<HostSyncCommitInfo>? originAheadCommits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        ArgumentNullException.ThrowIfNull(workingTreeEntries);
        if (behindOriginCommits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(behindOriginCommits), "behind-origin count must be non-negative.");
        }
        if (aheadOriginCommits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(aheadOriginCommits), "ahead-origin count must be non-negative.");
        }

        var localOnly = localOnlyCommits ?? Array.Empty<HostSyncCommitInfo>();
        var originAhead = originAheadCommits ?? Array.Empty<HostSyncCommitInfo>();

        var mismatchSet = submoduleGitlinkMismatchPaths is { Count: > 0 }
            ? new HashSet<string>(submoduleGitlinkMismatchPaths, StringComparer.Ordinal)
            : null;

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

        // G357: submodule-checkout-mismatch applies when ALL dirty-other paths are
        // known gitlink mismatches (parent gitlink advanced, submodule itself is clean).
        var allOtherAreGitlinkMismatches = mismatchSet is not null
            && dirtyOther.Count > 0
            && dirtyOther.All(e => mismatchSet.Contains(e.Path));

        var clean = behindOriginCommits == 0 && dirtyDurable.Count == 0 && dirtyOther.Count == 0;
        var classification = ClassifyTerminal(
            behindOriginCommits, aheadOriginCommits, dirtyDurable.Count, dirtyOther.Count, allOtherAreGitlinkMismatches);

        // G306: when ONLY unrelated dirty paths are present (no dirty
        // durable host-state, no `dirty-mixed`, no submodule-checkout-mismatch),
        // the host loop can proceed through the safe-stash lane: stash the unrelated
        // paths via `automation workspace-guard --mode begin --write`, run the wake,
        // then restore via `--mode end --write`. Dirty durable host-state and
        // `dirty-mixed` remain hard stops because automation cannot safely stash files
        // it might also be about to mutate. G357 submodule-checkout-mismatch uses the
        // `git submodule update --init` lane instead — NOT the safe-stash lane.
        var safeStashAllowed = dirtyDurable.Count == 0
            && dirtyOther.Count > 0
            && !allOtherAreGitlinkMismatches;
        var proceedAllowed = clean || safeStashAllowed;

        // SubmoduleCheckoutMismatchPaths is only populated for the
        // submodule-checkout-mismatch classification; dirty-mixed and other
        // classifications that also touch dirtyOther do not surface this field.
        var mismatchPathsList = string.Equals(classification, ClassificationSubmoduleCheckoutMismatch, StringComparison.Ordinal)
            ? dirtyOther.Select(e => e.Path).ToArray()
            : Array.Empty<string>();

        var isFfBlocked = string.Equals(classification, ClassificationFfBlocked, StringComparison.Ordinal);
        var nextSteps = BuildNextSteps(
            classification, behindOriginCommits, aheadOriginCommits, dirtyDurable, dirtyOther, mismatchPathsList, localOnly, originAhead);

        return new HostSyncPreflightResult
        {
            Branch = branch,
            BehindOriginCommits = behindOriginCommits,
            AheadOriginCommits = aheadOriginCommits,
            Classification = classification,
            ProceedAllowed = proceedAllowed,
            // G400: ff-blocked is a PRE-MUTATION abort — the wake stops before any
            // review/closeout/publish mutation, so it must NOT append a durable
            // abort summary to .intent-cli/runs.jsonl. Repeated wakes therefore
            // produce no additional durable state. Other non-proceed
            // classifications keep their existing behaviour (default allowed).
            PreMutationAbort = isFfBlocked,
            DurableRunsSummaryAllowed = !isFfBlocked,
            SafeStashRequired = safeStashAllowed,
            // G306: SafeStashPaths only populated when the safe-stash lane is active.
            // G357: submodule-checkout-mismatch uses the update lane, not safe-stash.
            SafeStashPaths = safeStashAllowed ? dirtyOther : Array.Empty<HostSyncWorkingTreeEntry>(),
            DirtyDurableStatePaths = dirtyDurable,
            DirtyUnrelatedPaths = dirtyOther,
            SubmoduleCheckoutMismatchPaths = mismatchPathsList,
            LocalOnlyCommits = localOnly,
            OriginAheadCommits = originAhead,
            Summary = BuildSummary(
                classification, branch, behindOriginCommits, aheadOriginCommits, dirtyDurable.Count, dirtyOther.Count, mismatchPathsList),
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

    private static string ClassifyTerminal(
        int behindOriginCommits,
        int aheadOriginCommits,
        int dirtyDurableCount,
        int dirtyOtherCount,
        bool allOtherAreGitlinkMismatches)
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
            // G357: all dirty-other paths are submodule gitlink mismatches whose
            // submodules themselves are clean → deterministic safe-update lane.
            return allOtherAreGitlinkMismatches
                ? ClassificationSubmoduleCheckoutMismatch
                : ClassificationDirtyUnrelatedSubmodule;
        }
        // G400: divergence — local has unpushed commit(s) AND origin advanced.
        // `git pull --ff-only` is blocked; this is a pre-mutation operator stop,
        // distinct from a plain behind-origin fast-forward. Local-ahead-only
        // (origin not advanced) is NOT ff-blocked: the host can push, so the
        // wake is not stopped here.
        if (behindOriginCommits > 0 && aheadOriginCommits > 0)
        {
            return ClassificationFfBlocked;
        }
        if (behindOriginCommits > 0)
        {
            return ClassificationBehindOrigin;
        }
        return ClassificationClean;
    }

    private static string BuildSummary(
        string classification,
        string branch,
        int behindCount,
        int aheadCount,
        int dirtyDurableCount,
        int dirtyOtherCount,
        IReadOnlyList<string> mismatchPaths)
    {
        return classification switch
        {
            ClassificationClean =>
                $"Host repo on `{branch}` is clean and up to date with origin. Pre-wake sync boundary satisfied.",
            ClassificationBehindOrigin =>
                $"Host repo on `{branch}` is behind origin by {behindCount} commit(s). Run `git pull --ff-only` before any read-only preflight (G304).",
            ClassificationFfBlocked =>
                $"Host repo on `{branch}` has DIVERGED from origin: {aheadCount} local-only commit(s) and {behindCount} origin-ahead commit(s), so `git pull --ff-only` is blocked (G400). This is a pre-mutation operator stop — do NOT append an abort summary to `.intent-cli/runs.jsonl`; report the divergence and let the operator push, relocate, or explicitly authorize discarding the local-only commit(s). Automation never resets or discards the foreign commit(s).",
            ClassificationDirtyDurableState =>
                $"Host repo on `{branch}` has {dirtyDurableCount} uncommitted change(s) to durable host-state files. Refusing to proceed (G304); commit/push or revert before the wake continues.",
            ClassificationDirtyUnrelatedSubmodule =>
                $"Host repo on `{branch}` has {dirtyOtherCount} uncommitted change(s) outside durable host-state. Use the G306 safe-stash lane (`automation workspace-guard --mode begin --write` before the wake, `--mode end --write` after) to preserve and restore the unrelated changes; durable host-state remains untouched.",
            ClassificationDirtyMixed =>
                $"Host repo on `{branch}` has {dirtyDurableCount} dirty durable-state path(s) AND {dirtyOtherCount} unrelated dirty path(s). Refusing to proceed (G304); both buckets must be reconciled before the wake.",
            ClassificationSubmoduleCheckoutMismatch =>
                $"Host repo on `{branch}` has {mismatchPaths.Count} submodule gitlink mismatch(es): the parent gitlink has advanced but the submodule checkout is stale (G357). Run `git submodule update --init {string.Join(" ", mismatchPaths)}` to update the submodule checkout(s) to the parent-recorded commit, then re-run `automation host-sync-preflight` to confirm clean.",
            _ => throw new InvalidOperationException($"Unknown host-sync classification '{classification}'.")
        };
    }

    private static IReadOnlyList<string> BuildNextSteps(
        string classification,
        int behindCommits,
        int aheadCommits,
        IReadOnlyList<HostSyncWorkingTreeEntry> dirtyDurable,
        IReadOnlyList<HostSyncWorkingTreeEntry> dirtyOther,
        IReadOnlyList<string> mismatchPaths,
        IReadOnlyList<HostSyncCommitInfo> localOnlyCommits,
        IReadOnlyList<HostSyncCommitInfo> originAheadCommits)
    {
        return classification switch
        {
            ClassificationClean => Array.Empty<string>(),
            ClassificationBehindOrigin => new[]
            {
                $"Run `git pull --ff-only` to fast-forward (host repo is {behindCommits} commit(s) behind).",
                "Re-run the host-loop read-only preflight (`automation doctor`, `host-review-preflight`) after the pull lands."
            },
            ClassificationFfBlocked => BuildFfBlockedSteps(behindCommits, aheadCommits, localOnlyCommits, originAheadCommits),
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
            ClassificationSubmoduleCheckoutMismatch => BuildSubmoduleMismatchSteps(mismatchPaths),
            _ => throw new InvalidOperationException($"Unknown host-sync classification '{classification}'.")
        };
    }

    private static IReadOnlyList<string> BuildSubmoduleMismatchSteps(IReadOnlyList<string> mismatchPaths)
    {
        var pathArgs = string.Join(" ", mismatchPaths);
        var pathList = string.Join(", ", mismatchPaths.Select(p => $"`{p}`"));
        return new[]
        {
            $"Run `git submodule update --init {pathArgs}` to update the submodule checkout(s) to the parent-recorded commit (G357). " +
            $"This is safe because the submodule(s) have no local uncommitted changes (verified by host-sync preflight). Mismatch path(s): {pathList}.",
            "Re-run `intent-cli automation host-sync-preflight --format json` after the update to confirm `classification: clean`."
        };
    }

    private static IReadOnlyList<string> BuildFfBlockedSteps(
        int behindCommits,
        int aheadCommits,
        IReadOnlyList<HostSyncCommitInfo> localOnlyCommits,
        IReadOnlyList<HostSyncCommitInfo> originAheadCommits)
    {
        static string Describe(IReadOnlyList<HostSyncCommitInfo> commits) =>
            commits.Count == 0
                ? "(commit details unavailable)"
                : string.Join("; ", commits.Select(c => $"{c.Sha} {c.Title}"));

        return new[]
        {
            $"STOP: host `main` has diverged from origin ({aheadCommits} local-only commit(s), {behindCommits} origin-ahead commit(s)); `git pull --ff-only` is blocked. This is a pre-mutation operator stop (G400).",
            $"Local-only commit(s): {Describe(localOnlyCommits)}.",
            $"Origin-ahead commit(s): {Describe(originAheadCommits)}.",
            "Do NOT append an abort wake-summary to `.intent-cli/runs.jsonl`: no review/closeout/publish mutation occurred, and repeated wakes must leave durable host-state untouched. Report diagnostics in stdout only.",
            "Operator must resolve the divergence: push the local-only commit(s) to a branch/PR, relocate the work, or explicitly authorize discarding the local-only commit(s). Automation must NOT reset, discard, or overwrite the foreign commit(s) without that authorization.",
            "After the operator resolves the divergence, re-run `intent-cli automation host-sync-preflight --format json` to confirm `classification: clean` before resuming the wake."
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

/// <summary>
/// G400: a single commit on one side of an ff-blocked divergence, identified by
/// short SHA and subject line so the diagnostics name exactly which local-only
/// and origin-ahead commits the operator must reconcile.
/// </summary>
internal sealed record HostSyncCommitInfo
{
    [JsonPropertyName("sha")]
    public required string Sha { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }
}

internal sealed record HostSyncPreflightResult
{
    [JsonPropertyName("branch")]
    public required string Branch { get; init; }

    [JsonPropertyName("behind_origin_commits")]
    public required int BehindOriginCommits { get; init; }

    /// <summary>G400: local commits not yet on origin/&lt;branch&gt; (origin/&lt;branch&gt;..HEAD).</summary>
    [JsonPropertyName("ahead_origin_commits")]
    public int AheadOriginCommits { get; init; }

    [JsonPropertyName("classification")]
    public required string Classification { get; init; }

    [JsonPropertyName("proceed_allowed")]
    public required bool ProceedAllowed { get; init; }

    /// <summary>
    /// G400: true when the classification is a pre-mutation operator stop
    /// (currently <c>ff-blocked</c>) reached before any review/closeout/publish
    /// mutation. The wake must report diagnostics only and must NOT append a
    /// durable abort summary to <c>.intent-cli/runs.jsonl</c>.
    /// </summary>
    [JsonPropertyName("pre_mutation_abort")]
    public bool PreMutationAbort { get; init; }

    /// <summary>
    /// G400: false when the wake must NOT append a durable run summary for this
    /// classification (pre-mutation abort). Defaults true so successful
    /// mutations and other classifications keep their existing behaviour.
    /// </summary>
    [JsonPropertyName("durable_runs_summary_allowed")]
    public bool DurableRunsSummaryAllowed { get; init; } = true;

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

    /// <summary>
    /// G357: submodule paths where the parent gitlink has advanced but the local
    /// checkout is stale, AND the submodule itself has no uncommitted changes. The
    /// safe repair is <c>git submodule update --init &lt;path&gt;</c> for each path.
    /// Empty unless <see cref="Classification"/> is <c>submodule-checkout-mismatch</c>.
    /// </summary>
    [JsonPropertyName("submodule_checkout_mismatch_paths")]
    public IReadOnlyList<string> SubmoduleCheckoutMismatchPaths { get; init; } = Array.Empty<string>();

    /// <summary>G400: local-only commits (origin/&lt;branch&gt;..HEAD) when ff-blocked. Empty otherwise.</summary>
    [JsonPropertyName("local_only_commits")]
    public IReadOnlyList<HostSyncCommitInfo> LocalOnlyCommits { get; init; } = Array.Empty<HostSyncCommitInfo>();

    /// <summary>G400: origin-ahead commits (HEAD..origin/&lt;branch&gt;) when ff-blocked. Empty otherwise.</summary>
    [JsonPropertyName("origin_ahead_commits")]
    public IReadOnlyList<HostSyncCommitInfo> OriginAheadCommits { get; init; } = Array.Empty<HostSyncCommitInfo>();

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("next_steps")]
    public required IReadOnlyList<string> NextSteps { get; init; }
}
