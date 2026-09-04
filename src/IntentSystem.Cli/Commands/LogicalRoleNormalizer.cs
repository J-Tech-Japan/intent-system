namespace IntentSystem.Cli.Commands;

/// <summary>
/// Canonical logical-role vocabulary shared by every role-scoped closeout
/// surface.  The wire vocabulary is deliberately independent from runtime
/// and model names: a role describes a responsibility, while a runtime (for
/// example <c>opencode</c>) describes the participant that carries it.
/// </summary>
internal static class LogicalRoleNormalizer
{
    public const string Architect = "architect";
    public const string Orchestrator = "orchestrator";
    public const string Builder = "builder";
    public const string Reviewer = "reviewer";
    public const string Steward = "steward";

    public static IReadOnlyList<string> CanonicalRoles { get; } =
    [
        Architect,
        Orchestrator,
        Builder,
        Reviewer,
        Steward,
    ];

    /// <summary>
    /// The only legacy spellings accepted by the G795 vocabulary layer.  Keep
    /// this table here, rather than in a command, so persistence, reads and
    /// all three automation commands cannot drift apart.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Aliases { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["design"] = Architect,
            ["orchestration"] = Orchestrator,
            ["implementation"] = Builder,
            ["review"] = Reviewer,
        };

    public static IReadOnlyList<string> AcceptedRoles { get; } =
        CanonicalRoles
            .Concat(Aliases.Keys)
            .ToArray();

    public static bool TryNormalize(string? value, out string? canonical, out string error)
    {
        canonical = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = BuildUnknownRoleMessage(value);
            return false;
        }

        var candidate = value.Trim().ToLowerInvariant();
        if (CanonicalRoles.Contains(candidate, StringComparer.Ordinal))
        {
            canonical = candidate;
            error = string.Empty;
            return true;
        }

        if (Aliases.TryGetValue(candidate, out var aliasTarget))
        {
            canonical = aliasTarget;
            error = string.Empty;
            return true;
        }

        error = BuildUnknownRoleMessage(value);
        return false;
    }

    /// <summary>
    /// Resolve a value for a newly-written role-bearing field. Known canonical
    /// names and aliases are projected to the canonical vocabulary. Unknown
    /// values are intentionally not treated as roles; callers receive the
    /// trimmed legacy value so compatibility surfaces can preserve it and
    /// report it as legacy. A missing value receives the supplied canonical
    /// fallback (for example <c>builder</c> or <c>reviewer</c>). Existing
    /// queue-state values are read as-is so legacy records remain displayable
    /// and round-trippable.
    /// </summary>
    public static string NormalizeForWrite(string? value, string fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);

        if (TryNormalize(fallback, out var canonicalFallback, out var fallbackError)
            && canonicalFallback is not null)
        {
            if (TryNormalize(value, out var canonical, out _)
                && canonical is not null)
            {
                return canonical;
            }

            return string.IsNullOrWhiteSpace(value) ? canonicalFallback : value.Trim();
        }

        throw new ArgumentException(fallbackError, nameof(fallback));
    }

    /// <summary>
    /// Resolve a configuration value while retaining an unknown legacy value
    /// for diagnostics. This is the compatibility path for vendor/action
    /// values in the historical <c>[roles]</c> section; it never invents a
    /// second role vocabulary.
    /// </summary>
    public static string NormalizeOrPreserveLegacy(string? value, string fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);

        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return TryNormalize(value, out var canonical, out _)
            && canonical is not null
            ? canonical
            : value.Trim();
    }

    public static string BuildUnknownRoleMessage(string? value) =>
        $"role '{value ?? ""}' is unknown; accepted canonical roles are "
        + $"{FormatRoles(CanonicalRoles)}; accepted aliases are {FormatAliases()}.";

    public static string FormatAcceptedRoles() => FormatRoles(AcceptedRoles);

    public static string FormatCanonicalRoles() => FormatRoles(CanonicalRoles);

    public static string FormatAliases() =>
        string.Join(", ", Aliases
            .Select(entry => $"'{entry.Key}'→'{entry.Value}'"));

    private static string FormatRoles(IEnumerable<string> roles) =>
        string.Join(", ", roles.Select(role => $"'{role}'"));
}
