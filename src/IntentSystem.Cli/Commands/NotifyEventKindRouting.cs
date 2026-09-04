using System.Collections.ObjectModel;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// The event-kind routing contract introduced by G796.  A logical seat is a
/// responsibility, not a destination for every message: routine events may
/// be absorbed by the Steward while judgement stays on a specialist seat.
/// This type is deliberately small and transport-neutral so delegate, report,
/// escalation, and reader-event paths all use the same decision.
/// </summary>
internal static class NotifyEventKindRouting
{
    public const string Completion = "completion";
    public const string Transition = "transition";
    public const string Acknowledgement = "acknowledgement";
    public const string Escalation = "escalation";
    public const string Question = "question";
    public const string Blocked = "blocked";

    public static IReadOnlyList<string> SupportedKinds { get; } =
    [
        Completion,
        Transition,
        Acknowledgement,
        Escalation,
        Question,
        Blocked,
    ];

    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["acknowledgment"] = Acknowledgement,
        });

    public static bool IsRoutine(string eventKind) =>
        eventKind is Completion or Transition or Acknowledgement;

    public static bool IsJudgement(string eventKind) =>
        eventKind is Escalation or Question or Blocked;

    public static bool TryNormalize(string? value, out string? normalized, out string error)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "event kind is required when supplied.";
            return false;
        }

        var candidate = value.Trim().ToLowerInvariant();
        if (SupportedKinds.Contains(candidate, StringComparer.Ordinal))
        {
            normalized = candidate;
            error = string.Empty;
            return true;
        }

        if (Aliases.TryGetValue(candidate, out var alias))
        {
            normalized = alias;
            error = string.Empty;
            return true;
        }

        error = $"event kind '{value}' is unknown; accepted kinds are {string.Join(", ", SupportedKinds)}.";
        return false;
    }

    /// <summary>
    /// Maps the existing notify operations to their event-kind vocabulary.
    /// The optional explicit value is useful for transition and acknowledgement
    /// events, which do not have a dedicated command operation.
    /// </summary>
    public static string ResolveForOperation(string operation, string? status, string? explicitKind)
    {
        if (explicitKind is not null
            && TryNormalize(explicitKind, out var normalized, out _)
            && normalized is not null)
        {
            return normalized;
        }

        if (string.Equals(operation, "escalate", StringComparison.Ordinal))
        {
            return Escalation;
        }

        if (string.Equals(operation, "delegate", StringComparison.Ordinal))
        {
            return Question;
        }

        if (status is not null
            && string.Equals(status, "completed", StringComparison.Ordinal))
        {
            return Completion;
        }

        if (status is not null
            && string.Equals(status, "blocked", StringComparison.Ordinal))
        {
            return Blocked;
        }

        return Question;
    }

    /// <summary>
    /// Returns the effective logical destination.  With no recorded Steward,
    /// <paramref name="currentTarget"/> is returned unchanged, preserving the
    /// pre-G796 route.  For a question explicitly aimed at review, the
    /// Reviewer remains the specialist; all other judgement kinds use the
    /// Architect.  A missing target is filled with the historical design alias
    /// only for the escalation path.
    /// </summary>
    public static string? ResolveTarget(
        string eventKind,
        string? currentTarget,
        bool stewardRecorded,
        string? fallbackTarget = null)
    {
        if (!stewardRecorded)
        {
            return currentTarget ?? fallbackTarget;
        }

        if (IsRoutine(eventKind))
        {
            return LogicalRoleNormalizer.Steward;
        }

        if (eventKind == Question
            && LogicalRoleNormalizer.TryNormalize(currentTarget, out var normalized, out _)
            && string.Equals(normalized, LogicalRoleNormalizer.Reviewer, StringComparison.Ordinal))
        {
            return LogicalRoleNormalizer.Reviewer;
        }

        return LogicalRoleNormalizer.Architect;
    }

    public static bool HasRecordedSteward(NotifyTeamTopology? topology)
    {
        if (topology is null)
        {
            return false;
        }

        return NotifyRoleTopologyStore.ResolveRecordedRole(
            topology,
            LogicalRoleNormalizer.Steward).Resolved;
    }
}
