using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G199 reviewed task packet publish bridge artifact. Snake_case JSON shape
/// emitted by <c>intent-cli tasking publish-reviewed-bridge</c> when a verified
/// G194 handoff bundle plus an operator-supplied G183-valid Markdown body plus
/// an explicit approval marker have all passed local checks. The artifact is
/// the "ready-for-real-publish" handoff that a future operator (or G184
/// <c>issue publish-reviewed</c>) can consume.
///
/// <para>
/// The artifact is local-only by contract:
/// <see cref="IsPublished"/> and <see cref="IsAutomationVisible"/> are ALWAYS
/// literal <c>false</c>; <see cref="ReviewedBridgeStatus"/> is ALWAYS the
/// literal value
/// <see cref="TaskingPublishReviewedBridgeConstants.LocalOnlyStatus"/>. The
/// command performs no GitHub network calls, applies no labels, does not
/// mutate queue/runs state, does not launch providers, and does not create
/// branches/worktrees.
/// </para>
/// </summary>
internal sealed record TaskingPublishReviewedBridgeArtifact
{
    [JsonPropertyName("source_bundle_path")]
    public required string SourceBundlePath { get; init; }

    [JsonPropertyName("source_bundle_sha256")]
    public required string SourceBundleSha256 { get; init; }

    [JsonPropertyName("source_body_path")]
    public required string SourceBodyPath { get; init; }

    [JsonPropertyName("source_body_sha256")]
    public required string SourceBodySha256 { get; init; }

    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("is_published")]
    public required bool IsPublished { get; init; }

    [JsonPropertyName("is_automation_visible")]
    public required bool IsAutomationVisible { get; init; }

    [JsonPropertyName("reviewed_bridge_status")]
    public required string ReviewedBridgeStatus { get; init; }

    [JsonPropertyName("approval_marker")]
    public required string ApprovalMarker { get; init; }

    [JsonPropertyName("approval_marker_kind")]
    public required string ApprovalMarkerKind { get; init; }

    [JsonPropertyName("verify_summary")]
    public required TaskingPublishReviewedBridgeVerifySummary VerifySummary { get; init; }

    [JsonPropertyName("body_contract_validation")]
    public required TaskingPublishReviewedBridgeBodyValidation BodyContractValidation { get; init; }

    [JsonPropertyName("generated_at_utc")]
    public required string GeneratedAtUtc { get; init; }

    [JsonPropertyName("artifact_path")]
    public required string ArtifactPath { get; init; }

    [JsonPropertyName("summary_line")]
    public required string SummaryLine { get; init; }
}

/// <summary>
/// G199 — minimal projection of the G196/G197 verify check list. Only carries
/// the boolean validity and the failed check ids, mirroring the dry-run
/// import projection.
/// </summary>
internal sealed record TaskingPublishReviewedBridgeVerifySummary
{
    [JsonPropertyName("valid")]
    public required bool Valid { get; init; }

    [JsonPropertyName("failed_check_ids")]
    public required IReadOnlyList<string> FailedCheckIds { get; init; }
}

/// <summary>
/// G199 — Child Issue Contract validation projection. Mirrors
/// <see cref="IssueValidateBodyResult"/> but only the fields the operator
/// needs from the artifact: validity, missing-heading list, related-links
/// invalid flag, and reason.
/// </summary>
internal sealed record TaskingPublishReviewedBridgeBodyValidation
{
    [JsonPropertyName("is_valid")]
    public required bool IsValid { get; init; }

    [JsonPropertyName("missing_headings")]
    public required IReadOnlyList<string> MissingHeadings { get; init; }

    [JsonPropertyName("related_links_invalid")]
    public required bool RelatedLinksInvalid { get; init; }

    [JsonPropertyName("related_links_reason")]
    public string? RelatedLinksReason { get; init; }
}

/// <summary>
/// G199 stable constants for the reviewed publish bridge command's output
/// contract. Status values, approval-marker shapes, and the canonical
/// "local_only" status are centralized here so future drift breaks the
/// regression tests deterministically.
/// </summary>
internal static class TaskingPublishReviewedBridgeConstants
{
    /// <summary>
    /// Stable literal value of <c>reviewed_bridge_status</c>. ALWAYS this
    /// value when the bridge succeeds; never overridden by operator input.
    /// </summary>
    public const string LocalOnlyStatus = "local_only";

    /// <summary>
    /// Stable summary line. <c>{0}</c> is the bundle's domain (or
    /// <c>(unavailable)</c> when not parseable), <c>{1}</c> is the status
    /// enum value. Tests assert the literal phrase
    /// "UNPUBLISHED, local-only, not automation-visible".
    /// </summary>
    public const string SummaryLineFormat =
        "Reviewed task packet publish bridge for {0} — UNPUBLISHED, local-only, not automation-visible. status={1}.";

    public const string UnpublishedStatusPhrase =
        "UNPUBLISHED, local-only, not automation-visible";

    public static class Statuses
    {
        public const string Ok = "ok";
        public const string VerifyFailed = "verify_failed";
        public const string MissingBundle = "missing_bundle";
        public const string MalformedBundle = "malformed_bundle";
        public const string MissingBody = "missing_body";
        public const string BodyContractInvalid = "body_contract_invalid";
        public const string ApprovalMarkerInvalid = "approval_marker_invalid";
    }

    /// <summary>
    /// Stable approval marker kinds emitted into the artifact's
    /// <c>approval_marker_kind</c> field. The operator-supplied marker is
    /// classified into exactly one of these three forms before the artifact is
    /// written.
    /// </summary>
    public static class ApprovalMarkerKinds
    {
        public const string Approved = "approved";
        public const string ReviewedByOperator = "reviewed_by_operator";
        public const string ApprovedWithTag = "approved_with_tag";
    }

    /// <summary>
    /// Stable approval marker shapes accepted by the bridge. Anything not
    /// matching one of these shapes (case-sensitive) — including blank,
    /// <c>"yes"</c>, <c>"ok"</c>, <c>"true"</c> — is rejected as
    /// <c>approval_marker_invalid</c>. The accepted shapes are:
    /// <list type="bullet">
    ///   <item><description>literal <c>"approved"</c></description></item>
    ///   <item><description>literal <c>"reviewed-by-operator"</c></description></item>
    ///   <item><description>any string starting with <c>"approved:"</c>
    ///   (e.g. <c>"approved:tomohisa-2026-04-29"</c> for human traceability)
    ///   </description></item>
    /// </list>
    /// </summary>
    public static readonly IReadOnlyList<string> AcceptedApprovalShapes = new[]
    {
        "approved",
        "reviewed-by-operator",
        "approved:<tag>"
    };

    public const string ApprovedLiteral = "approved";
    public const string ReviewedByOperatorLiteral = "reviewed-by-operator";
    public const string ApprovedWithTagPrefix = "approved:";
}
