namespace IntentSystem.Cli;

internal static class CliRuntimeContracts
{
    public const string IntentCliDirectoryName = ".intent-cli";
    public const string ConfigFileName = "config.toml";
    public const string QueueStateFileName = "queue-state.json";
    public const string RunLogFileName = "runs.jsonl";
    public const string OperatorAttentionFileName = "operator-attention.json";
    public const string ProjectSectionName = "project";
    public const string DefaultDomainKey = "default_domain";
    public const string DomainKey = "domain";
    public const string ArtifactRootKey = "artifact_root";
    public const string WorktreeRootKey = "worktree_root";
    public const string WorkRepoPathKey = "work_repo_path";
    public const string ParentIntentRepoRootKey = "parent_intent_repo_root";
    public const string RolesSectionName = "roles";
    public const string SupervisionSectionName = "supervision";
    public const string RunSectionName = "run";
    public const string DirectRunSectionName = "direct_backend";
    public const string ImplementRoleKey = "implement";
    public const string ReviewRoleKey = "review";
    public const string InterviewRoleKey = "interview";
    public const string ClarifyRoleKey = "clarify";
    public const string ProviderKey = "provider";
    public const string ModelKey = "model";
    public const string TransportKey = "transport";
    public const string CommandKey = "command";
    public const string ArgsKey = "args";
    public const string StaleHeartbeatTimeoutMinutesKey = "stale_heartbeat_timeout_minutes";
    public const string RetryDelayMinutesKey = "retry_delay_minutes";
    public const string RetryBudgetKey = "retry_budget";
    public const string PostFixWorktreeProgressPolicyKey = "post_fix_worktree_progress_policy";
    public const string BaseBranchPolicyKey = "base_branch_policy";
    public const string DirectMainBaseBranchPolicy = "direct-main";
    public const string MainAiBaseBranchPolicy = "main-ai";
    public const string DefaultBaseBranchPolicy = DirectMainBaseBranchPolicy;
    public const string DirectMainBaseBranch = "main";
    public const string MainAiIntegrationBaseBranch = "main-ai";
    /// <summary>G350: config key for the final stable branch in same-repo topology (e.g. "main").</summary>
    public const string StableBranchKey = "stable_branch";
    /// <summary>G350: config key for the branch implementation PRs target in same-repo topology.</summary>
    public const string ImplementationBaseBranchKey = "implementation_base_branch";
    /// <summary>G350: config key for the dedicated metadata direct-push branch in same-repo topology.</summary>
    public const string MetadataBranchKey = "metadata_branch";

    /// <summary>
    /// G362: config key for the branch the host loop READS metadata
    /// from each wake (queue-state / runs / packets / next-slice).
    /// Distinct from <see cref="MetadataBranchKey"/> so a stale
    /// long-lived metadata branch can be detected. Empty falls back
    /// to <see cref="MetadataBranchKey"/>.
    /// </summary>
    public const string MetadataSourceBranchKey = "metadata_source_branch";

    /// <summary>
    /// G362: config key for the branch the host loop WRITES metadata
    /// commits to. Distinct from <see cref="MetadataSourceBranchKey"/>
    /// so the same-repo preflight can compare source-vs-write
    /// freshness. Empty falls back to <see cref="MetadataBranchKey"/>.
    /// </summary>
    public const string MetadataWriteBranchKey = "metadata_write_branch";

    /// <summary>
    /// G362: opt-in flag for same-repository topology. Hosts that
    /// share metadata + implementation code in one repo set this to
    /// <c>true</c> to enable the same-repo preflight gates;
    /// generic hosts leave it false (the default) to keep pre-G362
    /// pull-first <c>main</c> behavior (G357).
    /// </summary>
    public const string SameRepoTopologyKey = "same_repo_topology";
    /// <summary>G350: conventional default for the stable branch.</summary>
    public const string DefaultStableBranch = "main";
    /// <summary>G350: conventional default for the metadata branch in same-repo topology.</summary>
    public const string DefaultMetadataBranch = "main-metadata";
    public const string DefaultWorktreeRoot = ".intent-cli/worktrees";
    public const string DefaultSupervisionArtifactRoot = ".intent-cli/supervision";
    public const string DefaultDirectRunArtifactRoot = ".intent-cli/runs";
    public const int DefaultSupervisionStaleHeartbeatTimeoutMinutes = 15;
    public const int DefaultSupervisionRetryDelayMinutes = 5;
    public const int DefaultSupervisionRetryBudget = 3;
    // G614: role routing defaults are logical roles, not product names. Existing
    // explicit [roles] values (including legacy Claude/Codex mappings) remain
    // valid and are preserved by CliConfigLoader.
    public const string DefaultImplementRole = "implementation";
    public const string DefaultReviewRole = "review";
    public const string DefaultInterviewRole = "interview";
    public const string DefaultClarifyRole = "clarify";
    public const string ConfirmPostFixWorktreeProgressPolicy = "confirm";
    public const string AutoContinuePostFixWorktreeProgressPolicy = "auto-continue";
    public const string DefaultPostFixWorktreeProgressPolicy = ConfirmPostFixWorktreeProgressPolicy;
    public const string DefaultDirectRunModel = "default";
    public const string DefaultCodexDirectRunModel = "gpt-5.4-mini";
    public const string DefaultDirectRunTransport = "stdio";

    public static string GetIntentCliDirectoryPath(string repoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        return Path.Combine(repoRoot, IntentCliDirectoryName);
    }

    public static string GetConfigPath(string repoRoot)
    {
        return Path.Combine(GetIntentCliDirectoryPath(repoRoot), ConfigFileName);
    }

    public static string GetQueueStatePath(string repoRoot)
    {
        return Path.Combine(GetIntentCliDirectoryPath(repoRoot), QueueStateFileName);
    }

    public static string GetRunLogPath(string repoRoot)
    {
        return Path.Combine(GetIntentCliDirectoryPath(repoRoot), RunLogFileName);
    }

    public static string GetOperatorAttentionPath(string repoRoot)
    {
        return Path.Combine(GetIntentCliDirectoryPath(repoRoot), OperatorAttentionFileName);
    }
}
