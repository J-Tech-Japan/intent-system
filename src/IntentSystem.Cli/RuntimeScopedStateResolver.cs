namespace IntentSystem.Cli;

/// <summary>
/// G327: pure runtime-scoped state resolver. Defines the on-disk layout
/// for review runtime state (<c>.intent-cli/runtime/&lt;domain&gt;/&lt;owner&gt;__&lt;repo&gt;/...</c>)
/// and chooses between the scoped path and the legacy root path so
/// callers can preserve packet backlog operations on
/// <c>.intent-cli/issues/&lt;execution-unit&gt;</c> while migrating
/// closeout / runs / leases state to the per-(domain, repo) scope.
///
/// Pure — no <c>File.Exists</c> happens inside the constants; the
/// resolver methods read the filesystem to decide which path to
/// return but never mutate it.
/// </summary>
internal static class RuntimeScopedStateResolver
{
    public const string RuntimeDirectoryName = "runtime";
    public const string LeasesFileName = "leases.json";
    public const string CloseoutsDirectoryName = "closeouts";

    /// <summary>
    /// G327: directory holding scoped runtime state for a single
    /// <c>(domain, owner/repo)</c> pair.
    /// </summary>
    public static string GetScopedRuntimeDirectory(string repoRoot, string domain, string ownerRepo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerRepo);

