namespace IntentSystem.Review;

public static class ChildRepoMainSynchronizer
{
    public static void Sync(string parentRepoRoot, string childRepoRef, string mergedCommitSha, IGitCommandRunner commandRunner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentRepoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(childRepoRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(mergedCommitSha);
        ArgumentNullException.ThrowIfNull(commandRunner);

        var childRepoPath = ResolveChildRepoPath(parentRepoRoot, childRepoRef);
        if (!Directory.Exists(childRepoPath))
        {
            throw new InvalidOperationException($"Child repo path was not found at {childRepoPath}");
        }

        Run(commandRunner, childRepoPath, ["fetch", "origin", "main"], "child repo fetch failed.");
        Run(commandRunner, childRepoPath, ["switch", "main"], "child repo switch to main failed.");
        Run(commandRunner, childRepoPath, ["merge", "--ff-only", "origin/main"], "child repo main fast-forward failed.");

        var headResult = Run(commandRunner, childRepoPath, ["rev-parse", "HEAD"], "child repo HEAD resolve failed.");
        var headCommit = headResult.StdOut.Trim();
        if (!string.Equals(headCommit, mergedCommitSha, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Child repo main HEAD '{headCommit}' must match merged commit '{mergedCommitSha}'.");
        }
    }

    public static string ResolveChildRepoPath(string parentRepoRoot, string childRepoRef)
    {
        return Path.IsPathRooted(childRepoRef)
            ? Path.GetFullPath(childRepoRef)
            : Path.GetFullPath(Path.Combine(parentRepoRoot, childRepoRef));
    }

    private static GitCommandResult Run(
        IGitCommandRunner commandRunner,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        string defaultError)
    {
        var result = commandRunner.Run(workingDirectory, arguments);
        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StdErr)
                ? defaultError
                : result.StdErr.Trim();
            throw new InvalidOperationException(error);
        }

        return result;
    }
}
