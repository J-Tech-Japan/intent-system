using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G462: pure classifier that decides whether a packet / issue is
/// <em>host-only</em> — i.e. its declared target paths point exclusively at
/// host/design-owned metadata (<c>intents/**</c>, <c>.intent-cli/**</c>, and the
/// host-owned root docs <c>AGENTS.md</c> / <c>CLAUDE.md</c>) with no
/// child-owned implementation path (<c>src/**</c>, <c>tests/**</c>,
/// <c>docs/**</c>, <c>README.md</c>, or any other target-repo source file).
///
/// The G458 / issue #1018 regression: a product-goal / intent-tree refresh
/// packet targeting only <c>intents/intent-cli/**</c> was published as a child
/// <c>intent-target</c> issue. The child implementation loop selected it every
/// wake, but a GitHub-contract-only child repo cannot edit host-owned
/// <c>intents/**</c> metadata, so every wake stopped with
/// <c>host-artifact-repair-required</c> — and <c>worker issue-preflight</c>
/// still classified it as <c>ready-to-implement</c>.
///
/// This classifier gives intent-cli a deterministic signal to (a) refuse
/// publishing host-only packets as child issues, and (b) classify a
/// host-only issue as non-actionable in <c>worker issue-preflight</c>.
///
/// Pure: no I/O, no GitHub, no process launch.
/// </summary>
internal static class HostOnlyPacketClassifier
{
    // Prefixes that identify host/design-owned metadata. Aligned with the
    // host-path classification used by host-sync / durable-state preflight
    // (intents/**, .intent-cli/**) plus the always-unsafe host root docs.
    private static readonly string[] HostOwnedPrefixes =
    {
        "intents/",
        ".intent-cli/",
    };

    private static readonly string[] HostOwnedExactPaths =
    {
        "agents.md",
        "claude.md",
    };

    // Matches the `Target paths: ...` line in the issue body's
    // `## Target Repo / Path / Part` section. Captures everything after the
    // colon on that line. Tolerant of leading `-`/`*` bullets and backticks.
    private static readonly Regex TargetPathsLineRegex = new(
        @"(?im)^\s*[-*]?\s*Target\s*paths?\s*:\s*(?<paths>.+)$",
        RegexOptions.Compiled);

    /// <summary>
    /// True when <paramref name="path"/> is a host/design-owned path the child
    /// implementation loop must not edit.
    /// </summary>
    public static bool IsHostOwnedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/').Trim().Trim('`').TrimStart('/');
        if (normalized.Length == 0)
        {
            return false;
        }

        foreach (var prefix in HostOwnedPrefixes)
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var exact in HostOwnedExactPaths)
        {
            if (string.Equals(normalized, exact, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Split a `Target paths:` value into individual paths. Handles
    /// comma-separated and/or whitespace-separated lists and strips surrounding
    /// backticks/quotes.
    /// </summary>
    public static IReadOnlyList<string> SplitTargetPaths(string? targetPathsValue)
    {
        if (string.IsNullOrWhiteSpace(targetPathsValue))
        {
            return Array.Empty<string>();
        }

        return targetPathsValue
            .Split(new[] { ',', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            // Strip surrounding backticks/quotes only — NEVER trim '.', which
            // would corrupt host-owned `.intent-cli/...` paths into `intent-cli/...`.
            .Select(p => p.Trim().Trim('`', '"', '\''))
            .Where(p => p.Length > 0)
            .ToArray();
    }

    /// <summary>
    /// Extract the `Target paths:` list from a packet / issue body's
    /// `## Target Repo / Path / Part` section. Returns the individual paths in
    /// document order (deduplicated, order-preserving). Empty when no
    /// `Target paths:` line is present.
    /// </summary>
    public static IReadOnlyList<string> ExtractTargetPaths(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        var all = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in TargetPathsLineRegex.Matches(body))
        {
            foreach (var path in SplitTargetPaths(match.Groups["paths"].Value))
            {
                if (seen.Add(path))
                {
                    all.Add(path);
                }
            }
        }

        return all;
    }

    /// <summary>
    /// Classify whether the supplied target paths are host-only: at least one
    /// path is present AND every path is host-owned. Returns false when the set
    /// is empty (no evidence) or when any child-owned path is present.
    /// </summary>
    public static bool AllPathsAreHostOwned(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var nonEmpty = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        return nonEmpty.Length > 0 && nonEmpty.All(IsHostOwnedPath);
    }

    /// <summary>
    /// Convenience: parse a packet / issue body and decide if it is a host-only
    /// packet (all declared target paths are host-owned). Returns a structured
    /// verdict so callers can surface the offending paths.
    /// </summary>
    public static HostOnlyPacketVerdict Classify(string? body)
    {
        var paths = ExtractTargetPaths(body);
        var hostOwned = paths.Where(IsHostOwnedPath).ToArray();
        var childOwned = paths.Where(p => !IsHostOwnedPath(p)).ToArray();
        var isHostOnly = paths.Count > 0 && childOwned.Length == 0;
        return new HostOnlyPacketVerdict
        {
            IsHostOnly = isHostOnly,
            TargetPaths = paths,
            HostOwnedPaths = hostOwned,
            ChildOwnedPaths = childOwned,
        };
    }
}

/// <summary>
/// G462: structured result of <see cref="HostOnlyPacketClassifier.Classify"/>.
/// </summary>
internal sealed record HostOnlyPacketVerdict
{
    /// <summary>True when target paths are present and ALL are host-owned.</summary>
    public required bool IsHostOnly { get; init; }

    public required IReadOnlyList<string> TargetPaths { get; init; }

    public required IReadOnlyList<string> HostOwnedPaths { get; init; }

    public required IReadOnlyList<string> ChildOwnedPaths { get; init; }
}
