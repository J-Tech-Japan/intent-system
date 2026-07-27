using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G545: <c>intent-cli automation issue-block &lt;execution-unit&gt; --repo
/// &lt;owner/repo&gt; --issue &lt;n&gt; --reason &lt;text&gt; [--write] [--dry-run]
/// [--format text|json]</c> — the ONE canonical, bounded transition that
/// converges BOTH authoritative representations of "blocked" for a single
/// execution unit: queue-state (<c>state=blocked</c> + <c>blocked_by</c>
/// reason, via the existing, unmodified <see cref="QueueManager.TransitionBlocking"/>)
/// and the GitHub issue-level <see cref="WorkerNextActionConstants.Labels.IntentIssueBlocked"/>
/// label (via the installed <see cref="IGitHubLabelMutator"/>). The
/// counterpart <c>--clear</c> (no <c>--reason</c>) converges both back to
/// unblocked. Never a raw <c>gh ... edit --add-label</c>/<c>--remove-label</c>;
/// never a direct queue-state hand-edit.
///
/// Review repair (G545 round 2): a prior label-only version of this command
/// left queue-state untouched, immediately recreating the exact drift this
/// slice exists to close. The execution unit is now a REQUIRED positional
/// argument (never guessed from the issue title) so "which queue item does
/// this affect" is resolved exactly, not heuristically — and the queue item
/// must carry a COMPLETE <see cref="QueueItem.LinkedIssue"/> whose repo
/// (canonicalized, case-insensitive) and number BOTH agree with
/// <c>--repo</c>/<c>--issue</c> (G545 round 3). Missing linkage, a different
/// repo, and a different number are each refused before any interaction
/// with the run log, queue-state, or GitHub — a missing linkage is absent
/// evidence, not consent, and issue #818 exists in almost every repository.
///
/// Fail-closed, repairable write order — mirrors <see
/// cref="QueueReprioritizeCommand"/>'s established convention: the queue-side
/// <c>runs.jsonl</c> audit event is appended FIRST, <c>queue-state.json</c>
/// SECOND, so a failure at either step never produces a silent, unaudited
/// queue mutation. The two authoritative sides (queue-state, GitHub label)
/// are then converged INDEPENDENTLY of each other — if the queue side is
/// already in the target state, its own mutation is skipped, but the label
/// side is still checked/applied, and vice versa — so re-running this exact
/// command after ANY partial failure retries only whatever has not yet
/// converged, without duplicating a completed step. Idempotency for the
/// queue-side audit event itself is decided by <see cref="FindPendingRetryEvent"/>:
/// a matching event is treated as an already-recorded pending write (reused,
/// never duplicated) only when it is the MOST RECENT run-log event for this
/// execution unit AND queue-state does not yet reflect it — distinguishing a
/// genuine partial-failure retry from an unrelated later re-request that
/// happens to reuse the same reason text after a full block/unblock cycle.
///
/// Clearing also empties <see cref="QueueItem.BlockedBy"/>, not just the
/// state. <see cref="QueueManager.TransitionNonBlocking"/> deliberately
/// leaves <c>blocked_by</c> alone (it is a general-purpose transition that
/// must not assume the caller owns that list), but the blocking half of THIS
/// command sets it wholesale to the single recorded reason, so its exact
/// inverse is to empty it. Leaving the stale reason behind would keep the
/// unit permanently ineligible for selection — <see
/// cref="IntentNextSliceCommand"/>'s dependency/blocked-by gate rejects any
/// item with a non-empty <c>blocked_by</c> regardless of its state — i.e. a
/// "cleared" unit that is still, in effect, blocked.
/// </summary>
internal static class AutomationIssueBlockCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";
    private const string BlockedLabel = WorkerNextActionConstants.Labels.IntentIssueBlocked;
    private const string TransitionActor = "intent-cli automation issue-block";

    /// <summary>The queue-side target for <c>--clear</c> — restores the item to the pool <see cref="IntentNextSliceCommand"/>/<c>next-slice</c> selects from.</summary>
    private const QueueItemState UnblockTargetState = QueueItemState.Queued;

    public static Func<IGitHubLabelMutator>? MutatorFactory { get; set; }

    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    /// <summary>Test seam: throws to simulate a runs.jsonl append failure AFTER it would have succeeded, or replaces the real append.</summary>
    internal static Action<string, RunEvent>? AppendRunEventOverride { get; set; }

    /// <summary>Test seam: throws to simulate a queue-state.json write failure AFTER the runs event was already durably appended.</summary>
    internal static Action<string, QueueState>? WriteQueueStateOverride { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private const string UsageLine =
        "Usage: intent-cli automation issue-block <execution-unit> --repo <owner/repo> --issue <n> --reason <text> [--write] [--dry-run] [--format text|json]\n"
        + "       intent-cli automation issue-block <execution-unit> --repo <owner/repo> --issue <n> --clear [--write] [--dry-run] [--format text|json]";

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

        if (!TryParseArguments(args, out var executionUnit, out var repo, out var issue, out var reason, out var clear, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var queueStatePath = context.GetQueueStatePath();
        if (!File.Exists(queueStatePath))
        {
            writer.WriteLine($"No queue state found at {queueStatePath}");
            return 1;
        }

        QueueState queueState;
        try
        {
            queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or JsonException)
        {
            writer.WriteLine($"queue-state.json could not be parsed: {exception.Message}");
            return 1;
        }

        var queueItem = queueState.Items.FirstOrDefault(item => string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));
        if (queueItem is null)
        {
            writer.WriteLine($"queue-state.json has no item with execution_unit '{executionUnit}'.");
            return 1;
        }

        if (!TryValidateLinkedIssue(queueItem, executionUnit!, repo!, issue!.Value, out var linkageError))
        {
            writer.WriteLine(linkageError);
            return 1;
        }

        var now = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var runLogPath = context.GetRunLogPath();

        // G545 round 3: a present-but-unreadable audit trail stops everything
        // BEFORE any interaction — no append, no queue write, no label read or
        // mutation. Transitioning against a run log we cannot parse would
        // record the transition into a file whose existing content is already
        // unknown, and would decide retry-vs-fresh-append from no evidence at
        // all. A MISSING run log stays the valid first-event case.
        if (!TryLoadRunEvents(runLogPath, out var runEvents, out var runLogError))
        {
            writer.WriteLine(runLogError);
            return 1;
        }
        var beforeState = queueItem.State;
        var beforeBlockedBy = queueItem.BlockedBy;

        QueueQueueSideResult queueResult;
        try
        {
            queueResult = clear
                ? ConvergeQueueSide(queueStatePath, runLogPath, runEvents, queueState, queueItem, executionUnit!, UnblockTargetState, reason: null, write, now)
                : ConvergeQueueSide(queueStatePath, runLogPath, runEvents, queueState, queueItem, executionUnit!, QueueItemState.Blocked, reason, write, now);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or JsonException)
        {
            writer.WriteLine($"failed to converge queue-state for '{executionUnit}': {exception.Message}");
            return 1;
        }

        IGitHubLabelMutator mutator;
        try
        {
            mutator = MutatorFactory?.Invoke() ?? new GhCliGitHubLabelMutator();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            writer.WriteLine($"failed to initialize GitHub mutator: {exception.Message}");
            return 1;
        }

        IReadOnlyList<string> currentLabels;
        try
        {
            currentLabels = mutator
                .ReadLabels(repo!, GhCliGitHubLabelMutator.Kinds.Issue, issue!.Value)
                .Select(label => label.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            writer.WriteLine(
                $"queue-state converged for '{executionUnit}' (or already was), but failed to read current labels "
                + $"for issue #{issue} in {repo}: {exception.Message}. Re-run this exact command to retry the label step only.");
            return 1;
        }

        var hasBlockedLabel = currentLabels.Contains(BlockedLabel, StringComparer.Ordinal);
        var labelApplied = false;

        if (write)
        {
            if (!clear && !hasBlockedLabel)
            {
                try
                {
                    mutator.ApplyLabelTransitions(repo!, GhCliGitHubLabelMutator.Kinds.Issue, issue!.Value, [BlockedLabel], Array.Empty<string>());
                    labelApplied = true;
                }
                catch (Exception exception) when (exception is InvalidOperationException or IOException)
                {
                    writer.WriteLine(
                        $"queue-state converged for '{executionUnit}' (or already was), but failed to apply the "
                        + $"{BlockedLabel} label on issue #{issue} in {repo}: {exception.Message}. Re-run this exact "
                        + "command to retry the label step only.");
                    return 1;
                }
            }
            else if (clear && hasBlockedLabel)
            {
                try
                {
                    mutator.ApplyLabelTransitions(repo!, GhCliGitHubLabelMutator.Kinds.Issue, issue!.Value, Array.Empty<string>(), [BlockedLabel]);
                    labelApplied = true;
                }
                catch (Exception exception) when (exception is InvalidOperationException or IOException)
                {
                    writer.WriteLine(
                        $"queue-state converged for '{executionUnit}' (or already was), but failed to clear the "
                        + $"{BlockedLabel} label on issue #{issue} in {repo}: {exception.Message}. Re-run this exact "
                        + "command to retry the label step only.");
                    return 1;
                }
            }
        }

        // Converged only ever means "both sides now agree" — dry-run never
        // claims it, since nothing was actually written.
        var queueConverged = clear
            ? queueResult.AfterState != QueueItemState.Blocked && queueResult.AfterBlockedBy.Count == 0
            : queueResult.AfterState == QueueItemState.Blocked;
        var labelConverged = clear
            ? !hasBlockedLabel || labelApplied
            : hasBlockedLabel || labelApplied;
        var converged = write && queueConverged && labelConverged;

        var result = new AutomationIssueBlockResult
        {
            Repo = repo!,
            ExecutionUnit = executionUnit!,
            Issue = issue!.Value,
            IssueUrl = BuildIssueUrl(repo!, issue.Value),
            Mode = write ? WorkerClaimCompleteConstants.Modes.Write : WorkerClaimCompleteConstants.Modes.DryRun,
            Clear = clear,
            Reason = reason,
            Queue = new AutomationIssueBlockQueueResult
            {
                BeforeState = FormatState(beforeState),
                BeforeBlockedBy = beforeBlockedBy,
                TargetState = FormatState(clear ? UnblockTargetState : QueueItemState.Blocked),
                AlreadyConverged = queueResult.AlreadyConverged,
                Applied = queueResult.Applied,
                AfterState = FormatState(queueResult.AfterState),
                AfterBlockedBy = queueResult.AfterBlockedBy,
            },
            Label = new AutomationIssueBlockLabelResult
            {
                BlockedLabel = BlockedLabel,
                HadBlockedLabel = hasBlockedLabel,
                Applied = labelApplied,
                CurrentLabels = currentLabels,
            },
            Converged = converged,
            TransitionedAt = now,
            Summary = BuildSummary(executionUnit!, issue.Value, clear, write, queueResult, hasBlockedLabel, labelApplied),
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteText(writer, result);
        }

        return 0;
    }

    private readonly record struct QueueQueueSideResult(
        bool AlreadyConverged, bool Applied, QueueItemState AfterState, IReadOnlyList<string> AfterBlockedBy);

    /// <summary>
    /// Converges the queue-side state toward <paramref name="targetState"/>
    /// (Blocked with <paramref name="reason"/>, or the unblock target with no
    /// reason). Idempotent: a no-op when already converged. Dry-run reports
    /// what WOULD happen without writing anything.
    /// </summary>
    private static QueueQueueSideResult ConvergeQueueSide(
        string queueStatePath,
        string runLogPath,
        IReadOnlyList<RunEvent> runEvents,
        QueueState queueState,
        QueueItem queueItem,
        string executionUnit,
        QueueItemState targetState,
        string? reason,
        bool write,
        DateTimeOffset now)
    {
        // Convergence is judged on BOTH queue fields this command owns:
        // blocked means state=blocked AND blocked_by == [reason]; unblocked
        // means state!=blocked AND blocked_by empty. A unit left with a
        // stale blocked_by is NOT converged — the selector still treats it
        // as blocked.
        var alreadyConverged = targetState == QueueItemState.Blocked
            ? queueItem.State == QueueItemState.Blocked && queueItem.BlockedBy.Count == 1 && string.Equals(queueItem.BlockedBy[0], reason, StringComparison.Ordinal)
            : queueItem.State != QueueItemState.Blocked && queueItem.BlockedBy.Count == 0;

        if (alreadyConverged)
        {
            return new QueueQueueSideResult(true, false, queueItem.State, queueItem.BlockedBy);
        }

        if (targetState == QueueItemState.Blocked && queueItem.State == QueueItemState.Blocked)
        {
            // Already blocked, but with a DIFFERENT reason than requested —
            // a genuine conflict, never silently overwritten.
            throw new InvalidOperationException(
                $"'{executionUnit}' is already blocked with reason '{string.Join("; ", queueItem.BlockedBy)}', "
                + $"which does not match the requested reason '{reason}'. Reconcile manually (e.g. clear first) "
                + "before requesting a different reason.");
        }

        if (!write)
        {
            var previewState = targetState == QueueItemState.Blocked
                ? QueueManager.TransitionBlocking(queueState, executionUnit, targetState, reason!, TransitionActor, now).UpdatedState
                : ClearQueueSideBlocking(
                    QueueManager.TransitionNonBlocking(queueState, executionUnit, targetState, TransitionActor, now).UpdatedState,
                    executionUnit,
                    targetState);
            var previewItem = previewState.Items.First(item => string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));
            return new QueueQueueSideResult(false, true, previewItem.State, previewItem.BlockedBy);
        }

        var eventName = targetState == QueueItemState.Blocked ? "blocked" : MapUnblockEventName(targetState);
        var pendingEvent = FindPendingRetryEvent(runEvents, executionUnit, eventName, reason);

        RunEvent runEvent;
        QueueState updatedState;
        if (pendingEvent is not null)
        {
            // A matching event is already durably recorded from a prior
            // attempt whose queue-state write must have failed — reuse it
            // rather than appending a duplicate.
            runEvent = pendingEvent;
            updatedState = targetState == QueueItemState.Blocked
                ? ApplyBlockedState(queueState, executionUnit, reason!)
                : ClearQueueSideBlocking(queueState, executionUnit, targetState);
        }
        else
        {
            var transitionResult = targetState == QueueItemState.Blocked
                ? QueueManager.TransitionBlocking(queueState, executionUnit, targetState, reason!, TransitionActor, now)
                : QueueManager.TransitionNonBlocking(queueState, executionUnit, targetState, TransitionActor, now);
            runEvent = transitionResult.Event;
            updatedState = targetState == QueueItemState.Blocked
                ? transitionResult.UpdatedState
                : ClearQueueSideBlocking(transitionResult.UpdatedState, executionUnit, targetState);

            AppendRunEvent(runLogPath, runEvent);
        }

        WriteQueueState(queueStatePath, queueState, updatedState);

        var finalItem = updatedState.Items.First(item => string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));
        return new QueueQueueSideResult(false, true, finalItem.State, finalItem.BlockedBy);
    }

    private static QueueState ApplyBlockedState(QueueState state, string executionUnit, string reason) =>
        state with
        {
            Items = state.Items.Select(item => string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                ? item with { State = QueueItemState.Blocked, BlockedBy = [reason] }
                : item).ToArray(),
        };

    /// <summary>
    /// The exact inverse of the blocking half of this command: restores
    /// <paramref name="targetState"/> AND empties <c>blocked_by</c>, which
    /// <see cref="QueueManager.TransitionNonBlocking"/> deliberately leaves
    /// untouched. Without this, a "cleared" unit keeps the stale reason and
    /// stays ineligible for selection — the drift this slice exists to close,
    /// merely relocated from GitHub to queue-state.
    /// </summary>
    private static QueueState ClearQueueSideBlocking(QueueState state, string executionUnit, QueueItemState targetState) =>
        state with
        {
            Items = state.Items.Select(item => string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                ? item with { State = targetState, BlockedBy = Array.Empty<string>() }
                : item).ToArray(),
        };

    private static string MapUnblockEventName(QueueItemState targetState) => targetState switch
    {
        QueueItemState.Queued => "queued",
        QueueItemState.Active => "activated",
        QueueItemState.Review => "review-started",
        QueueItemState.Fixing => "fix-requested",
        _ => throw new InvalidOperationException($"Unsupported unblock target state '{targetState}'."),
    };

    /// <summary>
    /// G545 round 2: a matching event is treated as an already-recorded
    /// PENDING write — reused rather than duplicated — only when it is the
    /// MOST RECENT run-log event for this execution unit AND (implicitly,
    /// since this is only called when queue-state does NOT yet show the
    /// target) queue-state has not caught up to it yet. If anything else
    /// happened to this unit afterward (including a full unblock/reblock
    /// cycle reusing the same reason text), the matching event is historical,
    /// not pending, and a fresh event is appended instead.
    /// </summary>
    private static RunEvent? FindPendingRetryEvent(
        IReadOnlyList<RunEvent> events, string executionUnit, string eventName, string? reason)
    {
        var lastForUnit = events.LastOrDefault(runEvent => string.Equals(runEvent.ExecutionUnit, executionUnit, StringComparison.Ordinal));
        if (lastForUnit is null
            || !string.Equals(lastForUnit.Event, eventName, StringComparison.Ordinal)
            || !string.Equals(lastForUnit.By, TransitionActor, StringComparison.Ordinal))
        {
            return null;
        }

        if (eventName == "blocked" && !string.Equals(lastForUnit.Reason, reason, StringComparison.Ordinal))
        {
            return null;
        }

        return lastForUnit;
    }

    /// <summary>
    /// G545 round 3: the queue item's <c>linked_issue</c> must be COMPLETE
    /// (present, with both a repo and a number) and must agree with BOTH
    /// <c>--repo</c> and <c>--issue</c>. A missing linkage is not "no
    /// objection" — it is missing evidence, and this command is the one that
    /// binds a queue item to a GitHub issue, so it must not guess. Repos are
    /// compared canonically (URL/ssh/<c>.git</c>/trailing-slash shapes
    /// normalized to <c>owner/repo</c>) and case-insensitively, since GitHub
    /// owner/repo names are case-insensitive; the issue number is compared
    /// exactly. Same number in a DIFFERENT repo is the dangerous case this
    /// closes: issue #818 exists in almost every repository.
    /// </summary>
    private static bool TryValidateLinkedIssue(QueueItem queueItem, string executionUnit, string repo, int issue, out string error)
    {
        var linked = queueItem.LinkedIssue;
        if (linked is null || string.IsNullOrWhiteSpace(linked.Repo) || linked.Number is null)
        {
            error =
                $"'{executionUnit}' has no complete queue-state linked_issue (need both repo and number, found "
                + $"{DescribeLinkage(linked)}); refusing to bind issue #{issue} in {repo} to it. Record the linkage "
                + "in queue-state.json first, then re-run this exact command.";
            return false;
        }

        var canonicalLinkedRepo = CanonicalizeRepo(linked.Repo);
        var canonicalRequestedRepo = CanonicalizeRepo(repo);
        if (canonicalLinkedRepo is null || canonicalRequestedRepo is null)
        {
            error =
                $"'{executionUnit}'s queue-state linked_issue repo '{linked.Repo}' or the requested repo '{repo}' is "
                + "not a recognizable owner/repo; refusing to act on an ambiguous repository.";
            return false;
        }

        if (!string.Equals(canonicalLinkedRepo, canonicalRequestedRepo, StringComparison.Ordinal))
        {
            error =
                $"'{executionUnit}'s queue-state linked_issue is in {linked.Repo}, not the requested {repo}; "
                + "refusing to label an issue in a different repository (the same issue number exists in almost "
                + "every repo). Re-invoke against the linked repo, or fix the queue-state linkage first.";
            return false;
        }

        if (linked.Number.Value != issue)
        {
            error =
                $"'{executionUnit}'s queue-state linked_issue is #{linked.Number.Value}, not the requested #{issue}; "
                + "refusing to label a possibly-wrong issue. Re-invoke with the linked issue number, or fix the "
                + "queue-state linkage first.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string DescribeLinkage(LinkedIssue? linked) => linked is null
        ? "no linked_issue at all"
        : $"repo '{(string.IsNullOrWhiteSpace(linked.Repo) ? "(none)" : linked.Repo)}', number {(linked.Number is null ? "(none)" : linked.Number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}";

    /// <summary>
    /// Normalizes the GitHub repository shapes queue-state has been observed
    /// to carry (bare <c>owner/repo</c>, an https URL, an ssh remote, any of
    /// them with a <c>.git</c> suffix or trailing slash) down to a
    /// lowercased <c>owner/repo</c>. Returns null when the value is not a
    /// recognizable repository, so the caller can fail closed rather than
    /// compare two unparseable strings.
    /// </summary>
    private static string? CanonicalizeRepo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Contains("://", StringComparison.Ordinal)
            || normalized.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                normalized = GitHubRepositoryTargetResolver.ParseRemoteUrl(normalized);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                return null;
            }
        }

        normalized = normalized.Trim('/');
        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 2
            ? $"{segments[0].ToLowerInvariant()}/{segments[1].ToLowerInvariant()}"
            : null;
    }

    /// <summary>
    /// G545 round 3: loads the existing audit trail, failing LOUD when the
    /// file is present but unreadable/unparseable. The previous fail-open
    /// behavior let the command append a fresh event and mutate queue-state
    /// against a trail whose content it could not read — deciding
    /// "retry or fresh append" from no evidence, and writing into a file
    /// already known to be corrupt. A MISSING run log is still the valid
    /// first-event case and yields an empty list.
    /// </summary>
    private static bool TryLoadRunEvents(string runLogPath, out IReadOnlyList<RunEvent> events, out string error)
    {
        events = Array.Empty<RunEvent>();
        error = string.Empty;

        if (!File.Exists(runLogPath))
        {
            return true;
        }

        try
        {
            events = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or JsonException)
        {
            error =
                $"the existing run log at {runLogPath} could not be read: {exception.Message}. Refusing to transition "
                + "against an unreadable audit trail — nothing was appended, no queue state was written, and GitHub "
                + "labels were not read or changed. Repair the run log first (e.g. `intent-cli automation runs-audit "
                + "--format json`), then re-run this exact command.";
            return false;
        }
    }

    private static void AppendRunEvent(string runLogPath, RunEvent runEvent)
    {
        if (AppendRunEventOverride is not null)
        {
            AppendRunEventOverride(runLogPath, runEvent);
            return;
        }

        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");
        Directory.CreateDirectory(runLogDirectory);
        File.AppendAllText(runLogPath, RunLogSerializer.SerializeLine(runEvent) + Environment.NewLine);
    }

    private static void WriteQueueState(string queueStatePath, QueueState baseState, QueueState state)
    {
        if (WriteQueueStateOverride is not null)
        {
            WriteQueueStateOverride(queueStatePath, state);
            return;
        }

        // G548: guarded write (no-item-loss + stale-base re-application).
        QueueStatePersistence.Persist(queueStatePath, baseState, state);
    }

    private static string BuildSummary(
        string executionUnit, int issue, bool clear, bool write, QueueQueueSideResult queueResult, bool hadBlockedLabel, bool labelApplied)
    {
        var mode = write ? "Applied" : "Would apply";
        var queuePart = queueResult.AlreadyConverged
            ? "queue-state already converged"
            : $"queue-state {(write ? "transitioned" : "would transition")} to {FormatState(queueResult.AfterState)}";
        var labelPart = clear
            ? (hadBlockedLabel ? $"{mode.ToLowerInvariant()} label removal" : "label already absent")
            : (hadBlockedLabel ? "label already present" : $"{mode.ToLowerInvariant()} label addition");
        return clear
            ? $"Clearing the blocked transition for '{executionUnit}' (issue #{issue}): {queuePart}; {labelPart}."
            : $"Applying the blocked transition for '{executionUnit}' (issue #{issue}): {queuePart}; {labelPart}.";
    }

    private static bool TryParseArguments(
        string[] args,
        out string? executionUnit,
        out string? repo,
        out int? issue,
        out string? reason,
        out bool clear,
        out bool write,
        out string format,
        out string error)
    {
        executionUnit = null;
        repo = null;
        issue = null;
        reason = null;
        clear = false;
        write = false;
        format = FormatText;
        error = string.Empty;

        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]) || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            error = "automation issue-block requires an execution unit as the first argument.";
            return false;
        }

        executionUnit = args[0];

        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) { error = "--repo requires a value (e.g. owner/repo)."; return false; }
                    repo = args[++index].Trim();
                    break;
                case "--issue":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var issueNumber)
                        || issueNumber <= 0)
                    {
                        error = "--issue requires a positive integer.";
                        return false;
                    }
                    issue = issueNumber;
                    index++;
                    break;
                case "--reason":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) { error = "--reason requires a value."; return false; }
                    reason = args[++index].Trim();
                    break;
                case "--clear":
                    clear = true;
                    break;
                case "--write":
                    write = true;
                    break;
                case "--dry-run":
                    write = false;
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) { error = "--format requires a value (text or json)."; return false; }
                    var requestedFormat = args[++index];
                    if (!string.Equals(requestedFormat, FormatText, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'text' or 'json' (got '{requestedFormat}').";
                        return false;
                    }
                    format = requestedFormat;
                    break;
                default:
                    error = $"Unknown argument '{argument}'. Supported: <execution-unit> --repo <owner/repo> --issue <n> [--reason <text>] [--clear] [--write] [--dry-run] [--format text|json].";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required.";
            return false;
        }

        if (issue is null)
        {
            error = "--issue is required.";
            return false;
        }

        if (clear && !string.IsNullOrWhiteSpace(reason))
        {
            error = "--reason is only supported when applying the blocked transition, not with --clear.";
            return false;
        }

        if (!clear && string.IsNullOrWhiteSpace(reason))
        {
            error = "--reason is required unless --clear is given — a blocked transition without a recorded reason is not permitted.";
            return false;
        }

        return true;
    }

    private static string FormatState(QueueItemState state) => state switch
    {
        QueueItemState.ClarifyBlocked => "clarify-blocked",
        _ => state.ToString().ToLowerInvariant(),
    };

    private static string BuildIssueUrl(string repo, int issue) =>
        $"https://github.com/{repo}/issues/{issue.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    private static void WriteText(TextWriter writer, AutomationIssueBlockResult result)
    {
        writer.WriteLine(result.Summary);
        writer.WriteLine($"mode: {result.Mode}");
        writer.WriteLine($"execution_unit: {result.ExecutionUnit}");
        writer.WriteLine($"repo: {result.Repo}");
        writer.WriteLine($"issue: {result.Issue.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        writer.WriteLine($"issue_url: {result.IssueUrl}");
        writer.WriteLine($"clear: {result.Clear.ToString().ToLowerInvariant()}");
        if (!string.IsNullOrWhiteSpace(result.Reason))
        {
            writer.WriteLine($"reason: {result.Reason}");
        }
        writer.WriteLine($"queue.before_state: {result.Queue.BeforeState}");
        writer.WriteLine($"queue.target_state: {result.Queue.TargetState}");
        writer.WriteLine($"queue.already_converged: {result.Queue.AlreadyConverged.ToString().ToLowerInvariant()}");
        writer.WriteLine($"queue.applied: {result.Queue.Applied.ToString().ToLowerInvariant()}");
        writer.WriteLine($"queue.after_state: {result.Queue.AfterState}");
        writer.WriteLine($"queue.after_blocked_by: {(result.Queue.AfterBlockedBy.Count == 0 ? "(none)" : string.Join(", ", result.Queue.AfterBlockedBy))}");
        writer.WriteLine($"label.blocked_label: {result.Label.BlockedLabel}");
        writer.WriteLine($"label.had_blocked_label: {result.Label.HadBlockedLabel.ToString().ToLowerInvariant()}");
        writer.WriteLine($"label.applied: {result.Label.Applied.ToString().ToLowerInvariant()}");
        writer.WriteLine($"converged: {result.Converged.ToString().ToLowerInvariant()}");
        writer.WriteLine($"transitioned_at: {result.TransitionedAt:O}");
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("automation issue-block");
        writer.WriteLine(UsageLine);
        writer.WriteLine("The ONE canonical transition that converges BOTH queue-state (state=blocked + blocked_by");
        writer.WriteLine("reason, via the existing queue transition mechanism) and the GitHub intent-issue-blocked");
        writer.WriteLine("label (G545) for a single execution unit. --clear converges both back to unblocked. Each");
        writer.WriteLine("side is converged independently and idempotently, so re-running after a partial failure");
        writer.WriteLine("only retries whatever has not yet converged. Never uses raw gh label mutation or a direct");
        writer.WriteLine("queue-state hand-edit.");
    }
}

