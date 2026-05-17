using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G312: <c>intent-cli automation durable-state-preflight</c> — read-only
/// host preflight that classifies dirty host-durable-state paths as
/// <c>verified-commit-ready</c>, <c>needs-operator-review</c>, or
/// <c>unsafe-durable-state</c>. The host loop calls this when
/// <c>host-sync-preflight</c> reports <c>dirty-host-durable-state</c>;
/// when this command returns <c>verified-commit-ready</c>, the host
/// loop's prompt-matrix guidance directs the operator/agent to
/// <c>git pull --ff-only</c>, stage the verified files only, commit
/// with the recommended message, push, and re-run
/// <c>host-sync-preflight</c>.
///
/// Captures (read-only):
/// <list type="bullet">
/// <item><c>git status --porcelain</c> for the dirty path list (and the per-path porcelain status code that signals deletion vs modification).</item>
/// <item>For <c>.intent-cli/queue-state.json</c>: <c>git show :path</c> (HEAD blob) + working-tree disk read.</item>
/// <item>For <c>.intent-cli/runs.jsonl</c>: same.</item>
/// </list>
/// Feeds those into <see cref="DurableStatePreflightAnalyzer"/>,
/// <see cref="QueueStateForwardDeltaAnalyzer"/>, and
/// <see cref="RunsJsonlAppendOnlyAnalyzer"/>. No mutation, no
/// network.
/// </summary>
internal static class AutomationDurableStatePreflightCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    /// <summary>
    /// Test seam — replaces the default git/disk capture with a
    /// pre-prepared probe so tests can exercise the analyzer wiring
    /// without spawning <c>git</c> or touching real files.
    /// </summary>
    public static Func<string, DurableStatePreflightProbe>? ProbeFactory { get; set; }

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

        if (!TryParseArguments(args, out var format, out var domain, out var targetRepo, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        DurableStatePreflightProbe probe;
        try
        {
            probe = ProbeFactory?.Invoke(context.RepoRoot)
                ?? CaptureProbe(context, domain, targetRepo);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            writer.WriteLine($"failed to capture durable-state probe: {exception.Message}");
            return 1;
        }

        var input = new DurableStatePreflightInput
        {
            DirtyPaths = probe.DirtyPaths,
        };
        var result = DurableStatePreflightAnalyzer.Analyze(input);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        return string.Equals(result.Classification, DurableStatePreflightAnalyzer.ClassificationVerifiedCommitReady, StringComparison.Ordinal)
            ? 0
            : 1;
    }

    /// <summary>
    /// Captures dirty durable-state paths from <c>git status --porcelain</c>,
    /// then for the recognized forward-only kinds runs the per-format
    /// delta analyzer. Untracked or always-unsafe paths get an empty
    /// per-path delta — the analyzer routes them through its
    /// review/unsafe lanes.
    ///
    /// G361: prepared-packet canonical files
    /// (<c>.intent-cli/issues/&lt;unit&gt;/{packet.yaml,implementation.md,review-context.md,github-body.md}</c>)
    /// are grouped per execution-unit; the four files are read from the
    /// working tree and classified once via
    /// <see cref="PreparedPacketCommitReadyAnalyzer"/> using the
    /// active domain's binding regex and the requested target repo.
    /// The shared verdict is attached to each path's delta so each one
    /// is routed through the verified or unsafe lane together.
    /// </summary>
    private static DurableStatePreflightProbe CaptureProbe(
        CliContext context,
        string? domain,
        string? targetRepo)
    {
        var repoRoot = context.RepoRoot;
        var statusOutput = RunGit(repoRoot, "status --porcelain").Replace("\r\n", "\n");
        var rawEntries = new List<(string Path, string Status, bool IsDeleted)>();
        foreach (var rawLine in statusOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawLine.Length < 4)
            {
                continue;
            }
            var status = rawLine[..2];
            var path = rawLine[3..].Trim();
            // Renames look like `R  oldpath -> newpath`. Treat the destination
            // side as the dirty path.
            var arrowIndex = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrowIndex >= 0)
            {
                path = path[(arrowIndex + 4)..].Trim();
            }

            // G361: `git status --porcelain` collapses entire untracked
            // directories into a single trailing-slash entry (e.g.
            // `?? .intent-cli/issues/Z4R-G3/`) by default. To recognize
            // prepared-packet canonical files inside a new packet
            // directory, expand directory entries by walking the
            // working tree ourselves. We intentionally avoid the
            // global `-uall` flag (the system-wide guidance flags it
            // as memory-unsafe on large repos); this targeted
            // expansion only walks the durable-state prefixes we
            // already care about.
            if (path.EndsWith('/'))
            {
                // G361: also accept directory entries that are PARENTS of
                // durable-state paths (e.g. a brand-new `.intent-cli/`
                // tree shows up as `?? .intent-cli/`); the per-file
                // filter below will drop anything outside the durable
                // surface after expansion.
                if (!IsDurableStatePath(path) && !IsDurableStateParent(path))
                {
                    continue;
                }
                foreach (var expanded in ExpandUntrackedDirectory(repoRoot, path))
                {
                    if (!IsDurableStatePath(expanded))
                    {
                        continue;
                    }
                    rawEntries.Add((expanded, status, false));
                }
                continue;
            }

            // G304 already filters by durable-state prefix in
            // host-sync-preflight; here we replicate the filter so this
            // command can be invoked standalone and only durable-state
            // paths are reported. Anything outside the durable surface is
            // ignored — the operator will use safe-stash for it.
            if (!IsDurableStatePath(path))
            {
                continue;
            }
            rawEntries.Add((path, status, status.Contains('D')));
        }

        // G361: group dirty prepared-packet files by execution-unit so the
        // analyzer runs once per EU. The shared verdict is then attached
        // to each individual canonical file's DurableStateDirtyPath.
        var preparedPacketEUs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, _, isDeleted) in rawEntries)
        {
            if (isDeleted) continue;
            if (TryGetPreparedPacketExecutionUnit(path, out var eu))
            {
                preparedPacketEUs.Add(eu);
            }
        }

        var preparedPacketResults = new Dictionary<string, PreparedPacketCommitReadyResult>(StringComparer.Ordinal);
        string? executionUnitRegex = null;
        // PR #824 review repair #4: only require / resolve the
        // bindings when `--domain` was EXPLICITLY supplied on the
        // command line. The configured project domain
        // (`context.Config.Project.Domain`) is metadata, not an
        // opt-in to fail-closed scoping. Without this guard the
        // legacy host-loop path (`automation durable-state-preflight
        // --format json`, no flag) would fail closed for any
        // configured host even though the analyzer / tests document
        // the legacy fail-open behavior. Track the explicit flag
        // here so the bindings probe and the analyzer's
        // `RequireDomainBinding` only fire on operator opt-in.
        var domainExplicitlySupplied = !string.IsNullOrWhiteSpace(domain);
        if (preparedPacketEUs.Count > 0)
        {
            executionUnitRegex = domainExplicitlySupplied
                ? TryResolveExecutionUnitRegex(context, domain)
                : null;
            foreach (var eu in preparedPacketEUs)
            {
                preparedPacketResults[eu] = BuildPreparedPacketResult(
                    repoRoot, eu, executionUnitRegex, targetRepo, domainExplicitlySupplied);
            }
        }

        var dirtyPaths = new List<DurableStateDirtyPath>();
        foreach (var (path, _, isDeleted) in rawEntries)
        {
            QueueStateForwardDeltaResult? queueDelta = null;
            RunsJsonlAppendOnlyResult? runsDelta = null;
            PublishYamlCanonicalResult? publishYamlDelta = null;
            PreparedPacketCommitReadyResult? preparedPacketDelta = null;

            if (!isDeleted)
            {
                if (path.Equals(DurableStatePreflightAnalyzer.PathQueueStateJson, StringComparison.Ordinal))
                {
                    queueDelta = TryQueueStateDelta(repoRoot, path);
                }
                else if (path.Equals(DurableStatePreflightAnalyzer.PathRunsJsonl, StringComparison.Ordinal))
                {
                    runsDelta = TryRunsJsonlDelta(repoRoot, path);
                }
                else if (IsPublishYamlPath(path, out var executionUnit))
                {
                    // G343: read working-tree publish.yaml and classify
                    // its content as canonical / non-canonical /
                    // invalid. The analyzer downstream uses this delta
                    // to route the path through the verified lane
                    // instead of the legacy blanket-unsafe rule.
                    publishYamlDelta = TryPublishYamlDelta(repoRoot, path, executionUnit);
                }
                else if (TryGetPreparedPacketExecutionUnit(path, out var packetEu)
                    && preparedPacketResults.TryGetValue(packetEu, out var sharedVerdict))
                {
                    // G361: attach the shared per-EU verdict to this
                    // canonical file's delta. The analyzer routes the
                    // path through the verified or unsafe lane based
                    // on the verdict.
                    preparedPacketDelta = sharedVerdict;
                }
            }

            dirtyPaths.Add(new DurableStateDirtyPath
            {
                Path = path,
                IsDeleted = isDeleted,
                QueueStateDelta = queueDelta,
                RunsJsonlDelta = runsDelta,
                PublishYamlDelta = publishYamlDelta,
                PreparedPacketDelta = preparedPacketDelta,
            });
        }

        return new DurableStatePreflightProbe { DirtyPaths = dirtyPaths };
    }

    /// <summary>
    /// G361: enumerate the on-disk files under an untracked durable-state
    /// directory and return them as forward-slash repo-relative paths.
    /// Used only for directory entries (those trailing-slash collapsed
    /// rows in <c>git status --porcelain</c>); recurses through
    /// subdirectories. Returns an empty enumerable when the directory
    /// is unreadable or missing.
    /// </summary>
    private static IEnumerable<string> ExpandUntrackedDirectory(string repoRoot, string directoryRelPath)
    {
        var normalized = directoryRelPath.Replace('\\', '/').TrimStart('/').TrimEnd('/');
        var absolute = Path.Combine(repoRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(absolute))
        {
            yield break;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(absolute, "*", SearchOption.AllDirectories);
        }
        catch (IOException) { yield break; }
        catch (UnauthorizedAccessException) { yield break; }

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            yield return relative;
        }
    }

    /// <summary>
    /// G361: detect a prepared-packet canonical file path and extract
    /// the directory-derived execution unit. Returns false for any path
    /// outside <c>.intent-cli/issues/&lt;unit&gt;/{packet.yaml,implementation.md,review-context.md,github-body.md}</c>.
    /// </summary>
    private static bool TryGetPreparedPacketExecutionUnit(string path, out string executionUnit)
    {
        executionUnit = string.Empty;
        if (string.IsNullOrEmpty(path)) return false;
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (!normalized.StartsWith(DurableStatePreflightAnalyzer.IssuesDirectorySegment, StringComparison.Ordinal))
        {
            return false;
        }
        var remainder = normalized[DurableStatePreflightAnalyzer.IssuesDirectorySegment.Length..];
        var slashIndex = remainder.IndexOf('/');
        if (slashIndex <= 0)
        {
            return false;
        }
        var fileSegment = remainder[(slashIndex + 1)..];
        var matchedCanonical = false;
        foreach (var canonical in PreparedPacketCommitReadyAnalyzer.CanonicalFileNames)
        {
            if (fileSegment.Equals(canonical, StringComparison.Ordinal))
            {
                matchedCanonical = true;
                break;
            }
        }
        if (!matchedCanonical)
        {
            return false;
        }
        executionUnit = remainder[..slashIndex];
        return !string.IsNullOrWhiteSpace(executionUnit);
    }

    /// <summary>
    /// G361: read the four canonical files for a prepared-packet
    /// execution unit from the working tree and feed them into
    /// <see cref="PreparedPacketCommitReadyAnalyzer.Analyze"/>. Missing
    /// files are forwarded as null so the analyzer's
    /// <c>missing-canonical-file</c> branch fires.
    /// </summary>
    private static PreparedPacketCommitReadyResult BuildPreparedPacketResult(
        string repoRoot,
        string executionUnit,
        string? executionUnitRegex,
        string? requestedTargetRepo,
        bool requireDomainBinding)
    {
        var packetDir = Path.Combine(repoRoot, ".intent-cli", "issues", executionUnit);

        return PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = executionUnit,
            PacketYaml = TryReadFile(Path.Combine(packetDir, PreparedPacketCommitReadyAnalyzer.FileNamePacketYaml)),
            ImplementationMarkdown = TryReadFile(Path.Combine(packetDir, PreparedPacketCommitReadyAnalyzer.FileNameImplementationMarkdown)),
            ReviewContextMarkdown = TryReadFile(Path.Combine(packetDir, PreparedPacketCommitReadyAnalyzer.FileNameReviewContextMarkdown)),
            GithubBodyMarkdown = TryReadFile(Path.Combine(packetDir, PreparedPacketCommitReadyAnalyzer.FileNameGithubBodyMarkdown)),
            ExecutionUnitRegex = executionUnitRegex,
            RequestedTargetRepo = requestedTargetRepo,
            RequireDomainBinding = requireDomainBinding,
        });
    }

    private static string? TryReadFile(string absolutePath)
    {
        if (!File.Exists(absolutePath))
        {
            return null;
        }
        try
        {
            return File.ReadAllText(absolutePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// G361: resolve the active domain's <c>execution_unit_regex</c>
    /// from <c>intents/&lt;domain&gt;/automation/bindings.md</c>.
    /// Mirrors the resolution order used by
    /// <see cref="AutomationSummaryAnalyzer"/> — child <c>RepoRoot</c>
    /// first, then the configured parent intent repo root. Returns null
    /// when no domain is supplied, the bindings file is missing, or the
    /// field is absent. The analyzer downstream interprets null as
    /// "no cross-domain check" so a misconfigured host cannot
    /// indefinitely block the prepared-packet lane.
    /// </summary>
    private static string? TryResolveExecutionUnitRegex(CliContext context, string? domain)
    {
        // PR #824 review repair #4: do NOT fall back to
        // `context.Config.Project.Domain` when the caller did not
        // supply --domain. The caller already gates this method on
        // `domainExplicitlySupplied` so the legacy host-loop path
        // (no flag) skips bindings resolution entirely; this guard
        // is a defense-in-depth backstop.
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        var childPath = string.IsNullOrWhiteSpace(context.RepoRoot)
            ? null
            : Path.Combine(context.RepoRoot, "intents", domain, "automation", "bindings.md");
        if (!string.IsNullOrWhiteSpace(childPath) && File.Exists(childPath))
        {
            var fromChild = ExtractExecutionUnitRegex(TryReadFile(childPath));
            if (!string.IsNullOrWhiteSpace(fromChild))
            {
                return fromChild;
            }
        }

        var parentRoot = context.ResolveParentIntentRepoRootPath();
        if (string.IsNullOrWhiteSpace(parentRoot))
        {
            return null;
        }
        var parentPath = Path.Combine(parentRoot, "intents", domain, "automation", "bindings.md");
        if (!File.Exists(parentPath))
        {
            return null;
        }
        return ExtractExecutionUnitRegex(TryReadFile(parentPath));
    }

    /// <summary>
    /// G361: minimal tolerant parser for the
    /// <c>execution_unit_regex</c> scalar inside a bindings.md file.
    /// Mirrors the tolerant scan in
    /// <see cref="AutomationSummaryAnalyzer"/>.
    /// </summary>
    private static string? ExtractExecutionUnitRegex(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var rawLine in normalized.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0) continue;
            if (line[0] == ' ' || line[0] == '\t') continue;
            if (line.StartsWith('#') || line.StartsWith("- ", StringComparison.Ordinal)) continue;
            if (string.Equals(line.Trim(), "---", StringComparison.Ordinal)) continue;
            var colonIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex <= 0) continue;
            var key = line[..colonIndex].Trim();
            if (!string.Equals(key, "execution_unit_regex", StringComparison.Ordinal)) continue;
            var value = line[(colonIndex + 1)..].Trim();
            if (value.Length >= 2
                && ((value[0] == '\'' && value[^1] == '\'')
                    || (value[0] == '"' && value[^1] == '"')))
            {
                value = value[1..^1];
            }
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        return null;
    }

    private static QueueStateForwardDeltaResult TryQueueStateDelta(string repoRoot, string path)
    {
        // `git show :path` resolves the staged version of the file. For
        // the host-loop scenario (where the dirty change is in the
        // working tree, not yet staged), `:path` falls back to HEAD when
        // nothing is staged; that's the comparison we want — HEAD vs
        // working tree.
        string headBlob;
        try
        {
            headBlob = RunGit(repoRoot, $"show HEAD:{path}");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return new QueueStateForwardDeltaResult
            {
                Classification = QueueStateForwardDeltaAnalyzer.ClassificationInvalid,
                Summary = $"could not read HEAD blob for `{path}`: {exception.Message}",
                Changes = Array.Empty<QueueStateForwardChange>(),
            };
        }

        var workingPath = Path.Combine(repoRoot, path);
        if (!File.Exists(workingPath))
        {
            return new QueueStateForwardDeltaResult
            {
                Classification = QueueStateForwardDeltaAnalyzer.ClassificationInvalid,
                Summary = $"working-tree path `{path}` does not exist on disk.",
                Changes = Array.Empty<QueueStateForwardChange>(),
            };
        }

        string workingBlob;
        try
        {
            workingBlob = File.ReadAllText(workingPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new QueueStateForwardDeltaResult
            {
                Classification = QueueStateForwardDeltaAnalyzer.ClassificationInvalid,
                Summary = $"could not read working-tree `{path}`: {exception.Message}",
                Changes = Array.Empty<QueueStateForwardChange>(),
            };
        }

        return QueueStateForwardDeltaAnalyzer.Analyze(headBlob, workingBlob);
    }

    private static RunsJsonlAppendOnlyResult TryRunsJsonlDelta(string repoRoot, string path)
    {
        string headBlob;
        try
        {
            headBlob = RunGit(repoRoot, $"show HEAD:{path}");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return new RunsJsonlAppendOnlyResult
            {
                Classification = RunsJsonlAppendOnlyAnalyzer.ClassificationInvalid,
                Summary = $"could not read HEAD blob for `{path}`: {exception.Message}",
                AppendedEventCount = 0,
            };
        }

        var workingPath = Path.Combine(repoRoot, path);
        if (!File.Exists(workingPath))
        {
            return new RunsJsonlAppendOnlyResult
            {
                Classification = RunsJsonlAppendOnlyAnalyzer.ClassificationInvalid,
                Summary = $"working-tree path `{path}` does not exist on disk.",
                AppendedEventCount = 0,
            };
        }

        string workingBlob;
        try
        {
            workingBlob = File.ReadAllText(workingPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new RunsJsonlAppendOnlyResult
            {
                Classification = RunsJsonlAppendOnlyAnalyzer.ClassificationInvalid,
                Summary = $"could not read working-tree `{path}`: {exception.Message}",
                AppendedEventCount = 0,
            };
        }

        return RunsJsonlAppendOnlyAnalyzer.Analyze(headBlob, workingBlob);
    }

    /// <summary>
    /// G343: detect <c>.intent-cli/issues/&lt;execution-unit&gt;/publish.yaml</c>
    /// paths and extract the directory-derived execution-unit so the
    /// caller can build the canonical-content delta.
    /// </summary>
    private static bool IsPublishYamlPath(string path, out string executionUnit)
    {
        executionUnit = string.Empty;
        if (string.IsNullOrEmpty(path)) return false;
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (!normalized.StartsWith(DurableStatePreflightAnalyzer.IssuesDirectorySegment, StringComparison.Ordinal))
        {
            return false;
        }
        var remainder = normalized[DurableStatePreflightAnalyzer.IssuesDirectorySegment.Length..];
        var slashIndex = remainder.IndexOf('/');
        if (slashIndex <= 0)
        {
            return false;
        }
        var fileSegment = remainder[(slashIndex + 1)..];
        if (!fileSegment.Equals(DurableStatePreflightAnalyzer.PublishYamlFileName, StringComparison.Ordinal))
        {
            return false;
        }
        executionUnit = remainder[..slashIndex];
        return !string.IsNullOrWhiteSpace(executionUnit);
    }

    private static PublishYamlCanonicalResult TryPublishYamlDelta(
        string repoRoot,
        string path,
        string executionUnit)
    {
        var workingPath = Path.Combine(repoRoot, path);
        if (!File.Exists(workingPath))
        {
            return new PublishYamlCanonicalResult
            {
                Classification = PublishYamlCanonicalAnalyzer.ClassificationInvalid,
                Summary = $"working-tree path `{path}` does not exist on disk; cannot validate canonical publish.yaml content. Run `{PublishYamlCanonicalAnalyzer.RecoveryCommand}` to regenerate.",
            };
        }

        string content;
        try
        {
            content = File.ReadAllText(workingPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new PublishYamlCanonicalResult
            {
                Classification = PublishYamlCanonicalAnalyzer.ClassificationInvalid,
                Summary = $"could not read working-tree `{path}`: {exception.Message}. Run `{PublishYamlCanonicalAnalyzer.RecoveryCommand}` to regenerate.",
            };
        }

        return PublishYamlCanonicalAnalyzer.Analyze(content, executionUnit);
    }

    /// <summary>
    /// G361: returns true when <paramref name="path"/> is a trailing-slash
    /// directory entry that could be a PARENT of a durable-state path —
    /// e.g. <c>.intent-cli/</c> when the entire artifact root is
    /// untracked. The caller still expands and per-file filters.
    /// </summary>
    private static bool IsDurableStateParent(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var normalized = path.Replace('\\', '/').TrimStart('/');
        // Strip the trailing slash for prefix comparisons.
        if (normalized.EndsWith('/'))
        {
            normalized = normalized[..^1];
        }
        return normalized.Equals(".intent-cli", StringComparison.Ordinal)
            || normalized.StartsWith(".intent-cli/", StringComparison.Ordinal)
            || normalized.Equals("intents", StringComparison.Ordinal)
            || normalized.StartsWith("intents/", StringComparison.Ordinal);
    }

    private static bool IsDurableStatePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var normalized = path.Replace('\\', '/').TrimStart('/');
        return normalized.StartsWith(".intent-cli/queue-state.json", StringComparison.Ordinal)
            || normalized.StartsWith(".intent-cli/runs.jsonl", StringComparison.Ordinal)
            || normalized.StartsWith(".intent-cli/issues/", StringComparison.Ordinal)
            || normalized.StartsWith("intents/", StringComparison.Ordinal)
            || normalized.Equals("AGENTS.md", StringComparison.Ordinal)
            || normalized.Equals("CLAUDE.md", StringComparison.Ordinal)
            || normalized.Equals(".intent-cli/host-binding.toml", StringComparison.Ordinal)
            || normalized.Equals(".intent-cli/config.toml", StringComparison.Ordinal);
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
        return stdout;
    }

    private static void WriteMarkdown(TextWriter writer, DurableStatePreflightResult result)
    {
        writer.WriteLine($"# automation durable-state-preflight (G312)");
        writer.WriteLine();
        writer.WriteLine($"- classification: **{result.Classification}**");
        writer.WriteLine($"- verified paths: {result.VerifiedPaths.Count}");
        writer.WriteLine($"- review paths: {result.ReviewPaths.Count}");
        writer.WriteLine($"- unsafe paths: {result.UnsafePaths.Count}");
        writer.WriteLine();
        writer.WriteLine(result.Summary);

        if (result.VerifiedPaths.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Verified (safe to auto-commit)");
            foreach (var path in result.VerifiedPaths)
            {
                writer.WriteLine($"- `{path.Path}` — {path.Summary}");
            }
        }
        if (result.ReviewPaths.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Needs operator review");
            foreach (var path in result.ReviewPaths)
            {
                writer.WriteLine($"- `{path.Path}` — {path.Reason}");
            }
        }
        if (result.UnsafePaths.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Unsafe (hard stop)");
            foreach (var path in result.UnsafePaths)
            {
                writer.WriteLine($"- `{path.Path}` — {path.Reason}");
            }
        }
        if (!string.IsNullOrWhiteSpace(result.RecommendedCommitMessage))
        {
            writer.WriteLine();
            writer.WriteLine("## Recommended commit message");
            writer.WriteLine();
            writer.WriteLine("```");
            writer.WriteLine(result.RecommendedCommitMessage);
            writer.WriteLine("```");
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string format,
        out string? domain,
        out string? targetRepo,
        out string error)
    {
        format = FormatMarkdown;
        domain = null;
        targetRepo = null;
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

                case "--domain":
                    // G361: domain selects the active
                    // intents/<domain>/automation/bindings.md so the
                    // prepared-packet lane can enforce
                    // execution_unit_regex. Ignored when no dirty
                    // prepared-packet directory is present.
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }
                    domain = args[++index].Trim();
                    break;

                case "--target-repo":
                    // G361: target-repo enforces that prepared-packet
                    // directories declaring a different child repo are
                    // refused. Ignored when no dirty prepared-packet
                    // directory is present.
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--target-repo requires a value (owner/repo).";
                        return false;
                    }
                    targetRepo = args[++index].Trim();
                    break;

                // Allow --repo for parity with other automation commands;
                // ignored because durable-state-preflight is repo-content
                // only and the relevant child target is `--target-repo`.
                case "--repo":
                    if (index + 1 >= args.Length)
                    {
                        error = $"{args[index]} requires a value.";
                        return false;
                    }
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }
        return true;
    }
}

internal sealed record DurableStatePreflightProbe
{
    public required IReadOnlyList<DurableStateDirtyPath> DirtyPaths { get; init; }
}
