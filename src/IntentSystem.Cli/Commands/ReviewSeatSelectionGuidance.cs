using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G789: renders the static review-seat rule on every guide surface. The
/// resident-specific record fields are deliberately the sole input for the
/// optional topology resolution: herdr seats contribute <c>kind</c>, while
/// external seats contribute <c>frontend</c>. Role names, models, and
/// co-location never infer a kind.
/// </summary>
internal sealed record ReviewSeatSelectionGuidance
{
    [JsonPropertyName("mixed_kind_rule")]
    public required string MixedKindRule { get; init; }

    [JsonPropertyName("single_kind_allowance")]
    public required string SingleKindAllowance { get; init; }

    [JsonPropertyName("recorded_fields_decide")]
    public required string RecordedFieldsDecide { get; init; }

    [JsonPropertyName("topology_team")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TopologyTeam { get; init; }

    [JsonPropertyName("recorded_seat_kinds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? RecordedSeatKinds { get; init; }

    [JsonPropertyName("design_seat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DesignSeat { get; init; }

    [JsonPropertyName("review_seat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReviewSeat { get; init; }

    [JsonPropertyName("selected_review_seat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SelectedReviewSeat { get; init; }

    [JsonPropertyName("selection")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Selection { get; init; }
}

internal static class ReviewSeatSelectionGuidanceResolver
{
    private const string DesignRole = "design";
    private const string ReviewRole = "review";

    public static ReviewSeatSelectionGuidance? Resolve(
        string routingRoot,
        string? domain,
        string? team)
    {
        var topology = ResolveTopology(routingRoot, domain, team);
        return topology is null ? null : Create(topology);
    }

    public static ReviewSeatSelectionGuidance CreateStatic() => new()
    {
        MixedKindRule = "When recorded seat kinds differ, the review seat with a recorded kind/frontend different from design reviews design output as well as PRs.",
        SingleKindAllowance = "When all recorded seat kinds are the same, design↔orchestration cross-review is acceptable.",
        RecordedFieldsDecide = "Only recorded topology fields decide: `kind` for a herdr seat and `frontend` for an external seat. Do not infer a kind from role name, model, residence, or co-location.",
    };

    public static NotifyTeamTopology? ResolveTopology(
        string routingRoot,
        string? domain,
        string? team)
    {
        if (string.IsNullOrWhiteSpace(routingRoot)
            || string.IsNullOrWhiteSpace(domain)
            || string.IsNullOrWhiteSpace(team))
        {
            return null;
        }

        var resolution = NotifyRoleTopologyStore.Resolve(routingRoot, domain.Trim(), team.Trim());
        return resolution.Resolved ? resolution.Topology : null;
    }

    /// <summary>
    /// Guide review intentionally has no team argument. It can render the
    /// topology-specific rule only when exactly one recorded team exists for
    /// the selected domain; otherwise it leaves the existing standing policy
    /// unchanged rather than guessing a team.
    /// </summary>
    public static ReviewSeatSelectionGuidance? ResolveUniqueTeam(string routingRoot, string? domain)
    {
        if (string.IsNullOrWhiteSpace(routingRoot) || string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        var directory = Path.Combine(
            routingRoot,
            NotifyRoleTopologyStore.TopologyDirectoryRelativePath.Replace('/', Path.DirectorySeparatorChar),
            domain.Trim());
        string[] candidates;
        try
        {
            if (!Directory.Exists(directory))
            {
                return null;
            }

            candidates = Directory
                .EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A directory that cannot be inspected is just as unsuitable for
            // topology-specific selection as a malformed record. Callers keep
            // the static rule and never guess a team.
            return null;
        }
        if (candidates.Length != 1)
        {
            return null;
        }

        var team = Path.GetFileNameWithoutExtension(candidates[0]);
        return Resolve(routingRoot, domain, team);
    }

    public static ReviewSeatSelectionGuidance? Create(NotifyTeamTopology topology)
    {
        if (!TryGetRecordedKind(topology, DesignRole, out var design)
            || !TryGetRecordedKind(topology, ReviewRole, out var review))
        {
            return null;
        }

        var seats = topology.Roles
            .OrderBy(pair => SeatOrder(pair.Key))
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => TryGetRecordedKind(topology, pair.Key, out var seat) ? seat.Display : null)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
        if (seats.Length == 0)
        {
            return null;
        }

        var recordedKinds = topology.Roles
            .Select(pair => TryGetRecordedKind(topology, pair.Key, out var seat) ? seat.Identity : null)
            .Where(value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var mixedKinds = recordedKinds.Length > 1;
        var reviewDiffersFromDesign = !string.Equals(
            design.Identity,
            review.Identity,
            StringComparison.OrdinalIgnoreCase);
        var selection = mixedKinds
            ? reviewDiffersFromDesign
                ? "Mixed recorded kinds: review has a different recorded kind/frontend from design, so review reviews design output and PRs."
                : "Mixed recorded kinds: review does not have a different recorded kind/frontend from design; no distinct-kind review seat is resolved for design output. Select a recorded different-kind review seat before it reviews design output, while review still reviews PRs."
            : "Single recorded kind: design↔orchestration cross-review is acceptable; review continues to review PRs.";

        return CreateStatic() with
        {
            TopologyTeam = topology.Team,
            RecordedSeatKinds = seats,
            DesignSeat = design.Display,
            ReviewSeat = review.Display,
            SelectedReviewSeat = mixedKinds && !reviewDiffersFromDesign ? null : ReviewRole,
            Selection = selection,
        };
    }

    public static bool IsExternalDesign(NotifyTeamTopology topology) =>
        topology.Roles.TryGetValue(DesignRole, out var design)
        && string.Equals(design.Resident, NotifyRecordedRole.ExternalResident, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetRecordedKind(
        NotifyTeamTopology topology,
        string role,
        out RecordedSeatKind seat)
    {
        seat = default;
        if (!topology.Roles.TryGetValue(role, out var recorded))
        {
            return false;
        }

        var value = string.Equals(recorded.Resident, NotifyRecordedRole.HerdrResident, StringComparison.OrdinalIgnoreCase)
            ? recorded.Kind
            : string.Equals(recorded.Resident, NotifyRecordedRole.ExternalResident, StringComparison.OrdinalIgnoreCase)
                ? recorded.Frontend
                : null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var field = string.Equals(recorded.Resident, NotifyRecordedRole.HerdrResident, StringComparison.OrdinalIgnoreCase)
            ? "kind"
            : "frontend";
        var normalized = value.Trim();
        seat = new RecordedSeatKind(normalized, $"{role} ({field}:{normalized})");
        return true;
    }

    private static int SeatOrder(string role) => role switch
    {
        DesignRole => 0,
        "implementation" => 1,
        "orchestration" => 2,
        ReviewRole => 3,
        _ => 4,
    };

    private readonly record struct RecordedSeatKind(string Identity, string Display);
}
