using IntentSystem.Supervisor;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G331: <c>intent-cli migrate host-state --domain &lt;d&gt;
/// --target-repo &lt;owner/repo&gt; --role &lt;design|review-runtime&gt;
/// [--dry-run|--write] [--format markdown|json]</c>.
///
/// Direct offline migration for the operator's local canonical host
/// worktrees (MyIntentHost, IntentSystemReview, SekibanAsAServiceReview,
/// TraceForgeHost). Reads root <c>.intent-cli/queue-state.json</c> +
/// <c>.intent-cli/runs.jsonl</c>, attributes items to the named
/// <c>(domain, target-repo)</c> via GitHub linkage (linked_issue.repo
/// / linked_pr), and (in <c>--write</c> mode) creates / merges
/// <c>.intent-cli/runtime/&lt;domain&gt;/&lt;owner&gt;__&lt;repo&gt;/queue-state.json</c>
/// + <c>runs.jsonl</c> with the matching subset, plus a legacy
/// archive copy under
/// <c>&lt;scope&gt;/legacy-archive/queue-state-&lt;ts&gt;.json</c> and
/// <c>runs-&lt;ts&gt;.jsonl</c> for audit.
///
/// Idempotent — running <c>--write</c> twice is safe (items / runs
/// already in scoped state are not duplicated). Existing packets
/// under <c>.intent-cli/issues/&lt;execution-unit&gt;/</c> are NEVER
/// touched (G300 / packet authoring out of scope per G331 packet).
/// </summary>
internal static class MigrateHostStateCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";
    private const string SubcommandHostState = "host-state";

    /// <summary>
    /// Test seam: tests inject a deterministic timestamp factory so
    /// the legacy-archive filenames are reproducible.
    /// </summary>
    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    private const string UsageLine =
        "Usage: intent-cli migrate host-state --domain <domain> --target-repo <owner/repo> --role design|review-runtime [--dry-run|--write] [--format markdown|json]";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 0 || !string.Equals(args[0], SubcommandHostState, StringComparison.Ordinal))
        {
            writer.WriteLine($"Unknown subcommand. {UsageLine}");
            return 1;
        }

        var rest = args.Skip(1).ToArray();
        if (rest.Length == 1 && string.Equals(rest[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseArguments(rest, out var domain, out var targetRepo, out var role, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var repoRoot = context.RepoRoot;
        var legacyQueueStatePath = CliRuntimeContracts.GetQueueStatePath(repoRoot);
        var legacyRunsLogPath = CliRuntimeContracts.GetRunLogPath(repoRoot);

        QueueState? legacyQueueState = null;
        if (File.Exists(legacyQueueStatePath))
        {
            try
            {
                legacyQueueState = QueueStateSerializer.Deserialize(File.ReadAllText(legacyQueueStatePath));
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                writer.WriteLine($"failed to parse legacy queue-state at {legacyQueueStatePath}: {exception.Message}");
                return 1;
            }
        }

        var legacyRuns = ReadRuns(legacyRunsLogPath);

        var scopedQueuePath = RuntimeScopedStateResolver.GetScopedQueueStatePath(repoRoot, domain!, targetRepo!);
        var scopedRunsPath = RuntimeScopedStateResolver.GetScopedRunLogPath(repoRoot, domain!, targetRepo!);

        QueueState? scopedQueueState = null;
        if (File.Exists(scopedQueuePath))
        {
            try
            {
                scopedQueueState = QueueStateSerializer.Deserialize(File.ReadAllText(scopedQueuePath));
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                writer.WriteLine($"failed to parse scoped queue-state at {scopedQueuePath}: {exception.Message}");
                return 1;
            }
        }
        var scopedRuns = ReadRuns(scopedRunsPath);

        var plan = MigrateHostStateAnalyzer.Analyze(new MigrateHostStateInputs
        {
            Domain = domain!,
            TargetRepo = targetRepo!,
            Role = role!,
            LegacyQueueState = legacyQueueState,
            LegacyRuns = legacyRuns,
            ExistingScopedQueueState = scopedQueueState,
            ExistingScopedRuns = scopedRuns
        });

        var mode = write ? "write" : "dry-run";
        var archive = new List<string>();
        var applied = false;

        // G331 review fix: refuse ANY filesystem mutation when the
        // analyzer surfaced ambiguities, even if some other items in
        // the same legacy file are deterministically matched. Partial
        // application would leave the operator with half-migrated
        // scoped state plus an archive copy, and the structured
        // `ambiguities` payload would still demand operator review.
        // Atomic refuse-on-any-gap matches the packet's "ambiguous
        // matches produce structured unsafe metadata, not guessing"
        // acceptance criterion.
        if (write
            && plan.Ambiguities.Count == 0
            && (plan.ItemsToAdd.Count > 0 || plan.RunsToAdd.Count > 0))
        {
            ApplyMigration(repoRoot, domain!, targetRepo!,
                legacyQueueStatePath, legacyRunsLogPath,
                scopedQueuePath, scopedRunsPath,
                scopedQueueState, scopedRuns,
                plan, archive);
            applied = true;
        }

        var result = new MigrateHostStateResult
        {
            Domain = domain!,
            TargetRepo = targetRepo!,
            Role = role!,
            Mode = mode,
            Applied = applied,
            LegacyQueueStatePath = legacyQueueStatePath,
            LegacyRunsLogPath = legacyRunsLogPath,
            ScopedQueueStatePath = scopedQueuePath,
            ScopedRunsLogPath = scopedRunsPath,
            Plan = plan,
            ArchiveFiles = archive
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        // Exit non-zero when ambiguities / missing-linkage records exist
        // so automation noticing the migration must investigate. Idempotent
        // already-migrated wakes return 0.
        return plan.Ambiguities.Count > 0 ? 2 : 0;
    }

    private static void ApplyMigration(
        string repoRoot,
        string domain,
        string targetRepo,
        string legacyQueueStatePath,
        string legacyRunsLogPath,
        string scopedQueueStatePath,
        string scopedRunsLogPath,
        QueueState? scopedQueueState,
        IReadOnlyList<RunEvent> scopedRuns,
        MigrateHostStatePlan plan,
        List<string> archive)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(scopedQueueStatePath)!);

        // Merge new items into scoped queue-state.
        var mergedItems = new List<QueueItem>(scopedQueueState?.Items ?? Array.Empty<QueueItem>());
        mergedItems.AddRange(plan.ItemsToAdd);
        var mergedState = new QueueState
        {
            SchemaVersion = scopedQueueState?.SchemaVersion ?? "1",
            UpdatedAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            Items = mergedItems
        };
        // G548: guarded write. Migration only ever ADDS items to the scoped
        // state, so any item that would disappear is unrequested loss.
        QueueStatePersistence.Persist(
            scopedQueueStatePath,
            scopedQueueState ?? new QueueState { SchemaVersion = "1", UpdatedAt = mergedState.UpdatedAt, Items = Array.Empty<QueueItem>() },
            mergedState);

        // Append new runs to scoped runs.jsonl.
        if (plan.RunsToAdd.Count > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(scopedRunsLogPath)!);
            using var stream = new FileStream(scopedRunsLogPath, FileMode.Append, FileAccess.Write);
            using var streamWriter = new StreamWriter(stream);
            foreach (var runEvent in plan.RunsToAdd)
            {
                streamWriter.WriteLine(RunLogSerializer.SerializeLine(runEvent));
            }
        }

        // Archive legacy files (copy, never delete) for audit. The
        // copies live under the scoped runtime tree so the operator
        // can find them next to the migrated state.
        var ts = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow)
            .ToUniversalTime()
            .ToString("yyyyMMddTHHmmssZ", System.Globalization.CultureInfo.InvariantCulture);
        var archiveDir = Path.Combine(
            RuntimeScopedStateResolver.GetScopedRuntimeDirectory(repoRoot, domain, targetRepo),
            "legacy-archive");
        Directory.CreateDirectory(archiveDir);
        if (File.Exists(legacyQueueStatePath))
        {
            var archivePath = Path.Combine(archiveDir, $"queue-state-{ts}.json");
            File.Copy(legacyQueueStatePath, archivePath, overwrite: false);
            archive.Add(archivePath);
        }
        if (File.Exists(legacyRunsLogPath))
        {
            var archivePath = Path.Combine(archiveDir, $"runs-{ts}.jsonl");
            File.Copy(legacyRunsLogPath, archivePath, overwrite: false);
            archive.Add(archivePath);
        }
    }

    private static IReadOnlyList<RunEvent> ReadRuns(string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<RunEvent>();
        }
        try
        {
            return RunLogSerializer.DeserializeAll(File.ReadAllText(path)).ToArray();
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            // Tolerate corrupt / unreadable legacy runs by treating
            // them as empty — the unresolved_legacy_records signal in
            // the plan tells the operator they may need to handle this
            // separately. Failing the whole migration on an unparseable
            // runs.jsonl would block all hosts.
            return Array.Empty<RunEvent>();
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? domain,
        out string? targetRepo,
        out string? role,
        out bool write,
        out string format,
        out string error)
    {
        domain = null;
        targetRepo = null;
        role = null;
        write = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--domain":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }
                    domain = args[i + 1];
                    i++;
                    break;
                case "--target-repo":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = "--target-repo requires a value (owner/repo).";
                        return false;
                    }
                    targetRepo = args[i + 1];
                    i++;
                    break;
                case "--role":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = "--role requires a value (design or review-runtime).";
                        return false;
                    }
                    role = args[i + 1];
                    if (!string.Equals(role, MigrateHostStateAnalyzer.RoleDesign, StringComparison.Ordinal)
                        && !string.Equals(role, MigrateHostStateAnalyzer.RoleReviewRuntime, StringComparison.Ordinal))
                    {
                        error = $"--role must be 'design' or 'review-runtime' (got '{role}').";
                        return false;
                    }
                    i++;
                    break;
                case "--dry-run":
                    write = false;
                    break;
                case "--write":
                    write = true;
                    break;
                case "--format":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requested = args[i + 1];
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }
                    format = requested;
                    i++;
                    break;
                default:
                    error = $"Unknown argument '{arg}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            error = "--domain is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(targetRepo))
        {
            error = "--target-repo is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(role))
        {
            error = "--role is required (design or review-runtime).";
            return false;
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("migrate host-state (G331)");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Move root queue-state / runs.jsonl into the G327 role-scoped runtime layout for a single (domain, target-repo) pair. Dry-run reports the plan; --write applies the migration idempotently and archives the legacy files under `<scope>/legacy-archive/`.");
    }

    private static void WriteMarkdown(TextWriter writer, MigrateHostStateResult result)
    {
        writer.WriteLine($"# Migrate host-state — {result.Domain} / {result.TargetRepo}");
        writer.WriteLine();
        writer.WriteLine($"- role: {result.Role}");
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- applied: {result.Applied.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- already migrated: {result.Plan.AlreadyMigrated.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- legacy queue-state: {result.LegacyQueueStatePath}");
        writer.WriteLine($"- legacy runs.jsonl: {result.LegacyRunsLogPath}");
        writer.WriteLine($"- scoped queue-state: {result.ScopedQueueStatePath}");
        writer.WriteLine($"- scoped runs.jsonl: {result.ScopedRunsLogPath}");
        writer.WriteLine();
        writer.WriteLine($"## Matching items ({result.Plan.MatchingItems.Count})");
        foreach (var item in result.Plan.MatchingItems)
        {
            writer.WriteLine($"- {item.ExecutionUnit} (state={item.State.ToString().ToLowerInvariant()})");
        }
        writer.WriteLine();
        writer.WriteLine($"## Items to add ({result.Plan.ItemsToAdd.Count})");
        foreach (var item in result.Plan.ItemsToAdd)
        {
            writer.WriteLine($"- {item.ExecutionUnit}");
        }
        writer.WriteLine();
        writer.WriteLine($"## Runs to add ({result.Plan.RunsToAdd.Count})");
        writer.WriteLine();
        if (result.Plan.Ambiguities.Count > 0)
        {
            writer.WriteLine("## Ambiguities");
            foreach (var gap in result.Plan.Ambiguities)
            {
                writer.WriteLine($"- {gap}");
            }
            writer.WriteLine();
        }
        if (result.Plan.MissingGitHubLinkage.Count > 0)
        {
            writer.WriteLine("## Missing GitHub linkage");
            foreach (var gap in result.Plan.MissingGitHubLinkage)
            {
                writer.WriteLine($"- {gap}");
            }
            writer.WriteLine();
        }
        if (result.Plan.UnresolvedLegacyRecords.Count > 0)
        {
            writer.WriteLine("## Unresolved legacy records");
            foreach (var gap in result.Plan.UnresolvedLegacyRecords)
            {
                writer.WriteLine($"- {gap}");
            }
            writer.WriteLine();
        }
        if (result.ArchiveFiles.Count > 0)
        {
            writer.WriteLine("## Archive");
            foreach (var path in result.ArchiveFiles)
            {
                writer.WriteLine($"- {path}");
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>
/// G331: structured result emitted by <c>intent-cli migrate host-state</c>.
/// </summary>
internal sealed record MigrateHostStateResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("target_repo")]
    public required string TargetRepo { get; init; }

    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }

    [JsonPropertyName("legacy_queue_state_path")]
    public required string LegacyQueueStatePath { get; init; }

    [JsonPropertyName("legacy_runs_log_path")]
    public required string LegacyRunsLogPath { get; init; }

    [JsonPropertyName("scoped_queue_state_path")]
    public required string ScopedQueueStatePath { get; init; }

    [JsonPropertyName("scoped_runs_log_path")]
    public required string ScopedRunsLogPath { get; init; }

    [JsonPropertyName("plan")]
    public required MigrateHostStatePlan Plan { get; init; }

    [JsonPropertyName("archive_files")]
    public required IReadOnlyList<string> ArchiveFiles { get; init; }
}
