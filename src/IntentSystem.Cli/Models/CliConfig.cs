namespace IntentSystem.Cli.Models;

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

    /// <summary>G350: final stable branch in same-repo topology (e.g. "main"). Empty = not configured.</summary>
    public string StableBranch { get; init; } = string.Empty;

    /// <summary>G350: branch implementation PRs target in same-repo topology (e.g. "main" or "main-ai"). Empty = derive from BaseBranchPolicy.</summary>
    public string ImplementationBaseBranch { get; init; } = string.Empty;

    /// <summary>G350: dedicated metadata direct-push branch in same-repo topology (e.g. "main-metadata"). Empty = not configured.</summary>
    public string MetadataBranch { get; init; } = string.Empty;
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
