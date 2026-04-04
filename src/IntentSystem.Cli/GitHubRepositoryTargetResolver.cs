namespace IntentSystem.Cli;

internal static class GitHubRepositoryTargetResolver
{
    public static string Resolve(string repoRoot, string packetTargetRepo, IGitRemoteCommandRunner commandRunner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(packetTargetRepo);
        ArgumentNullException.ThrowIfNull(commandRunner);

        var childRepoPath = Path.IsPathRooted(packetTargetRepo)
            ? Path.GetFullPath(packetTargetRepo)
            : Path.GetFullPath(Path.Combine(repoRoot, packetTargetRepo));

        if (!Directory.Exists(childRepoPath))
        {
            throw new InvalidOperationException($"Child repo path was not found at {childRepoPath}");
        }

        var result = commandRunner.Run(childRepoPath, ["remote", "get-url", "origin"]);
        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StdErr)
                ? "git remote get-url origin failed."
                : result.StdErr.Trim();
            throw new InvalidOperationException(error);
        }

        return ParseRemoteUrl(result.StdOut.Trim());
    }

    internal static string ParseRemoteUrl(string remoteUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteUrl);

        if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var absoluteUri))
        {
            if (!string.Equals(absoluteUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Remote URL '{remoteUrl}' must point to github.com.");
            }

            return NormalizeRepositoryPath(absoluteUri.AbsolutePath);
        }

        const string sshPrefix = "git@github.com:";
        if (remoteUrl.StartsWith(sshPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeRepositoryPath(remoteUrl[sshPrefix.Length..]);
        }

        throw new InvalidOperationException($"Remote URL '{remoteUrl}' must use a GitHub remote URL shape.");
    }

    private static string NormalizeRepositoryPath(string repositoryPath)
    {
        var normalized = repositoryPath.Trim().Trim('/');
        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 2)
        {
            throw new InvalidOperationException(
                $"Remote repository path '{repositoryPath}' must use the GitHub owner/repo shape.");
        }

        return $"{segments[0]}/{segments[1]}";
    }
}
