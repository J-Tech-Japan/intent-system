namespace IntentSystem.Cli.Commands;

/// <summary>
/// Shared display vocabulary for the role-bearing guide surfaces.  Logical
/// role identity still comes from <see cref="LogicalRoleNormalizer"/>; this
/// type only projects that identity into the words a person reads and into
/// the route-compatible identifier used by a paste-ready command.
/// </summary>
internal static class GuideRoleVocabulary
{
    public const string Architect = "Architect";
    public const string Orchestrator = "Orchestrator";
    public const string Builder = "Builder";
    public const string Reviewer = "Reviewer";
    public const string Steward = "Steward";

    public static IReadOnlyList<string> DisplayNames { get; } =
    [
        Architect,
        Orchestrator,
        Builder,
        Reviewer,
        Steward,
    ];

    /// <summary>
    /// Return the human-facing name for a canonical or accepted legacy role.
    /// The normalizer remains the only alias table.
    /// </summary>
    public static string Display(string? role)
    {
        if (LogicalRoleNormalizer.TryNormalize(role, out var canonical, out _)
            && canonical is not null)
        {
            return canonical switch
            {
                LogicalRoleNormalizer.Architect => Architect,
                LogicalRoleNormalizer.Orchestrator => Orchestrator,
                LogicalRoleNormalizer.Builder => Builder,
                LogicalRoleNormalizer.Reviewer => Reviewer,
                LogicalRoleNormalizer.Steward => Steward,
                _ => role!.Trim(),
            };
        }

        return string.IsNullOrWhiteSpace(role) ? "unresolved role" : role.Trim();
    }

    /// <summary>
    /// Resolve the identifier emitted in guide commands.  Route projections
    /// are compatibility surfaces, so the existing accepted spelling is kept
    /// (notably <c>orchestration</c> for the Orchestrator seat).
    /// </summary>
    public static string Identifier(string role)
    {
        if (!LogicalRoleNormalizer.TryNormalize(role, out var canonical, out _)
            || canonical is null)
        {
            return role;
        }

        return GuideRoleContractGuidance.Normalize(canonical) ?? canonical;
    }

    public static string CanonicalNamesSentence =>
        $"Canonical role names: {string.Join(", ", DisplayNames)}. Role identity is logical, not a runtime/vendor.";

    public static string OrchestratorIdentifierSentence =>
        $"The {Orchestrator} guide route remains `guide orchestrator-thread`; its working role identifier is "
        + $"`{Identifier(LogicalRoleNormalizer.Orchestrator)}`. Use that identifier in role-bearing commands; "
        + "the retired `orchestrator` recording spelling belongs only in the glossary.";

    public static string StewardBoundarySentence =>
        $"{Steward} is a transmission boundary: it relays weight without making design judgement (that belongs to "
        + $"{Architect}) or review judgement (that belongs to {Reviewer}), and it answers neither itself.";

    public static string RetiredNameGlossarySentence =>
        "Retired role names remain readable as keys in archived packets, closeout records, runs, and consumer issues; "
        + "they are not new runtime identities.";

    public static string RenderMarkdownBlock()
    {
        return $"## Canonical role vocabulary (G797)\n\n"
            + $"- {CanonicalNamesSentence}\n"
            + $"- {OrchestratorIdentifierSentence}\n"
            + $"- {StewardBoundarySentence}\n"
            + $"- {RetiredNameGlossarySentence}";
    }
}
