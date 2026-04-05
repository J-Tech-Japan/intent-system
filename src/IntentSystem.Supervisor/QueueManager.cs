using IntentSystem.Supervisor.Models;

namespace IntentSystem.Supervisor;

/// <summary>
/// Manages deterministic state transitions on a queue snapshot.
/// Each method returns a new QueueState (immutable) and the RunEvent to append.
/// The caller is responsible for persisting the snapshot and appending the event.
/// </summary>
public static class QueueManager
{
    public static QueueTransitionResult TransitionNonBlocking(
        QueueState state,
        string executionUnit,
        QueueItemState targetState,
        string by,
        DateTimeOffset ts)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        ValidateNonBlockingTargetState(targetState);
        FindItem(state, executionUnit);

        return ApplyTransition(state, executionUnit, targetState, new RunEvent
        {
            Ts = ts,
            ExecutionUnit = executionUnit,
            Event = MapNonBlockingEventName(targetState),
            By = by
        });
    }

    public static QueueTransitionResult TransitionBlocking(
        QueueState state,
        string executionUnit,
        QueueItemState targetState,
        string reason,
        string by,
        DateTimeOffset ts)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        ValidateBlockingTargetState(targetState);
        FindItem(state, executionUnit);

        return ApplyTransition(
            state,
            executionUnit,
            targetState,
            [reason],
            new RunEvent
            {
                Ts = ts,
                ExecutionUnit = executionUnit,
                Event = MapBlockingEventName(targetState),
                By = by,
                Reason = reason
            });
    }

    /// <summary>
    /// Transition a queued item to active.
    /// </summary>
    public static QueueTransitionResult Activate(
        QueueState state, string executionUnit, string by, DateTimeOffset ts)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        var item = FindItem(state, executionUnit);
        AssertState(item, QueueItemState.Queued, "activate");

        return ApplyTransition(state, executionUnit, QueueItemState.Active, new RunEvent
        {
            Ts = ts,
            ExecutionUnit = executionUnit,
            Event = "activated",
            By = by
        });
    }

    /// <summary>
    /// Transition an active item to review.
    /// </summary>
    public static QueueTransitionResult SubmitForReview(
        QueueState state, string executionUnit, string by, DateTimeOffset ts,
        string? linkedPr = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        var item = FindItem(state, executionUnit);
        AssertState(item, QueueItemState.Active, "submit-for-review");

        return ApplyTransition(state, executionUnit, QueueItemState.Review, new RunEvent
        {
            Ts = ts,
            ExecutionUnit = executionUnit,
            Event = "review",
            By = by,
            LinkedPr = linkedPr
        });
    }

    /// <summary>
    /// Transition a review item to fixing (repair-in-place).
    /// </summary>
    public static QueueTransitionResult RequestFix(
        QueueState state, string executionUnit, string by, DateTimeOffset ts,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        var item = FindItem(state, executionUnit);
        AssertState(item, QueueItemState.Review, "request-fix");

        return ApplyTransition(state, executionUnit, QueueItemState.Fixing, new RunEvent
        {
            Ts = ts,
            ExecutionUnit = executionUnit,
            Event = "fix-requested",
            By = by,
            Reason = reason
        });
    }

    /// <summary>
    /// Transition a fixing item back to review (repair-in-place cycle).
    /// </summary>
    public static QueueTransitionResult ResubmitForReview(
        QueueState state, string executionUnit, string by, DateTimeOffset ts,
        string? linkedPr = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        var item = FindItem(state, executionUnit);
        AssertState(item, QueueItemState.Fixing, "resubmit-for-review");

        return ApplyTransition(state, executionUnit, QueueItemState.Review, new RunEvent
        {
            Ts = ts,
            ExecutionUnit = executionUnit,
            Event = "review-started",
            By = by,
            LinkedPr = linkedPr
        });
    }

    /// <summary>
    /// Transition a review item to clarify-blocked.
    /// </summary>
    public static QueueTransitionResult RequestClarification(
        QueueState state, string executionUnit, string by, DateTimeOffset ts,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        var item = FindItem(state, executionUnit);
        AssertState(item, QueueItemState.Review, "request-clarification");

        return ApplyTransition(state, executionUnit, QueueItemState.ClarifyBlocked, new RunEvent
        {
            Ts = ts,
            ExecutionUnit = executionUnit,
            Event = "clarify-requested",
            By = by,
            Reason = reason
        });
    }

    /// <summary>
    /// Resolve a clarify-blocked item back to active.
    /// </summary>
    public static QueueTransitionResult ResolveClarification(
        QueueState state, string executionUnit, string by, DateTimeOffset ts)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        var item = FindItem(state, executionUnit);
        AssertState(item, QueueItemState.ClarifyBlocked, "resolve-clarification");

        return ApplyTransition(state, executionUnit, QueueItemState.Active, new RunEvent
        {
            Ts = ts,
            ExecutionUnit = executionUnit,
            Event = "clarify-resolved",
            By = by
        });
    }

    /// <summary>
    /// Complete a reviewed item.
    /// </summary>
    public static QueueTransitionResult Complete(
        QueueState state, string executionUnit, string by, DateTimeOffset ts)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        var item = FindItem(state, executionUnit);
        AssertState(item, QueueItemState.Review, "complete");

        var result = ApplyTransition(state, executionUnit, QueueItemState.Completed, new RunEvent
        {
            Ts = ts,
            ExecutionUnit = executionUnit,
            Event = "completed",
            By = by
        });

        // After completion, unblock dependents
        var unblocked = UnblockDependents(result.UpdatedState, executionUnit, ts);
        return result with { UpdatedState = unblocked };
    }

    /// <summary>
    /// Accept a reviewed item as completed without mutating dependent items.
    /// </summary>
    public static QueueTransitionResult AcceptReview(
        QueueState state, string executionUnit, string by, DateTimeOffset ts)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        var item = FindItem(state, executionUnit);
        AssertState(item, QueueItemState.Review, "accept-review");

        return ApplyTransition(state, executionUnit, QueueItemState.Completed, new RunEvent
        {
            Ts = ts,
            ExecutionUnit = executionUnit,
            Event = "completed",
            By = by
        });
    }

    public static QueueTransitionResult LinkIssue(
        QueueState state,
        string executionUnit,
        LinkedIssue linkedIssue,
        string by,
        DateTimeOffset ts)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentNullException.ThrowIfNull(linkedIssue);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        var item = FindItem(state, executionUnit);
        AssertState(item, QueueItemState.Queued, "link-issue");

        var updatedItems = new QueueItem[state.Items.Count];

        for (var i = 0; i < state.Items.Count; i++)
        {
            var currentItem = state.Items[i];
            updatedItems[i] = string.Equals(currentItem.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                ? currentItem with { LinkedIssue = linkedIssue }
                : currentItem;
        }

        return new QueueTransitionResult
        {
            UpdatedState = state with
            {
                Items = updatedItems,
                UpdatedAt = ts
            },
            Event = new RunEvent
            {
                Ts = ts,
                ExecutionUnit = executionUnit,
                Event = "issue-created",
                By = by,
                LinkedIssue = linkedIssue.Url
            }
        };
    }

    /// <summary>
    /// Recalculates blocked/queued states for all items based on current dependency resolution.
    /// Items whose dependencies are all completed move from blocked to queued.
    /// </summary>
    public static QueueState RefreshDependencies(QueueState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var completedUnits = state.Items
            .Where(i => i.State == QueueItemState.Completed)
            .Select(i => i.ExecutionUnit)
            .ToHashSet(StringComparer.Ordinal);

        var updatedItems = new QueueItem[state.Items.Count];

        for (var i = 0; i < state.Items.Count; i++)
        {
            var item = state.Items[i];

            if (item.State == QueueItemState.Blocked)
            {
                var unresolvedDeps = item.Dependencies
                    .Where(dep => !completedUnits.Contains(dep))
                    .ToArray();

                if (unresolvedDeps.Length == 0)
                {
                    updatedItems[i] = item with
                    {
                        State = QueueItemState.Queued,
                        BlockedBy = []
                    };
                    continue;
                }

                updatedItems[i] = item with
                {
                    BlockedBy = unresolvedDeps
                };
                continue;
            }

            updatedItems[i] = item;
        }

        return state with
        {
            Items = updatedItems,
            UpdatedAt = state.UpdatedAt
        };
    }

    private static QueueState UnblockDependents(QueueState state, string completedUnit, DateTimeOffset ts)
    {
        return RefreshDependencies(state) with { UpdatedAt = ts };
    }

    private static void ValidateNonBlockingTargetState(QueueItemState targetState)
    {
        if (targetState is QueueItemState.Blocked or QueueItemState.ClarifyBlocked)
        {
            throw new InvalidOperationException(
                $"Unsupported queue transition target state '{FormatState(targetState)}'.");
        }
    }

    private static void ValidateBlockingTargetState(QueueItemState targetState)
    {
        if (targetState is not (QueueItemState.Blocked or QueueItemState.ClarifyBlocked))
        {
            throw new InvalidOperationException(
                $"Unsupported blocking queue transition target state '{FormatState(targetState)}'.");
        }
    }

    private static string MapNonBlockingEventName(QueueItemState targetState)
    {
        return targetState switch
        {
            QueueItemState.Queued => "queued",
            QueueItemState.Active => "activated",
            QueueItemState.Review => "review-started",
            QueueItemState.Fixing => "fix-requested",
            QueueItemState.Completed => "completed",
            _ => throw new InvalidOperationException(
                $"Unsupported queue transition target state '{FormatState(targetState)}'.")
        };
    }

    private static string MapBlockingEventName(QueueItemState targetState)
    {
        return targetState switch
        {
            QueueItemState.Blocked => "blocked",
            QueueItemState.ClarifyBlocked => "clarify-requested",
            _ => throw new InvalidOperationException(
                $"Unsupported blocking queue transition target state '{FormatState(targetState)}'.")
        };
    }

    private static string FormatState(QueueItemState state)
    {
        return state switch
        {
            QueueItemState.ClarifyBlocked => "clarify-blocked",
            _ => state.ToString().ToLowerInvariant()
        };
    }

    private static QueueItem FindItem(QueueState state, string executionUnit)
    {
        for (var i = 0; i < state.Items.Count; i++)
        {
            if (string.Equals(state.Items[i].ExecutionUnit, executionUnit, StringComparison.Ordinal))
            {
                return state.Items[i];
            }
        }

        throw new InvalidOperationException(
            $"Execution unit '{executionUnit}' not found in queue state.");
    }

    private static void AssertState(QueueItem item, QueueItemState expected, string operation)
    {
        if (item.State != expected)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} execution unit '{item.ExecutionUnit}': " +
                $"expected state '{expected}' but found '{item.State}'.");
        }
    }

    private static QueueTransitionResult ApplyTransition(
        QueueState state, string executionUnit, QueueItemState newState, RunEvent runEvent)
    {
        return ApplyTransition(state, executionUnit, newState, blockedBy: null, runEvent);
    }

    private static QueueTransitionResult ApplyTransition(
        QueueState state,
        string executionUnit,
        QueueItemState newState,
        IReadOnlyList<string>? blockedBy,
        RunEvent runEvent)
    {
        var updatedItems = new QueueItem[state.Items.Count];

        for (var i = 0; i < state.Items.Count; i++)
        {
            var item = state.Items[i];
            if (string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal))
            {
                updatedItems[i] = blockedBy is null
                    ? item with { State = newState }
                    : item with { State = newState, BlockedBy = blockedBy };
            }
            else
            {
                updatedItems[i] = item;
            }
        }

        var updatedState = state with
        {
            Items = updatedItems,
            UpdatedAt = runEvent.Ts
        };

        return new QueueTransitionResult
        {
            UpdatedState = updatedState,
            Event = runEvent
        };
    }
}
