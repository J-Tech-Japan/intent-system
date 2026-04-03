namespace IntentSystem.Cli.Infrastructure;

internal static class RepoRootResolver
{
    public static string? Resolve(string startPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startPath);

        var absoluteStartPath = Path.GetFullPath(startPath);
        if (!Directory.Exists(absoluteStartPath))
        {
            throw new DirectoryNotFoundException($"Start path '{absoluteStartPath}' does not exist.");
        }

        var currentDirectory = new DirectoryInfo(absoluteStartPath);
        while (currentDirectory is not null)
        {
            var intentCliPath = Path.Combine(currentDirectory.FullName, CliRuntimeContracts.IntentCliDirectoryName);
            if (Directory.Exists(intentCliPath))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        return null;
    }
}
