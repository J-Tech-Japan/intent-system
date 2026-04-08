using IntentSystem.Cli.Models;

namespace IntentSystem.Cli;

internal sealed record CliContext
{
    public required string RepoRoot { get; init; }

    public required CliConfig Config { get; init; }

    public string GetIntentCliDirectoryPath()
    {
        return CliRuntimeContracts.GetIntentCliDirectoryPath(RepoRoot);
    }

    public string GetConfigPath()
    {
        return CliRuntimeContracts.GetConfigPath(RepoRoot);
    }

    public string GetQueueStatePath()
    {
        return CliRuntimeContracts.GetQueueStatePath(RepoRoot);
    }

    public string GetRunLogPath()
    {
        return CliRuntimeContracts.GetRunLogPath(RepoRoot);
    }

    public string ResolveArtifactRootPath()
    {
        var artifactRoot = Config.Project.ArtifactRoot;
        if (Path.IsPathRooted(artifactRoot))
        {
            return artifactRoot;
        }

        return Path.GetFullPath(Path.Combine(RepoRoot, artifactRoot));
    }

    public string ResolveWorktreeRootPath()
    {
        var worktreeRoot = Config.Project.WorktreeRoot;
        if (Path.IsPathRooted(worktreeRoot))
        {
            return worktreeRoot;
        }

        return Path.GetFullPath(Path.Combine(RepoRoot, worktreeRoot));
    }

    public string ResolveSupervisionArtifactRootPath()
    {
        var supervisionArtifactRoot = Config.Supervision.ArtifactRoot;
        if (Path.IsPathRooted(supervisionArtifactRoot))
        {
            return supervisionArtifactRoot;
        }

        return Path.GetFullPath(Path.Combine(RepoRoot, supervisionArtifactRoot));
    }

    public string? ResolveParentIntentRepoRootPath()
    {
        var parentIntentRepoRoot = Config.Project.ParentIntentRepoRoot;
        if (string.IsNullOrWhiteSpace(parentIntentRepoRoot))
        {
            return null;
        }

        if (Path.IsPathRooted(parentIntentRepoRoot))
        {
            return parentIntentRepoRoot;
        }

        return Path.GetFullPath(Path.Combine(RepoRoot, parentIntentRepoRoot));
    }
}
