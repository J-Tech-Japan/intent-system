using System.Text.RegularExpressions;

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
    /// Resolve the canonical identifier emitted in guide commands. Legacy
    /// spellings are accepted as input by the shared normalizer, but they are
    /// never projected back into new guidance. Route names remain separate
    /// compatibility surfaces owned by their individual guide commands.
    /// </summary>
    public static string Identifier(string role)
    {
        if (!LogicalRoleNormalizer.TryNormalize(role, out var canonical, out _)
            || canonical is null)
        {
            return role;
        }

        return canonical;
    }

    /// <summary>
    /// Project role-bearing command and structured-payload values at the
    /// rendering boundary.  The guide model still accepts historical aliases
    /// on input, but newly rendered commands must teach the canonical
    /// identifiers.  Deliberately limit the replacements to command flags and
    /// JSON fields so route names (for example <c>guide design-thread</c>) and
    /// prose/glossary history remain untouched.
    /// </summary>
    public static string ProjectRenderedRoleValues(string rendered)
    {
        ArgumentNullException.ThrowIfNull(rendered);

        var projected = rendered;
        foreach (var (alias, canonical) in LogicalRoleNormalizer.Aliases)
        {
            foreach (var flag in new[] { "--role", "--from", "--to", "--report-to", "--owner-role", "--owner" })
            {
                projected = projected.Replace($"{flag} {alias}", $"{flag} {canonical}", StringComparison.Ordinal);
            }

            // Structured guide payloads carry role identifiers in more than
            // the original command-envelope fields. Keep this list at the
            // one rendering boundary so every structured role value still
            // resolves through LogicalRoleNormalizer, while human-facing
            // herdr pane labels remain untouched.
            foreach (var property in new[]
            {
                "role", "from", "to", "report_to", "thread", "owner_role",
                "answerable_by", "decision_actor_role", "adjudication_target_role",
                "subject_role", "wake_target_role", "agent_role", "role_identifier",
                "logical_role", "review_role", "worker_role", "sender_role",
                "recipient_role", "destination_role",
            })
            {
                projected = projected.Replace($"\"{property}\":\"{alias}\"", $"\"{property}\":\"{canonical}\"", StringComparison.Ordinal);
                projected = projected.Replace($"\"{property}\": \"{alias}\"", $"\"{property}\": \"{canonical}\"", StringComparison.Ordinal);
                // Nested JSON templates are escaped by the outer guide JSON
                // serializer; project those values as well.
                projected = projected.Replace($"\\\"{property}\\\":\\\"{alias}\\\"", $"\\\"{property}\\\":\\\"{canonical}\\\"", StringComparison.Ordinal);
                projected = projected.Replace($"\\\"{property}\\\": \\\"{alias}\\\"", $"\\\"{property}\\\": \\\"{canonical}\\\"", StringComparison.Ordinal);
                // The outer guide JSON serializer may encode the nested
                // template quotes as literal \\u0022 sequences.
                projected = projected.Replace($"\\u0022{property}\\u0022:\\u0022{alias}\\u0022", $"\\u0022{property}\\u0022:\\u0022{canonical}\\u0022", StringComparison.Ordinal);
                projected = projected.Replace($"\\u0022{property}\\u0022: \\u0022{alias}\\u0022", $"\\u0022{property}\\u0022: \\u0022{canonical}\\u0022", StringComparison.Ordinal);
            }

            projected = projected.Replace($"\"destination_thread\":\"{alias}@", $"\"destination_thread\":\"{canonical}@", StringComparison.Ordinal);
            projected = projected.Replace($"\\\"destination_thread\\\":\\\"{alias}@", $"\\\"destination_thread\\\":\\\"{canonical}@", StringComparison.Ordinal);
            projected = projected.Replace($"\\u0022destination_thread\\u0022:\\u0022{alias}@", $"\\u0022destination_thread\\u0022:\\u0022{canonical}@", StringComparison.Ordinal);

            // Keep command option inventories canonical too (the accepted
            // aliases remain documented by the glossary, never re-emitted as
            // the value a new command should copy).
            projected = projected.Replace($"<{alias}|", $"<{canonical}|", StringComparison.Ordinal);
            projected = projected.Replace($"|{alias}>", $"|{canonical}>", StringComparison.Ordinal);
            projected = projected.Replace($"<{alias}>", $"<{canonical}>", StringComparison.Ordinal);

            // Session-layer templates carry the role as the positional
            // argument to agmsg join.  Canonicalize the two installed
            // placeholder forms without touching route names or prose.
            foreach (var teamPlaceholder in new[] { "__TEAM__", "<team>" })
            {
                projected = projected.Replace(
                    $"agmsg join.sh {teamPlaceholder} {alias} ",
                    $"agmsg join.sh {teamPlaceholder} {canonical} ",
                    StringComparison.Ordinal);
            }

            // Setup-ready output can carry a concrete team name instead of a
            // placeholder. Keep the positional role argument canonical in
            // that form as well, without touching route names or prose.
            projected = Regex.Replace(
                projected,
                $@"(?i)(agmsg\s+join\.sh\s+\S+\s+){Regex.Escape(alias)}(?=\s)",
                $"$1{canonical}",
                RegexOptions.CultureInvariant);
        }

        return projected;
    }

    public static string CanonicalNamesSentence =>
        $"Canonical role names: {string.Join(", ", DisplayNames)}. Role identity is logical, not a runtime/vendor.";

    public static string OrchestratorIdentifierSentence =>
        $"The {Orchestrator} guide route remains `guide orchestrator-thread`; its working role identifier is "
        + $"`{Identifier(LogicalRoleNormalizer.Orchestrator)}`. Use that identifier in role-bearing commands; "
        + "the retired `orchestration` recording spelling belongs only in the glossary.";

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
