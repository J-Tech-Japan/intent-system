using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal sealed record SoloConductorClaimReleaseCompletion
{
    [JsonPropertyName("decision")]
    public required string Decision { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("domain")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Domain { get; init; }

    [JsonPropertyName("team")]
    public required string Team { get; init; }

    [JsonPropertyName("target_repo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetRepo { get; init; }

    [JsonPropertyName("linked_pr")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LinkedPr { get; init; }

    [JsonPropertyName("canonical_snapshot_oid")]
    public required string CanonicalSnapshotOid { get; init; }

    [JsonPropertyName("canonical_target_ref")]
    public required string CanonicalTargetRef { get; init; }

    [JsonPropertyName("applicability")]
    public required SoloConductorClaimReleaseApplicability Applicability { get; init; }

    [JsonPropertyName("duties")]
    public required IReadOnlyList<SoloConductorClaimReleaseDuty> Duties { get; init; }
}

internal sealed record SoloConductorClaimReleaseApplicability
{
    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("cause")]
    public required string Cause { get; init; }

    [JsonPropertyName("detail")]
    public required string Detail { get; init; }

    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode { get; init; }

    [JsonPropertyName("mode_source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ModeSource { get; init; }

    [JsonPropertyName("evidence_paths")]
    public required IReadOnlyList<string> EvidencePaths { get; init; }
}

internal sealed record SoloConductorClaimReleaseDuty
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("cause")]
    public required string Cause { get; init; }

    [JsonPropertyName("detail")]
    public required string Detail { get; init; }

    [JsonPropertyName("declared_facets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? DeclaredFacets { get; init; }

    [JsonPropertyName("declared_targets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? DeclaredTargets { get; init; }

    [JsonPropertyName("declared_routes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SoloConductorClaimReleaseRoute>? DeclaredRoutes { get; init; }

    [JsonPropertyName("evidence")]
    public required IReadOnlyList<SoloConductorClaimReleaseEvidence> Evidence { get; init; }

    [JsonPropertyName("recovery_commands")]
    public required IReadOnlyList<string> RecoveryCommands { get; init; }

    [JsonPropertyName("publication_step")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PublicationStep { get; init; }

    [JsonPropertyName("repair_unavailable_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RepairUnavailableReason { get; init; }
}

internal sealed record SoloConductorClaimReleaseRoute
{
    [JsonPropertyName("guide_surface")]
    public required string GuideSurface { get; init; }

    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("target_surface")]
    public required string TargetSurface { get; init; }
}

internal sealed record SoloConductorClaimReleaseEvidence
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("artifact_kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ArtifactKind { get; init; }

    [JsonPropertyName("execution_unit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExecutionUnit { get; init; }

    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; init; }

    [JsonPropertyName("host_commit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HostCommit { get; init; }

    [JsonPropertyName("recorded_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? RecordedAt { get; init; }

    [JsonPropertyName("targets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Targets { get; init; }

    [JsonPropertyName("guide_surfaces")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? GuideSurfaces { get; init; }

    [JsonPropertyName("routing_roles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? RoutingRoles { get; init; }

    [JsonPropertyName("event")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Event { get; init; }

    [JsonPropertyName("repo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Repo { get; init; }

    [JsonPropertyName("pr")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Pr { get; init; }

    [JsonPropertyName("queue_state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? QueueState { get; init; }
}

internal sealed record SoloConductorClaimReleaseGateResult(
    bool IsApplicable,
    SoloConductorClaimReleaseCompletion? Completion)
{
    public bool IsRefused => Completion?.Decision == "refused";

    [JsonIgnore]
    public string? RunLogRelativePath { get; init; }

    public static SoloConductorClaimReleaseGateResult NotApplicable() => new(false, null);
}