internal sealed record AutomationIssueBlockResult
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("issue")]
    public required int Issue { get; init; }

    [JsonPropertyName("issue_url")]
    public required string IssueUrl { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("clear")]
    public required bool Clear { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("queue")]
    public required AutomationIssueBlockQueueResult Queue { get; init; }

    [JsonPropertyName("label")]
    public required AutomationIssueBlockLabelResult Label { get; init; }

    [JsonPropertyName("converged")]
    public required bool Converged { get; init; }

    [JsonPropertyName("transitioned_at")]
    public required DateTimeOffset TransitionedAt { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}

internal sealed record AutomationIssueBlockQueueResult
{
    [JsonPropertyName("before_state")]
    public required string BeforeState { get; init; }

    [JsonPropertyName("before_blocked_by")]
    public required IReadOnlyList<string> BeforeBlockedBy { get; init; }

    [JsonPropertyName("target_state")]
    public required string TargetState { get; init; }

    [JsonPropertyName("already_converged")]
    public required bool AlreadyConverged { get; init; }

    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }

    [JsonPropertyName("after_state")]
    public required string AfterState { get; init; }

    [JsonPropertyName("after_blocked_by")]
    public required IReadOnlyList<string> AfterBlockedBy { get; init; }
}

internal sealed record AutomationIssueBlockLabelResult
{
    [JsonPropertyName("blocked_label")]
    public required string BlockedLabel { get; init; }

    [JsonPropertyName("had_blocked_label")]
    public required bool HadBlockedLabel { get; init; }

    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }

    [JsonPropertyName("current_labels")]
    public required IReadOnlyList<string> CurrentLabels { get; init; }
}
