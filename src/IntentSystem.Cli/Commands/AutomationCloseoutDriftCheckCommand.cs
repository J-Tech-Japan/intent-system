using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G356: <c>intent-cli automation closeout-drift-check
/// --repo &lt;owner/repo&gt; [--dry-run|--write] [--format json|text]</c> —
/// host-side repair lane that detects when a queue item is not marked
/// Completed even though its linked GitHub PR is already merged. A single
/// merged PR that maps unambiguously to one non-Completed queue item is a
/// high-confidence, deterministic closeout-drift repair. Ambiguous
/// mappings (multiple items share the same PR number) are surfaced as
/// unsafe stops. All mutations are host-only; this command MUST NOT be
/// invoked from a child implementation loop.
///
/// With <c>--write</c>: marks matched queue items Completed and appends
/// <c>pr-merged</c> and <c>closeout-recorded</c> run events to
/// <c>runs.jsonl</c>. Durable-state paths follow the runtime-scoped
/// resolver so that scoped and legacy layouts are handled consistently.
/// </summary>
internal static class AutomationCloseoutDriftCheckCommand
{
    private const string FormatJson = "json";
    private const string FormatText = "text";

    private const string ModeDryRun = "dry-run";
    private const string ModeWrite = "write";

    internal const string ResultSafeRepair = "safe-repair";
    internal const string ResultUnsafeStop = "unsafe-stop";
    internal const string ResultSkipped = "skipped";

    internal const string ReasonPrMerged = "pr-merged";
    internal const string ReasonPrNotMerged = "pr-not-merged";
    internal const string ReasonAmbiguousMapping = "ambiguous-pr-mapping";
    internal const string ReasonNoLinkedPr = "no-linked-pr";
    internal const string ReasonLookupFailed = "pr-lookup-failed";
    internal const string ReasonMissingClosingIssueLinkage = "missing-closing-issue-linkage";
    internal const string ReasonClosingIssueMismatch = "closing-issue-mismatch";

    /// <summary>Test seam — replaces the gh-backed PR lookup.</summary>
    public static Func<IGitHubPrLookup>? PrLookupFactory { get; set; }

