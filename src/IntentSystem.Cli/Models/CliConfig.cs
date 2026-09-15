namespace IntentSystem.Cli.Models;

using IntentSystem.Cli.Commands;

internal sealed record CliConfig
{
    public required ProjectConfig Project { get; init; }

    public RoleMappings Roles { get; init; } = new();

    public SupervisionConfig Supervision { get; init; } = new();

    public RunConfig Run { get; init; } = new();

    public DirectRunConfig DirectRun { get; init; } = new();

    public CrossRuntimeReviewConfig CrossRuntimeReview { get; init; } = new();
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

    /// <summary>
    /// Explicit <c>[roles]</c> values that are not logical roles. Historical
    /// configs use this map for runtime names such as <c>Claude</c> and
    /// <c>Codex</c>; retaining the values lets existing configs load without a
    /// rewrite while making the legacy meaning observable to diagnostics.
    /// </summary>
    public IReadOnlyDictionary<string, string> LegacyValues { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    public bool HasLegacyValues => LegacyValues.Count > 0;

    /// <summary>
    /// Returns the value safe for a queue writer. Explicit vendor/runtime
    /// values loaded from the historical config are not persisted into the
    /// logical queue vocabulary; they fall back to the corresponding
    /// responsibility. Values supplied directly by compatibility callers are
    /// still preserved unless they are a known alias/canonical role.
    /// </summary>
    public string WorkerRoleForQueue => ResolveForQueue(
        CliRuntimeContracts.ImplementRoleKey,
        Implement,
        LogicalRoleNormalizer.Builder);

    public string ReviewRoleForQueue => ResolveForQueue(
        CliRuntimeContracts.ReviewRoleKey,
        Review,
        LogicalRoleNormalizer.Reviewer);

    private string ResolveForQueue(string key, string value, string fallback)
    {
        if (LegacyValues.ContainsKey(key))
        {
            return LogicalRoleNormalizer.NormalizeForWrite(null, fallback);
        }

        return LogicalRoleNormalizer.NormalizeForWrite(value, fallback);
    }
}

internal sealed record SupervisionConfig
{
    public string ArtifactRoot { get; init; } = CliRuntimeContracts.DefaultSupervisionArtifactRoot;

    public int StaleHeartbeatTimeoutMinutes { get; init; } =
        CliRuntimeContracts.DefaultSupervisionStaleHeartbeatTimeoutMinutes;

    public int RetryDelayMinutes { get; init; } = CliRuntimeContracts.DefaultSupervisionRetryDelayMinutes;

    public int RetryBudget { get; init; } = CliRuntimeContracts.DefaultSupervisionRetryBudget;

    /// <summary>
    /// G828: teams that opted in to standing supervision, as exact
    /// <c>&lt;domain&gt;/&lt;team&gt;</c> entries from
    /// <c>[supervision] opt_in_teams</c>. Supervision is opt-in: guidance
    /// recommends it and bootstrap requires it only for these teams. No file,
    /// cycle, or install record opts a team in.
    /// </summary>
    public IReadOnlyList<string> OptInTeams { get; init; } = [];

    public const string OptInSource = "config:supervision.opt_in_teams";

    public bool IsOptedIn(string? domain, string? team) =>
        !string.IsNullOrWhiteSpace(domain)
        && !string.IsNullOrWhiteSpace(team)
        && OptInTeams.Contains($"{domain.Trim()}/{team.Trim()}", StringComparer.Ordinal);
}

/// <summary>
/// G834: teams that declared cross-runtime implementation review, from
/// <c>[[cross_runtime_review.teams]]</c>. Each entry names one exact
/// <c>&lt;domain&gt;/&lt;team&gt;</c>, that team's declared conductor runtime, and
/// the repositories the gate applies to. Nothing else declares a team: no record,
/// claim, or caller argument does.
/// </summary>
internal sealed record CrossRuntimeReviewConfig
{
    public IReadOnlyList<CrossRuntimeReviewTeamDeclaration> Teams { get; init; } = [];

    public const string Source = "config:cross_runtime_review.teams";

    public bool TryGetDeclared(string? domain, string? team, out CrossRuntimeReviewTeamDeclaration declaration)
    {
        declaration = null!;
        if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(team))
        {
            return false;
        }

        var key = $"{domain}/{team}";
        var match = Teams.FirstOrDefault(entry => string.Equals(entry.Team, key, StringComparison.Ordinal));
        if (match is null)
        {
            return false;
        }

        declaration = match;
        return true;
    }

    /// <summary>Repository names compare case-insensitively, as <c>CiWaitStore</c> does.</summary>
    public bool IsGatedRepo(string? repo) =>
        !string.IsNullOrWhiteSpace(repo)
        && Teams.Any(entry => entry.Repos.Contains(repo.Trim(), StringComparer.OrdinalIgnoreCase));
}

internal sealed record CrossRuntimeReviewTeamDeclaration
{
    /// <summary>Exact <c>&lt;domain&gt;/&lt;team&gt;</c>.</summary>
    public required string Team { get; init; }

    public required string ConductorRuntime { get; init; }

    public required IReadOnlyList<string> Repos { get; init; }
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
