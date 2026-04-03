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

    public string ResolveArtifactRootPath()
    {
        var artifactRoot = Config.Project.ArtifactRoot;
        if (Path.IsPathRooted(artifactRoot))
        {
            return artifactRoot;
        }

        return Path.GetFullPath(Path.Combine(RepoRoot, artifactRoot));
    }
}
