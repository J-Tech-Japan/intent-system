using IntentSystem.Clarify.Models;
using IntentSystem.Projection;
using IntentSystem.Projection.Models;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Clarify;

/// <summary>
/// Orchestrates the clarify resume flow: applies a clarification answer,
/// advances the artifact to applied, optionally regenerates the issue packet,
/// and resumes the queue item from clarify-blocked.
///
/// This gateway does not modify the parent Intent repo directly; it provides
/// the boundary contract for the resume flow. Parent Markdown updates are
/// expected to be performed by the caller before invoking the gateway.
/// </summary>
public static class ClarifyGateway
{
    /// <summary>
    /// Applies a clarification answer to the artifact, resumes the queue item,
    /// and optionally regenerates the issue packet.
    /// </summary>
    /// <param name="clarification">The answered clarification artifact to apply.</param>
    /// <param name="queueState">The current queue state snapshot.</param>
    /// <param name="by">Who is performing the action.</param>
    /// <param name="ts">Timestamp for the events.</param>
    /// <param name="regeneratePacket">
    /// Optional function to regenerate the issue packet after clarification apply.
    /// If provided, the regenerated packet is included in the result.
    /// </param>
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

        var resumeResult = QueueManager.ResolveClarification(
            queueState, clarification.ExecutionUnit, by, ts);

        var events = new List<RunEvent> { applyEvent, resumeResult.Event };

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
            UpdatedQueueState = resumeResult.UpdatedState,
            Events = events.ToArray(),
            RegeneratedPacket = packet
        };
    }

    /// <summary>
    /// Applies multiple answered clarifications for the same execution unit in batch.
    /// All clarifications are advanced to applied, and the queue item is resumed once.
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

        foreach (var clarification in answeredClarifications)
        {
            ValidateAnswered(clarification);

            if (!string.Equals(clarification.ExecutionUnit, executionUnit, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"All clarifications in a batch must share the same execution unit. " +
                    $"Expected '{executionUnit}' but found '{clarification.ExecutionUnit}'.");
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

        var resumeResult = QueueManager.ResolveClarification(
            queueState, executionUnit, by, ts);

        events.Add(resumeResult.Event);

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
            UpdatedQueueState = resumeResult.UpdatedState,
            Events = events.ToArray(),
            RegeneratedPacket = packet
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
