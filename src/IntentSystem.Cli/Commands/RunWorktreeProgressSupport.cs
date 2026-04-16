using IntentSystem.Review;

namespace IntentSystem.Cli.Commands;

internal static class RunWorktreeProgressSupport
{
    public static bool TryResolveMeaningfulWorktreeDiffPaths(
        IGitCommandRunner gitCommandRunner,
        string worktreePath,
        out IReadOnlyList<string> changedPaths)
    {
        ArgumentNullException.ThrowIfNull(gitCommandRunner);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);

        changedPaths = [];
        GitCommandResult statusResult;
        try
        {
            statusResult = gitCommandRunner.Run(
                worktreePath,
                ["status", "--short", "--untracked-files=all"]);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException
            or ArgumentException
            or DirectoryNotFoundException
            or System.ComponentModel.Win32Exception)
        {
            return false;
        }

        if (statusResult.ExitCode != 0 || string.IsNullOrWhiteSpace(statusResult.StdOut))
        {
            return false;
        }

        var paths = new List<string>();
        foreach (var rawLine in statusResult.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var pathText = line.Length > 3 ? line[3..].Trim() : line.Trim();
            if (string.IsNullOrWhiteSpace(pathText))
            {
                continue;
            }

            var normalizedPath = pathText.Contains(" -> ", StringComparison.Ordinal)
                ? pathText[(pathText.LastIndexOf(" -> ", StringComparison.Ordinal) + 4)..].Trim()
                : pathText;
            normalizedPath = normalizedPath.Trim().Trim('"').Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalizedPath)
                || IsIgnoredWorktreeArtifactPath(normalizedPath))
            {
                continue;
            }

            paths.Add(normalizedPath);
        }

        if (paths.Count == 0)
        {
            return false;
        }

        changedPaths = paths;
        return true;
    }

    public static string SummarizePaths(IReadOnlyList<string> changedPaths)
    {
        ArgumentNullException.ThrowIfNull(changedPaths);

        var summarizedPaths = string.Join(", ", changedPaths.Take(3));
        if (changedPaths.Count > 3)
        {
            summarizedPaths += ", ...";
        }

        return summarizedPaths;
    }

    private static bool IsIgnoredWorktreeArtifactPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedPath = path.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return true;
        }

        if (normalizedPath.StartsWith(".intent-cli/", StringComparison.Ordinal)
            || normalizedPath.StartsWith(".takt/", StringComparison.Ordinal)
            || normalizedPath.StartsWith("node_modules/", StringComparison.Ordinal))
        {
            return true;
        }

        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment =>
            string.Equals(segment, "bin", StringComparison.Ordinal)
            || string.Equals(segment, "obj", StringComparison.Ordinal)
            || string.Equals(segment, "TestResults", StringComparison.Ordinal)
            || string.Equals(segment, ".vs", StringComparison.Ordinal));
    }
}
