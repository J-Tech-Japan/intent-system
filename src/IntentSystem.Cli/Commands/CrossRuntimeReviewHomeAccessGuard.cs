namespace IntentSystem.Cli.Commands;

/// <summary>
/// G842: refuses reads of operator runtime state and unsafe reviewer layouts.
/// Path checks use metadata only; protected roots are never enumerated or read.
/// </summary>
internal static class CrossRuntimeReviewHomeAccessGuard
{
    public static Func<string, bool>? ShouldRefusePath { get; set; }

    // Tests use this seam to make any protected-root enumeration or content
    // read fail loudly. Metadata-only lstat and path resolution do not call it.
    internal static Action<string>? ProtectedPathAccessProbe { get; set; }

    internal static void BeforeProtectedPathAccess(string path)
    {
        if (IsProtectedOperatorPath(path))
        {
            ProtectedPathAccessProbe?.Invoke(path);
        }
    }

    public static bool IsProtectedOperatorPath(string path)
    {
        if (ShouldRefusePath?.Invoke(path) == true)
        {
            return true;
        }

        var normalized = ResolvePath(path);
        return EnumerateProtectedHomes().Any(root =>
            PathsOverlap(normalized, ResolvePath(root))
            || LexicalPathsOverlap(path, root));
    }

    public static void GuardPath(string path)
    {
        if (IsProtectedOperatorPath(path))
        {
            throw new InvalidOperationException($"cross-runtime review must not read operator path '{path}'.");
        }
    }

    public static bool TryRefuseProtectedPath(string path, out string detail)
    {
        if (!IsProtectedOperatorPath(path))
        {
            detail = string.Empty;
            return false;
        }

        detail = $"cross-runtime review must not read operator path '{path}'.";
        return true;
    }

    internal static bool TryRefuseRequestPaths(
        string outDir,
        string runtime,
        string kind,
        bool hasClone,
        string workspace,
        string? providerConfigPath,
        out string detail)
    {
        detail = string.Empty;
        if (runtime is not (CrossRuntimeReviewRuntimes.Copilot or CrossRuntimeReviewRuntimes.Opencode))
        {
            return false;
        }

        var rendered = CrossRuntimeReviewFiles.RenderedFor(runtime, kind, hasClone);
        var planned = rendered.Select(name => Path.Combine(outDir, name)).ToList();
        planned.Insert(0, outDir);
        var resolvedOutDir = ResolvePath(outDir);
        var resolvedWorkspace = ResolvePath(workspace);

        foreach (var root in EnumerateProtectedHomes())
        {
            var resolvedRoot = ResolvePath(root);
            foreach (var path in planned)
            {
                var resolvedPath = ResolvePath(path);
                if (PathsOverlap(resolvedPath, resolvedRoot))
                {
                    detail = $"planned path '{resolvedPath}' overlaps protected home '{resolvedRoot}'.";
                    return true;
                }

                if (LexicalPathsOverlap(path, root))
                {
                    detail = $"planned path '{resolvedPath}' overlaps protected home '{resolvedRoot}'.";
                    return true;
                }
            }

            if (PathsOverlap(resolvedWorkspace, resolvedRoot))
            {
                detail = $"review workspace '{resolvedWorkspace}' overlaps protected home '{resolvedRoot}'.";
                return true;
            }

            if (LexicalPathsOverlap(workspace, root))
            {
                detail = $"review workspace '{resolvedWorkspace}' overlaps protected home '{resolvedRoot}'.";
                return true;
            }
        }

        if (hasClone && PathsOverlap(resolvedWorkspace, resolvedOutDir))
        {
            detail = $"review workspace '{resolvedWorkspace}' overlaps out-dir '{resolvedOutDir}'.";
            return true;
        }

        if (hasClone && LexicalPathsOverlap(workspace, outDir))
        {
            detail = $"review workspace '{resolvedWorkspace}' overlaps out-dir '{resolvedOutDir}'.";
            return true;
        }

        foreach (var path in planned)
        {
            var resolvedPath = ResolvePath(path);
            if (!hasClone && (PathEquals(path, workspace) || PathEquals(path, outDir)))
            {
                // <out>/workspace is the intentional design fallback. It is
                // listed in RenderedFor and is never compared against itself.
                continue;
            }

            if (PathsOverlap(resolvedPath, resolvedWorkspace))
            {
                detail = $"planned path '{resolvedPath}' overlaps review workspace '{resolvedWorkspace}'.";
                return true;
            }

            if (LexicalPathsOverlap(path, workspace))
            {
                detail = $"planned path '{resolvedPath}' overlaps review workspace '{resolvedWorkspace}'.";
                return true;
            }
        }

        foreach (var path in planned)
        {
            if (!TryValidateExistingPlannedPath(path, resolvedOutDir, path == outDir, out detail))
            {
                return true;
            }
        }

        if (!TryValidateExistingPlannedPath(workspace, resolvedWorkspace, isRoot: true, out detail))
        {
            // A design fallback is allowed to be created below a valid out-dir.
            if (!hasClone && !PathIsPresent(workspace))
            {
                detail = string.Empty;
            }
            else
            {
                return true;
            }
        }

        if (!TryValidateWorkspaceSymlinks(workspace, resolvedWorkspace, out detail))
        {
            return true;
        }

        if (providerConfigPath is not null && TryRefuseProviderSource(providerConfigPath, resolvedWorkspace, out detail))
        {
            return true;
        }

        return false;
    }

