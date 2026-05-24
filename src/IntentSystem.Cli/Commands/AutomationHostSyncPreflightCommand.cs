using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G304: <c>intent-cli automation host-sync-preflight</c> — read-only host
/// pre-wake sync gate. Runs <c>git rev-parse --abbrev-ref HEAD</c>,
/// <c>git rev-list --count HEAD..origin/&lt;branch&gt;</c>, and
/// <c>git status --porcelain</c> in the host repo cwd, feeds them to
/// <see cref="HostSyncPreflightAnalyzer"/>, and emits the structured
/// classification (<c>clean</c>, <c>behind-origin</c>,
/// <c>dirty-host-durable-state</c>, <c>dirty-unrelated-submodule</c>,
/// <c>dirty-mixed</c>) plus the recommended next steps. Exits 0 only when
/// the analyzer reports <c>proceed_allowed = true</c>; the host-loop
/// guidance reads the exit code (and the structured JSON) to decide
/// whether to proceed past the read-only preflight.
///
/// Never fetches automatically and never mutates state — the contract is
/// to surface the gap so the operator (or the host-loop guidance) can
/// `git pull --ff-only` / commit / revert as appropriate.
/// </summary>
internal static class AutomationHostSyncPreflightCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    /// <summary>Test seam — replaces the default git output capture.</summary>
    public static Func<string, HostSyncGitProbe>? GitProbeFactory { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        HostSyncGitProbe probe;
        try
        {
            probe = GitProbeFactory?.Invoke(context.RepoRoot)
                ?? CapturePorcelainProbe(context.RepoRoot);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            writer.WriteLine($"failed to capture host-sync probe: {exception.Message}");
            return 1;
        }

        var result = HostSyncPreflightAnalyzer.Analyze(
            probe.Branch,
            probe.BehindOriginCommits,
            probe.WorkingTreeEntries,
            probe.SubmoduleGitlinkMismatchPaths,
            probe.AheadOriginCommits,
            probe.LocalOnlyCommits,
            probe.OriginAheadCommits);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }
        return result.ProceedAllowed ? 0 : 1;
    }

    private static void WriteMarkdown(TextWriter writer, HostSyncPreflightResult result)
    {
        writer.WriteLine($"# automation host-sync-preflight (G304) — `{result.Branch}`");
        writer.WriteLine();
        writer.WriteLine($"- Classification: **{result.Classification}**");
        writer.WriteLine($"- Behind origin: {result.BehindOriginCommits} commit(s)");
        writer.WriteLine($"- Ahead of origin: {result.AheadOriginCommits} commit(s)");
        writer.WriteLine($"- Dirty durable-state paths: {result.DirtyDurableStatePaths.Count}");
        writer.WriteLine($"- Dirty unrelated paths: {result.DirtyUnrelatedPaths.Count}");
        writer.WriteLine($"- Proceed allowed: {(result.ProceedAllowed ? "yes" : "**no**")}");
        writer.WriteLine();
        writer.WriteLine(result.Summary);

        if (result.DirtyDurableStatePaths.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Dirty durable-state paths");
            foreach (var entry in result.DirtyDurableStatePaths)
            {
                writer.WriteLine($"- `{entry.Status}` `{entry.Path}`");
            }
        }
        if (result.DirtyUnrelatedPaths.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Dirty unrelated paths");
            foreach (var entry in result.DirtyUnrelatedPaths)
            {
                writer.WriteLine($"- `{entry.Status}` `{entry.Path}`");
            }
        }
        if (string.Equals(result.Classification, HostSyncPreflightAnalyzer.ClassificationFfBlocked, StringComparison.Ordinal))
        {
            writer.WriteLine();
            writer.WriteLine("## Diverged commits (G400 — ff-blocked)");
            writer.WriteLine();
            writer.WriteLine("Local-only commit(s):");
            if (result.LocalOnlyCommits.Count == 0)
            {
                writer.WriteLine("- (details unavailable)");
            }
            foreach (var commit in result.LocalOnlyCommits)
            {
                writer.WriteLine($"- `{commit.Sha}` {commit.Title}");
            }
            writer.WriteLine();
            writer.WriteLine("Origin-ahead commit(s):");
            if (result.OriginAheadCommits.Count == 0)
            {
                writer.WriteLine("- (details unavailable)");
            }
            foreach (var commit in result.OriginAheadCommits)
            {
                writer.WriteLine($"- `{commit.Sha}` {commit.Title}");
            }
        }
        if (result.SubmoduleCheckoutMismatchPaths.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Submodule checkout mismatch paths (G357)");
            foreach (var path in result.SubmoduleCheckoutMismatchPaths)
            {
                writer.WriteLine($"- `{path}` — parent gitlink advanced; run `git submodule update --init {path}`");
            }
        }
        if (result.NextSteps.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Next steps");
            foreach (var step in result.NextSteps)
            {
                writer.WriteLine($"- {step}");
            }
        }
    }

    private static HostSyncGitProbe CapturePorcelainProbe(string repoRoot)
    {
        var branch = RunGit(repoRoot, "rev-parse --abbrev-ref HEAD").Trim();
        if (string.IsNullOrWhiteSpace(branch))
        {
            throw new InvalidOperationException($"Could not resolve current branch in '{repoRoot}'.");
        }

        var behindOutput = RunGit(repoRoot, $"rev-list --count HEAD..origin/{branch}").Trim();
        if (!int.TryParse(behindOutput, out var behindCommits) || behindCommits < 0)
        {
            // No upstream / unparseable — treat as 0 (caller will surface
            // the absence of an upstream via separate flow).
            behindCommits = 0;
        }

        // G400: capture the ahead count and the diverged commit details so an
        // ff-blocked (local-ahead + origin-ahead) divergence is named precisely.
        var aheadOutput = RunGit(repoRoot, $"rev-list --count origin/{branch}..HEAD").Trim();
        if (!int.TryParse(aheadOutput, out var aheadCommits) || aheadCommits < 0)
        {
            aheadCommits = 0;
        }

        var localOnlyCommits = aheadCommits > 0
            ? CaptureCommitInfos(repoRoot, $"origin/{branch}..HEAD")
            : Array.Empty<HostSyncCommitInfo>();
        var originAheadCommits = behindCommits > 0
            ? CaptureCommitInfos(repoRoot, $"HEAD..origin/{branch}")
            : Array.Empty<HostSyncCommitInfo>();

        var statusOutput = RunGit(repoRoot, "status --porcelain").Replace("\r\n", "\n");
        var entries = new List<HostSyncWorkingTreeEntry>();
        foreach (var rawLine in statusOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawLine.Length < 4)
            {
                continue;
            }
            var status = rawLine[..2];
            var path = rawLine[3..].Trim();
            // git status --porcelain renames look like `R  oldpath -> newpath`.
            // Use the destination side for classification.
            var arrowIndex = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrowIndex >= 0)
            {
                path = path[(arrowIndex + 4)..].Trim();
            }
            entries.Add(new HostSyncWorkingTreeEntry { Path = path, Status = status });
        }

        // G357: detect submodule gitlink mismatches (parent gitlink advanced after
        // git pull, submodule checkout is stale but the submodule itself is clean).
        var submoduleGitlinkMismatchPaths = DetectSubmoduleGitlinkMismatches(repoRoot, entries);

        return new HostSyncGitProbe
        {
            Branch = branch,
            BehindOriginCommits = behindCommits,
            AheadOriginCommits = aheadCommits,
            LocalOnlyCommits = localOnlyCommits,
            OriginAheadCommits = originAheadCommits,
            WorkingTreeEntries = entries,
            SubmoduleGitlinkMismatchPaths = submoduleGitlinkMismatchPaths
        };
    }

    /// <summary>
    /// G400: capture short SHA + subject for each commit in a revision range
    /// (e.g. <c>origin/main..HEAD</c>) so ff-blocked diagnostics name the exact
    /// local-only and origin-ahead commits. Capped to keep output bounded.
    /// </summary>
    private static IReadOnlyList<HostSyncCommitInfo> CaptureCommitInfos(string repoRoot, string range)
    {
        var output = RunGit(repoRoot, $"log --max-count=20 --format=%h%x09%s {range}").Replace("\r\n", "\n");
        var commits = new List<HostSyncCommitInfo>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var tabIndex = line.IndexOf('\t', StringComparison.Ordinal);
            if (tabIndex <= 0)
            {
                continue;
            }
            var sha = line[..tabIndex].Trim();
            var title = line[(tabIndex + 1)..].Trim();
            if (!string.IsNullOrEmpty(sha))
            {
                commits.Add(new HostSyncCommitInfo { Sha = sha, Title = title });
            }
        }
        return commits;
    }

    /// <summary>
    /// G357: determine which dirty working-tree paths are submodule gitlink
    /// mismatches where the parent gitlink has advanced but the submodule working
    /// tree is still at an older commit, AND the submodule itself has no local
    /// uncommitted changes (so <c>git submodule update --init &lt;path&gt;</c> is safe).
    /// </summary>
    private static IReadOnlyList<string> DetectSubmoduleGitlinkMismatches(
        string repoRoot,
        IReadOnlyList<HostSyncWorkingTreeEntry> entries)
    {
        if (entries.Count == 0)
        {
            return Array.Empty<string>();
        }

        // Collect known submodule paths from `git submodule status`.
        // Format per line: [prefix(1)] sha1(40) space path [space (desc)]
        // Prefix: ' '=ok  '+'=HEAD≠parent-gitlink  '-'=unregistered  'U'=conflict
        var submoduleStatusOutput = RunGit(repoRoot, "submodule status").Replace("\r\n", "\n");
        var knownSubmodulePaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in submoduleStatusOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // Need at least: prefix(1) + sha1(40) + space(1) + path(1) = 43 chars
            if (line.Length < 43)
            {
                continue;
            }
            var rest = line[42..].TrimStart();
            // rest = "path (optional desc)" — take everything up to the first space
            var spaceIdx = rest.IndexOf(' ', StringComparison.Ordinal);
            var path = spaceIdx >= 0 ? rest[..spaceIdx] : rest;
            if (!string.IsNullOrWhiteSpace(path))
            {
                knownSubmodulePaths.Add(path);
            }
        }

        if (knownSubmodulePaths.Count == 0)
        {
            return Array.Empty<string>();
        }

        var mismatchPaths = new List<string>();
        foreach (var entry in entries)
        {
            if (!knownSubmodulePaths.Contains(entry.Path))
            {
                continue;
            }

            // Verify the submodule directory is an initialized git repo (.git file
            // or .git dir) and that the submodule itself has a clean working tree.
            // If the submodule has no local uncommitted changes, updating is safe.
            var submoduleDir = Path.Combine(repoRoot, entry.Path);
            if (!Directory.Exists(submoduleDir))
            {
                continue;
            }
            var gitMarker = Path.Combine(submoduleDir, ".git");
            if (!Directory.Exists(gitMarker) && !File.Exists(gitMarker))
            {
                continue;
            }
            var subStatus = RunGit(submoduleDir, "status --porcelain").Trim();
            if (string.IsNullOrEmpty(subStatus))
            {
                // Submodule has no local changes — gitlink mismatch is safe to repair.
                mismatchPaths.Add(entry.Path);
            }
        }

        return mismatchPaths;
    }

    private static string RunGit(string workingDirectory, string arguments)
    {
        using var process = new Process();
        process.StartInfo.FileName = "git";
        process.StartInfo.Arguments = arguments;
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        // Note: we tolerate non-zero exit (e.g. when origin/<branch> is
        // missing) and let the caller fall back to behind=0; the analyzer
        // only requires non-negative integers.
        return stdout;
    }

    private static bool TryParseArguments(string[] args, out string format, out string error)
    {
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requested = args[++index].Trim();
                    if (!string.Equals(requested, FormatJson, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatMarkdown, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }
                    format = requested;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }
        return true;
    }
}

internal sealed record HostSyncGitProbe
{
    public required string Branch { get; init; }
    public required int BehindOriginCommits { get; init; }

    /// <summary>G400: local commits not yet on origin (origin/&lt;branch&gt;..HEAD).</summary>
    public int AheadOriginCommits { get; init; }

    /// <summary>G400: local-only commit details when ahead &gt; 0.</summary>
    public IReadOnlyList<HostSyncCommitInfo> LocalOnlyCommits { get; init; } = Array.Empty<HostSyncCommitInfo>();

    /// <summary>G400: origin-ahead commit details when behind &gt; 0.</summary>
    public IReadOnlyList<HostSyncCommitInfo> OriginAheadCommits { get; init; } = Array.Empty<HostSyncCommitInfo>();

    public required IReadOnlyList<HostSyncWorkingTreeEntry> WorkingTreeEntries { get; init; }

    /// <summary>
    /// G357: submodule paths where the parent gitlink has advanced but the
    /// submodule checkout is stale AND the submodule itself is clean (so
    /// <c>git submodule update --init &lt;path&gt;</c> is the safe repair).
    /// </summary>
    public IReadOnlyList<string> SubmoduleGitlinkMismatchPaths { get; init; } = Array.Empty<string>();
}
