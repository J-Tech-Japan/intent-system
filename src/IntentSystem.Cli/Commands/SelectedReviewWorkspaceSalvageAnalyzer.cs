namespace IntentSystem.Cli.Commands;

/// <summary>
/// G453: pure decision classifier that prevents review-host cleanup from
/// destroying unpublished host metadata needed for PR review.
///
/// The failure mode (observed on AIC PR #3746): <c>host-review-preflight</c>
/// selects a PR, but the host worktree is dirty (<c>dirty-mixed</c>) with a
/// mix of unpublished host durable metadata
/// (<c>.intent-cli/queue-state.json</c>, <c>.intent-cli/runs.jsonl</c>,
/// <c>.intent-cli/issues/&lt;unit&gt;</c> packets/publish artifacts,
/// <c>intents/&lt;domain&gt;/**</c>) and unrelated implementation residue
/// (lock files, build output). An operator or agent "fixes" the dirty state
/// with <c>git reset --hard</c> / <c>git clean</c> — and the unpublished
/// metadata is gone. <c>review closeout-plan</c> / <c>guide review</c> then
/// report <c>host-metadata-blocked</c> because no queue item with a
/// <c>linked_pr</c> for the PR survives.
///
/// This classifier makes intent-cli diagnose the situation BEFORE any
/// destructive cleanup and recommend salvage-first actions. It keeps four
/// concerns separated:
/// <list type="bullet">
///   <item>wrong review worktree / branch (same-repo topology running from an
///   implementation feature branch),</item>
///   <item>workspace dirty with unpublished host metadata (salvage first),</item>
///   <item>required CI failing (an implementation blocker, reported separately
///   from any workspace concern),</item>
///   <item>clean and review-ready.</item>
/// </list>
///
/// Pure: no I/O, no git, no GitHub, no process launch. Callers feed it facts
/// already gathered by <c>host-sync-preflight</c>, <c>automation summary</c>,
/// and the PR's CI status.
/// </summary>
internal static class SelectedReviewWorkspaceSalvageAnalyzer
{
    /// <summary>CI rollup states the analyzer recognizes (others treated as unknown).</summary>
    public const string CiFailing = "failing";

