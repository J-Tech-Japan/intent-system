using IntentSystem.Clarify.Models;
using IntentSystem.Projection;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Clarify;

/// <summary>
/// Orchestrates the clarify resume flow: applies a clarification answer,
/// advances the artifact to applied, optionally regenerates the issue packet,
/// and resumes the queue item from clarify-blocked.
///
/// This gateway owns the D2 resume policy that determines the target state
/// after clarification apply:
/// - blocking clarification → resume to review (review-time blocker resolved)
/// - nonblocking clarification → resume to queued (re-enter queue before execution)
///
/// B2 QueueManager owns generic queue transitions; D2 owns the clarification-apply
/// policy that decides which state the execution unit returns to.
///
/// Parent Markdown updates are expected to be performed by the caller before
/// invoking the gateway.
/// </summary>
public static class ClarifyGateway
{
    private const string BlockingValue = "blocking";

    /// <summary>
    /// Applies a clarification answer to the artifact, resumes the queue item
    /// according to the D2 resume policy, and optionally regenerates the issue packet.
    /// </summary>
    public static ClarifyResumeResult Apply(
        ClarificationItem clarification,
        QueueState queueState,
        string by,
        DateTimeOffset ts,
        Func<GeneratedPacket>? regeneratePacket = null)
    {
        ArgumentNullException.ThrowIfNull(clarification);
        ArgumentNullException.ThrowIfNull(queueState);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        ValidateAnswered(clarification);

        var appliedClarification = clarification with
        {
            Status = ClarificationStatus.Applied
        };

        var applyEvent = new RunEvent
        {
            Ts = ts,
            ExecutionUnit = clarification.ExecutionUnit,
            Event = "clarify-applied",
            By = by,
            Reason = $"Applied clarification {clarification.QuestionId}"
        };

        var targetState = ResolveResumeTarget(clarification);
        var updatedState = TransitionQueueItem(
            queueState, clarification.ExecutionUnit, targetState, ts);

        var resumeEvent = new RunEvent
        {
            Ts = ts,
            ExecutionUnit = clarification.ExecutionUnit,
            Event = "clarify-resumed",
            By = by,
            Reason = $"Resumed to {FormatState(targetState)}"
        };

        var events = new List<RunEvent> { applyEvent, resumeEvent };

        GeneratedPacket? packet = null;
        if (regeneratePacket is not null)
        {
            packet = regeneratePacket();
            events.Add(new RunEvent
            {
                Ts = ts,
                ExecutionUnit = clarification.ExecutionUnit,
                Event = "packet-regenerated",
                By = by
            });
        }

        return new ClarifyResumeResult
        {
            AppliedClarification = appliedClarification,
            UpdatedQueueState = updatedState,
            Events = events.ToArray(),
            RegeneratedPacket = packet
        };
    }

    /// <summary>
    /// Applies multiple answered clarifications for the same execution unit in batch.
    /// All clarifications are advanced to applied, and the queue item is resumed once.
    /// The resume target is determined by the strictest (blocking) clarification in the batch.
    /// </summary>
    public static ClarifyResumeResult ApplyAll(
        IReadOnlyList<ClarificationItem> answeredClarifications,
        QueueState queueState,
        string by,
        DateTimeOffset ts,
        Func<GeneratedPacket>? regeneratePacket = null)
    {
        ArgumentNullException.ThrowIfNull(answeredClarifications);
        ArgumentNullException.ThrowIfNull(queueState);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        if (answeredClarifications.Count == 0)
        {
            throw new InvalidOperationException("No answered clarifications to apply.");
        }

        var executionUnit = answeredClarifications[0].ExecutionUnit;
        var events = new List<RunEvent>();
        ClarificationItem? lastApplied = null;
        var hasBlocking = false;

        foreach (var clarification in answeredClarifications)
        {
            ValidateAnswered(clarification);

            if (!string.Equals(clarification.ExecutionUnit, executionUnit, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"All clarifications in a batch must share the same execution unit. " +
                    $"Expected '{executionUnit}' but found '{clarification.ExecutionUnit}'.");
            }

            if (IsBlocking(clarification))
            {
                hasBlocking = true;
            }

            lastApplied = clarification with
            {
                Status = ClarificationStatus.Applied
            };

            events.Add(new RunEvent
            {
                Ts = ts,
                ExecutionUnit = executionUnit,
                Event = "clarify-applied",
                By = by,
                Reason = $"Applied clarification {clarification.QuestionId}"
            });
        }

        var targetState = hasBlocking ? QueueItemState.Review : QueueItemState.Queued;
        var updatedState = TransitionQueueItem(queueState, executionUnit, targetState, ts);

        events.Add(new RunEvent
        {
            Ts = ts,
            ExecutionUnit = executionUnit,
            Event = "clarify-resumed",
            By = by,
            Reason = $"Resumed to {FormatState(targetState)}"
        });

        GeneratedPacket? packet = null;
        if (regeneratePacket is not null)
        {
            packet = regeneratePacket();
            events.Add(new RunEvent
            {
                Ts = ts,
                ExecutionUnit = executionUnit,
                Event = "packet-regenerated",
                By = by
            });
        }

        return new ClarifyResumeResult
        {
            AppliedClarification = lastApplied!,
            UpdatedQueueState = updatedState,
            Events = events.ToArray(),
            RegeneratedPacket = packet
        };
    }

    /// <summary>
    /// D2 resume policy: determines the target queue state after clarification apply.
    /// - blocking → review (review-time blocker resolved, return to review)
    /// - nonblocking → queued (re-enter queue before execution)
    /// </summary>
    private static QueueItemState ResolveResumeTarget(ClarificationItem clarification)
    {
        return IsBlocking(clarification) ? QueueItemState.Review : QueueItemState.Queued;
    }

    private static bool IsBlocking(ClarificationItem clarification)
    {
        return string.Equals(clarification.BlockingOrNonblocking, BlockingValue, StringComparison.Ordinal);
    }

    private static QueueState TransitionQueueItem(
        QueueState state, string executionUnit, QueueItemState targetState, DateTimeOffset ts)
    {
        var updatedItems = new QueueItem[state.Items.Count];
        var found = false;

        for (var i = 0; i < state.Items.Count; i++)
        {
            var item = state.Items[i];
            if (string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal))
            {
                if (item.State != QueueItemState.ClarifyBlocked)
                {
                    throw new InvalidOperationException(
                        $"Cannot resume execution unit '{executionUnit}': " +
                        $"expected state 'ClarifyBlocked' but found '{item.State}'.");
                }

                updatedItems[i] = item with { State = targetState };
                found = true;
            }
            else
            {
                updatedItems[i] = item;
            }
        }

        if (!found)
        {
            throw new InvalidOperationException(
                $"Execution unit '{executionUnit}' not found in queue state.");
        }

        return state with
        {
            Items = updatedItems,
            UpdatedAt = ts
        };
    }

    private static string FormatState(QueueItemState state)
    {
        return state switch
        {
            QueueItemState.Review => "review",
            QueueItemState.Queued => "queued",
            _ => state.ToString().ToLowerInvariant()
        };
    }

    private static void ValidateAnswered(ClarificationItem clarification)
    {
        if (clarification.Status != ClarificationStatus.Answered)
        {
            throw new InvalidOperationException(
                $"Clarification '{clarification.QuestionId}' must be in 'Answered' status to apply, " +
                $"but found '{clarification.Status}'.");
        }

        if (clarification.Answer is null)
        {
            throw new InvalidOperationException(
                $"Clarification '{clarification.QuestionId}' must have an answer to apply.");
        }
    }
}
