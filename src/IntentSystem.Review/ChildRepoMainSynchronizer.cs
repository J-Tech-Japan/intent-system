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
        var mergeResult = commandRunner.Run(childRepoPath, ["merge", "--ff-only", "origin/main"]);
        if (mergeResult.ExitCode != 0
            && !TryReconcileAcceptedWorktreeState(commandRunner, childRepoPath, mergedCommitSha, mergeResult))
        {
            var error = string.IsNullOrWhiteSpace(mergeResult.StdErr)
                ? "child repo main fast-forward failed."
                : mergeResult.StdErr.Trim();
            throw new InvalidOperationException(error);
        }

        var headResult = Run(commandRunner, childRepoPath, ["rev-parse", "HEAD"], "child repo HEAD resolve failed.");
        var headCommit = headResult.StdOut.Trim();
        if (string.Equals(headCommit, mergedCommitSha, StringComparison.Ordinal))
        {
            return;
        }

        var mergedCommitContainedInHead = commandRunner.Run(
            childRepoPath,
            ["merge-base", "--is-ancestor", mergedCommitSha, headCommit]);
        if (mergedCommitContainedInHead.ExitCode != 0)
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

    private static bool TryReconcileAcceptedWorktreeState(
        IGitCommandRunner commandRunner,
        string childRepoPath,
        string mergedCommitSha,
        GitCommandResult mergeResult)
    {
        if (!TryResolveMergeOverwritePaths(
                mergeResult.StdErr + Environment.NewLine + mergeResult.StdOut,
                out var conflictingPaths,
                out var untrackedConflictingPaths))
        {
            return false;
        }

        var statusResult = Run(
            commandRunner,
            childRepoPath,
            ["status", "--porcelain=v1", "--untracked-files=all"],
            "child repo status resolve failed.");
        var dirtyPaths = ParseStatusPaths(statusResult.StdOut);
        if (dirtyPaths.Count == 0)
        {
            return false;
        }

        var outOfScopeDirtyPaths = dirtyPaths
            .Where(path => !conflictingPaths.Contains(path) && !IsRepoLocalAcceptedCloseoutStatePath(path))
            .ToArray();
        if (outOfScopeDirtyPaths.Length > 0)
        {
            throw new InvalidOperationException(
                $"child repo accepted closeout reconciliation found additional dirty paths outside the merge-overwrite set: {string.Join(", ", outOfScopeDirtyPaths)}");
        }

        if (untrackedConflictingPaths.Count > 0)
        {
            Run(
                commandRunner,
                childRepoPath,
                ["clean", "-fd", "--", .. untrackedConflictingPaths],
                "child repo accepted closeout clean failed.");
        }

        Run(
            commandRunner,
            childRepoPath,
            ["reset", "--hard", mergedCommitSha],
            "child repo accepted closeout reset failed.");

        var postResetStatusResult = Run(
            commandRunner,
            childRepoPath,
            ["status", "--porcelain=v1", "--untracked-files=all"],
            "child repo status resolve failed.");
        var postResetDirtyPaths = ParseStatusPaths(postResetStatusResult.StdOut)
            .Where(path => !IsRepoLocalAcceptedCloseoutStatePath(path))
            .ToArray();
        if (postResetDirtyPaths.Length > 0)
        {
            throw new InvalidOperationException(
                "child repo accepted closeout reconciliation left unexpected worktree drift after resetting to the merged commit.");
        }

        return true;
    }

    private static bool IsRepoLocalAcceptedCloseoutStatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalized = path.Replace('\\', '/').Trim();
        return normalized.StartsWith(".intent-cli/", StringComparison.Ordinal)
            || string.Equals(normalized, ".intent-cli", StringComparison.Ordinal)
            || normalized.EndsWith("/clarifications/open.md", StringComparison.Ordinal)
            || normalized.EndsWith("/execution/01-issue-ready-slices.md", StringComparison.Ordinal)
            || normalized.EndsWith("/intent-tree/00-map.md", StringComparison.Ordinal);
    }

    private static bool TryResolveMergeOverwritePaths(
        string value,
        out HashSet<string> conflictingPaths,
        out HashSet<string> untrackedConflictingPaths)
    {
        conflictingPaths = [];
        untrackedConflictingPaths = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var lines = value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToArray();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var isUntrackedSection =
                line.Contains("The following untracked working tree files would be overwritten by merge:", StringComparison.Ordinal);
            var isTrackedSection =
                line.Contains("Your local changes to the following files would be overwritten by merge:", StringComparison.Ordinal);
            if (!isUntrackedSection && !isTrackedSection)
            {
                continue;
            }

            for (var pathIndex = index + 1; pathIndex < lines.Length; pathIndex++)
            {
                var candidate = lines[pathIndex];
                if (candidate.StartsWith("Please ", StringComparison.Ordinal)
                    || candidate.StartsWith("Aborting", StringComparison.Ordinal)
                    || candidate.Contains("would be overwritten by merge", StringComparison.Ordinal))
                {
                    break;
                }

                conflictingPaths.Add(candidate);
                if (isUntrackedSection)
                {
                    untrackedConflictingPaths.Add(candidate);
                }
            }
        }

        return conflictingPaths.Count > 0;
    }

    private static IReadOnlyList<string> ParseStatusPaths(string stdOut)
    {
        if (string.IsNullOrWhiteSpace(stdOut))
        {
            return [];
        }

        return stdOut
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(
                line =>
                {
                    var path = line.Length > 3
                        ? line[3..].Trim()
                        : line.Trim();
                    var renameSeparatorIndex = path.IndexOf(" -> ", StringComparison.Ordinal);
                    return renameSeparatorIndex >= 0
                        ? path[(renameSeparatorIndex + 4)..].Trim()
                        : path;
                })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }
}