    internal static bool TryRefuseProviderSource(string path, string workspace, out string detail)
    {
        detail = string.Empty;
        if (IsProtectedOperatorPath(path))
        {
            detail = $"opencode provider config source '{path}' resolves inside an operator-protected root.";
            return true;
        }

        if (PathsOverlap(ResolvePath(path), ResolvePath(workspace)))
        {
            detail = $"opencode provider config source '{path}' resolves inside the review workspace.";
            return true;
        }

        if (LexicalPathsOverlap(path, workspace))
        {
            detail = $"opencode provider config source '{path}' resolves inside the review workspace.";
            return true;
        }

        return false;
    }

    // Kept for the existing request-writer seam. The planned-path checks are
    // the section 3a protections for the two new runtimes only; legacy
    // runtimes must retain their merge-base behavior.
    public static bool TryRefusePlannedPaths(string outDir, string runtime, out string detail)
    {
        if (runtime is not (CrossRuntimeReviewRuntimes.Copilot or CrossRuntimeReviewRuntimes.Opencode))
        {
            detail = string.Empty;
            return false;
        }

        detail = string.Empty;
        var resolvedOutDir = ResolvePath(outDir);
        foreach (var planned in CrossRuntimeReviewFiles.RenderedFor(runtime)
                     .Select(name => Path.Combine(outDir, name)))
        {
            if (!TryValidateExistingPlannedPath(planned, resolvedOutDir, isRoot: false, out detail))
            {
                return true;
            }
        }

        return false;
    }

    internal static IEnumerable<string> EnumerateProtectedHomes()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        yield return Path.Combine(home, ".copilot");

        var copilotHome = Environment.GetEnvironmentVariable("COPILOT_HOME");
        if (!string.IsNullOrWhiteSpace(copilotHome))
        {
            yield return copilotHome;
        }

        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configHome = string.IsNullOrWhiteSpace(xdgConfigHome) ? Path.Combine(home, ".config") : xdgConfigHome;
        yield return Path.Combine(home, ".config", "opencode");
        yield return Path.Combine(configHome, "opencode");

        var opencodeConfigDir = Environment.GetEnvironmentVariable("OPENCODE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(opencodeConfigDir))
        {
            yield return opencodeConfigDir;
        }

        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var dataHome = string.IsNullOrWhiteSpace(xdgDataHome) ? Path.Combine(home, ".local", "share") : xdgDataHome;
        yield return Path.Combine(home, ".local", "share", "opencode");
        yield return Path.Combine(dataHome, "opencode");

        var xdgStateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        var stateHome = string.IsNullOrWhiteSpace(xdgStateHome) ? Path.Combine(home, ".local", "state") : xdgStateHome;
        yield return Path.Combine(home, ".local", "state", "opencode");
        yield return Path.Combine(stateHome, "opencode");

        var xdgCacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var cacheHome = string.IsNullOrWhiteSpace(xdgCacheHome) ? Path.Combine(home, ".cache") : xdgCacheHome;
        yield return Path.Combine(home, ".cache", "opencode");
        yield return Path.Combine(cacheHome, "opencode");

        var ghConfigDir = Environment.GetEnvironmentVariable("GH_CONFIG_DIR");
        if (string.IsNullOrWhiteSpace(ghConfigDir))
        {
            ghConfigDir = Path.Combine(configHome, "gh");
        }

        yield return ghConfigDir;
    }

    internal static bool IsRegularFile(string path)
    {
        return CrossRuntimeReviewFileMode.IsRegularFile(path);
    }

    internal static string ResolvePath(string path)
    {
        try
        {
            return ResolveRealPath(Path.GetFullPath(path), new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);
        }
        catch (IOException)
        {
            return Path.GetFullPath(path);
        }
        catch (UnauthorizedAccessException)
        {
            return Path.GetFullPath(path);
        }
    }

    private static string ResolveRealPath(string path, HashSet<string> seen, int depth)
    {
        if (depth > 64 || !seen.Add(path))
        {
            return path;
        }

        var root = Path.GetPathRoot(path) ?? string.Empty;
        var relative = path[root.Length..];
        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        var resolved = root;
        foreach (var segment in segments)
        {
            var current = Path.GetFullPath(Path.Combine(resolved, segment));
            if (!TryGetLinkTarget(current, out var target))
            {
                resolved = current;
                continue;
            }

            var targetPath = Path.IsPathRooted(target)
                ? target
                : Path.Combine(Path.GetDirectoryName(current) ?? root, target);
            resolved = ResolveRealPath(Path.GetFullPath(targetPath), seen, depth + 1);
        }

        return Path.GetFullPath(resolved);
    }

