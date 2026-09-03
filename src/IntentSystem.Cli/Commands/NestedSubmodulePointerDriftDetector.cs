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
    public static bool TryGetParentRecordedGitlinkCommit(IGitRunner runner, string path, out string parentCommit)
    {
        var result = runner.Run(["ls-files", "--stage", "--", path]);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            parentCommit = string.Empty;
            return false;
        }

        var firstLine = result.StandardOutput.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];
        var parts = firstLine.Split(' ', 3);
        if (parts.Length < 2 || !string.Equals(parts[0], "160000", StringComparison.Ordinal))
        {
            parentCommit = string.Empty;
            return false;
        }

        parentCommit = parts[1].Trim();
        return true;
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

internal sealed record NestedPointerDriftSubmodule
{
    [JsonPropertyName("owning_submodule_path")]
    public required string OwningSubmodulePath { get; init; }

    [JsonPropertyName("parent_recorded_commit")]
    public required string ParentRecordedCommit { get; init; }

    [JsonPropertyName("untouched_nested_paths")]
    public required IReadOnlyList<string> UntouchedNestedPaths { get; init; }
}
