namespace IntentSystem.Review;

public static class ParentSubmodulePointerUpdater
{
    public static void Stage(string parentRepoRoot, string childRepoRef, IGitCommandRunner commandRunner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentRepoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(childRepoRef);
        ArgumentNullException.ThrowIfNull(commandRunner);

        var childRepoPath = ChildRepoMainSynchronizer.ResolveChildRepoPath(parentRepoRoot, childRepoRef);
        if (!Directory.Exists(childRepoPath))
        {
            throw new InvalidOperationException($"Child repo path was not found at {childRepoPath}");
        }

        var result = commandRunner.Run(parentRepoRoot, ["add", childRepoRef]);
        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StdErr)
                ? "parent submodule pointer stage failed."
                : result.StdErr.Trim();
            throw new InvalidOperationException(error);
        }
    }
}
