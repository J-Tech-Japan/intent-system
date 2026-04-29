using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G187 nested-provider handoff artifact. Snake_case JSON shape that
/// <c>intent-cli safety nested-provider-handoff</c> writes. The
/// <c>NestedProviderLaunched</c> field is always literal <c>false</c> for the
/// first accepted artifact-only slice — there is no path that sets it to
/// <c>true</c>. The field exists so future tooling and tests can observe the
/// no-launch invariant.
/// </summary>
internal sealed record SafetyNestedProviderHandoffArtifact
{
    /// <summary>
    /// Stable prefix asserted by the no-launch tests. The full summary line
    /// substitutes the concrete artifact path.
    /// </summary>
    public const string NotStartedMessagePrefix = "Nested provider execution was not started";

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("intended_role")]
    public required string IntendedRole { get; init; }

    [JsonPropertyName("intended_action")]
    public required string IntendedAction { get; init; }

    [JsonPropertyName("target_repo")]
    public required string TargetRepo { get; init; }

    [JsonPropertyName("target_path")]
    public required string TargetPath { get; init; }

    [JsonPropertyName("packet_path")]
    public string? PacketPath { get; init; }

    [JsonPropertyName("review_context_path")]
    public string? ReviewContextPath { get; init; }

    [JsonPropertyName("requested_command")]
    public required string RequestedCommand { get; init; }

    [JsonPropertyName("generated_at_utc")]
    public required string GeneratedAtUtc { get; init; }

    [JsonPropertyName("nested_provider_launched")]
    public required bool NestedProviderLaunched { get; init; }

    [JsonPropertyName("artifact_path")]
    public required string ArtifactPath { get; init; }

    [JsonPropertyName("summary_line")]
    public required string SummaryLine { get; init; }
}
