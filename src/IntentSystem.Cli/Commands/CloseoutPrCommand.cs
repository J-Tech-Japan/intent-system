using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G246: <c>intent-cli closeout pr</c> command. Records and applies parent
/// state updates for an already-accepted child PR without depending on
/// environment-specific closeout skills. Resolves the queue item via the
/// PR's <c>linked_pr</c> reference, plans (or applies) the
/// queue-completion transition, appends <c>runs.jsonl</c> events, and
/// emits a continuation classification hint plus the submodule sync
/// step the operator must run separately. Never merges a PR. Never
/// launches an AI provider.
/// </summary>
internal static class CloseoutPrCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string ModeWrite = "write";
    private const string ModeDryRun = "dry-run";

    private const string ContinuationNextSliceReady = "next-slice-ready";
    private const string ContinuationNoActionableItem = "no-actionable-item";
    private const string ContinuationClarificationRequired = "clarification-required";

    private const string UsageLine =
        "Usage: intent-cli closeout pr --pr <n> --repo <owner/repo> [--issue <n>] [--domain <name>] [--pr-merged true|false] [--dry-run|--write] [--format json|markdown]";

    /// <summary>
    /// Test seam — replaces the default UTC timestamp source for runs events.
    /// </summary>
    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

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

        if (!TryParseArguments(args, out var pr, out var repo, out var domainOverride, out var linkedIssueNumber, out var prMerged, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride!;

        var queueStatePath = context.GetQueueStatePath();
        var runsLogPath = context.GetRunLogPath();

        // G297: when the host loop explicitly tells us the PR has not merged
        // (`--pr-merged false`, e.g. captured via `gh pr view <n> --json
        // merged --jq .merged`), refuse closeout — recording closeout for a
        // non-merged PR would diverge parent durable state from GitHub.
        if (prMerged == false)
        {
            EmitErrorResult(writer, format, NewFailureResult(domain, repo!, pr!.Value, queueStatePath, runsLogPath, write,
                $"PR #{pr} is not merged; closeout pr requires merge success (G297). Re-run after the PR is merged. If the PR is still draft, ready it for review and re-run host review/merge first."));
            return 1;
        }

        if (!File.Exists(queueStatePath))
        {
            EmitErrorResult(writer, format, NewFailureResult(domain, repo!, pr!.Value, queueStatePath, runsLogPath, write,
                $"queue-state file not found: {queueStatePath}"));
            return 1;
        }

        QueueState queueState;
        try
        {
            queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        }
        catch (JsonException jsonException)
        {
            EmitErrorResult(writer, format, NewFailureResult(domain, repo!, pr!.Value, queueStatePath, runsLogPath, write,
                $"queue-state JSON could not be parsed: {jsonException.Message}"));
            return 1;
        }
        catch (InvalidOperationException invalidOperation)
        {
            EmitErrorResult(writer, format, NewFailureResult(domain, repo!, pr!.Value, queueStatePath, runsLogPath, write,
                $"queue-state payload was invalid: {invalidOperation.Message}"));
            return 1;
        }

        var prToken = pr!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var matchedItem = queueState.Items.FirstOrDefault(item => MatchesLinkedPr(item.LinkedPr, repo!, prToken));

        // Fallback: when linked_pr is absent, resolve via --issue (linked_issue.Number).
        if (matchedItem is null && linkedIssueNumber.HasValue)
        {
            matchedItem = queueState.Items.FirstOrDefault(item =>
                item.LinkedIssue?.Number == linkedIssueNumber.Value);
        }

        if (matchedItem is null)
        {
            var hint = linkedIssueNumber.HasValue
                ? $"no queue item found with linked_pr matching #{pr} or linked_issue.number {linkedIssueNumber.Value}."
                : $"no queue item found with linked_pr matching #{pr}. If the queue item has linked_issue but not linked_pr, retry with --issue <n>.";
            EmitErrorResult(writer, format, NewFailureResult(domain, repo!, pr.Value, queueStatePath, runsLogPath, write, hint));
            return 1;
        }

        var alreadyCompleted = matchedItem.State == QueueItemState.Completed;
        var beforeState = matchedItem.State.ToString().ToLowerInvariant();
        var mode = write ? ModeWrite : ModeDryRun;

        // Plan the queue transition. Queued/Active/Review/Fixing → Completed is supported here.
        // Queued is included because the publish/review loop may leave items in queued state
        // when the PR is accepted before all intermediate state transitions are recorded.
        if (!alreadyCompleted
            && matchedItem.State != QueueItemState.Queued
            && matchedItem.State != QueueItemState.Active
            && matchedItem.State != QueueItemState.Review
            && matchedItem.State != QueueItemState.Fixing)
        {
            EmitErrorResult(writer, format, NewFailureResult(domain, repo!, pr.Value, queueStatePath, runsLogPath, write,
                $"queue item '{matchedItem.ExecutionUnit}' is in state '{beforeState}'; closeout supports queued/active/review/fixing → completed only."));
            return 1;
        }

        var executionUnit = matchedItem.ExecutionUnit;
        var nowIso = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow)
            .ToUniversalTime()
            .ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", System.Globalization.CultureInfo.InvariantCulture);

        var runsEvents = new List<string>
        {
            BuildRunsEvent(executionUnit, "pr-merged", repo!, pr.Value, nowIso),
            BuildRunsEvent(executionUnit, "closeout-recorded", repo!, pr.Value, nowIso)
        };

        if (write && !alreadyCompleted)
        {
            var updatedItems = queueState.Items
                .Select(item => item.ExecutionUnit == matchedItem.ExecutionUnit
                    ? UpdateItemState(item, QueueItemState.Completed)
                    : item)
                .ToArray();
            var updatedState = new QueueState
            {
                SchemaVersion = queueState.SchemaVersion,
                UpdatedAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
                Items = updatedItems
            };
            File.WriteAllText(queueStatePath, QueueStateSerializer.Serialize(updatedState));

            using var stream = new FileStream(runsLogPath, FileMode.Append, FileAccess.Write);
            using var streamWriter = new StreamWriter(stream);
            foreach (var line in runsEvents)
            {
                streamWriter.WriteLine(line);
            }
        }

        var continuation = ClassifyContinuation(queueState, matchedItem.ExecutionUnit);

        var nextSteps = new List<string>();

        var linkedIssue = matchedItem.LinkedIssue;
        if (linkedIssue is not null)
        {
            if (linkedIssue.Number.HasValue)
            {
                nextSteps.Add($"Close the linked issue: `gh issue close {linkedIssue.Number.Value} --repo {linkedIssue.Repo} --comment 'Closed by PR #{pr}.'`");
            }
            else if (!string.IsNullOrWhiteSpace(linkedIssue.Url))
            {
                nextSteps.Add($"Close the linked issue at {linkedIssue.Url}: `gh issue close <n> --repo {linkedIssue.Repo} --comment 'Closed by PR #{pr}.'`");
            }
            else
            {
                nextSteps.Add($"Confirm and close the linked issue in {linkedIssue.Repo} (number not resolved): `gh issue close <n> --repo {linkedIssue.Repo} --comment 'Closed by PR #{pr}.'`");
            }
        }
        else
        {
            nextSteps.Add("Confirm and close the linked issue if one exists (linked_issue not set in queue state).");
        }

        nextSteps.Add($"Sync the parent submodule pointer for {repo} to the merge commit (manual step: git -C submodules/<child> fetch && git -C submodules/<child> reset --hard <merge-sha>; git add submodules/<child>).");
        nextSteps.Add("Commit and push the parent durable state (queue-state, runs, submodule pointer).");

        var result = new CloseoutPrResult
        {
            Domain = domain,
            Repo = repo!,
            Pr = pr.Value,
            ExecutionUnit = executionUnit,
            Mode = mode,
            QueueStatePath = queueStatePath,
            RunsLogPath = runsLogPath,
            QueueStateBeforeState = beforeState,
            QueueStateAfterState = "completed",
            QueueAlreadyCompleted = alreadyCompleted,
            RunsEvents = runsEvents,
            ContinuationHint = continuation,
            NextSteps = nextSteps,
            Error = null
        };

        EmitResult(writer, result, format);
        return 0;
    }

    private static bool MatchesLinkedPr(string? linkedPr, string repo, string prToken)
    {
        if (string.IsNullOrWhiteSpace(linkedPr))
        {
            return false;
        }

        if (string.Equals(linkedPr, prToken, StringComparison.Ordinal))
        {
            return true;
        }

        if (linkedPr!.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            return linkedPr.StartsWith($"https://github.com/{repo}/pull/", StringComparison.OrdinalIgnoreCase)
                && linkedPr.EndsWith($"/{prToken}", StringComparison.Ordinal);
        }

        return linkedPr!.EndsWith($"/{prToken}", StringComparison.Ordinal);
    }

    private static QueueItem UpdateItemState(QueueItem item, QueueItemState state)
    {
        return new QueueItem
        {
            ExecutionUnit = item.ExecutionUnit,
            Title = item.Title,
            State = state,
            Dependencies = item.Dependencies,
            BlockedBy = item.BlockedBy,
            ClarificationReturnPath = item.ClarificationReturnPath,
            PacketPaths = item.PacketPaths,
            LinkedIssue = item.LinkedIssue,
            LinkedPr = item.LinkedPr,
            WorkerRole = item.WorkerRole,
            ReviewRole = item.ReviewRole,
            Priority = item.Priority
        };
    }

    private static string BuildRunsEvent(string executionUnit, string kind, string repo, int pr, string timestampIso)
    {
        var payload = new Dictionary<string, object?>
        {
            ["timestamp"] = timestampIso,
            ["execution_unit"] = executionUnit,
            ["kind"] = kind,
            ["repo"] = repo,
            ["pr"] = pr
        };
        return JsonSerializer.Serialize(payload, RunsEventJsonOptions);
    }

    private static string ClassifyContinuation(QueueState state, string completingUnit)
    {
        var openClarification = state.Items.Any(item => item.State == QueueItemState.ClarifyBlocked);
        if (openClarification)
        {
            return ContinuationClarificationRequired;
        }

        var queuedRemaining = state.Items.Any(item =>
            item.State == QueueItemState.Queued
            && !string.Equals(item.ExecutionUnit, completingUnit, StringComparison.Ordinal));
        if (queuedRemaining)
        {
            return ContinuationNextSliceReady;
        }

        return ContinuationNoActionableItem;
    }

    private static CloseoutPrResult NewFailureResult(
        string domain,
        string repo,
        int pr,
        string queueStatePath,
        string runsLogPath,
        bool write,
        string error)
    {
        return new CloseoutPrResult
        {
            Domain = domain,
            Repo = repo,
            Pr = pr,
            ExecutionUnit = null,
            Mode = write ? ModeWrite : ModeDryRun,
            QueueStatePath = queueStatePath,
            RunsLogPath = runsLogPath,
            QueueStateBeforeState = null,
            QueueStateAfterState = null,
            QueueAlreadyCompleted = false,
            RunsEvents = Array.Empty<string>(),
            ContinuationHint = null,
            NextSteps = Array.Empty<string>(),
            Error = error
        };
    }

    private static void EmitErrorResult(TextWriter writer, string format, CloseoutPrResult result)
    {
        EmitResult(writer, result, format);
    }

    private static void EmitResult(TextWriter writer, CloseoutPrResult result, string format)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }
    }

    private static void WriteMarkdown(TextWriter writer, CloseoutPrResult result)
    {
        writer.WriteLine($"# Closeout PR — {result.Repo}#{result.Pr}");
        writer.WriteLine();
        writer.WriteLine($"- domain: {result.Domain}");
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- queue-state path: {result.QueueStatePath}");
        writer.WriteLine($"- runs-log path: {result.RunsLogPath}");
        if (!string.IsNullOrWhiteSpace(result.ExecutionUnit))
        {
            writer.WriteLine($"- execution unit: {result.ExecutionUnit}");
        }
        writer.WriteLine();

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            writer.WriteLine("## Error");
            writer.WriteLine($"- {result.Error}");
            return;
        }

        writer.WriteLine("## Queue transition");
        writer.WriteLine($"- before: {result.QueueStateBeforeState}");
        writer.WriteLine($"- after: {result.QueueStateAfterState}");
        writer.WriteLine($"- already completed: {(result.QueueAlreadyCompleted ? "yes" : "no")}");
        writer.WriteLine();

        writer.WriteLine("## Runs events");
        if (result.RunsEvents.Count == 0)
        {
            writer.WriteLine("- none");
        }
        else
        {
            foreach (var line in result.RunsEvents)
            {
                writer.WriteLine($"- {line}");
            }
        }
        writer.WriteLine();

        writer.WriteLine($"## Continuation hint: {result.ContinuationHint}");
        writer.WriteLine();

        if (result.NextSteps.Count > 0)
        {
            writer.WriteLine("## Next steps");
            foreach (var step in result.NextSteps)
            {
                writer.WriteLine($"- {step}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out int? pr,
        out string? repo,
        out string? domainOverride,
        out int? linkedIssueNumber,
        out bool? prMerged,
        out bool write,
        out string format,
        out string error)
    {
        pr = null;
        repo = null;
        domainOverride = null;
        linkedIssueNumber = null;
        prMerged = null;
        write = false;
        var dryRun = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--pr":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--pr requires a value.";
                        return false;
                    }

                    if (!int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var prValue) || prValue <= 0)
                    {
                        error = $"--pr must be a positive integer (got '{args[index + 1]}').";
                        return false;
                    }

                    pr = prValue;
                    index++;
                    break;

                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value.";
                        return false;
                    }

                    repo = args[index + 1];
                    index++;
                    break;

                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }

                    domainOverride = args[index + 1];
                    index++;
                    break;

                case "--issue":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--issue requires a value.";
                        return false;
                    }

                    if (!int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var issueValue) || issueValue <= 0)
                    {
                        error = $"--issue must be a positive integer (got '{args[index + 1]}').";
                        return false;
                    }

                    linkedIssueNumber = issueValue;
                    index++;
                    break;

                case "--write":
                    if (dryRun)
                    {
                        error = "--write and --dry-run are mutually exclusive.";
                        return false;
                    }

                    write = true;
                    break;

                case "--dry-run":
                    if (write)
                    {
                        error = "--write and --dry-run are mutually exclusive.";
                        return false;
                    }

                    dryRun = true;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }

                    var requested = args[index + 1];
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }

                    format = requested;
                    index++;
                    break;

                case "--pr-merged":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--pr-merged requires a value (true or false).";
                        return false;
                    }
                    var rawMerged = args[index + 1].Trim().ToLowerInvariant();
                    if (string.Equals(rawMerged, "true", StringComparison.Ordinal))
                    {
                        prMerged = true;
                    }
                    else if (string.Equals(rawMerged, "false", StringComparison.Ordinal))
                    {
                        prMerged = false;
                    }
                    else
                    {
                        error = $"--pr-merged must be 'true' or 'false' (got '{args[index + 1]}').";
                        return false;
                    }
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        if (pr is null)
        {
            error = "--pr is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required.";
            return false;
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("closeout pr");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Records the queue/runs closeout for an accepted child PR. --dry-run plans only; --write applies queue + runs updates. Submodule sync remains a manual next step.");
        writer.WriteLine("  Supported states: queued, active, review, fixing → completed.");
        writer.WriteLine("  --issue <n>      Optional: fallback linked-issue number for queue items where linked_pr is absent.");
        writer.WriteLine("  --pr-merged true|false  Optional (G297): explicit GitHub merge state. When 'false', closeout refuses the operation so a draft / unmerged PR cannot record closeout; capture the value via 'gh pr view <n> --json merged --jq .merged'.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions RunsEventJsonOptions = new()
    {
        WriteIndented = false
    };
}

internal sealed record CloseoutPrResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("pr")]
    public required int Pr { get; init; }

    [JsonPropertyName("execution_unit")]
    public string? ExecutionUnit { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("queue_state_path")]
    public required string QueueStatePath { get; init; }

    [JsonPropertyName("runs_log_path")]
    public required string RunsLogPath { get; init; }

    [JsonPropertyName("queue_state_before")]
    public string? QueueStateBeforeState { get; init; }

    [JsonPropertyName("queue_state_after")]
    public string? QueueStateAfterState { get; init; }

    [JsonPropertyName("queue_already_completed")]
    public required bool QueueAlreadyCompleted { get; init; }

    [JsonPropertyName("runs_events")]
    public required IReadOnlyList<string> RunsEvents { get; init; }

    [JsonPropertyName("continuation_hint")]
    public string? ContinuationHint { get; init; }

    [JsonPropertyName("next_steps")]
    public required IReadOnlyList<string> NextSteps { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
