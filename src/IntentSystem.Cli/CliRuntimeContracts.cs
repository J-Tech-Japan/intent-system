namespace IntentSystem.Cli;

internal static class CliRuntimeContracts
{
    public const string IntentCliDirectoryName = ".intent-cli";
    public const string ConfigFileName = "config.toml";
    public const string QueueStateFileName = "queue-state.json";
    public const string RunLogFileName = "runs.jsonl";
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
    public const string DefaultImplementRole = "Claude";
    public const string DefaultReviewRole = "Codex";
    public const string DefaultInterviewRole = "Claude";
    public const string DefaultClarifyRole = "Codex";
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
}