    public static SelectedReviewWorkspaceSalvageDecision Analyze(SelectedReviewWorkspaceSalvageInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var dirtyDurable = Normalize(input.DirtyDurablePaths);
        var dirtyUnrelated = Normalize(input.DirtyUnrelatedPaths);
        var hasUnpublishedMetadata = dirtyDurable.Count > 0;
        var ciFailing = string.Equals(input.CiStatus?.Trim(), CiFailing, StringComparison.OrdinalIgnoreCase);

        var expectedReviewBranch = string.IsNullOrWhiteSpace(input.MetadataSourceBranch)
            ? null
            : input.MetadataSourceBranch!.Trim();
        var currentBranch = string.IsNullOrWhiteSpace(input.CurrentBranch)
            ? null
            : input.CurrentBranch!.Trim();

        // Wrong review worktree / branch (AC4). Only meaningful in same-repo
        // topology with a configured metadata source branch: when the review
        // wake is running from a different (implementation feature) branch, the
        // fix is to switch to a clean review worktree on the metadata branch —
        // NOT to clean the current dirty worktree (which would discard the
        // in-flight metadata). This is checked FIRST because it is the
        // structural root cause of the dirty-metadata situation.
        var wrongBranch = input.SameRepoTopology
            && expectedReviewBranch is not null
            && currentBranch is not null
            && !string.Equals(currentBranch, expectedReviewBranch, StringComparison.Ordinal);

        if (wrongBranch)
        {
            var actions = new List<string>
            {
                $"Switch to a clean review worktree checked out on the metadata branch `{expectedReviewBranch}` (it already has the published metadata) instead of reviewing from `{currentBranch}`.",
                $"If no such worktree exists, create one: `git worktree add ../review-host {expectedReviewBranch}` and run the host review loop there.",
            };
            if (hasUnpublishedMetadata)
            {
                actions.Add("Do NOT `git reset`/`clean` the current worktree to make it clean — the dirty set contains unpublished host metadata that review/closeout needs (salvage it first; see salvage actions).");
                actions.AddRange(MetadataSalvageActions(input, expectedReviewBranch));
            }

            return new SelectedReviewWorkspaceSalvageDecision
            {
                Classification = AutomationHostReviewDiagnosticsClassifications.WrongReviewWorktreeBranch,
                Reason = $"Same-repo topology is configured (metadata branch `{expectedReviewBranch}`) but the review wake is running from `{currentBranch}`, an implementation/feature branch. Reviewing from an implementation branch is the root cause of the dirty in-flight host metadata. Switch to a clean review worktree on the metadata branch rather than cleaning this one.",
                DestructiveCleanupWarning = hasUnpublishedMetadata,
                SalvageFirst = true,
                CiFailing = ciFailing,
                UnpublishedMetadataPaths = dirtyDurable,
                UnrelatedPaths = dirtyUnrelated,
                RecommendedSalvageActions = actions,
                RecommendedNextCommand = null,
            };
        }

        // Workspace dirty with unpublished host metadata (AC1 + AC2 + AC3).
        if (hasUnpublishedMetadata)
        {
            return new SelectedReviewWorkspaceSalvageDecision
            {
                Classification = AutomationHostReviewDiagnosticsClassifications.SelectedReviewBlockedByUnpublishedHostMetadata,
                Reason = $"Review PR #{input.SelectedPr} is blocked by a dirty host worktree whose dirty set includes unpublished host durable metadata ({Join(dirtyDurable)}). "
                    + (dirtyUnrelated.Count > 0
                        ? $"It also contains unrelated implementation residue ({Join(dirtyUnrelated)}). "
                        : string.Empty)
                    + "This is NOT a generic dirty stop: clearing it with reset/clean would delete the metadata review/closeout depends on.",
                DestructiveCleanupWarning = true,
                SalvageFirst = true,
                CiFailing = ciFailing,
                UnpublishedMetadataPaths = dirtyDurable,
                UnrelatedPaths = dirtyUnrelated,
                RecommendedSalvageActions = MetadataSalvageActions(input, expectedReviewBranch),
                // The entry point to salvage is durable-state-preflight, which
                // classifies which durable paths are forward-only-safe to commit
                // vs need operator review. It never blindly auto-commits
                // operator-owned `intents/**` files.
                RecommendedNextCommand = string.IsNullOrWhiteSpace(input.Domain) || string.IsNullOrWhiteSpace(input.Repo)
                    ? "intent-cli automation durable-state-preflight --format json"
                    : $"intent-cli automation durable-state-preflight --domain {input.Domain} --target-repo {input.Repo} --format json",
            };
        }

        // Required CI failing, with no workspace/worktree blocker (AC5). CI is
        // an implementation blocker, kept separate from workspace dirtiness.
        if (ciFailing)
        {
            return new SelectedReviewWorkspaceSalvageDecision
            {
                Classification = AutomationHostReviewDiagnosticsClassifications.RequiredCiFailing,
                Reason = $"Review PR #{input.SelectedPr} has failing required CI. This is an implementation-actionable blocker, reported separately from any workspace-dirty or host-metadata condition — do not treat it as a cleanup problem.",
                DestructiveCleanupWarning = false,
                SalvageFirst = false,
                CiFailing = true,
                UnpublishedMetadataPaths = dirtyDurable,
                UnrelatedPaths = dirtyUnrelated,
                RecommendedSalvageActions = new[]
                {
                    "Route the failing CI back to the implementation worker via `intent-cli automation pr-transition --transition request-update --write` with the failing-check evidence, or surface an operator stop when the failure is infrastructural.",
                },
                RecommendedNextCommand = null,
            };
        }

        // Clean and review-ready (no cleanup-safety blocker).
        return new SelectedReviewWorkspaceSalvageDecision
        {
            Classification = AutomationHostReviewDiagnosticsClassifications.WorkspaceCleanReviewReady,
            Reason = $"Review PR #{input.SelectedPr} has no host-metadata cleanup-safety blocker: no dirty host durable metadata, correct review worktree/branch, and CI is not failing. Review may proceed.",
            DestructiveCleanupWarning = false,
            SalvageFirst = false,
            CiFailing = false,
            UnpublishedMetadataPaths = dirtyDurable,
            UnrelatedPaths = dirtyUnrelated,
            RecommendedSalvageActions = Array.Empty<string>(),
            RecommendedNextCommand = null,
        };
    }

