using System.Globalization;
using System.Text.RegularExpressions;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class QueueTransitionCommand
{
    private const string TransitionActor = "intent-cli";

    // G534 review repair: matches a GitHub PR URL as stored in
    // QueueItem.LinkedPr (e.g. "https://github.com/org/repo/pull/42"),
    // capturing the owner/repo and PR number so the linked PR's
    // authoritative state can be looked up before a retirement mutates
    // queue-state, independent of (and not trusting) queue-state's own
    // possibly-stale item state.
    private static readonly Regex LinkedPrUrlPattern = new(
        @"^https://github\.com/(?<repo>[^/]+/[^/]+)/pull/(?<number>\d+)/?$",
        RegexOptions.Compiled);

    /// <summary>
    /// G534 review repair test seam: tests inject a fake
    /// <see cref="IGitHubPrLookup"/> to avoid GitHub network access when
    /// verifying a linked PR's state before allowing retirement.
    /// </summary>
    public static Func<IGitHubPrLookup>? PrLookupFactory { get; set; }

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length < 2
            || string.IsNullOrWhiteSpace(args[0])
            || string.IsNullOrWhiteSpace(args[1]))
        {
            writer.WriteLine("Queue transition command requires an execution unit and target state.");
            return 1;
        }

        var queueState = QueueCommandSupport.LoadQueueState(context, writer);
        if (queueState is null)
        {
            return 1;
        }

        if (!TryParseTargetState(args[1], out var targetState))
        {
            writer.WriteLine(
                "Unsupported queue transition target state. Supported states: queued, active, review, fixing, completed, retired, blocked, clarify-blocked.");
            return 1;
        }

        if (!TryParseReason(args[2..], out var reason, out var reasonError))
        {
            writer.WriteLine(reasonError);
            return 1;
        }

        if (IsBlockingTargetState(targetState) && string.IsNullOrWhiteSpace(reason))
        {
            writer.WriteLine("Blocking queue transitions require --reason <text>.");
            return 1;
        }

        if (!IsBlockingTargetState(targetState) && reason is not null)
        {
            writer.WriteLine("Reason is only supported for blocked and clarify-blocked transitions.");
            return 1;
        }

        try
        {
            // G534 review repair: `retired` is a guarded, idempotent,
            // terminal transition (refuses Completed, no-ops on an
            // already-Retired item) — routed through the dedicated
            // QueueManager.Retire rather than the generic non-blocking
            // path, which never asserted a source state at all.
            if (targetState == QueueItemState.Retired)
            {
                var linkedPrVerification = ResolveLinkedPrVerification(queueState, args[0]);

                var retireResult = QueueManager.Retire(
                    queueState,
                    args[0],
                    TransitionActor,
                    DateTimeOffset.UtcNow,
                    linkedPrVerification);

                if (!retireResult.WasRetired)
                {
                    writer.WriteLine($"{args[0]} is already retired; no changes made (idempotent).");
                    return 0;
                }

                PersistTransition(context, queueState, new QueueTransitionResult
                {
                    UpdatedState = retireResult.UpdatedState,
                    Event = retireResult.Event!
                });

                writer.WriteLine($"Transitioned {args[0]} to retired.");
                return 0;
            }

            var result = IsBlockingTargetState(targetState)
                ? QueueManager.TransitionBlocking(
                    queueState,
                    args[0],
                    targetState,
                    reason!,
                    TransitionActor,
                    DateTimeOffset.UtcNow)
                : QueueManager.TransitionNonBlocking(
                    queueState,
                    args[0],
                    targetState,
                    TransitionActor,
                    DateTimeOffset.UtcNow);

            PersistTransition(context, queueState, result);

            writer.WriteLine($"Transitioned {args[0]} to {FormatState(targetState)}.");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static bool TryParseTargetState(string rawState, out QueueItemState state)
    {
        switch (rawState.Trim().ToLowerInvariant())
        {
            case "queued":
                state = QueueItemState.Queued;
                return true;
            case "active":
                state = QueueItemState.Active;
                return true;
            case "review":
                state = QueueItemState.Review;
                return true;
            case "fixing":
                state = QueueItemState.Fixing;
                return true;
            case "completed":
                state = QueueItemState.Completed;
                return true;
            case "retired":
                // G534: backfill target for a packet already retired
                // outside queue tracking (G525 lifecycle) — e.g. a packet
                // predating queue-state that must be recorded without a
                // hand-edit. QueueManager.MapNonBlockingEventName carries
                // the matching "retired" run-event case.
                state = QueueItemState.Retired;
                return true;
            case "blocked":
                state = QueueItemState.Blocked;
                return true;
            case "clarify-blocked":
                state = QueueItemState.ClarifyBlocked;
                return true;
            default:
                state = default;
                return false;
        }
    }

    private static bool TryParseReason(string[] args, out string? reason, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            reason = null;
            error = null;
            return true;
        }

        if (args.Length < 2 || !string.Equals(args[0], "--reason", StringComparison.Ordinal))
        {
            reason = null;
            error = "Queue transition command only supports optional '--reason <text>'.";
            return false;
        }

        reason = string.Join(' ', args[1..]).Trim();
        if (reason.Length == 0)
        {
            error = "Queue transition reason must not be empty.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// G534 review repair: resolves the authoritative linked-PR state for a
    /// retirement candidate BEFORE any mutation, since queue-state alone
    /// (a stale `Queued`/`Review`/`Fixing` item) is not trustworthy
    /// evidence that the work is actually still open. This is the only
    /// place in the retirement path permitted to reach GitHub — the result
    /// is a plain <see cref="LinkedPrVerification"/> value handed to the
    /// network-free <see cref="QueueManager.Retire"/>.
    /// </summary>
    private static LinkedPrVerification ResolveLinkedPrVerification(QueueState queueState, string executionUnit)
    {
        var existingItem = queueState.Items.FirstOrDefault(item =>
            string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));

        // Unknown execution unit: let QueueManager.Retire's own "not found"
        // refusal handle it — no GitHub call needed.
        if (existingItem is null || string.IsNullOrWhiteSpace(existingItem.LinkedPr))
        {
            return LinkedPrVerification.NotLinked;
        }

        var match = LinkedPrUrlPattern.Match(existingItem.LinkedPr.Trim());
        if (!match.Success || !int.TryParse(
                match.Groups["number"].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var prNumber))
        {
            // Malformed/unparseable linked-PR reference — there IS a link,
            // but its state cannot be resolved. Fail closed rather than
            // treating it as unlinked.
            return LinkedPrVerification.Unverifiable;
        }

        var repo = match.Groups["repo"].Value;

        IGitHubPrLookup prLookup;
        GitHubPrLookupResult lookupResult;
        try
        {
            prLookup = PrLookupFactory?.Invoke() ?? new GhCliGitHubPrLookup();
            lookupResult = prLookup.Lookup(repo, prNumber);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            // Lookup failure (network error, wrong repo, PR not found,
            // malformed gh response, etc.) — ambiguous evidence must never
            // be presumed open.
            return LinkedPrVerification.Unverifiable;
        }

        if (lookupResult.Merged
            || string.Equals(lookupResult.State, "MERGED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(lookupResult.State, "CLOSED", StringComparison.OrdinalIgnoreCase))
        {
            return LinkedPrVerification.ConfirmedMergedOrClosed;
        }

        if (string.Equals(lookupResult.State, "OPEN", StringComparison.OrdinalIgnoreCase))
        {
            return LinkedPrVerification.ConfirmedOpen;
        }

        // Unexpected/empty state string from GitHub — ambiguous.
        return LinkedPrVerification.Unverifiable;
    }

    private static bool IsBlockingTargetState(QueueItemState state)
    {
        return state is QueueItemState.Blocked or QueueItemState.ClarifyBlocked;
    }

    private static void PersistTransition(CliContext context, QueueState baseState, QueueTransitionResult result)
    {
        var queueStatePath = context.GetQueueStatePath();
        // G548: guarded write — never silently drops another domain's item,
        // and re-applies this item-scoped transition if the shared file moved
        // since it was read.
        QueueStatePersistence.Persist(queueStatePath, baseState, result.UpdatedState);

        var runLogPath = context.GetRunLogPath();
        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");
        Directory.CreateDirectory(runLogDirectory);
        File.AppendAllText(
            runLogPath,
            RunLogSerializer.SerializeLine(result.Event) + Environment.NewLine);
    }

    private static string FormatState(QueueItemState state)
    {
        return state switch
        {
            QueueItemState.ClarifyBlocked => "clarify-blocked",
            _ => state.ToString().ToLowerInvariant()
        };
    }
}
