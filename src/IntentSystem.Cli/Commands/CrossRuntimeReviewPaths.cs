namespace IntentSystem.Cli.Commands;

/// <summary>
/// G834: path, repository, and shell-quoting rules shared by the cross-runtime
/// review request, record store, and gate.
/// </summary>
internal static class CrossRuntimeReviewPaths
{
    public const string StoreRootRelativePath = ".intent-cli/cross-runtime-reviews";
    public const string PacketRootRelativePath = ".intent-cli/issues";

    /// <summary>
    /// <c>&lt;owner&gt;/&lt;repo&gt;</c> with ASCII letters, digits, <c>-</c>, <c>_</c>,
    /// and <c>.</c> in each segment, never a dot-segment. The same rule guards the
    /// store folder, so a repository name can never escape the store root.
    /// </summary>
    public static bool IsRepositoryName(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var segments = value.Split('/');
        return segments.Length == 2
            && segments.All(segment =>
                segment.Length > 0
                && segment[0] != '.'
                && segment.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'));
    }

    public static bool IsFullHeadSha(string? value) =>
        value is { Length: 40 } && value.All(Uri.IsHexDigit);

    /// <summary>Lower-cased <c>&lt;owner&gt;__&lt;repo&gt;</c>.</summary>
    public static string RepoFolder(string repo)
    {
        if (!IsRepositoryName(repo))
        {
            throw new InvalidOperationException($"repository '{repo}' must be '<owner>/<repo>'.");
        }

        return string.Join("__", repo.Split('/')).ToLowerInvariant();
    }

    public static string PrRelativeDirectory(string repo, int pr) =>
        $"{StoreRootRelativePath}/{RepoFolder(repo)}/pr-{pr.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public static string PrDirectory(string repoRoot, string repo, int pr)
    {
        var root = Path.GetFullPath(Path.Combine(repoRoot, StoreRootRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var resolved = Path.GetFullPath(Path.Combine(repoRoot, PrRelativeDirectory(repo, pr).Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"resolved cross-runtime review directory for {repo}#{pr} escapes `{StoreRootRelativePath}` ({resolved}).");
        }

        return resolved;
    }

    public static string DesignRelativeDirectory(string executionUnit) =>
        $"{StoreRootRelativePath}/design/{executionUnit}";

    public static string DesignDirectory(string repoRoot, string executionUnit)
    {
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out var error))
        {
            throw new InvalidOperationException(error);
        }

        var root = Path.GetFullPath(Path.Combine(repoRoot, StoreRootRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var resolved = Path.GetFullPath(Path.Combine(repoRoot, DesignRelativeDirectory(executionUnit).Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"resolved cross-runtime design review directory for {executionUnit} escapes `{StoreRootRelativePath}` ({resolved}).");
        }

        return resolved;
    }

    public static string PacketDirectory(string repoRoot, string executionUnit)
    {
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out var error))
        {
            throw new InvalidOperationException(error);
        }

        return Path.GetFullPath(Path.Combine(
            repoRoot,
            PacketRootRelativePath.Replace('/', Path.DirectorySeparatorChar),
            executionUnit));
    }

    /// <summary>True when a path may be interpolated into rendered text at all.</summary>
    public static bool IsRenderablePath(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.IndexOfAny(['\n', '\r', '\0']) < 0;

    /// <summary>
    /// POSIX single-quote escaping: the whole value becomes one argument, and an
    /// embedded <c>'</c> becomes <c>'\''</c>. Spaces, <c>$</c>, <c>;</c>, and
    /// backticks are inert inside single quotes. Newline and NUL are refused
    /// before rendering (<see cref="IsRenderablePath"/>).
    /// </summary>
    public static string ShellQuote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!IsRenderablePath(value))
        {
            throw new InvalidOperationException("a path containing a newline or NUL cannot be rendered.");
        }

        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }
}