    private static bool TryValidateExistingPlannedPath(string path, string resolvedRoot, bool isRoot, out string detail)
    {
        detail = string.Empty;
        var present = PathIsPresent(path);
        if (present && IsSymlink(path))
        {
            detail = $"planned path '{path}' is a symlink.";
            return false;
        }

        var resolved = ResolvePath(path);
        if (!isRoot && !IsInsideOrEqual(resolved, resolvedRoot))
        {
            detail = $"planned path '{path}' resolves outside the out-dir.";
            return false;
        }

        if (!present)
        {
            return true;
        }

        if (isRoot)
        {
            return true;
        }

        var name = Path.GetFileName(path);
        if (name is not null && RenderedFileNames.Contains(name, StringComparer.Ordinal) && !IsRegularFile(path))
        {
            detail = $"planned rendered path '{path}' is not a regular file.";
            return false;
        }

        if (name == CrossRuntimeReviewFiles.Workspace
            || name == CrossRuntimeReviewFiles.CopilotHome
            || name == CrossRuntimeReviewFiles.CopilotXdg
            || name == CrossRuntimeReviewFiles.OpencodeXdg
            || name == CrossRuntimeReviewFiles.OpencodeConfigDir)
        {
            if (!Directory.Exists(path))
            {
                detail = $"planned isolation path '{path}' is not a directory.";
                return false;
            }
        }

        return true;
    }

    private static bool TryValidateWorkspaceSymlinks(string workspace, string resolvedWorkspace, out string detail)
    {
        detail = string.Empty;
        if (!PathIsPresent(workspace))
        {
            return true;
        }

        if (!Directory.Exists(resolvedWorkspace))
        {
            detail = $"review workspace '{workspace}' is not a directory.";
            return false;
        }

        var pending = new Stack<string>();
        pending.Push(resolvedWorkspace);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> entries;
            try
            {
                BeforeProtectedPathAccess(directory);
                entries = Directory.EnumerateFileSystemEntries(directory).ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                detail = $"review workspace '{workspace}' could not be inspected: {exception.Message}";
                return false;
            }

            foreach (var entry in entries.OrderBy(value => value, StringComparer.Ordinal))
            {
                if (IsSymlink(entry))
                {
                    if (!TryResolveExistingLink(entry, out var target)
                        || !PathIsPresent(target)
                        || !IsInsideOrEqual(ResolvePath(target), resolvedWorkspace))
                    {
                        var relative = Path.GetRelativePath(resolvedWorkspace, entry).Replace(Path.DirectorySeparatorChar, '/');
                        detail = $"review workspace contains an escaping symlink at '{relative}'.";
                        return false;
                    }

                    continue;
                }

                if (Directory.Exists(entry))
                {
                    pending.Push(entry);
                }
            }
        }

        return true;
    }

    private static bool TryResolveExistingLink(string path, out string target)
    {
        target = string.Empty;
        if (!TryGetLinkTarget(path, out var linkTarget))
        {
            return false;
        }

        target = Path.IsPathRooted(linkTarget)
            ? linkTarget
            : Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, linkTarget);
        return true;
    }

    private static bool TryGetLinkTarget(string path, out string target)
    {
        target = string.Empty;
        try
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.LinkTarget is not null)
            {
                target = fileInfo.LinkTarget;
                return true;
            }

            var directoryInfo = new DirectoryInfo(path);
            if (directoryInfo.LinkTarget is not null)
            {
                target = directoryInfo.LinkTarget;
                return true;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    internal static bool PathIsPresent(string path) =>
        IsSymlink(path) || File.Exists(path) || Directory.Exists(path);

    private static bool PathsOverlap(string left, string right) =>
        PathEquals(left, right) || IsInside(left, right) || IsInside(right, left);

    private static bool LexicalPathsOverlap(string left, string right) =>
        PathsOverlap(Path.GetFullPath(left), Path.GetFullPath(right));

    private static bool IsInsideOrEqual(string path, string root) =>
        PathEquals(path, root) || IsInside(path, root);

    private static bool PathEquals(string left, string right) =>
        string.Equals(TrimTrailingSeparator(left), TrimTrailingSeparator(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsInside(string path, string root)
    {
        var normalizedPath = TrimTrailingSeparator(path);
        var normalizedRoot = TrimTrailingSeparator(root);
        var prefix = normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimTrailingSeparator(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 ? path : trimmed;
    }

    private static bool IsSymlink(string path) => TryGetLinkTarget(path, out _);

    private static readonly HashSet<string> RenderedFileNames = new(StringComparer.Ordinal)
    {
        CrossRuntimeReviewFiles.Prompt,
        CrossRuntimeReviewFiles.Schema,
        CrossRuntimeReviewFiles.Invocation,
        CrossRuntimeReviewFiles.OpencodeReviewerConfig,
    };
}
