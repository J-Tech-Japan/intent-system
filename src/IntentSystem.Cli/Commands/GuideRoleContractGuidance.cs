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
            "design" => Create(
                "design",
                "intent-cli guide design-thread",
                "FIRST — before acting on the rest of this output, read your role's operating guide (`intent-cli guide design-thread`) if you have not read it this session. Do not force a reread on every wake; re-read after a CLI-version or session-layer configuration change."),
            "orchestration" => Create(
                "orchestration",
                "intent-cli guide orchestrator-thread",
                "FIRST — before acting on the rest of this output, read your role's operating guide (`intent-cli guide orchestrator-thread`) if you have not read it this session. Do not force a reread on every wake; re-read after a CLI-version or session-layer configuration change."),
            "implementation" => Create(
                "implementation",
                "intent-cli guide worker issue-to-pr",
                "FIRST — before acting on the rest of this output, read your role's operating guide (`intent-cli guide worker issue-to-pr`) if you have not read it this session. Do not force a reread on every wake; re-read after a CLI-version or session-layer configuration change."),
            "review" => Create(
                "review",
                "intent-cli guide review",
                "FIRST — before acting on the rest of this output, read your role's operating guide (`intent-cli guide review`) if you have not read it this session. Do not force a reread on every wake; re-read after a CLI-version or session-layer configuration change."),
            _ => null,
        };
    }

    public static string? Normalize(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        return role.Trim().ToLowerInvariant() switch
        {
            "orchestrator" => "orchestration",
            "child-implementation" or "worker" => "implementation",
            "reviewer" or "review-runtime" => "review",
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
