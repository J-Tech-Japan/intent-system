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
        if (!TryClassifyWorktreeDiffPaths(
                gitCommandRunner,
                worktreePath,
                out var meaningfulPaths,
                out _))
        {
            return false;
        }

        if (meaningfulPaths.Count == 0)
        {
            return false;
        }

        changedPaths = meaningfulPaths;
        return true;
    }

    public static bool TryResolveOutOfScopeRuntimeArtifactDiffPaths(
        IGitCommandRunner gitCommandRunner,
        string worktreePath,
        out IReadOnlyList<string> changedPaths)
    {
        ArgumentNullException.ThrowIfNull(gitCommandRunner);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);

        changedPaths = [];
        if (!TryClassifyWorktreeDiffPaths(
                gitCommandRunner,
                worktreePath,
                out _,
                out var runtimeArtifactPaths))
        {
            return false;
        }

        changedPaths = runtimeArtifactPaths;
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

    private static bool TryClassifyWorktreeDiffPaths(
        IGitCommandRunner gitCommandRunner,
        string worktreePath,
        out IReadOnlyList<string> meaningfulPaths,
        out IReadOnlyList<string> runtimeArtifactPaths)
    {
        meaningfulPaths = [];
        runtimeArtifactPaths = [];

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

        var meaningful = new List<string>();
        var runtimeArtifacts = new List<string>();
        foreach (var rawLine in statusResult.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var normalizedPath = TryNormalizeStatusPath(rawLine);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                continue;
            }

            if (IsOutOfScopeRuntimeArtifactPath(normalizedPath))
            {
                runtimeArtifacts.Add(normalizedPath);
                continue;
            }

            if (IsIgnoredWorktreeArtifactPath(normalizedPath))
            {
                continue;
            }

            meaningful.Add(normalizedPath);
        }

        if (meaningful.Count > 0)
        {
            meaningfulPaths = meaningful;
            return true;
        }

        if (runtimeArtifacts.Count > 0)
        {
            runtimeArtifactPaths = runtimeArtifacts;
            return true;
        }

        return false;
    }

    private static string? TryNormalizeStatusPath(string rawLine)
    {
        var line = rawLine.TrimEnd();
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var pathText = line.Length > 3 ? line[3..].Trim() : line.Trim();
        if (string.IsNullOrWhiteSpace(pathText))
        {
            return null;
        }

        var normalizedPath = pathText.Contains(" -> ", StringComparison.Ordinal)
            ? pathText[(pathText.LastIndexOf(" -> ", StringComparison.Ordinal) + 4)..].Trim()
            : pathText;
        normalizedPath = normalizedPath.Trim().Trim('"').Replace('\\', '/');
        return string.IsNullOrWhiteSpace(normalizedPath) ? null : normalizedPath;
    }

    private static bool IsOutOfScopeRuntimeArtifactPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return path.Replace('\\', '/').TrimStart('/')
            .StartsWith(".intent-cli/", StringComparison.Ordinal);
    }
}
