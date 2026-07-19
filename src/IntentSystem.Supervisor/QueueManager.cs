using IntentSystem.Supervisor.Models;

namespace IntentSystem.Supervisor;

/// <summary>
/// Manages deterministic state transitions on a queue snapshot.
/// Each method returns a new QueueState (immutable) and the RunEvent to append.
/// The caller is responsible for persisting the snapshot and appending the event.
/// </summary>
public static class QueueManager
{
    public static QueueEnqueueResult Enqueue(
        QueueState state,
        QueueItem queueItem,
        string by,
        DateTimeOffset ts)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(queueItem);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        var existingItem = state.Items.FirstOrDefault(item =>
            string.Equals(item.ExecutionUnit, queueItem.ExecutionUnit, StringComparison.Ordinal));
        if (existingItem is not null)
        {
            return new QueueEnqueueResult
            {
                UpdatedState = state,
                QueueItem = existingItem,
                WasEnqueued = false,
                Event = null
            };
        }

        var completedUnits = state.Items
            .Where(item => item.State == QueueItemState.Completed)
            .Select(item => item.ExecutionUnit)
            .ToHashSet(StringComparer.Ordinal);
        var blockedBy = queueItem.Dependencies
            .Where(dependency => !completedUnits.Contains(dependency))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(dependency => dependency, StringComparer.Ordinal)
            .ToArray();
        var normalizedItem = queueItem with
        {
            State = QueueItemState.Queued,
            Dependencies = queueItem.Dependencies
                .Distinct(StringComparer.Ordinal)
                .OrderBy(dependency => dependency, StringComparer.Ordinal)
                .ToArray(),
            BlockedBy = blockedBy
        };

