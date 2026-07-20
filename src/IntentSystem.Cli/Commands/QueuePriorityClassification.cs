namespace IntentSystem.Cli.Commands;

/// <summary>
/// G543: the single source of truth for the documented queue-item
/// <c>priority</c> enum (<c>high|normal|low</c>, introduced by G537) and for
/// how EVERY value the candidate selector can encounter — including
/// legacy/out-of-enum values such as the field-observed <c>"medium"</c> (host
/// queue-state, 2026-07-20: high 1405, medium 59, normal 3) — ranks for
/// ordering. Both <see cref="IntentNextSliceCommand"/> (candidate ordering)
/// and <see cref="QueuePriorityDriftCommand"/> (the read-only drift report)
/// use this so the two surfaces can never disagree about which values are
/// "documented" or how an unrecognized value orders. The documented enum
/// itself is NOT expanded by this type — <c>medium</c> is still not a valid
/// <c>--priority</c> argument to <c>queue reprioritize</c>; this only defines
/// how existing out-of-enum data behaves.
/// </summary>
internal static class QueuePriorityClassification
{
    public const string High = "high";
    public const string Normal = "normal";
    public const string Low = "low";

    /// <summary>The three documented enum members, in canonical high-to-low order.</summary>
    public static readonly IReadOnlyList<string> DocumentedValues = new[] { High, Normal, Low };

    /// <summary>Normalizes a raw priority value for comparison: trims and lowercases. Never null — blank/null input normalizes to an empty string.</summary>
    public static string Normalize(string? priority) => priority?.Trim().ToLowerInvariant() ?? string.Empty;

    /// <summary>True when the normalized value is exactly one of the three documented enum members.</summary>
    public static bool IsDocumented(string? priority) => DocumentedValues.Contains(Normalize(priority), StringComparer.Ordinal);

    /// <summary>
    /// Ranks a priority value for candidate ordering — lower rank sorts
    /// earlier. <c>"high"</c> ranks 0, <c>"low"</c> ranks 2. EVERY other
    /// value — missing, empty, or any out-of-enum/legacy string such as
    /// <c>"medium"</c> — ranks 1, the same as an explicit <c>"normal"</c>.
    /// This is deterministic and total: there is no priority value for
    /// which this function's ordering position is undefined.
    /// </summary>
    public static int Rank(string? priority) => Normalize(priority) switch
    {
        High => 0,
        Low => 2,
        _ => 1,
    };
}
