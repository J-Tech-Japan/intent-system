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

        if (!TryParseArguments(args, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        DurableStatePreflightProbe probe;
        try
        {
            probe = ProbeFactory?.Invoke(context.RepoRoot)
                ?? CaptureProbe(context.RepoRoot);
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
    /// </summary>
    private static DurableStatePreflightProbe CaptureProbe(string repoRoot)
    {
        var statusOutput = RunGit(repoRoot, "status --porcelain").Replace("\r\n", "\n");
        var dirtyPaths = new List<DurableStateDirtyPath>();
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

            // G304 already filters by durable-state prefix in
            // host-sync-preflight; here we replicate the filter so this
            // command can be invoked standalone and only durable-state
            // paths are reported. Anything outside the durable surface is
            // ignored — the operator will use safe-stash for it.
            if (!IsDurableStatePath(path))
            {
                continue;
            }

            var isDeleted = status.Contains('D');

            QueueStateForwardDeltaResult? queueDelta = null;
            RunsJsonlAppendOnlyResult? runsDelta = null;
            PublishYamlCanonicalResult? publishYamlDelta = null;

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
            }

            dirtyPaths.Add(new DurableStateDirtyPath
            {
                Path = path,
                IsDeleted = isDeleted,
                QueueStateDelta = queueDelta,
                RunsJsonlDelta = runsDelta,
                PublishYamlDelta = publishYamlDelta,
            });
        }

        return new DurableStatePreflightProbe { DirtyPaths = dirtyPaths };
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

                // Allow --domain / --repo flags (the host-loop guidance
                // passes them for parity with other automation commands)
                // but ignore — the analyzer is repo-content-only.
                case "--domain":
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