        return new QueueEnqueueResult
        {
            UpdatedState = state with
            {
                Items = state.Items.Concat([normalizedItem]).ToArray(),
                UpdatedAt = ts
            },
            QueueItem = normalizedItem,
            WasEnqueued = true,
            Event = new RunEvent
            {
                Ts = ts,
                ExecutionUnit = normalizedItem.ExecutionUnit,
                Event = "queued",
                By = by,
                LinkedIssue = normalizedItem.LinkedIssue?.Url
            }
        };
    }

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
        var item = FindItem(state, executionUnit);
        AssertNotRetired(item, targetState);

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
        var item = FindItem(state, executionUnit);
        AssertNotRetired(item, targetState);

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
    /// G534 review repair: retire a queue item as a canonical backfill for a
    /// packet retired outside queue tracking (packet-level
    /// <c>lifecycle.yaml</c>, or historically via <c>automation
    /// issue-retire</c>) — never as a way to reclassify completed or
    /// in-flight work. Consistent with <c>automation issue-retire</c>'s own
    /// refusal (G525): legal from any state except <see
    /// cref="QueueItemState.Completed"/> (merged/finished work — retirement
    /// only applies to work that can never be completed as authored), and
    /// idempotent when the item is already <see cref="QueueItemState.Retired"/>
    /// (no state churn, no duplicate run event — <see
    /// cref="QueueRetireResult.WasRetired"/> is <see langword="false"/> so
    /// the caller knows nothing changed). Once retired, the item becomes
    /// terminal: see the <see cref="QueueItemState.Retired"/> source guard
    /// in <see cref="TransitionNonBlocking"/> / <see cref="TransitionBlocking"/>,
    /// which refuses every other transition away from it.
    /// </summary>
    public static QueueRetireResult Retire(
        QueueState state,
        string executionUnit,
        string by,
        DateTimeOffset ts)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        var item = FindItem(state, executionUnit);

        if (item.State == QueueItemState.Completed)
        {
            throw new InvalidOperationException(
                $"Cannot retire execution unit '{executionUnit}': item is completed (merged/finished work); "
                + "retirement only applies to work that can never be completed as authored.");
        }

        if (item.State == QueueItemState.Retired)
        {
            return new QueueRetireResult
            {
                UpdatedState = state,
                QueueItem = item,
                WasRetired = false,
                Event = null
            };
        }

        var runEvent = new RunEvent
        {
            Ts = ts,
            ExecutionUnit = executionUnit,
            Event = "retired",
            By = by
        };

        var result = ApplyTransition(state, executionUnit, QueueItemState.Retired, runEvent);

        return new QueueRetireResult
        {
            UpdatedState = result.UpdatedState,
            QueueItem = FindItem(result.UpdatedState, executionUnit),
            WasRetired = true,
            Event = result.Event
        };
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
    /// Marker prefix recorded in <see cref="QueueItem.BlockedBy"/> when a direct run
    /// emits <c>worktree-progress-adoption-required</c>. Submit-for-review accepts a
    /// blocked item only when its <c>BlockedBy</c> reason starts with this prefix so
    /// the deterministic continuation path "run submit" can carry the worktree
    /// progress into the existing submit/review boundary instead of leaving the
    /// item stuck.
    /// </summary>
    public const string WorktreeProgressAdoptionRequiredBlockedReasonPrefix =
        "worktree-progress-adoption-required";

    /// <summary>
    /// Transition an active item — or an adoption-required blocked item — to review.
    /// </summary>
    /// <param name="allowBlockedWorktreeAdoption">
    /// When <c>true</c>, accept a blocked queue item even when its
    /// <see cref="QueueItem.BlockedBy"/> reason does not carry the
    /// <see cref="WorktreeProgressAdoptionRequiredBlockedReasonPrefix"/> marker.
    /// The caller is responsible for verifying out-of-band that the same bounded
    /// worktree-progress evidence which originally produced the marker is still
    /// present (rerun-stable adoption). Ordinary blocked rejection is unaffected
    /// when the flag is false.
    /// </param>
    public static QueueTransitionResult SubmitForReview(
        QueueState state, string executionUnit, string by, DateTimeOffset ts,
        string? linkedPr = null,
        bool allowBlockedWorktreeAdoption = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        var item = FindItem(state, executionUnit);
        var isBlockedAdoption =
            IsWorktreeProgressAdoptionRequiredBlocked(item)
            || (allowBlockedWorktreeAdoption && item.State == QueueItemState.Blocked);
        if (item.State != QueueItemState.Active && !isBlockedAdoption)
        {
            AssertState(item, QueueItemState.Active, "submit-for-review");
        }

        return ApplyTransition(state, executionUnit, QueueItemState.Review, new RunEvent
        {
            Ts = ts,
            ExecutionUnit = executionUnit,
            Event = "review",
            By = by,
            LinkedPr = linkedPr
        });
    }

    private static bool IsWorktreeProgressAdoptionRequiredBlocked(QueueItem item)
    {
        if (item.State != QueueItemState.Blocked)
        {
            return false;
        }

        foreach (var reason in item.BlockedBy)
        {
            if (reason is null)
            {
                continue;
            }

            if (reason.StartsWith(
                    WorktreeProgressAdoptionRequiredBlockedReasonPrefix,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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
            Event = "review",
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
        // G534 review repair: `retired` is no longer routed through the
        // generic, source-state-agnostic non-blocking path — it has its own
        // guarded entry point (see Retire) that refuses Completed and is
        // idempotent on an already-Retired item. Excluding it here means a
        // caller can never bypass that guard via TransitionNonBlocking.
        if (targetState is QueueItemState.Blocked or QueueItemState.ClarifyBlocked or QueueItemState.Retired)
        {
            throw new InvalidOperationException(
                $"Unsupported queue transition target state '{FormatState(targetState)}'.");
        }
    }

    /// <summary>
    /// G534 review repair: <see cref="QueueItemState.Retired"/> is terminal
    /// (G525) — once an item is retired it must never be transitioned to any
    /// other state through the generic non-blocking/blocking surfaces
    /// (previously a Retired item could be silently reactivated to
    /// <c>queued</c>/<c>active</c>/etc., since neither path asserted a
    /// source state at all). The dedicated <see cref="Retire"/> method
    /// handles the one legal exception (Retired → Retired, idempotent).
    /// </summary>
    private static void AssertNotRetired(QueueItem item, QueueItemState targetState)
    {
        if (item.State == QueueItemState.Retired)
        {
            throw new InvalidOperationException(
                $"Cannot transition execution unit '{item.ExecutionUnit}' to '{FormatState(targetState)}': "
                + "item is retired (G525 terminal state) and can never be reclassified through this surface.");
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
            // G534 review repair: Retired now routes through the dedicated
            // Retire method exclusively (see ValidateNonBlockingTargetState);
            // it is never a legal target here.
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
                var updatedItem = blockedBy is null
                    ? item with { State = newState }
                    : item with { State = newState, BlockedBy = blockedBy };

                if (!string.IsNullOrWhiteSpace(runEvent.LinkedPr))
                {
                    updatedItem = updatedItem with { LinkedPr = runEvent.LinkedPr };
                }

                updatedItems[i] = updatedItem;
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
