using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G362: <c>intent-cli automation same-repo-metadata-preflight</c> —
/// read-only check that the host loop's CONFIGURED metadata source
/// branch is up to date before the wake reads queue-state, runs,
/// packets, or next-slice candidates.
///
/// Same-repository topology problem: when host metadata and
/// implementation code share one repo, the operator may have current
/// durable state on <c>main</c> but a long-lived <c>main-metadata</c>
/// branch that is stale. A loop that blindly reads from the wrong
/// branch will think older packets are still active. This command
/// resolves the configured <c>MetadataSourceBranch</c> (G362 field,
/// falling back to <c>MetadataBranch</c>), runs a fetch against
/// <c>origin</c>, and reports one of:
/// <list type="bullet">
/// <item><c>clean</c> — local branch equals <c>origin/&lt;branch&gt;</c>; safe to read.</item>
/// <item><c>behind-origin</c> — local is behind origin; recommend
///   <c>git pull --ff-only</c> on the metadata branch and retry.</item>
/// <item><c>diverged</c> — local and origin have diverged; structured
///   stop with operator recovery instructions.</item>
/// <item><c>not-configured</c> — no metadata source branch is set;
///   fall through to the legacy G357 pull-first main behavior.</item>
/// <item><c>missing-branch</c> — origin has no such branch.</item>
/// <item><c>missing-local-branch</c> — origin has the branch but
///   the local ref hasn't been created yet (common bootstrap case
///   on a fresh checkout). Recommend creating / tracking the local
///   branch from <c>origin/&lt;branch&gt;</c> rather than treating
///   it as a true fork. PR #829 review repair.</item>
/// </list>
///
/// READ-ONLY: never pushes, never mutates the working tree, never
/// touches GitHub via API.
/// </summary>
internal static class AutomationSameRepoMetadataPreflightCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    public const string ClassificationClean = "clean";
    public const string ClassificationBehindOrigin = "behind-origin";
    public const string ClassificationDiverged = "diverged";
    public const string ClassificationNotConfigured = "not-configured";
    public const string ClassificationMissingBranch = "missing-branch";
    /// <summary>
    /// PR #829 review repair: origin has the configured metadata
    /// branch, but the local ref does not exist yet (e.g. a fresh
    /// clone where <c>origin/main-metadata</c> is fetched but no
    /// local <c>main-metadata</c> branch has been created). This
    /// MUST NOT be classified as <c>diverged</c> — the operator
    /// only needs to create / track the local branch.
    /// </summary>
    public const string ClassificationMissingLocalBranch = "missing-local-branch";

    /// <summary>
    /// Test seam: replaces the default git capture with an in-memory
    /// probe so unit tests do not have to set up a real git repo.
    /// </summary>
    public static Func<CliContext, string, SameRepoMetadataPreflightProbe>? ProbeFactory { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var format, out var branchOverride, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        // G362: prefer the explicit MetadataSourceBranch, fall back
        // to the legacy MetadataBranch (G350), then to the CLI
        // override flag. Empty means "no same-repo gate configured".
        var branch = !string.IsNullOrWhiteSpace(branchOverride)
            ? branchOverride!.Trim()
            : ResolveMetadataSourceBranch(context);

        if (string.IsNullOrWhiteSpace(branch))
        {
            var result = new SameRepoMetadataPreflightResult
            {
                Classification = ClassificationNotConfigured,
                Branch = string.Empty,
                LocalSha = string.Empty,
                OriginSha = string.Empty,
                Summary = "no metadata source branch configured; falling back to legacy pull-first main behavior (G357). "
                    + "To enable same-repo gates set `[project] metadata_source_branch` (and `metadata_write_branch`) in `.intent-cli/config.toml`.",
                RecommendedActions = Array.Empty<string>(),
            };
            EmitResult(writer, format, result);
            return 0;
        }

        SameRepoMetadataPreflightProbe probe;
        try
        {
            probe = ProbeFactory?.Invoke(context, branch)
                ?? CaptureProbe(context.RepoRoot, branch);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            writer.WriteLine($"failed to capture same-repo metadata preflight for `{branch}`: {exception.Message}");
            return 1;
        }

        var verdict = Classify(probe, branch);
        EmitResult(writer, format, verdict);
        return verdict.Classification == ClassificationClean ? 0 : 1;
    }

    /// <summary>
    /// Pure classifier: data in, verdict out. Tests inject the probe
    /// directly so the classification matrix can be exercised without
    /// touching git.
    /// </summary>
    internal static SameRepoMetadataPreflightResult Classify(
        SameRepoMetadataPreflightProbe probe,
        string branch)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        if (probe.OriginBranchExists is false)
        {
            return new SameRepoMetadataPreflightResult
            {
                Classification = ClassificationMissingBranch,
                Branch = branch,
                LocalSha = probe.LocalSha ?? string.Empty,
                OriginSha = string.Empty,
                Summary = $"origin has no `{branch}` branch; same-repo metadata preflight cannot verify freshness. "
                    + "Push the branch to origin or correct `[project] metadata_source_branch`.",
                RecommendedActions = new[]
                {
                    $"git push origin {branch}",
                    "Update `[project] metadata_source_branch` in `.intent-cli/config.toml` if the branch name is wrong.",
                },
            };
        }

        // PR #829 review repair: when origin has the branch but the
        // local ref hasn't been created yet (fresh checkout — common
        // bootstrap case), `git rev-parse {branch}` fails and the
        // probe arrives with an empty LocalSha. Classifying that as
        // `diverged` was wrong: there is no local history to fork
        // FROM. Treat as a dedicated bootstrap state so the
        // recommendation is "create / track the local branch", not
        // "manually reconcile a true fork".
        if (string.IsNullOrWhiteSpace(probe.LocalSha))
        {
            return new SameRepoMetadataPreflightResult
            {
                Classification = ClassificationMissingLocalBranch,
                Branch = branch,
                LocalSha = string.Empty,
                OriginSha = probe.OriginSha ?? string.Empty,
                Summary = $"origin has `{branch}` but the local ref does not exist yet (fresh checkout / bootstrap case). "
                    + $"Metadata reads cannot proceed until a local `{branch}` is created and tracking origin.",
                RecommendedActions = new[]
                {
                    $"git fetch origin {branch} && git checkout -t origin/{branch}",
                    $"Or, if already on another branch: git branch --track {branch} origin/{branch}",
                    "Re-run `automation same-repo-metadata-preflight` to confirm `clean`.",
                },
            };
        }

        if (string.Equals(probe.LocalSha, probe.OriginSha, StringComparison.Ordinal))
        {
            return new SameRepoMetadataPreflightResult
            {
                Classification = ClassificationClean,
                Branch = branch,
                LocalSha = probe.LocalSha ?? string.Empty,
                OriginSha = probe.OriginSha ?? string.Empty,
                Summary = $"local `{branch}` matches `origin/{branch}` at `{probe.LocalSha}`; metadata source is fresh.",
                RecommendedActions = Array.Empty<string>(),
            };
        }

        // Behind-only: local is an ancestor of origin (fast-forward
        // possible). Diverged: neither side is an ancestor (true fork).
        if (probe.LocalIsAncestorOfOrigin)
        {
            return new SameRepoMetadataPreflightResult
            {
                Classification = ClassificationBehindOrigin,
                Branch = branch,
                LocalSha = probe.LocalSha ?? string.Empty,
                OriginSha = probe.OriginSha ?? string.Empty,
                Summary = $"local `{branch}` is behind `origin/{branch}`; metadata reads would be stale. "
                    + $"Pull before reading queue-state / runs / packets.",
                RecommendedActions = new[]
                {
                    $"git fetch origin {branch} && git checkout {branch} && git pull --ff-only origin {branch}",
                    "Re-run `automation same-repo-metadata-preflight` to confirm `clean`.",
                },
            };
        }

        return new SameRepoMetadataPreflightResult
        {
            Classification = ClassificationDiverged,
            Branch = branch,
            LocalSha = probe.LocalSha ?? string.Empty,
            OriginSha = probe.OriginSha ?? string.Empty,
            Summary = $"local `{branch}` and `origin/{branch}` have diverged (true fork). "
                + "Metadata reads from either side may be stale; structured stop — operator must reconcile manually.",
            RecommendedActions = new[]
            {
                $"Inspect `git log --oneline --left-right {branch}...origin/{branch}` to see the divergent commits.",
                $"Reconcile (typically `git rebase origin/{branch}` after backing up local-only commits), then re-run the preflight.",
                "Do NOT proceed with the host wake until this returns `clean`.",
            },
        };
    }

    private static string ResolveMetadataSourceBranch(CliContext context)
    {
        var explicitSource = context.Config?.Project?.MetadataSourceBranch;
        if (!string.IsNullOrWhiteSpace(explicitSource))
        {
            return explicitSource.Trim();
        }
        var legacy = context.Config?.Project?.MetadataBranch;
        return string.IsNullOrWhiteSpace(legacy) ? string.Empty : legacy.Trim();
    }

    private static SameRepoMetadataPreflightProbe CaptureProbe(string repoRoot, string branch)
    {
        // Best-effort fetch so the comparison sees fresh origin
        // state. PR #829 review repair: when `git fetch` fails, the
        // local remote-tracking ref (`origin/<branch>`) may be
        // stale OR refer to a branch that no longer exists on the
        // remote (deleted / renamed since the last successful
        // fetch). Verify with `git ls-remote --heads` before
        // trusting the cached ref, so we never report `clean`
        // against a branch the remote has removed.
        var (fetchExit, _) = RunGit(repoRoot, $"fetch origin {branch}", allowFailure: true);

        bool? remoteBranchExistsViaLsRemote = null;
        if (fetchExit != 0)
        {
            var (lsExit, lsOutput) = RunGit(repoRoot, $"ls-remote --heads origin {branch}", allowFailure: true);
            if (lsExit == 0)
            {
                // ls-remote --heads emits a line per matching head;
                // empty output means the remote no longer has it.
                remoteBranchExistsViaLsRemote = !string.IsNullOrWhiteSpace(lsOutput);
            }
            // lsExit != 0 → ls-remote also failed (likely offline);
            // leave the flag null and fall back to the cached ref
            // best-effort. The downstream `clean` check still
            // requires SHA equality, so a stale-but-equal local +
            // origin/<branch> remains the safe default.
        }

        var (localExit, localSha) = RunGit(repoRoot, $"rev-parse {branch}", allowFailure: true);
        var (originExit, originSha) = RunGit(repoRoot, $"rev-parse origin/{branch}", allowFailure: true);

        var originExists = originExit == 0 && !string.IsNullOrWhiteSpace(originSha);
        // PR #829 review repair: when ls-remote affirmatively
        // reports the remote branch is gone, override the cached
        // origin/<branch> signal so the classifier returns
        // `missing-branch` instead of a misleading clean / behind.
        if (remoteBranchExistsViaLsRemote == false)
        {
            originExists = false;
        }

        var localIsAncestor = false;
        if (originExists && localExit == 0 && !string.IsNullOrWhiteSpace(localSha))
        {
            // `merge-base --is-ancestor <local> origin/<branch>`
            // returns exit 0 when local is an ancestor of origin.
            var (ancestorExit, _) = RunGit(repoRoot, $"merge-base --is-ancestor {localSha.Trim()} origin/{branch}", allowFailure: true);
            localIsAncestor = ancestorExit == 0;
        }

        return new SameRepoMetadataPreflightProbe
        {
            LocalSha = localSha?.Trim(),
            OriginSha = originSha?.Trim(),
            OriginBranchExists = originExists,
            LocalIsAncestorOfOrigin = localIsAncestor,
        };
    }

    private static (int ExitCode, string StdOut) RunGit(string repoRoot, string arguments, bool allowFailure)
    {
        using var process = new Process();
        process.StartInfo.FileName = "git";
        process.StartInfo.Arguments = arguments;
        process.StartInfo.WorkingDirectory = repoRoot;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.StandardOutputEncoding = ProcessOutputEncoding.Utf8NoBom;
        process.StartInfo.StandardErrorEncoding = ProcessOutputEncoding.Utf8NoBom;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        try
        {
            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, stdout);
        }
        catch (Exception) when (allowFailure)
        {
            return (-1, string.Empty);
        }
    }

    private static void EmitResult(TextWriter writer, string format, SameRepoMetadataPreflightResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
            return;
        }
        writer.WriteLine($"# automation same-repo-metadata-preflight (G362)");
        writer.WriteLine();
        writer.WriteLine($"- classification: **{result.Classification}**");
        writer.WriteLine($"- branch: `{result.Branch}`");
        if (!string.IsNullOrWhiteSpace(result.LocalSha))
        {
            writer.WriteLine($"- local: `{result.LocalSha}`");
        }
        if (!string.IsNullOrWhiteSpace(result.OriginSha))
        {
            writer.WriteLine($"- origin: `{result.OriginSha}`");
        }
        writer.WriteLine();
        writer.WriteLine(result.Summary);
        if (result.RecommendedActions.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Recommended actions");
            foreach (var action in result.RecommendedActions)
            {
                writer.WriteLine($"- {action}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string format,
        out string? branchOverride,
        out string error)
    {
        format = FormatMarkdown;
        branchOverride = null;
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
                case "--branch":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--branch requires a value.";
                        return false;
                    }
                    branchOverride = args[++index].Trim();
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }
        return true;
    }
}

internal sealed record SameRepoMetadataPreflightProbe
{
    public string? LocalSha { get; init; }
    public string? OriginSha { get; init; }
    public bool OriginBranchExists { get; init; }
    public bool LocalIsAncestorOfOrigin { get; init; }
}

internal sealed record SameRepoMetadataPreflightResult
{
    public required string Classification { get; init; }
    public required string Branch { get; init; }
    public required string LocalSha { get; init; }
    public required string OriginSha { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> RecommendedActions { get; init; }
}
