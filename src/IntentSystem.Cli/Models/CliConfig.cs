namespace IntentSystem.Cli.Models;

internal sealed record CliConfig
{
    public required ProjectConfig Project { get; init; }
}

internal sealed record ProjectConfig
{
    public required string Domain { get; init; }

    public required string WorkflowEngine { get; init; }

    public required string ArtifactRoot { get; init; }

    public string WorktreeRoot { get; init; } = ".intent-cli/worktrees";
}