    /// <summary>Test seam — replaces the UTC timestamp source for run events.</summary>
    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseArguments(args, out var repo, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var domain = context.Config.Project.Domain;
        var queueStateLocation = RuntimeScopedStateResolver.ResolveQueueStatePathForRead(
            context.RepoRoot, domain, repo!);
        var queueStatePath = queueStateLocation.Path;

        string runsLogPath = queueStateLocation.Kind == StateLocationKind.Legacy
            ? RuntimeScopedStateResolver.GetLegacyRunLogPath(context.RepoRoot)
            : RuntimeScopedStateResolver.GetScopedRunLogPath(context.RepoRoot, domain, repo!);

        QueueState? queueState = null;
        if (File.Exists(queueStatePath))
        {
            try
            {
                queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                writer.WriteLine($"failed to parse queue-state at {queueStatePath}: {exception.Message}");
                return 1;
            }
        }

        if (queueState is null)
        {
            // No queue state: nothing to repair. Emit empty result.
            var empty = new CloseoutDriftCheckResult
            {
                Repo = repo!,
                Mode = write ? ModeWrite : ModeDryRun,
                SafeRepairCount = 0,
                UnsafeStopCount = 0,
                AppliedCount = 0,
                Records = Array.Empty<CloseoutDriftRecord>(),
                Warnings = Array.Empty<string>(),
                Summary = "closeout-drift-check: no queue-state found; nothing to repair.",
            };
            Emit(writer, empty, format);
            return 0;
        }

        // Collect non-Completed items that have a linked_pr.
        var candidates = queueState.Items
            .Where(item => item.State != QueueItemState.Completed
                && !string.IsNullOrWhiteSpace(item.LinkedPr))
            .ToList();

        if (candidates.Count == 0)
        {
            var empty = new CloseoutDriftCheckResult
            {
                Repo = repo!,
                Mode = write ? ModeWrite : ModeDryRun,
                SafeRepairCount = 0,
                UnsafeStopCount = 0,
                AppliedCount = 0,
                Records = Array.Empty<CloseoutDriftRecord>(),
                Warnings = Array.Empty<string>(),
                Summary = "closeout-drift-check: no candidate queue items with linked_pr; nothing to repair.",
            };
            Emit(writer, empty, format);
            return 0;
        }

        // Build a map from PR number → list of items claiming that PR
        // (repo-scoped, based on the PR URL pattern).
        var prToItems = new Dictionary<int, List<QueueItem>>();
        var itemsWithoutPrNumber = new List<QueueItem>();

        foreach (var item in candidates)
        {
            var prNumber = TryParsePrNumberFromUrl(item.LinkedPr);
            if (prNumber is null)
            {
                itemsWithoutPrNumber.Add(item);
                continue;
            }

            if (!prToItems.TryGetValue(prNumber.Value, out var list))
            {
                list = new List<QueueItem>();
                prToItems[prNumber.Value] = list;
            }
            list.Add(item);
        }

        var prLookup = PrLookupFactory?.Invoke() ?? new GhCliGitHubPrLookup();
        var records = new List<CloseoutDriftRecord>();
        var warnings = new List<string>();

        // Items whose linked_pr URL couldn't be parsed → skipped.
        foreach (var item in itemsWithoutPrNumber)
        {
            records.Add(new CloseoutDriftRecord
            {
                ExecutionUnit = item.ExecutionUnit,
                Result = ResultSkipped,
                ReasonCode = ReasonNoLinkedPr,
                Explanation = $"queue item '{item.ExecutionUnit}' has linked_pr '{item.LinkedPr}' that could not be parsed as a PR number; skipped.",
                LinkedPrNumber = null,
                LinkedIssueNumber = item.LinkedIssue?.Number,
            });
        }

        // Detect drift for each PR number.
        foreach (var (prNumber, items) in prToItems.OrderBy(kv => kv.Key))
        {
            // Ambiguous: multiple queue items claim the same PR number.
            if (items.Count > 1)
            {
                foreach (var item in items)
                {
                    records.Add(new CloseoutDriftRecord
                    {
                        ExecutionUnit = item.ExecutionUnit,
                        Result = ResultUnsafeStop,
                        ReasonCode = ReasonAmbiguousMapping,
                        Explanation = $"PR #{prNumber} is linked by {items.Count} non-Completed queue items ({string.Join(", ", items.Select(i => $"'{i.ExecutionUnit}'"))}); ambiguous — cannot apply closeout-drift repair without operator confirmation.",
                        LinkedPrNumber = prNumber,
                        LinkedIssueNumber = item.LinkedIssue?.Number,
                    });
                }
                continue;
            }

            var singleItem = items[0];
            GitHubPrLookupResult? prView;
            try
            {
                prView = prLookup.Lookup(repo!, prNumber);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                records.Add(new CloseoutDriftRecord
                {
                    ExecutionUnit = singleItem.ExecutionUnit,
                    Result = ResultSkipped,
                    ReasonCode = ReasonLookupFailed,
                    Explanation = $"PR #{prNumber} lookup failed: {exception.Message}; skipped.",
                    LinkedPrNumber = prNumber,
                    LinkedIssueNumber = singleItem.LinkedIssue?.Number,
                });
                warnings.Add($"PR #{prNumber} lookup failed for '{singleItem.ExecutionUnit}': {exception.Message}");
                continue;
            }

            if (!prView.Merged)
            {
                // PR not yet merged — not a drift candidate.
                records.Add(new CloseoutDriftRecord
                {
                    ExecutionUnit = singleItem.ExecutionUnit,
                    Result = ResultSkipped,
                    ReasonCode = ReasonPrNotMerged,
                    Explanation = $"PR #{prNumber} is not merged (state: {prView.State}); no closeout drift.",
                    LinkedPrNumber = prNumber,
                    LinkedIssueNumber = singleItem.LinkedIssue?.Number,
                });
                continue;
            }

            // G356: PR is merged — verify closing-issue linkage before emitting safe-repair.
            // The closing issue in GitHub must match the linked issue on the queue item.
            // Missing or mismatched linkage → unsafe-stop (determinism requirement).
            var linkedIssueNumber = singleItem.LinkedIssue?.Number;
            if (linkedIssueNumber is null)
            {
                records.Add(new CloseoutDriftRecord
                {
                    ExecutionUnit = singleItem.ExecutionUnit,
                    Result = ResultUnsafeStop,
                    ReasonCode = ReasonMissingClosingIssueLinkage,
                    Explanation = $"PR #{prNumber} is merged but queue item '{singleItem.ExecutionUnit}' has no linked_issue; cannot verify closing-issue linkage deterministically.",
                    LinkedPrNumber = prNumber,
                    LinkedIssueNumber = null,
                });
                continue;
            }

            var closingIssues = prView.ClosingIssuesReferences;
            if (closingIssues.Count == 0)
            {
                records.Add(new CloseoutDriftRecord
                {
                    ExecutionUnit = singleItem.ExecutionUnit,
                    Result = ResultUnsafeStop,
                    ReasonCode = ReasonMissingClosingIssueLinkage,
                    Explanation = $"PR #{prNumber} is merged but GitHub reports no closing-issue references; cannot confirm it closes issue #{linkedIssueNumber} deterministically.",
                    LinkedPrNumber = prNumber,
                    LinkedIssueNumber = linkedIssueNumber,
                });
                continue;
            }

            var matchesLinkedIssue = closingIssues.Any(r => r.Number == linkedIssueNumber.Value);
            if (!matchesLinkedIssue)
            {
                var closingNums = string.Join(", ", closingIssues.Select(r => $"#{r.Number}"));
                records.Add(new CloseoutDriftRecord
                {
                    ExecutionUnit = singleItem.ExecutionUnit,
                    Result = ResultUnsafeStop,
                    ReasonCode = ReasonClosingIssueMismatch,
                    Explanation = $"PR #{prNumber} is merged but its closing issues ({closingNums}) do not include the queue item's linked issue #{linkedIssueNumber}; closing-issue mismatch — cannot apply closeout-drift repair.",
                    LinkedPrNumber = prNumber,
                    LinkedIssueNumber = linkedIssueNumber,
                });
                continue;
            }

            // PR is merged and its closing issues include the queue item's linked issue → safe repair.
            records.Add(new CloseoutDriftRecord
            {
                ExecutionUnit = singleItem.ExecutionUnit,
                Result = ResultSafeRepair,
                ReasonCode = ReasonPrMerged,
                Explanation = $"PR #{prNumber} is merged and closes issue #{linkedIssueNumber}; queue item '{singleItem.ExecutionUnit}' is still in state '{singleItem.State.ToString().ToLowerInvariant()}' — closeout-drift repair available.",
                LinkedPrNumber = prNumber,
                LinkedIssueNumber = linkedIssueNumber,
            });
        }

        var safeRepairCount = records.Count(r => string.Equals(r.Result, ResultSafeRepair, StringComparison.Ordinal));
        var unsafeStopCount = records.Count(r => string.Equals(r.Result, ResultUnsafeStop, StringComparison.Ordinal));

        // Apply repairs if --write — but only when there are NO unsafe stops.
        // Mixed safe+unsafe: fail-closed; do not mutate durable state when any
        // unsafe stop is present, even if other items are safe-repair candidates.
        var appliedCount = 0;
        if (write && safeRepairCount > 0 && unsafeStopCount == 0)
        {
            var repairRecords = records
                .Where(r => string.Equals(r.Result, ResultSafeRepair, StringComparison.Ordinal))
                .ToList();

            // Rebuild the queue state with Completed transitions applied.
            var unitsToComplete = repairRecords
                .Select(r => r.ExecutionUnit)
                .ToHashSet(StringComparer.Ordinal);

            var prToApply = repairRecords
                .Where(r => r.LinkedPrNumber.HasValue)
                .ToDictionary(r => r.ExecutionUnit, r => r.LinkedPrNumber!.Value);

            var updatedItems = queueState.Items
                .Select(item => unitsToComplete.Contains(item.ExecutionUnit)
                    ? item with { State = QueueItemState.Completed }
                    : item)
                .ToArray();

            var updatedState = new QueueState
            {
                SchemaVersion = queueState.SchemaVersion,
                UpdatedAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
                Items = updatedItems,
            };

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(queueStatePath)!);
                File.WriteAllText(queueStatePath, QueueStateSerializer.Serialize(updatedState));

                // Append run events for each repaired item.
                Directory.CreateDirectory(Path.GetDirectoryName(runsLogPath)!);
                using var stream = new FileStream(runsLogPath, FileMode.Append, FileAccess.Write);
                using var streamWriter = new StreamWriter(stream);
                var nowTs = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
                foreach (var record in repairRecords)
                {
                    var prNum = prToApply.TryGetValue(record.ExecutionUnit, out var pn) ? pn : 0;
                    streamWriter.WriteLine(BuildRunsEvent(record.ExecutionUnit, "pr-merged", repo!, prNum, nowTs));
                    streamWriter.WriteLine(BuildRunsEvent(record.ExecutionUnit, "closeout-recorded", repo!, prNum, nowTs));
                }

                appliedCount = repairRecords.Count;
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                warnings.Add($"write failed: {exception.Message}");
            }
        }

        var result = new CloseoutDriftCheckResult
        {
            Repo = repo!,
            Mode = write ? ModeWrite : ModeDryRun,
            SafeRepairCount = safeRepairCount,
            UnsafeStopCount = unsafeStopCount,
            AppliedCount = appliedCount,
            Records = records,
            Warnings = warnings,
            Summary = BuildSummary(safeRepairCount, unsafeStopCount, appliedCount, write),
        };

        Emit(writer, result, format);

        if (warnings.Count > 0)
        {
            return 1;
        }
        // Return non-zero when any unsafe stop is present, regardless of safe
        // repair count. Mixed safe+unsafe is an operator-attention condition.
        if (unsafeStopCount > 0)
        {
            return 1;
        }
        return 0;
    }

    private static string BuildSummary(int safeRepairs, int unsafeStops, int applied, bool write)
    {
        if (write)
        {
            if (unsafeStops > 0)
            {
                return $"closeout-drift-check (write): blocked — {unsafeStops} unsafe stop(s) present; no mutations applied. {safeRepairs} safe repair(s) detected but skipped due to unsafe stop(s).";
            }
            return $"closeout-drift-check (write): applied {applied} repair(s); {safeRepairs} safe repair(s) detected, {unsafeStops} unsafe stop(s).";
        }
        return $"closeout-drift-check (dry-run): {safeRepairs} safe repair(s); {unsafeStops} unsafe stop(s).";
    }

    private static string BuildRunsEvent(string executionUnit, string eventName, string repo, int prNumber, DateTimeOffset ts)
    {
        var runEvent = new RunEvent
        {
            Ts = ts,
            ExecutionUnit = executionUnit,
            Event = eventName,
            By = "intent-cli automation closeout-drift-check",
            Repo = repo,
            Pr = prNumber > 0 ? prNumber : null,
        };
        return RunLogSerializer.SerializeLine(runEvent);
    }

    private static int? TryParsePrNumberFromUrl(string? linkedPr)
    {
        if (string.IsNullOrWhiteSpace(linkedPr))
        {
            return null;
        }

        if (int.TryParse(linkedPr, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var direct))
        {
            return direct;
        }

        var idx = linkedPr.LastIndexOf('/');
        if (idx >= 0 && idx + 1 < linkedPr.Length)
        {
            var tail = linkedPr[(idx + 1)..];
            if (int.TryParse(tail, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var n))
            {
                return n;
            }
        }

        return null;
    }

    private static void Emit(TextWriter writer, CloseoutDriftCheckResult result, string format)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteText(writer, result);
        }
    }

    private static void WriteText(TextWriter writer, CloseoutDriftCheckResult result)
    {
        writer.WriteLine($"# automation closeout-drift-check (G356) for {result.Repo}");
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- safe repairs: {result.SafeRepairCount}");
        writer.WriteLine($"- unsafe stops: {result.UnsafeStopCount}");
        writer.WriteLine($"- applied: {result.AppliedCount}");
        writer.WriteLine();
        writer.WriteLine(result.Summary);

        var repairRecords = result.Records.Where(r => string.Equals(r.Result, ResultSafeRepair, StringComparison.Ordinal)).ToArray();
        if (repairRecords.Length > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Safe repairs");
            foreach (var r in repairRecords)
            {
                writer.WriteLine($"- `{r.ExecutionUnit}` (PR #{r.LinkedPrNumber}): {r.Explanation}");
            }
        }

        var stops = result.Records.Where(r => string.Equals(r.Result, ResultUnsafeStop, StringComparison.Ordinal)).ToArray();
        if (stops.Length > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Unsafe stops");
            foreach (var s in stops)
            {
                writer.WriteLine($"- `{s.ExecutionUnit}` ({s.ReasonCode}): {s.Explanation}");
            }
        }

        if (result.Warnings.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Warnings");
            foreach (var w in result.Warnings)
            {
                writer.WriteLine($"- {w}");
            }
        }
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("automation closeout-drift-check (G356)");
        writer.WriteLine("Usage: intent-cli automation closeout-drift-check --repo <owner/repo> [--dry-run] [--write] [--format json|text]");
        writer.WriteLine("Detects non-Completed queue items whose linked GitHub PR is already merged.");
        writer.WriteLine("--write: marks matched queue items Completed and appends pr-merged/closeout-recorded run events.");
        writer.WriteLine("Host-only; never call from a child implementation loop.");
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out bool write,
        out string format,
        out string error)
    {
        repo = null;
        write = false;
        format = FormatText;
        error = string.Empty;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--repo":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = "--repo requires a value (e.g. owner/repo).";
                        return false;
                    }
                    repo = args[++i].Trim();
                    break;

                case "--write":
                    write = true;
                    break;

                case "--dry-run":
                    write = false;
                    break;

                case "--format":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = "--format requires a value (json or text).";
                        return false;
                    }
                    var requested = args[++i].Trim();
                    if (!string.Equals(requested, FormatJson, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatText, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'json' or 'text' (got '{requested}').";
                        return false;
                    }
                    format = requested;
                    break;

                default:
                    error = $"Unknown argument '{args[i]}'. Supported: --repo <owner/repo> [--dry-run] [--write] [--format json|text].";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required (e.g. --repo owner/repo).";
            return false;
        }

        return true;
    }
}

internal sealed record CloseoutDriftCheckResult
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("safe_repair_count")]
    public required int SafeRepairCount { get; init; }

    [JsonPropertyName("unsafe_stop_count")]
    public required int UnsafeStopCount { get; init; }

    [JsonPropertyName("applied_count")]
    public required int AppliedCount { get; init; }

    [JsonPropertyName("records")]
    public required IReadOnlyList<CloseoutDriftRecord> Records { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}

internal sealed record CloseoutDriftRecord
{
    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("result")]
    public required string Result { get; init; }

    [JsonPropertyName("reason_code")]
    public string? ReasonCode { get; init; }

    [JsonPropertyName("explanation")]
    public string? Explanation { get; init; }

    [JsonPropertyName("linked_pr_number")]
    public int? LinkedPrNumber { get; init; }

    [JsonPropertyName("linked_issue_number")]
    public int? LinkedIssueNumber { get; init; }
}
