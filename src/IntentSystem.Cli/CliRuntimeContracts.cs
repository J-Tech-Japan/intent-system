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
    public const string WorkflowEngineKey = "workflow_engine";
    public const string ArtifactRootKey = "artifact_root";
    public const string WorktreeRootKey = "worktree_root";
    public const string ParentIntentRepoRootKey = "parent_intent_repo_root";
    public const string RolesSectionName = "roles";
    public const string SupervisionSectionName = "supervision";
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
    public const string DefaultDirectRunModel = "default";
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