    /// <summary>
    /// Ordered salvage-first actions (AC3): commit/push durable metadata, sync
    /// to the metadata branch/root, run state-doctor / reconcile /
    /// publish-recovery, or switch to a clean review worktree — all BEFORE any
    /// destructive cleanup.
    /// </summary>
    private static List<string> MetadataSalvageActions(SelectedReviewWorkspaceSalvageInput input, string? metadataBranch)
    {
        var repo = string.IsNullOrWhiteSpace(input.Repo) ? "<owner/repo>" : input.Repo!;
        var actions = new List<string>
        {
            "SALVAGE FIRST — do NOT run `git reset --hard` / `git checkout -- .` / `git clean -fd` / stash-drop on the dirty host metadata; those commands delete unpublished queue-state, runs.jsonl, packets, publish artifacts, and intent-tree files that `review closeout-plan` and `guide review` need.",
            "1. Classify and commit forward-only durable metadata: `intent-cli automation durable-state-preflight --format json`; commit only the `verified-commit-ready` paths with its `recommended_commit_message`, then push.",
        };
        actions.Add(metadataBranch is not null
            ? $"2. Same-repo topology: commit/push host metadata to the metadata branch `{metadataBranch}` (the review worktree reads durable state from there), not onto an implementation feature branch."
            : "2. Push the committed durable metadata to the host repo root so the review worktree reads fresh published state.");
        actions.Add($"3. If linkage is what's missing, recover deterministically before any cleanup: `intent-cli automation publish-recovery --repo {repo} --format json` (then `--write` if a single high-confidence repair), or `intent-cli automation state-doctor --repo {repo} --read-only` / `intent-cli automation reconcile --lane host-review --repo {repo} --format json`.");
        actions.Add("4. Or switch to a clean review worktree that already has the published metadata and run the review there, leaving the dirty worktree untouched for the operator to resolve.");
        actions.Add("5. Operator-owned `intents/**` edits are never blindly auto-committed — if durable-state-preflight reports `needs-operator-review` / `unsafe-durable-state`, surface the paths to the operator instead of cleaning them.");
        return actions;
    }

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string>? paths)
    {
        if (paths is null || paths.Count == 0)
        {
            return Array.Empty<string>();
        }
        return paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToArray();
    }

    private static string Join(IReadOnlyList<string> paths) => string.Join(", ", paths);
}

/// <summary>
/// G453: facts fed to <see cref="SelectedReviewWorkspaceSalvageAnalyzer"/>.
/// Gathered by the host loop from <c>host-sync-preflight</c> (dirty path
/// buckets, current branch), <c>automation summary</c> (same-repo topology +
/// metadata source branch), and the PR's CI rollup.
/// </summary>
internal sealed record SelectedReviewWorkspaceSalvageInput
{
    public required int SelectedPr { get; init; }

    public int? LinkedIssue { get; init; }

    public string? Repo { get; init; }

    public string? Domain { get; init; }

    /// <summary>Dirty host durable-metadata paths (from host-sync-preflight `dirty_durable_state_paths`).</summary>
    public IReadOnlyList<string>? DirtyDurablePaths { get; init; }

    /// <summary>Dirty unrelated paths (lock files, build output) the salvage must not conflate with metadata.</summary>
    public IReadOnlyList<string>? DirtyUnrelatedPaths { get; init; }

    public bool SameRepoTopology { get; init; }

    /// <summary>Configured metadata source branch (e.g. `main-metadata`); null when not same-repo.</summary>
    public string? MetadataSourceBranch { get; init; }

    /// <summary>The branch the review worktree is currently checked out on.</summary>
    public string? CurrentBranch { get; init; }

    /// <summary>Required-CI rollup: `failing` is recognized; other values treated as not-failing/unknown.</summary>
    public string? CiStatus { get; init; }
}

/// <summary>
/// G453: the salvage decision. <see cref="DestructiveCleanupWarning"/> is the
/// AC2 signal that reset/clean may remove metadata; <see cref="SalvageFirst"/>
/// + <see cref="RecommendedSalvageActions"/> are the AC3 salvage-first
/// recommendation; <see cref="CiFailing"/> keeps the AC5 CI concern visible
/// independently of workspace state.
/// </summary>
internal sealed record SelectedReviewWorkspaceSalvageDecision
{
    public required string Classification { get; init; }

    public required string Reason { get; init; }

    public required bool DestructiveCleanupWarning { get; init; }

    public required bool SalvageFirst { get; init; }

    public required bool CiFailing { get; init; }

    public required IReadOnlyList<string> UnpublishedMetadataPaths { get; init; }

    public required IReadOnlyList<string> UnrelatedPaths { get; init; }

    public required IReadOnlyList<string> RecommendedSalvageActions { get; init; }

    public string? RecommendedNextCommand { get; init; }
}
