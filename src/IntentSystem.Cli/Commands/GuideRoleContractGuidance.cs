using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G672: maps an invoking logical role to the installed operating guide that
/// governs that role. Roles without an installed contract deliberately resolve
/// to no pointer so generic onboarding does not invent a new instruction.
/// </summary>
internal static class GuideRoleContractGuidance
{
    public const string MeasuredIncident =
        "Measured incident record — attributed to operator-filed feedback in issue #1441 sections D/B-1 for remote-herdr (48 units): over days, a design seat had not read its own `guide design-thread` contract; a parallel detector violated the rule; supervision ran with an undeclared bound, the default interval, no event mode, and a session-scoped nohup process that died twice unnoticed; seven findings were mis-filed. This records field evidence only and does not settle the substantive B-1 question.";

    public static GuideRoleContractPointer? Resolve(string? role)
    {
        return Normalize(role) switch
        {
            LogicalRoleNormalizer.Architect => Create(
                LogicalRoleNormalizer.Architect,
                "intent-cli guide design-thread",
                "FIRST — before acting on the rest of this output, read your role's operating guide (`intent-cli guide design-thread`) if you have not read it this session. Do not force a reread on every wake; re-read after a CLI-version or session-layer configuration change."),
            LogicalRoleNormalizer.Orchestrator => Create(
                LogicalRoleNormalizer.Orchestrator,
                "intent-cli guide orchestrator-thread",
                "FIRST — before acting on the rest of this output, read your role's operating guide (`intent-cli guide orchestrator-thread`) if you have not read it this session. Do not force a reread on every wake; re-read after a CLI-version or session-layer configuration change."),
            LogicalRoleNormalizer.Builder => Create(
                LogicalRoleNormalizer.Builder,
                "intent-cli guide worker issue-to-pr",
                "FIRST — before acting on the rest of this output, read your role's operating guide (`intent-cli guide worker issue-to-pr`) if you have not read it this session. Do not force a reread on every wake; re-read after a CLI-version or session-layer configuration change."),
            LogicalRoleNormalizer.Reviewer => Create(
                LogicalRoleNormalizer.Reviewer,
                "intent-cli guide review",
                "FIRST — before acting on the rest of this output, read your role's operating guide (`intent-cli guide review`) if you have not read it this session. Do not force a reread on every wake; re-read after a CLI-version or session-layer configuration change."),
            LogicalRoleNormalizer.Steward => Create(
                LogicalRoleNormalizer.Steward,
                GuideStewardThreadCommand.CommandName,
                "FIRST — before acting on the rest of this output, read your role's operating guide (`intent-cli guide steward-thread`) if you have not read it this session. The Steward relays evidence and hands judgment to the architect or reviewer; it does not decide."),
            _ => null,
        };
    }

    public static string? Normalize(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        var candidate = role.Trim().ToLowerInvariant();
        if (LogicalRoleNormalizer.TryNormalize(candidate, out var canonical, out _)
            && canonical is not null)
        {
            // G797: this is a vocabulary normalization, not a route
            // projection. Canonical role identifiers stay canonical in
            // rendered guidance; the legacy spellings accepted by
            // LogicalRoleNormalizer are inputs only. The installed guide
            // route names remain explicit in Resolve above.
            return canonical;
        }

        return candidate switch
        {
            // Keep the older guide-facing role categories accepted while
            // projecting them into the shared canonical vocabulary.
            "child-implementation" or "worker" => LogicalRoleNormalizer.Builder,
            "review-runtime" => LogicalRoleNormalizer.Reviewer,
            var normalized => normalized,
        };
    }

    private static GuideRoleContractPointer Create(string role, string guide, string instruction) => new()
    {
        Role = role,
        Guide = guide,
        Instruction = instruction,
    };
}

internal sealed record GuideRoleContractPointer
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("guide")]
    public required string Guide { get; init; }

    [JsonPropertyName("instruction")]
    public required string Instruction { get; init; }
}
