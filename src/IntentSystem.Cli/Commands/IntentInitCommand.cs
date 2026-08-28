using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G293: chat-first host-domain initialization command. Bootstraps an
/// <c>.intent-cli/config.toml</c> and a minimal <c>intents/&lt;domain&gt;</c>
/// scaffold in a parent host repository so a fresh AI agent can ask
/// <c>intent-cli</c> to set up a new domain without reading
/// <c>intents/rules/**</c>, copied prompt files, or local skills.
///
/// Idempotent: existing files are preserved (reported as <c>existing</c>),
/// missing files are created when <c>--write</c> is supplied. Without
/// <c>--write</c> the command is a dry-run.
///
/// Refuses to run inside an automation child worktree
/// (<c>.intent-cli/worktrees/**</c>) because host bootstrap belongs in the
/// parent host repo, not in a child checkout.
/// </summary>
internal static class IntentInitCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli intent init --domain <name> [--target-repo <owner/repo>] [--host-repo <owner/repo>] [--base-branch-policy direct-main|main-ai] [--write] [--format markdown|json]";

    internal static readonly IReadOnlyList<string> AppendOnlyGitAttributesLines =
    [
        ".intent-cli/runs.jsonl merge=union",
        ".intent-cli/**/*.jsonl merge=union",
        // G679: this later, more-specific rule must override broad JSONL/union
        // policies. A same-scope claim conflict is rejected, never merged.
        ".intent-cli/claims/** -merge",
    ];

    internal static readonly IReadOnlyList<string> SupervisionTelemetryGitIgnoreLines =
    [
        // Retained as an empty compatibility surface for callers that rendered
        // the former root-level G661 rules. Supervision ownership is now
        // directory-local.
    ];

    internal const string SupervisionLocalIgnoreRelativePath = ".intent-cli/supervision/.gitignore";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(UsageLine);
            return 0;
        }

        if (!TryParseArguments(args, out var request, out var parseError))
        {
            writer.WriteLine(parseError);
            writer.WriteLine(UsageLine);
            return 1;
        }

        try
        {
            var result = ExecuteCore(context.RepoRoot, request);
            WriteOutput(writer, request.Format, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static IntentInitResult ExecuteCore(string hostRepoRoot, IntentInitRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostRepoRoot);
        ArgumentNullException.ThrowIfNull(request);

        EnsureNotChildWorktree(hostRepoRoot);

        var configPath = ResolveHostPath(
            hostRepoRoot,
            $"{CliRuntimeContracts.IntentCliDirectoryName}/{CliRuntimeContracts.ConfigFileName}");
        var freshHost = !File.Exists(configPath);

        // G301: extend the new-host bootstrap to also write the canonical
        // host policy files (`AGENTS.md`, `CLAUDE.md`) and the host-binding
        // record so wrong-host operation can be detected later. The
        // existing four files (G293) are preserved verbatim.
        var planned = new List<string>
        {
            $"{CliRuntimeContracts.IntentCliDirectoryName}/{CliRuntimeContracts.ConfigFileName}",
            $"{CliRuntimeContracts.IntentCliDirectoryName}/host-binding.toml",
            // G441: scaffold the durable-state skeletons so a freshly
            // initialized host reports `ok` from `intent host-check`
            // instead of `partially-initialized` solely because the first
            // host-loop wake has not yet created them. Both are valid empty
            // skeletons (queue-state with zero items; an empty append-only
            // runs log) and are preserved idempotently if already present.
            $"{CliRuntimeContracts.IntentCliDirectoryName}/{CliRuntimeContracts.QueueStateFileName}",
            $"{CliRuntimeContracts.IntentCliDirectoryName}/{CliRuntimeContracts.RunLogFileName}",
            "AGENTS.md",
            "CLAUDE.md",
            $"intents/{request.Domain}/README.md",
            $"intents/{request.Domain}/clarifications/open.md",
            $"intents/{request.Domain}/intent-tree/00-map.md"
        };
        if (freshHost)
        {
            planned.Add(".gitattributes");
            planned.Add(SupervisionLocalIgnoreRelativePath);
        }

        var written = new List<string>();
        var existing = new List<string>();

        foreach (var relativePath in planned)
        {
            var absolutePath = ResolveHostPath(hostRepoRoot, relativePath);
            if (freshHost && relativePath == ".gitattributes")
            {
                var requiredLines = AppendOnlyGitAttributesLines;
                if (!request.Write)
                {
                    continue;
                }

                if (AppendMissingLines(absolutePath, requiredLines))
                {
                    written.Add(relativePath);
                }
                else
                {
                    existing.Add(relativePath);
                }
                continue;
            }
            if (freshHost && relativePath == SupervisionLocalIgnoreRelativePath)
            {
                if (!request.Write)
                {
                    continue;
                }

                var ignoreResult = NotifySupervisionStore.EnsureCycleHistoryIgnore(
                    Path.Combine(hostRepoRoot, CliRuntimeContracts.DefaultSupervisionArtifactRoot),
                    write: true);
                if (ignoreResult.Error is not null)
                {
                    throw new InvalidOperationException(
                        $"Could not create the supervision cycle-history ignore at '{ignoreResult.Path}': {ignoreResult.Error}");
                }

                if (ignoreResult.Applied)
                {
                    written.Add(relativePath);
                }
                else
                {
                    existing.Add(relativePath);
                }
                continue;
            }

            if (File.Exists(absolutePath))
            {
                existing.Add(relativePath);
                continue;
            }

            if (!request.Write)
            {
                continue;
            }

            var directoryPath = Path.GetDirectoryName(absolutePath)
                ?? throw new InvalidOperationException(
                    $"Intent init target '{absolutePath}' did not contain a directory.");
            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(absolutePath, RenderContent(relativePath, request));
            written.Add(relativePath);
        }

        return new IntentInitResult
        {
            Domain = request.Domain,
            TargetRepo = request.TargetRepo,
            HostRepoRoot = Path.GetFullPath(hostRepoRoot),
            WriteApplied = request.Write,
            PlannedPaths = planned,
            WrittenPaths = written,
            ExistingPaths = existing,
            NextSteps = BuildNextSteps(request, written.Count, existing.Count, freshHost),
            FreshHost = freshHost,
            GitAttributesLines = AppendOnlyGitAttributesLines,
            GitIgnoreLines = SupervisionTelemetryGitIgnoreLines,
            SupervisionIgnorePath = ResolveHostPath(hostRepoRoot, SupervisionLocalIgnoreRelativePath),
            SupervisionIgnoreLines = NotifySupervisionStore.CycleHistoryIgnoreLines,
            ExistingHostGuidance =
                "Existing hosts are never auto-migrated by intent init. Run "
                + "`intent-cli notify supervise repair-cycle-history --domain <domain> --team <team> --write --format json` "
                + "as the canonical migration; it preserves cycle files and makes the shared policy state trackable.",
        };
    }

    private static bool AppendMissingLines(string path, IReadOnlyList<string> requiredLines)
    {
        var existingText = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        var existingLines = existingText.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToHashSet(StringComparer.Ordinal);
        var missing = requiredLines.Where(line => !existingLines.Contains(line)).ToArray();
        if (missing.Length == 0)
        {
            return false;
        }

        var prefix = existingText.Length == 0 || existingText.EndsWith('\n') ? string.Empty : Environment.NewLine;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.AppendAllText(path, prefix + string.Join(Environment.NewLine, missing) + Environment.NewLine);
        return true;
    }

    private static void EnsureNotChildWorktree(string hostRepoRoot)
    {
        var normalized = Path.GetFullPath(hostRepoRoot)
            .Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var index = 0; index + 1 < segments.Length; index++)
        {
            if (string.Equals(segments[index], ".intent-cli", StringComparison.Ordinal)
                && string.Equals(segments[index + 1], "worktrees", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Refusing to run 'intent init' inside an automation child worktree at '{hostRepoRoot}'. Run this command from the parent host repository (the directory that owns '.intent-cli/').");
            }
        }
    }

    private static IReadOnlyList<string> BuildNextSteps(
        IntentInitRequest request,
        int writtenCount,
        int existingCount,
        bool freshHost)
    {
        var domain = request.Domain;
        var verb = request.Write
            ? (writtenCount == 0 ? "Already initialized" : "Initialized")
            : "Plan";

        var steps = new List<string>
        {
            $"{verb}: domain '{domain}' (written: {writtenCount}, existing: {existingCount})."
        };

        if (!request.Write)
        {
            steps.Add($"Re-run with --write to create the planned files: intent-cli intent init --domain {domain}{TargetArg(request)} --write");
        }

        steps.Add($"Open `intents/{domain}/intent-tree/00-map.md` and capture the initial domain shape.");
        steps.Add($"Use `intent-cli interview record-answer --write` (chat-first) to durably record durable Q/A for '{domain}'.");
        steps.Add($"Use `intent-cli intent next-slice --domain {domain} --dry-run` to plan the first publishable slice.");
        steps.Add(freshHost
            ? "Fresh-host git defaults include merge=union for append-only .intent-cli JSONL stores, then .intent-cli/claims/** -merge so claims never union-merge. The directory-local .intent-cli/supervision/.gitignore ignores cycle history only; shared stalls and policy manifests remain trackable."
            : "Existing host detected: no supervision migration was performed by intent init. Run `intent-cli notify supervise repair-cycle-history --domain <domain> --team <team> --write --format json` for the canonical cycle-history migration; intent-cli preserves the files and shared policy state.");
        steps.Add("Run this command from the parent host repository, never inside `.intent-cli/worktrees/**`.");

        return steps;
    }

    private static string TargetArg(IntentInitRequest request)
    {
        return string.IsNullOrWhiteSpace(request.TargetRepo)
            ? string.Empty
            : $" --target-repo {request.TargetRepo}";
    }

    private static string ResolveHostPath(string hostRepoRoot, string relativePath)
    {
        return Path.Combine(hostRepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string RenderContent(string relativePath, IntentInitRequest request)
    {
        return relativePath switch
        {
            var path when path.EndsWith($"/{CliRuntimeContracts.ConfigFileName}", StringComparison.Ordinal)
                => RenderConfigToml(request),
            var path when path.EndsWith("/host-binding.toml", StringComparison.Ordinal)
                => RenderHostBindingToml(request),
            var path when path.EndsWith($"/{CliRuntimeContracts.QueueStateFileName}", StringComparison.Ordinal)
                => RenderEmptyQueueState(),
            var path when path.EndsWith($"/{CliRuntimeContracts.RunLogFileName}", StringComparison.Ordinal)
                => string.Empty,
            "AGENTS.md" => RenderAgentsMarkdown(request),
            "CLAUDE.md" => RenderClaudeMarkdown(request),
            var path when path.EndsWith("/README.md", StringComparison.Ordinal)
                => RenderDomainReadme(request),
            var path when path.EndsWith("/clarifications/open.md", StringComparison.Ordinal)
                => RenderOpenClarifications(),
            var path when path.EndsWith("/intent-tree/00-map.md", StringComparison.Ordinal)
                => RenderIntentMap(request),
            _ => throw new InvalidOperationException(
                $"Intent init has no template for '{relativePath}'.")
        };
    }

    /// <summary>
    /// G441: canonical empty queue-state skeleton. Schema-version 1 with
    /// zero items and a sentinel <c>UpdatedAt</c> of the Unix epoch so the
    /// scaffold is byte-for-byte deterministic (no wall-clock in init) and
    /// clearly reads as "never updated". The first host-loop publish
    /// overwrites it with real items. Serialized through the same
    /// <see cref="QueueStateSerializer"/> the loader uses so the file is
    /// guaranteed to round-trip.
    /// </summary>
    private static string RenderEmptyQueueState()
    {
        var skeleton = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.UnixEpoch,
            Items = Array.Empty<QueueItem>()
        };
        return QueueStateSerializer.Serialize(skeleton);
    }

    /// <summary>
    /// G301: host-binding record so other commands can detect wrong-host
    /// usage. Captures the canonical owner/repo of the host (the repo that
    /// owns this `.intent-cli/` package) plus the target child repo and
    /// the domain. <see cref="WrongHostGuard"/> reads this file and
    /// compares against the observed git remote.
    /// </summary>
    private static string RenderHostBindingToml(IntentInitRequest request)
    {
        var hostRepoLine = string.IsNullOrWhiteSpace(request.HostRepo)
            ? "host_repo = \"\""
            : $"host_repo = \"{EscapeTomlString(request.HostRepo!)}\"";
        var targetRepoLine = string.IsNullOrWhiteSpace(request.TargetRepo)
            ? "target_repo = \"\""
            : $"target_repo = \"{EscapeTomlString(request.TargetRepo!)}\"";

        return $$"""
        # G301 host binding record. Other intent-cli commands consult this to
        # detect wrong-host operation (e.g. publishing a domain from a host
        # repo whose remote does not match `host_repo`). Empty values are
        # treated as "not yet bound" and skip the wrong-host check.
        [host]
        domain = "{{EscapeTomlString(request.Domain)}}"
        {{hostRepoLine}}
        {{targetRepoLine}}
        """;
    }

    /// <summary>
    /// G301: canonical host-side AGENTS.md so a fresh AI agent reading the
    /// repo immediately sees the host working policy without needing to
    /// rely on prompt memory. Keep this short and durable; it is not a
    /// substitute for `intent-cli guide ...`.
    /// </summary>
    private static string RenderAgentsMarkdown(IntentInitRequest request)
    {
        var targetLine = string.IsNullOrWhiteSpace(request.TargetRepo)
            ? string.Empty
            : $"- Target repo (child implementation): `{request.TargetRepo}`" + Environment.NewLine;
        var hostLine = string.IsNullOrWhiteSpace(request.HostRepo)
            ? string.Empty
            : $"- Host repo (this repo): `{request.HostRepo}`" + Environment.NewLine;

        return $$"""
        # AGENTS.md — host repo working policy

        This is an **intent host repository**. It owns durable parent state for the
        `{{request.Domain}}` domain under `.intent-cli/` and `intents/{{request.Domain}}/`.
        Child implementation repos do NOT own this state.

        {{hostLine}}{{targetLine}}- Domain: `{{request.Domain}}`
        - Bootstrapped by `intent-cli intent init` (G293 / G301).

        ## Host repo working policy

        - Work directly on `main`. Do NOT open a PR for routine host-state updates
          (queue-state, runs.jsonl, packets, intents/) unless the operator explicitly
          asks for one.
        - `git pull --ff-only` before edits. Commit and push to `main` after each
          coherent change.
        - All workflow label transitions go through installed `intent-cli automation`
          / `intent-cli worker` commands. Never edit GitHub labels by hand.
        - Routine collaboration uses `intent-cli guide ...`. Do NOT read
          `intents/rules/**`, copied prompt files, or local skill files that restate
          workflow for routine operation.
        - {{DispatcherSkillCarveOut.Sentence}}
        - Do NOT call `intent-cli run` (advanced runtime) or `dotnet run` as a
          fallback. Do NOT ask `intent-cli` to launch Claude/Codex.

        ## Wrong-host detection (G301)

        `.intent-cli/host-binding.toml` records the canonical host repo for this
        domain. If you operate this domain from a different host repo, expect
        `intent-cli` to surface a structured wrong-host warning with remediation
        steps; do not silently proceed with parent-state mutation.
        """;
    }

    /// <summary>
    /// G301: companion Claude-readable host policy. Mirrors AGENTS.md so a
    /// Claude agent reading either file gets the same canonical
    /// instructions regardless of which the host conventions name.
    /// </summary>
    private static string RenderClaudeMarkdown(IntentInitRequest request)
    {
        var targetLine = string.IsNullOrWhiteSpace(request.TargetRepo)
            ? string.Empty
            : $"- Target repo: `{request.TargetRepo}`" + Environment.NewLine;
        var hostLine = string.IsNullOrWhiteSpace(request.HostRepo)
            ? string.Empty
            : $"- Host repo (this repo): `{request.HostRepo}`" + Environment.NewLine;

        return $$"""
        # CLAUDE.md — host repo guide for Claude / chat-first agents

        This is an **intent host repository** for the `{{request.Domain}}` domain.
        See `AGENTS.md` for the canonical host working policy. This file mirrors
        that policy for Claude-specific tool conventions.

        {{hostLine}}{{targetLine}}
        ## Reading order

        1. The current GitHub Issue body (when running an automation loop).
        2. `AGENTS.md` (host policy baseline).
        3. `intent-cli guide ...` (chat-first canonical guidance — never read
           `intents/rules/**` or workflow-restating local skills for routine
           operation; {{DispatcherSkillCarveOut.SkillName}} installed by
           `{{DispatcherSkillCarveOut.InstallCommand}}` is exempt).

        ## Hard rules (host repo)

        - Work directly on `main` for host-state updates; pull before edit, commit
          and push after each coherent change. Open a PR only if the operator
          explicitly asks.
        - Workflow label transitions go through `intent-cli automation` /
          `intent-cli worker`. Never raw `gh ... edit --add-label`.
        - Do NOT call `intent-cli run`, `dotnet run`, or ask intent-cli to launch
          an AI provider.
        - On any `Could not find .intent-cli` failure, follow the structured
          fail-closed guidance (G299) — do not fall back to ordinary GitHub
          review or raw PR comments.
        """;
    }

    private static string RenderConfigToml(IntentInitRequest request)
    {
        // G346: persist base_branch_policy in the generated config so
        // `guide prompt-matrix` and `automation summary` can resolve the
        // effective policy without operator-supplied flags. Default to
        // direct-main so the file is self-explaining even when the flag
        // was not explicitly passed.
        var policy = string.IsNullOrWhiteSpace(request.BaseBranchPolicy)
            ? CliRuntimeContracts.DefaultBaseBranchPolicy
            : request.BaseBranchPolicy;

        return $$"""
        [project]
        domain = "{{EscapeTomlString(request.Domain)}}"
        artifact_root = ".intent-cli"
        worktree_root = ".intent-cli/worktrees"
        base_branch_policy = "{{policy}}"
        """;
    }

    private static string RenderDomainReadme(IntentInitRequest request)
    {
        var targetLine = string.IsNullOrWhiteSpace(request.TargetRepo)
            ? string.Empty
            : $"Target repo: `{request.TargetRepo}`" + Environment.NewLine + Environment.NewLine;

        return $$"""
        # {{request.Domain}}

        {{targetLine}}Bootstrapped by `intent-cli intent init`.

        Treat this as the parent host record for the `{{request.Domain}}` domain. Add
        upstream references and the canonical domain shape under
        `intent-tree/`, durable Q/A under `interviews/`, and open
        clarifications under `clarifications/open.md`.
        """;
    }

    private static string RenderOpenClarifications()
    {
        return """
        # Open Clarifications

        - none
        """;
    }

    private static string RenderIntentMap(IntentInitRequest request)
    {
        var targetLine = string.IsNullOrWhiteSpace(request.TargetRepo)
            ? string.Empty
            : $"- Target repo: `{request.TargetRepo}`" + Environment.NewLine;

        return $$"""
        # Intent Map

        - Domain: `{{request.Domain}}`
        {{targetLine}}- Initial map: pending

        Capture the canonical domain shape here. Each child intent should
        link back to this map and the host data under `.intent-cli/`.
        """;
    }

    private static string EscapeTomlString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static bool TryParseArguments(
        string[] args,
        out IntentInitRequest request,
        out string error)
    {
        request = default!;
        error = string.Empty;

        string? domain = null;
        string? targetRepo = null;
        string? hostRepo = null;
        string? baseBranchPolicy = null;
        var write = false;
        var format = FormatMarkdown;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "Missing value for '--domain'.";
                        return false;
                    }
                    domain = args[++index].Trim();
                    break;
                case "--target-repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "Missing value for '--target-repo'.";
                        return false;
                    }
                    targetRepo = args[++index].Trim();
                    break;
                case "--host-repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "Missing value for '--host-repo'.";
                        return false;
                    }
                    hostRepo = args[++index].Trim();
                    break;
                case "--base-branch-policy":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "Missing value for '--base-branch-policy'.";
                        return false;
                    }
                    var policyValue = args[++index].Trim();
                    if (!BaseBranchPolicyContract.IsKnownPolicy(policyValue))
                    {
                        error = $"--base-branch-policy must be '{CliRuntimeContracts.DirectMainBaseBranchPolicy}' or '{CliRuntimeContracts.MainAiBaseBranchPolicy}' (got '{policyValue}').";
                        return false;
                    }
                    baseBranchPolicy = policyValue;
                    break;
                case "--write":
                    write = true;
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "Missing value for '--format'.";
                        return false;
                    }
                    var nextValue = args[++index].Trim();
                    if (!string.Equals(nextValue, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(nextValue, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"Unsupported format '{nextValue}'. Expected 'markdown' or 'json'.";
                        return false;
                    }
                    format = nextValue;
                    break;
                default:
                    error = $"Unknown intent init option '{args[index]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            error = "Intent init requires '--domain <name>'.";
            return false;
        }

        if (!IsValidDomainSlug(domain))
        {
            error = $"Intent init '--domain' value '{domain}' must be a slug (letters, digits, '-', '_').";
            return false;
        }

        request = new IntentInitRequest
        {
            Domain = domain!,
            TargetRepo = targetRepo,
            HostRepo = hostRepo,
            BaseBranchPolicy = baseBranchPolicy,
            Write = write,
            Format = format
        };
        return true;
    }

    private static bool IsValidDomainSlug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var character in value)
        {
            var isValid = char.IsLetterOrDigit(character)
                || character == '-'
                || character == '_';
            if (!isValid)
            {
                return false;
            }
        }

        return true;
    }

    private static void WriteOutput(TextWriter writer, string format, IntentInitResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
            return;
        }

        IntentInitRenderer.WriteMarkdown(writer, result);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    internal sealed record IntentInitRequest
    {
        public required string Domain { get; init; }

        public string? TargetRepo { get; init; }

        public string? HostRepo { get; init; }

        /// <summary>
        /// G346: optional explicit base branch policy. When supplied, written
        /// to the generated <c>config.toml</c>; when omitted, <c>direct-main</c>
        /// is recorded as the documented default so the file is self-explaining.
        /// </summary>
        public string? BaseBranchPolicy { get; init; }

        public required bool Write { get; init; }

        public required string Format { get; init; }
    }
}
