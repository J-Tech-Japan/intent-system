namespace IntentSystem.Cli.Models;

using IntentSystem.Cli.Commands;

internal sealed record CliConfig
{
    public required ProjectConfig Project { get; init; }

    public RoleMappings Roles { get; init; } = new();

    public SupervisionConfig Supervision { get; init; } = new();

    public RunConfig Run { get; init; } = new();

    public DirectRunConfig DirectRun { get; init; } = new();
}

internal sealed record ProjectConfig
{
    public required string Domain { get; init; }

    public required string ArtifactRoot { get; init; }

    public string WorktreeRoot { get; init; } = ".intent-cli/worktrees";

    public string WorkRepoPath { get; init; } = string.Empty;

    public string ParentIntentRepoRoot { get; init; } = string.Empty;

    public string BaseBranchPolicy { get; init; } = CliRuntimeContracts.DefaultBaseBranchPolicy;

    /// <summary>
    /// G668 preview named branch lanes keyed by domain. An empty map keeps the
    /// legacy <c>base_branch_policy</c> path byte-for-byte compatible.
    /// </summary>
    public IReadOnlyDictionary<string, BranchLaneRegistry> BranchLanes { get; init; }
        = new Dictionary<string, BranchLaneRegistry>(StringComparer.Ordinal);

    /// <summary>G350: final stable branch in same-repo topology (e.g. "main"). Empty = not configured.</summary>
    public string StableBranch { get; init; } = string.Empty;

    /// <summary>G350: branch implementation PRs target in same-repo topology (e.g. "main" or "main-ai"). Empty = derive from BaseBranchPolicy.</summary>
    public string ImplementationBaseBranch { get; init; } = string.Empty;

    /// <summary>G350: dedicated metadata direct-push branch in same-repo topology (e.g. "main-metadata"). Empty = not configured.</summary>
    public string MetadataBranch { get; init; } = string.Empty;

    /// <summary>
    /// G362: branch the host loop must READ metadata (queue-state,
    /// runs.jsonl, packet directories) from before each wake. In a
    /// same-repo topology with a long-lived <c>main-metadata</c> branch
    /// the operator may pin reads to <c>main</c> (current durable state)
    /// while writes still target <c>main-metadata</c>. Empty falls back
    /// to <see cref="MetadataBranch"/>; if both are empty the loop
    /// keeps its pre-G362 pull-first <c>main</c> behavior (G357).
    /// </summary>
    public string MetadataSourceBranch { get; init; } = string.Empty;

    /// <summary>
    /// G362: branch the host loop must WRITE metadata commits to.
    /// Distinct from <see cref="MetadataSourceBranch"/> so a stale
    /// <c>main-metadata</c> can be detected (the operator periodically
    /// merges metadata writes back to <c>main</c>). Empty falls back
    /// to <see cref="MetadataBranch"/>.
    /// </summary>
    public string MetadataWriteBranch { get; init; } = string.Empty;

    /// <summary>
    /// G362: when true, the project is configured as same-repository
    /// topology (host metadata and implementation code share one
    /// repo). Triggers additional preflight gates so the host loop
    /// cannot read stale metadata or approve PRs against the wrong
    /// base branch. Defaults to false so generic host repos keep
    /// pre-G362 behavior.
    /// </summary>
    public bool SameRepoTopology { get; init; }
}

internal sealed record RoleMappings
{
    public string Implement { get; init; } = CliRuntimeContracts.DefaultImplementRole;

    public string Review { get; init; } = CliRuntimeContracts.DefaultReviewRole;

    public string Interview { get; init; } = CliRuntimeContracts.DefaultInterviewRole;

    public string Clarify { get; init; } = CliRuntimeContracts.DefaultClarifyRole;
}

internal sealed record SupervisionConfig
{
    public string ArtifactRoot { get; init; } = CliRuntimeContracts.DefaultSupervisionArtifactRoot;

    public int StaleHeartbeatTimeoutMinutes { get; init; } =
        CliRuntimeContracts.DefaultSupervisionStaleHeartbeatTimeoutMinutes;

    public int RetryDelayMinutes { get; init; } = CliRuntimeContracts.DefaultSupervisionRetryDelayMinutes;

    public int RetryBudget { get; init; } = CliRuntimeContracts.DefaultSupervisionRetryBudget;
}

internal sealed record RunConfig
{
    public string PostFixWorktreeProgressPolicy { get; init; } =
        CliRuntimeContracts.DefaultPostFixWorktreeProgressPolicy;
}

internal sealed record DirectRunConfig
{
    public string ArtifactRoot { get; init; } = CliRuntimeContracts.DefaultDirectRunArtifactRoot;

    public string Provider { get; init; } = string.Empty;

    public string Model { get; init; } = CliRuntimeContracts.DefaultDirectRunModel;

    public string Transport { get; init; } = CliRuntimeContracts.DefaultDirectRunTransport;

    public string Command { get; init; } = string.Empty;

    public IReadOnlyList<string> Args { get; init; } = [];

    public DirectRunEntryConfig Implement { get; init; } = new();

    public DirectRunEntryConfig Fix { get; init; } = new();

    public DirectRunEntryConfig Review { get; init; } = new();
}

internal sealed record DirectRunEntryConfig
{
    public string Provider { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string Transport { get; init; } = string.Empty;

    public string Command { get; init; } = string.Empty;

    public IReadOnlyList<string> Args { get; init; } = [];
}
