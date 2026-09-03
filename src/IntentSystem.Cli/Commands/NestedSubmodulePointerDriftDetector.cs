using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G791: recognizes the narrow safe case where a host submodule's own gitlink
/// is aligned, while only clean nested checkouts point at commits different
/// from that submodule's recorded gitlinks. This detector never repairs the
/// drift; it records the paths that must remain untouched by this wake.
/// </summary>
internal static class NestedSubmodulePointerDriftDetector
{
    /// <summary>
    /// Inspects the gitlink that the parent commit actually records for a
    /// submodule path. The index is intentionally not trusted here: an index
    /// modification means the host is no longer in the narrow G791 "aligned
    /// parent gitlink" state, even if the checkout happens to match the staged
    /// value. An unreadable staged comparison is likewise fail-closed.
    /// </summary>
    public static ParentGitlinkInspection InspectParentGitlink(IGitRunner runner, string path)
    {
        var headResult = runner.Run(["ls-tree", "HEAD", "--", path]);
        if (headResult.ExitCode != 0 || string.IsNullOrWhiteSpace(headResult.StandardOutput))
        {
            return ParentGitlinkInspection.NotGitlink;
        }

        var firstLine = headResult.StandardOutput.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];
        var parts = firstLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !string.Equals(parts[0], "160000", StringComparison.Ordinal))
        {
            return ParentGitlinkInspection.NotGitlink;
        }
        // `git ls-tree` prints "<mode> commit <sha>\t<path>". The fallback
        // form keeps the parser compatible with older lightweight test runners
        // that modeled just "<mode> <sha>".
        var commitIndex = parts.Length >= 3 && string.Equals(parts[1], "commit", StringComparison.Ordinal)
            ? 2
            : 1;
        if (parts.Length <= commitIndex || string.IsNullOrWhiteSpace(parts[commitIndex]))
        {
            return ParentGitlinkInspection.NotGitlink;
        }

        // `git diff --cached --quiet` exits 1 for a staged change. Any other
        // nonzero exit is also unsafe to treat as aligned: a read failure must
        // never become a direct-proceed decision.
        var stagedResult = runner.Run(["diff", "--cached", "--quiet", "--", path]);
        return new ParentGitlinkInspection
        {
            HeadRecordedCommit = parts[commitIndex].Trim(),
            HasStagedGitlinkChange = stagedResult.ExitCode != 0,
        };
    }

    /// <summary>
    /// Compatibility helper for callers that only need an aligned parent
    /// gitlink. The commit is read from <c>HEAD</c>, never from the index.
    /// </summary>
    public static bool TryGetParentRecordedGitlinkCommit(IGitRunner runner, string path, out string parentCommit)
    {
        var inspection = InspectParentGitlink(runner, path);
        parentCommit = inspection.HeadRecordedCommit ?? string.Empty;
        return inspection.IsAligned;
    }

    public static string? GetCurrentHead(IGitRunner runner, string submodulePath)
    {
        var result = runner.Run(["-C", submodulePath, "rev-parse", "HEAD"]);
        return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
    }

    public static bool TryDetect(
        IGitRunner runner,
        string owningSubmodulePath,
        string parentRecordedCommit,
        string owningSubmodulePorcelain,
        out NestedPointerDriftSubmodule drift)
    {
        drift = default!;
        if (string.IsNullOrWhiteSpace(owningSubmodulePorcelain))
        {
            return false;
        }

        // Fact 1: the host's gitlink is already aligned. A stale host checkout
        // belongs to G357's deterministic submodule-update lane, not this
        // leave-it-alone classification.
        var currentHead = GetCurrentHead(runner, owningSubmodulePath);
        if (!string.Equals(currentHead, parentRecordedCommit, StringComparison.Ordinal))
        {
            return false;
        }

        // Fact 2: the owning submodule itself reports nested gitlinks that
        // differ from its recorded values (the plus marker from submodule
        // status). We deliberately do not infer this from porcelain alone.
        var nestedStatus = runner.Run(["-C", owningSubmodulePath, "submodule", "status"]);
        if (nestedStatus.ExitCode != 0)
        {
            return false;
        }

        var nestedPointerPaths = ParseDifferingNestedSubmodulePaths(nestedStatus.StandardOutput);
        if (nestedPointerPaths.Count == 0)
        {
            return false;
        }

        // The owning submodule may be dirty only because of exactly those
        // nested pointer entries. Any regular file change, untracked file, or
        // another kind of nested status remains a refusal.
        var dirtyOwningPaths = ParsePorcelainPaths(owningSubmodulePorcelain);
        if (dirtyOwningPaths.Count == 0
            || dirtyOwningPaths.Any(path => !nestedPointerPaths.Contains(path, StringComparer.Ordinal)))
        {
            return false;
        }

        // Fact 3: every nested checkout is clean. A pointer difference is safe
        // to leave untouched only when there is no content edit inside it.
        var untouchedPaths = new List<string>();
        foreach (var nestedPath in nestedPointerPaths)
        {
            var fullPath = $"{owningSubmodulePath.TrimEnd('/')}/{nestedPath}";
            var nestedCheckoutStatus = runner.Run(["-C", fullPath, "status", "--porcelain"]);
            if (nestedCheckoutStatus.ExitCode != 0 || !string.IsNullOrWhiteSpace(nestedCheckoutStatus.StandardOutput))
            {
                return false;
            }
            untouchedPaths.Add(fullPath);
        }

        drift = new NestedPointerDriftSubmodule
        {
            OwningSubmodulePath = owningSubmodulePath,
            ParentRecordedCommit = parentRecordedCommit,
            UntouchedNestedPaths = untouchedPaths,
        };
        return true;
    }

    private static IReadOnlyList<string> ParseDifferingNestedSubmodulePaths(string output)
    {
        var paths = new List<string>();
        foreach (var rawLine in output.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawLine.Length < 3 || rawLine[0] != '+')
            {
                continue;
            }

            var rest = rawLine[1..].TrimStart();
            var firstSpace = rest.IndexOf(' ');
            if (firstSpace <= 0)
            {
                continue;
            }
            var pathAndDescription = rest[(firstSpace + 1)..].Trim();
            var pathEnd = pathAndDescription.IndexOf(' ');
            var path = pathEnd >= 0 ? pathAndDescription[..pathEnd] : pathAndDescription;
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path);
            }
        }
        return paths;
    }

    private static IReadOnlyList<string> ParsePorcelainPaths(string output)
    {
        var paths = new List<string>();
        foreach (var rawLine in output.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawLine.Length < 4)
            {
                continue;
            }
            var path = rawLine[3..].Trim();
            var arrowIndex = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrowIndex >= 0)
            {
                path = path[(arrowIndex + 4)..].Trim();
            }
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path);
            }
        }
        return paths;
    }
}

/// <summary>
/// The parent gitlink inspection used by both G791 callers. A gitlink with a
/// staged replacement is deliberately distinct from a non-gitlink path: it is
/// a known unsafe host state that must not fall through to a safe-stash or
/// direct-proceed lane.
/// </summary>
internal sealed record ParentGitlinkInspection
{
    public static ParentGitlinkInspection NotGitlink { get; } = new();

    public string? HeadRecordedCommit { get; init; }

    public bool HasStagedGitlinkChange { get; init; }

    public bool IsGitlink => !string.IsNullOrWhiteSpace(HeadRecordedCommit);

    public bool IsAligned => IsGitlink && !HasStagedGitlinkChange;
}

internal sealed record NestedPointerDriftSubmodule
{
    [JsonPropertyName("owning_submodule_path")]
    public required string OwningSubmodulePath { get; init; }

    [JsonPropertyName("parent_recorded_commit")]
    public required string ParentRecordedCommit { get; init; }

    [JsonPropertyName("untouched_nested_paths")]
    public required IReadOnlyList<string> UntouchedNestedPaths { get; init; }
}
