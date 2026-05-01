namespace IntentSystem.Cli.Commands;

/// <summary>
/// G213: <c>intent-cli automation check</c> is a local convenience wrapper
/// around <c>worker next-action</c>. It owns worktree-to-GitHub-repo inference
/// so prompt templates do not need shell snippets for common origin remotes.
/// The command is read-only and delegates selection to the existing worker
/// implementation.
/// </summary>
internal static class AutomationCheckCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var repoOverride, out var workdir, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var resolvedWorkdir = ResolveWorkdir(context, workdir);
        var repo = repoOverride;
        if (string.IsNullOrWhiteSpace(repo)
            && !TryInferGitHubRepo(resolvedWorkdir, out repo, out error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var workerArgs = new List<string>
        {
            "--repo", repo!,
            "--workdir", resolvedWorkdir,
            "--format", format
        };

        return WorkerNextActionCommand.Execute(context, workerArgs.ToArray(), writer);
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out string? workdir,
        out string format,
        out string error)
    {
        repo = null;
        workdir = null;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (e.g. owner/repo).";
                        return false;
                    }

                    repo = args[index + 1].Trim();
                    index++;
                    break;

                case "--workdir":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--workdir requires a value.";
                        return false;
                    }

                    workdir = args[index + 1];
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (text or json).";
                        return false;
                    }

                    var requestedFormat = args[index + 1];
                    if (!string.Equals(requestedFormat, FormatText, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'text' or 'json' (got '{requestedFormat}').";
                        return false;
                    }

                    format = requestedFormat;
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'. Supported: [--repo <owner/repo>] [--workdir <path>] [--format text|json].";
                    return false;
            }
        }

        return true;
    }

    private static string ResolveWorkdir(CliContext context, string? workdir)
    {
        if (string.IsNullOrWhiteSpace(workdir))
        {
            return context.RepoRoot;
        }

        return Path.GetFullPath(workdir);
    }

    internal static bool TryInferGitHubRepo(string workdir, out string? repo, out string error)
    {
        repo = null;
        error = string.Empty;

        if (!Directory.Exists(workdir))
        {
            error = $"Cannot infer repo: workdir does not exist: {workdir}";
            return false;
        }

        if (!TryFindGitDirectory(workdir, out var gitDirectory, out error))
        {
            return false;
        }

        if (!TryReadOriginRemoteUrl(gitDirectory, out var remoteUrl, out error))
        {
            return false;
        }

        if (!TryParseGitHubRepo(remoteUrl!, out repo))
        {
            error = $"Cannot infer GitHub repo from origin remote '{remoteUrl}' in {workdir}. Expected a github.com owner/repo remote.";
            return false;
        }

        return true;
    }

    private static bool TryFindGitDirectory(string workdir, out string gitDirectory, out string error)
    {
        var directory = new DirectoryInfo(workdir);
        while (directory is not null)
        {
            var gitPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitPath))
            {
                gitDirectory = gitPath;
                error = string.Empty;
                return true;
            }

            if (File.Exists(gitPath))
            {
                var line = File.ReadLines(gitPath).FirstOrDefault() ?? string.Empty;
                const string prefix = "gitdir:";
                if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    gitDirectory = string.Empty;
                    error = $"Cannot infer repo: {gitPath} is not a gitdir pointer.";
                    return false;
                }

                var gitDirValue = line[prefix.Length..].Trim();
                gitDirectory = Path.IsPathRooted(gitDirValue)
                    ? Path.GetFullPath(gitDirValue)
                    : Path.GetFullPath(Path.Combine(directory.FullName, gitDirValue));
                error = string.Empty;
                return true;
            }

            directory = directory.Parent;
        }

        gitDirectory = string.Empty;
        error = $"Cannot infer repo: no .git directory found from {workdir}.";
        return false;
    }

    private static bool TryReadOriginRemoteUrl(string gitDirectory, out string? remoteUrl, out string error)
    {
        remoteUrl = null;
        error = string.Empty;

        foreach (var configPath in EnumerateConfigPaths(gitDirectory))
        {
            if (!File.Exists(configPath))
            {
                continue;
            }

            if (TryReadOriginRemoteUrlFromConfig(configPath, out remoteUrl))
            {
                return true;
            }
        }

        error = $"Cannot infer repo: no origin remote URL found in git config for {gitDirectory}.";
        return false;
    }

    private static IEnumerable<string> EnumerateConfigPaths(string gitDirectory)
    {
        yield return Path.Combine(gitDirectory, "config");

        var commonDirPath = Path.Combine(gitDirectory, "commondir");
        if (File.Exists(commonDirPath))
        {
            var commonDirValue = File.ReadAllText(commonDirPath).Trim();
            if (!string.IsNullOrWhiteSpace(commonDirValue))
            {
                var commonDir = Path.IsPathRooted(commonDirValue)
                    ? Path.GetFullPath(commonDirValue)
                    : Path.GetFullPath(Path.Combine(gitDirectory, commonDirValue));
                yield return Path.Combine(commonDir, "config");
            }
        }
    }

    private static bool TryReadOriginRemoteUrlFromConfig(string configPath, out string? remoteUrl)
    {
        remoteUrl = null;
        var inOriginRemote = false;
        foreach (var rawLine in File.ReadLines(configPath))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                inOriginRemote = string.Equals(line, "[remote \"origin\"]", StringComparison.Ordinal);
                continue;
            }

            if (!inOriginRemote || !line.StartsWith("url", StringComparison.Ordinal))
            {
                continue;
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            remoteUrl = line[(separator + 1)..].Trim();
            return !string.IsNullOrWhiteSpace(remoteUrl);
        }

        return false;
    }

    internal static bool TryParseGitHubRepo(string remoteUrl, out string? repo)
    {
        repo = null;
        var normalized = remoteUrl.Trim();

        const string httpsPrefix = "https://github.com/";
        const string sshPrefix = "git@github.com:";
        if (normalized.StartsWith(httpsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[httpsPrefix.Length..];
        }
        else if (normalized.StartsWith(sshPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[sshPrefix.Length..];
        }
        else
        {
            return false;
        }

        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        repo = $"{parts[0]}/{parts[1]}";
        return true;
    }
}
