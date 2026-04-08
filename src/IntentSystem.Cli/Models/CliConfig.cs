namespace IntentSystem.Cli.Models;

internal sealed record CliConfig
{
    public required ProjectConfig Project { get; init; }

    public RoleMappings Roles { get; init; } = new();

    public SupervisionConfig Supervision { get; init; } = new();
}

internal sealed record ProjectConfig
{
    public required string Domain { get; init; }

    public required string WorkflowEngine { get; init; }

    public required string ArtifactRoot { get; init; }

    public string WorktreeRoot { get; init; } = ".intent-cli/worktrees";

    public string ParentIntentRepoRoot { get; init; } = string.Empty;
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