        return Path.Combine(
            CliRuntimeContracts.GetIntentCliDirectoryPath(repoRoot),
            RuntimeDirectoryName,
            domain.Trim(),
            NormalizeOwnerRepo(ownerRepo));
    }

    /// <summary>
    /// G327: scoped queue-state file for <c>(domain, owner/repo)</c>.
    /// Always returns the scoped path regardless of whether the file
    /// exists; callers that need legacy fallback should use
    /// <see cref="ResolveQueueStatePathForRead"/> instead.
    /// </summary>
    public static string GetScopedQueueStatePath(string repoRoot, string domain, string ownerRepo)
    {
        return Path.Combine(
            GetScopedRuntimeDirectory(repoRoot, domain, ownerRepo),
            CliRuntimeContracts.QueueStateFileName);
    }

    /// <summary>
    /// G327: scoped runs.jsonl file for <c>(domain, owner/repo)</c>.
    /// </summary>
    public static string GetScopedRunLogPath(string repoRoot, string domain, string ownerRepo)
    {
        return Path.Combine(
            GetScopedRuntimeDirectory(repoRoot, domain, ownerRepo),
            CliRuntimeContracts.RunLogFileName);
    }

    /// <summary>
    /// G327: scoped leases.json file for <c>(domain, owner/repo)</c>.
    /// </summary>
    public static string GetScopedLeasesPath(string repoRoot, string domain, string ownerRepo)
    {
        return Path.Combine(
            GetScopedRuntimeDirectory(repoRoot, domain, ownerRepo),
            LeasesFileName);
    }

    /// <summary>
    /// G327: scoped closeouts/ directory for <c>(domain, owner/repo)</c>.
    /// </summary>
    public static string GetScopedCloseoutsDirectory(string repoRoot, string domain, string ownerRepo)
    {
        return Path.Combine(
            GetScopedRuntimeDirectory(repoRoot, domain, ownerRepo),
            CloseoutsDirectoryName);
    }

    /// <summary>
    /// G327: legacy root-level queue-state path — the single
    /// <c>.intent-cli/queue-state.json</c> that pre-dates the
    /// per-(domain, repo) scoping. Kept so resolver callers can fall
    /// back when scoped state isn't on disk yet.
    /// </summary>
    public static string GetLegacyQueueStatePath(string repoRoot)
    {
        return CliRuntimeContracts.GetQueueStatePath(repoRoot);
    }

    /// <summary>
    /// G327: legacy root-level runs.jsonl path.
    /// </summary>
    public static string GetLegacyRunLogPath(string repoRoot)
    {
        return CliRuntimeContracts.GetRunLogPath(repoRoot);
    }

    /// <summary>
    /// G327: resolve the queue-state path a review runtime command
    /// should READ. Prefers the scoped path when it exists on disk;
    /// falls back to the legacy root path otherwise. Returning the
    /// scoped path even when missing makes write-side callers
    /// idempotent — they can pass the same result to
    /// <c>File.WriteAllText</c> to create the scoped file on first
    /// closeout.
    /// </summary>
    public static StateLocation ResolveQueueStatePathForRead(
        string repoRoot,
        string domain,
        string ownerRepo)
    {
        var scoped = GetScopedQueueStatePath(repoRoot, domain, ownerRepo);
        if (File.Exists(scoped))
        {
            return new StateLocation(scoped, StateLocationKind.Scoped);
        }

        var legacy = GetLegacyQueueStatePath(repoRoot);
        if (File.Exists(legacy))
        {
            return new StateLocation(legacy, StateLocationKind.Legacy);
        }

        return new StateLocation(scoped, StateLocationKind.MissingPreferScoped);
    }

    /// <summary>
    /// G327: same as <see cref="ResolveQueueStatePathForRead"/> for the
    /// runs.jsonl audit trail.
    /// </summary>
    public static StateLocation ResolveRunLogPathForRead(
        string repoRoot,
        string domain,
        string ownerRepo)
    {
        var scoped = GetScopedRunLogPath(repoRoot, domain, ownerRepo);
        if (File.Exists(scoped))
        {
            return new StateLocation(scoped, StateLocationKind.Scoped);
        }

        var legacy = GetLegacyRunLogPath(repoRoot);
        if (File.Exists(legacy))
        {
            return new StateLocation(legacy, StateLocationKind.Legacy);
        }

        return new StateLocation(scoped, StateLocationKind.MissingPreferScoped);
    }

    /// <summary>
    /// G327: normalize an <c>owner/repo</c> identifier into a
    /// filesystem-safe directory token. Collapses any sequence of path
    /// separators (<c>/</c> or <c>\\</c>) into a single canonical
    /// <c>__</c> token so the on-disk layout is
    /// <c>.intent-cli/runtime/&lt;domain&gt;/&lt;owner&gt;__&lt;repo&gt;/...</c>.
    /// Accepts bare <c>owner/repo</c> as well as full
    /// <c>https://github.com/owner/repo</c> URLs (case-insensitive
    /// prefix strip).
    /// </summary>
    public static string NormalizeOwnerRepo(string ownerRepo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerRepo);

        var trimmed = ownerRepo.Trim();
        const string githubPrefix = "https://github.com/";
        if (trimmed.StartsWith(githubPrefix, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[githubPrefix.Length..];
        }
        trimmed = trimmed.Trim('/');

        // Collapse runs of `/` or `\` into a single `__` separator so
        // pasted URLs and `owner//repo` typos resolve to the same
        // directory.
        var builder = new System.Text.StringBuilder(trimmed.Length);
        var lastWasSeparator = false;
        foreach (var ch in trimmed)
        {
            if (ch == '/' || ch == '\\')
            {
                if (!lastWasSeparator)
                {
                    builder.Append("__");
                    lastWasSeparator = true;
                }
            }
            else
            {
                builder.Append(ch);
                lastWasSeparator = false;
            }
        }
        return builder.ToString();
    }
}

/// <summary>
/// G327: resolved state location + which layout produced it. Callers
/// who want to surface "we read from the legacy root" diagnostics can
/// inspect <see cref="Kind"/>.
/// </summary>
internal readonly record struct StateLocation(string Path, StateLocationKind Kind);

/// <summary>
/// G327: enumeration of the three states the resolver can return.
/// </summary>
internal enum StateLocationKind
{
    /// <summary>The scoped path exists and is preferred.</summary>
    Scoped,

    /// <summary>The scoped path is missing; the legacy root path exists.</summary>
    Legacy,

    /// <summary>Neither path exists; the scoped path is returned so writes land in the new layout.</summary>
    MissingPreferScoped
}
