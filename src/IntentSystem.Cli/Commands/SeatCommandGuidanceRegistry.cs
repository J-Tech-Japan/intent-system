using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G696: the installed, read-only registry for seat command-form guidance.
/// The registry describes command shapes and measured alternatives; it never
/// changes an agent's settings, evaluates an operator allowlist, or approves
/// a command. A new registry version is an intentional machine-surface change.
/// </summary>
internal static class SeatCommandGuidanceRegistry
{
    public const string RegistryVersion = "1";
    public const string GuideSurface = "guide seat-commands";
    public const string ReviewRouteRole = "review";
    public const string ReviewRouteTarget = "per-kind sanctioned command forms and alternatives";

    public static readonly IReadOnlyList<string> SupportedKinds = ["claude", "codex"];

    /// <summary>
    /// G696/G645: keep the packet-declared role-facing route in the same
    /// structured registry as the surface it names. A role that is not the
    /// review seat receives an explicit absent declaration rather than an
    /// invented pointer.
    /// </summary>
    public static GuideReachabilityDeclaration ReachabilityForRole(string? role) =>
        string.Equals(GuideRoleContractGuidance.Normalize(role), LogicalRoleNormalizer.Reviewer, StringComparison.Ordinal)
            ? ReviewReachability()
            : GuideReachabilityDeclaration.Absent;

    public static GuideReachabilityDeclaration ReviewReachability() => new()
    {
        IsDeclared = true,
        NoRoleFacingSurface = false,
        Routes =
        [
            new GuideReachabilityRoute
            {
                GuideSurface = GuideSurface,
                Role = ReviewRouteRole,
                TargetSurface = ReviewRouteTarget,
            },
        ],
    };

    public static SeatCommandGuidance ForKind(string kind) => kind switch
    {
        "claude" => Build(
            kind,
            "Keep each review-seat action in the literal command shape the operator supplied for the seat; do not wrap it in a new shell construct.",
            "The Claude seat receives the same observation-only command-form rules as every other seat. The registry is guidance, not a permission grant."),
        "codex" => Build(
            kind,
            "Keep each implementation or review action in the literal command shape the operator supplied for the seat; do not wrap it in a new shell construct.",
            "The Codex seat receives the same observation-only command-form rules as every other seat. The registry is guidance, not a permission grant."),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Supported seat kinds are claude and codex."),
    };

    private static SeatCommandGuidance Build(string kind, string kindForm, string kindBoundary) => new()
    {
        RegistryVersion = RegistryVersion,
        Kind = kind,
        SanctionedForms =
        [
            new SeatCommandForm
            {
                Name = "literal single-command form",
                Command = "Use the exact allowlisted command prefix and argument order supplied by the operator.",
                Guidance = kindForm,
            },
            new SeatCommandForm
            {
                Name = "review submission",
                Command = "gh pr review --body-file <review.md> --comment",
                Guidance = "Submit review text through the review command and a body file; do not substitute a comment API call.",
            },
            new SeatCommandForm
            {
                Name = "head verification",
                Command = "git fetch origin <branch>\ngit diff --check <base>...HEAD",
                Guidance = "Fetch and inspect the requested head without changing the checkout.",
            },
            new SeatCommandForm
            {
                Name = "build evidence",
                Command = "Inspect CI for the exact PR head SHA.",
                Guidance = "Use the authoritative exact-head CI result when local package-manager builds are not sanctioned.",
            },
        ],
        Breakers =
        [
            new SeatCommandBreaker
            {
                Name = "quoted arguments",
                Form = "Adding quotes around arguments can change the literal argv prefix.",
                Guidance = "Keep the operator-approved argument spelling; a quoted variant is a different command form for prefix matching.",
            },
            new SeatCommandBreaker
            {
                Name = "flags before the path",
                Form = "Moving flags before a path changes the command prefix.",
                Guidance = "Keep flags and paths in the registered order instead of rearranging them for convenience.",
            },
            new SeatCommandBreaker
            {
                Name = "differently-prefixed commands chained with &&",
                Form = "A && chain combines two commands whose prefixes may be allowlisted separately.",
                Guidance = "Run each sanctioned step separately so the operator can authorize each literal prefix.",
            },
            new SeatCommandBreaker
            {
                Name = "$VAR expansion",
                Form = "Shell variable expansion hides the literal argument from a prefix matcher.",
                Guidance = "Use the explicit operator-approved value when the command form must be matched.",
            },
            new SeatCommandBreaker
            {
                Name = "for-loops",
                Form = "A for-loop wraps commands in a different shell form.",
                Guidance = "Do not replace a sanctioned single command with a loop; ask for a separately sanctioned form if repetition is needed.",
            },
        ],
        Alternatives =
        [
            new SeatCommandAlternative
            {
                DeniedForm = "gh pr comment",
                SanctionedForm = "gh pr review --body-file <review.md> --comment",
                Commands = ["gh pr review --body-file <review.md> --comment"],
                Guidance = "Use the review submission surface for review text.",
            },
            new SeatCommandAlternative
            {
                DeniedForm = "git checkout <branch>",
                SanctionedForm = "fetch + diff verification",
                Commands = ["git fetch origin <branch>", "git diff --check <base>...HEAD"],
                Guidance = "Verify the remote and exact head without changing the working checkout.",
            },
            new SeatCommandAlternative
            {
                DeniedForm = "local npm or package-manager build",
                SanctionedForm = "exact-head CI evidence",
                Commands = ["Inspect CI for the exact PR head SHA"],
                Guidance = "Use CI evidence tied to the exact head instead of running an unsanctioned local package-manager build.",
            },
        ],
        AuthorityBoundary = kindBoundary,
    };
}

internal sealed record SeatCommandGuidance
{
    [JsonPropertyName("registry_version")]
    public required string RegistryVersion { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("sanctioned_forms")]
    public required IReadOnlyList<SeatCommandForm> SanctionedForms { get; init; }

    [JsonPropertyName("breakers")]
    public required IReadOnlyList<SeatCommandBreaker> Breakers { get; init; }

    [JsonPropertyName("alternatives")]
    public required IReadOnlyList<SeatCommandAlternative> Alternatives { get; init; }

    [JsonPropertyName("authority_boundary")]
    public required string AuthorityBoundary { get; init; }
}

internal sealed record SeatCommandForm
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("guidance")]
    public required string Guidance { get; init; }
}

internal sealed record SeatCommandBreaker
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("form")]
    public required string Form { get; init; }

    [JsonPropertyName("guidance")]
    public required string Guidance { get; init; }
}

internal sealed record SeatCommandAlternative
{
    [JsonPropertyName("denied_form")]
    public required string DeniedForm { get; init; }

    [JsonPropertyName("sanctioned_form")]
    public required string SanctionedForm { get; init; }

    [JsonPropertyName("commands")]
    public required IReadOnlyList<string> Commands { get; init; }

    [JsonPropertyName("guidance")]
    public required string Guidance { get; init; }
}
