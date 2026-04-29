using System.Globalization;

namespace IntentSystem.Cli.Commands;

using ApprovalMarkerKinds = TaskingPublishReviewedBridgeConstants.ApprovalMarkerKinds;
using BridgeConstants = TaskingPublishReviewedBridgeConstants;
using BridgeStatuses = TaskingPublishReviewedBridgeConstants.Statuses;

/// <summary>
/// G199 — pure logic. Takes the deserialized G194 bundle, the G196/G197 verify
/// check list, the body content, the body sha256, and the operator-supplied
/// approval marker, and builds the reviewed-ready artifact record (or the
/// failure projections used by the command layer). No I/O, no network, no
/// process launches; the command layer handles file reads/writes and verify
/// dispatch.
///
/// Reuses <see cref="IssueValidateBodyValidator.Validate"/> for body contract
/// validation.
/// </summary>
internal static class TaskingPublishReviewedBridgeAnalyzer
{
    /// <summary>
    /// Classify the operator-supplied approval marker into one of the three
    /// stable shapes. Returns <c>null</c> when the marker is blank or does not
    /// match any accepted shape; the command layer treats <c>null</c> as
    /// <see cref="BridgeStatuses.ApprovalMarkerInvalid"/>.
    /// </summary>
    public static string? ClassifyApprovalMarker(string? approvalMarker)
    {
        if (string.IsNullOrWhiteSpace(approvalMarker))
        {
            return null;
        }

        if (string.Equals(approvalMarker, BridgeConstants.ApprovedLiteral, StringComparison.Ordinal))
        {
            return ApprovalMarkerKinds.Approved;
        }

        if (string.Equals(approvalMarker, BridgeConstants.ReviewedByOperatorLiteral, StringComparison.Ordinal))
        {
            return ApprovalMarkerKinds.ReviewedByOperator;
        }

        if (approvalMarker.StartsWith(BridgeConstants.ApprovedWithTagPrefix, StringComparison.Ordinal)
            && approvalMarker.Length > BridgeConstants.ApprovedWithTagPrefix.Length)
        {
            return ApprovalMarkerKinds.ApprovedWithTag;
        }

        return null;
    }

    /// <summary>
    /// Project the failing-check ids out of a verify check list, preserving
    /// the analyzer's stable order. Mirrors the G198 projection so both
    /// commands surface the same identifiers for automation logs.
    /// </summary>
    public static IReadOnlyList<string> ExtractFailedCheckIds(IReadOnlyList<VerifyCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        var ids = new List<string>();
        foreach (var check in checks)
        {
            if (!check.Passed)
            {
                ids.Add(check.Id);
            }
        }

        return ids;
    }

    /// <summary>
    /// Build the human-readable error messages for a verify-failed projection,
    /// using the same <c>{id}: {detail}</c> shape the G198 dry-run uses.
    /// </summary>
    public static IReadOnlyList<string> ExtractErrorMessages(IReadOnlyList<VerifyCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        var errors = new List<string>();
        foreach (var check in checks)
        {
            if (!check.Passed)
            {
                errors.Add($"{check.Id}: {check.Detail}");
            }
        }

        return errors;
    }

    /// <summary>
    /// Project an <see cref="IssueValidateBodyResult"/> into the artifact's
    /// body-contract sub-record.
    /// </summary>
    public static TaskingPublishReviewedBridgeBodyValidation ProjectBodyValidation(
        IssueValidateBodyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new TaskingPublishReviewedBridgeBodyValidation
        {
            IsValid = result.IsValid,
            MissingHeadings = result.MissingHeadings,
            RelatedLinksInvalid = result.RelatedLinksInvalid,
            RelatedLinksReason = result.RelatedLinksReason
        };
    }

    /// <summary>
    /// Build the reviewed-ready artifact for the success path. Caller must
    /// have already confirmed verify is valid, body contract is valid, and the
    /// approval marker classification is non-null.
    /// </summary>
    public static TaskingPublishReviewedBridgeArtifact BuildArtifact(
        string sourceBundlePath,
        string sourceBundleSha256,
        string sourceBodyPath,
        string sourceBodySha256,
        string domain,
        string approvalMarker,
        string approvalMarkerKind,
        IssueValidateBodyResult bodyValidation,
        string generatedAtUtc,
        string artifactPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceBundlePath);
        ArgumentException.ThrowIfNullOrEmpty(sourceBundleSha256);
        ArgumentException.ThrowIfNullOrEmpty(sourceBodyPath);
        ArgumentException.ThrowIfNullOrEmpty(sourceBodySha256);
        ArgumentException.ThrowIfNullOrEmpty(domain);
        ArgumentException.ThrowIfNullOrEmpty(approvalMarker);
        ArgumentException.ThrowIfNullOrEmpty(approvalMarkerKind);
        ArgumentException.ThrowIfNullOrEmpty(generatedAtUtc);
        ArgumentException.ThrowIfNullOrEmpty(artifactPath);
        ArgumentNullException.ThrowIfNull(bodyValidation);

        return new TaskingPublishReviewedBridgeArtifact
        {
            SourceBundlePath = sourceBundlePath,
            SourceBundleSha256 = sourceBundleSha256,
            SourceBodyPath = sourceBodyPath,
            SourceBodySha256 = sourceBodySha256,
            Domain = domain,
            IsPublished = false,
            IsAutomationVisible = false,
            ReviewedBridgeStatus = BridgeConstants.LocalOnlyStatus,
            ApprovalMarker = approvalMarker,
            ApprovalMarkerKind = approvalMarkerKind,
            VerifySummary = new TaskingPublishReviewedBridgeVerifySummary
            {
                Valid = true,
                FailedCheckIds = Array.Empty<string>()
            },
            BodyContractValidation = ProjectBodyValidation(bodyValidation),
            GeneratedAtUtc = generatedAtUtc,
            ArtifactPath = artifactPath,
            SummaryLine = BuildSummaryLine(domain, BridgeStatuses.Ok)
        };
    }

    /// <summary>
    /// Build the canonical summary line shared by the artifact's
    /// <c>summary_line</c> field and the text-format stdout header.
    /// </summary>
    public static string BuildSummaryLine(string? domain, string status) =>
        string.Format(
            CultureInfo.InvariantCulture,
            BridgeConstants.SummaryLineFormat,
            domain ?? "(unavailable)",
            status);
}
