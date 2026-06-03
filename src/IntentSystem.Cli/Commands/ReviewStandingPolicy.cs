using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G451: a domain-level standing-policy registry for recurring review
/// decisions. Review loops repeatedly stall on policy questions that should be
/// stable within a domain or wave — draft PR handling, device/operator/
/// hardware-gated evidence gaps, external issue/PR intake, test-evidence
/// sufficiency, and follow-up tracking. Encoding these as data lets
/// <c>guide review</c> return actionable, deterministic guidance instead of
/// re-asking the operator the same question per packet.
///
/// The policy is OPTIONAL: when no <c>.intent-cli/review-policy.json</c> exists
/// (or it is invalid), the built-in safe defaults apply and the host behaves
/// exactly as before. Existing hosts require no migration.
/// </summary>
internal sealed record ReviewStandingPolicy
{
    /// <summary>Where this resolved policy came from (see
    /// <see cref="ReviewStandingPolicySources"/>).</summary>
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    /// <summary>Non-fatal notes (e.g. an invalid file that fell back to defaults).</summary>
    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }

    [JsonPropertyName("device_gated_evidence")]
    public required ReviewDeviceGatedEvidencePolicy DeviceGatedEvidence { get; init; }

    [JsonPropertyName("draft_handling")]
    public required ReviewPolicySection DraftHandling { get; init; }

    [JsonPropertyName("external_artifact_intake")]
    public required ReviewPolicySection ExternalArtifactIntake { get; init; }

    [JsonPropertyName("test_evidence_sufficiency")]
    public required ReviewPolicySection TestEvidenceSufficiency { get; init; }

    [JsonPropertyName("follow_up_tracking")]
    public required ReviewPolicySection FollowUpTracking { get; init; }

    /// <summary>
    /// G451: the safe built-in default policy. Reproduces the prior G445
    /// device-gated evidence rules verbatim plus conservative defaults for the
    /// other recurring decisions, so a host with no policy file keeps today's
    /// behavior.
    /// </summary>
    public static ReviewStandingPolicy Default(string? domain = null) => new()
    {
        Source = ReviewStandingPolicySources.BuiltInDefault,
        Domain = domain,
        Warnings = Array.Empty<string>(),
        DeviceGatedEvidence = new ReviewDeviceGatedEvidencePolicy
        {
            ApproveWithRecordedGapAllowed = true,
            HardBlockCategories = ["safety", "security", "data-loss", "payment", "primary-deliverable"],
            Rules = DefaultDeviceGatedEvidenceRules,
        },
        DraftHandling = new ReviewPolicySection
        {
            Rules =
            [
                "A draft PR is NOT review-ready: do not approve, merge, or run the approval transition on a draft. Request the author mark it ready-for-review first.",
                "Do not re-ask whether drafts are reviewable once this policy is established for the domain; treat every draft as not-ready by default.",
            ],
        },
        ExternalArtifactIntake = new ReviewPolicySection
        {
            Rules =
            [
                "GitHub Issues are the default work-item provider and GitHub PRs the review/merge provider; an external (non-intent-target) issue/PR is intake-only until the host promotes it through the normal publish path.",
                "Do not auto-promote or auto-label an external artifact from the review loop; surface it for host triage instead.",
            ],
        },
        TestEvidenceSufficiency = new ReviewPolicySection
        {
            Rules =
            [
                "Passing tests are NECESSARY but NOT SUFFICIENT: after tests pass, restate at least one packet contract clause AND one intent reference touched by the diff in the approval summary, or treat the PR as request-update.",
                "Do not approve on a green CI signal alone when the packet's acceptance criteria are not individually evidenced.",
            ],
        },
        FollowUpTracking = new ReviewPolicySection
        {
            Rules =
            [
                "Every accepted gap (device-gap, deferred verification, recorded limitation) MUST be tracked durably via a PR comment, closeout note, or follow-up issue — never only in chat.",
                "A follow-up that exists only in conversation is not durable workflow state and does not satisfy this policy.",
            ],
        },
    };

    /// <summary>G445/G451: the canonical device-gated evidence rules. Kept as a
    /// shared constant so the default policy and any tests reference one source.</summary>
    public static readonly IReadOnlyList<string> DefaultDeviceGatedEvidenceRules = new[]
    {
        "Definition: a `device-gap` is an acceptance criterion whose ONLY missing evidence is real physical-device / operator / hardware proof that this automation cannot generate (e.g. a real two-finger touch gesture), while source, log, unit-test, and simulator evidence for the same criterion IS available and passes.",
        "Approve-with-recorded-gap (NOT an operator stop) when ALL hold: (a) code/packet conformance is verified by available source/log/unit/simulator evidence; (b) the missing evidence is purely a device/automation limitation; (c) the gap is explicitly recorded as a `device-gap` in the approval summary AND a durable follow-up (PR comment / closeout note / follow-up issue) tracks collecting the real-device evidence.",
        "HARD-BLOCK (request-update or operator stop, never approve) when the device-gated evidence IS the primary deliverable of the slice, OR is safety / security / data-loss / payment / other high-risk proof. These are not eligible for approve-with-gap.",
        "NEVER claim physical evidence was collected when it was not. State plainly: \"real-device evidence for <criterion> was NOT collected (device-gap); verified by <source/log/unit/simulator> instead\" — fabricating or implying device evidence is a false-claim violation.",
        "Do NOT re-ask the standing-policy question once a device-gap policy is established for this domain/wave; apply the same rule to subsequent packets with equivalent device gaps and only escalate genuinely new high-risk gates.",
        "Record every accepted device-gap durably (closeout note / PR comment / follow-up issue) so the deferred real-device verification is tracked and not silently lost."
    };
}

/// <summary>G451: a simple list-of-rules policy section.</summary>
internal sealed record ReviewPolicySection
{
    [JsonPropertyName("rules")]
    public required IReadOnlyList<string> Rules { get; init; }
}

/// <summary>
/// G451: the device/operator/hardware-gated evidence policy. The structured
/// flags let a domain configure whether approve-with-recorded-gap is permitted
/// at all and which categories are always hard-blocked (never approve-with-gap).
/// </summary>
internal sealed record ReviewDeviceGatedEvidencePolicy
{
    [JsonPropertyName("approve_with_recorded_gap_allowed")]
    public required bool ApproveWithRecordedGapAllowed { get; init; }

    [JsonPropertyName("hard_block_categories")]
    public required IReadOnlyList<string> HardBlockCategories { get; init; }

    [JsonPropertyName("rules")]
    public required IReadOnlyList<string> Rules { get; init; }
}

internal static class ReviewStandingPolicySources
{
    public const string BuiltInDefault = "built-in-default";
    public const string DomainFile = "domain-file";
    public const string InvalidFallbackDefault = "invalid-fallback-default";
}
