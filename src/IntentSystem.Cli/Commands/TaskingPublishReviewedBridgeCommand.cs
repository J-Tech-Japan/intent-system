using System.Globalization;
using System.Text;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

using BridgeConstants = TaskingPublishReviewedBridgeConstants;
using BridgeStatuses = TaskingPublishReviewedBridgeConstants.Statuses;

/// <summary>
/// G199: <c>intent-cli tasking publish-reviewed-bridge</c>. A LOCAL
/// pre-publish artifact builder that bundles a verified G194
/// <see cref="TaskingHandoffBundleArtifact"/>, an operator-supplied
/// G183-valid Markdown body, and an explicit approval marker into a
/// reviewed-ready packet at <c>--out</c>. Sits beside the existing
/// <c>handoff</c>, <c>task-packet</c>, <c>task-packet-preview</c>,
/// <c>task-packet-checklist</c>, <c>handoff-bundle</c>,
/// <c>handoff-bundle-inspect</c>, <c>handoff-bundle-verify</c>, and
/// <c>handoff-bundle-import-dry-run</c> commands under the same
/// <c>tasking</c> group.
///
/// <para>
/// Distinct from G184 <c>issue publish-reviewed</c>: this G199 bridge is
/// purely local and never publishes a GitHub issue, applies labels, mutates
/// queue/runs state, launches providers, creates branches/worktrees, or
/// overwrites unrelated artifacts. The artifact JSON it writes is a
/// "ready-for-real-publish" handoff that a future operator (or the G184
/// command) can consume.
/// </para>
///
/// Network-mutation invariance: the hot path contains no
/// <c>Process.Start</c>, no shell-out to <c>gh</c>, no <c>git</c> shell-out,
/// and no provider launcher. Tests validate this via the
/// <see cref="NestedProviderLauncher"/> sentinel and source-scan assertions.
///
/// Exit code: <c>0</c> iff all four checks pass (bundle parses, verify
/// reports <c>valid == true</c>, body file exists and passes G183, approval
/// marker is one of the accepted shapes); <c>1</c> otherwise. On failure no
/// artifact is written.
/// </summary>
internal static class TaskingPublishReviewedBridgeCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    private static readonly JsonSerializerOptions JsonOutputOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Test seam for the prepared-at timestamp. Defaults to
    /// <see cref="DateTimeOffset.UtcNow"/>; tests can pin a deterministic
    /// timestamp for round-trip and stability assertions.
    ///
    /// G569 review repair: this used to describe itself as mirroring
    /// <c>IssuePrepareCommand.TimestampFactory</c>, which no longer exists —
    /// that command now takes its clock per call
    /// (<see cref="IssuePrepareCommand.Execute(CliContext, string[], TextWriter, Func{DateTimeOffset})"/>)
    /// because a process-global mutable clock raced across parallel test
    /// classes. This one is assigned by a single test class today, so it is not
    /// that race; the per-call seam above is simply the shape that cannot
    /// become one.
    /// </summary>
    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// Test seam mirroring G190/G191/G192/G193/G194/G195/G196/G198
    /// <c>NestedProviderLauncher</c>. G199 must NEVER invoke this delegate.
    /// Tests register a sentinel that flips a flag if invoked; the bridge
    /// path leaves it untouched.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(
                args,
                out var fromBundle,
                out var fromBody,
                out var approval,
                out var outPath,
                out var format,
                out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        // Step 2: read bundle file.
        var resolvedFromBundle = Path.GetFullPath(fromBundle);

        if (!File.Exists(resolvedFromBundle))
        {
            WriteFailure(
                writer,
                format,
                BridgeStatuses.MissingBundle,
                domain: null,
                bundlePath: fromBundle,
                bundleSha256: null,
                bodyPath: fromBody,
                bodySha256: null,
                approvalMarker: approval,
                approvalMarkerKind: null,
                errors: new[]
                {
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Bundle path does not exist: {0}",
                        fromBundle)
                },
                verifyValid: false,
                failedCheckIds: new[]
                {
                    TaskingHandoffBundleVerifyConstants.CheckIds.BundlePathPresentAndReadable
                });
            return 1;
        }

        byte[] bundleBytes;
        try
        {
            bundleBytes = File.ReadAllBytes(resolvedFromBundle);
        }
        catch (Exception exception)
        {
            WriteFailure(
                writer,
                format,
                BridgeStatuses.MissingBundle,
                domain: null,
                bundlePath: fromBundle,
                bundleSha256: null,
                bodyPath: fromBody,
                bodySha256: null,
                approvalMarker: approval,
                approvalMarkerKind: null,
                errors: new[]
                {
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Bundle path could not be read: {0} ({1})",
                        fromBundle,
                        exception.Message)
                },
                verifyValid: false,
                failedCheckIds: new[]
                {
                    TaskingHandoffBundleVerifyConstants.CheckIds.BundlePathPresentAndReadable
                });
            return 1;
        }

        var bundleSha256 = IssuePrepareCommand.ComputeSha256Hex(bundleBytes);

        TaskingHandoffBundleArtifact? bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<TaskingHandoffBundleArtifact>(bundleBytes);
        }
        catch (JsonException exception)
        {
            WriteFailure(
                writer,
                format,
                BridgeStatuses.MalformedBundle,
                domain: null,
                bundlePath: fromBundle,
                bundleSha256: bundleSha256,
                bodyPath: fromBody,
                bodySha256: null,
                approvalMarker: approval,
                approvalMarkerKind: null,
                errors: new[]
                {
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Failed to parse bundle JSON as TaskingHandoffBundleArtifact: {0}",
                        exception.Message)
                },
                verifyValid: false,
                failedCheckIds: new[]
                {
                    TaskingHandoffBundleVerifyConstants.CheckIds.BundleJsonParses
                });
            return 1;
        }

        if (bundle is null)
        {
            WriteFailure(
                writer,
                format,
                BridgeStatuses.MalformedBundle,
                domain: null,
                bundlePath: fromBundle,
                bundleSha256: bundleSha256,
                bodyPath: fromBody,
                bodySha256: null,
                approvalMarker: approval,
                approvalMarkerKind: null,
                errors: new[] { "Bundle JSON deserialized to a null bundle." },
                verifyValid: false,
                failedCheckIds: new[]
                {
                    TaskingHandoffBundleVerifyConstants.CheckIds.BundleJsonParses
                });
            return 1;
        }

        // Step 3: reuse G196/G197 verify.
        var observations =
            TaskingHandoffBundleVerifyCommand.ObserveReferencedSourceArtifacts(bundle);
        var verifyChecks = TaskingHandoffBundleVerifyAnalyzer.BuildChecks(bundle, observations);
        var verifyValid = verifyChecks.All(c => c.Passed);

        if (!verifyValid)
        {
            WriteFailure(
                writer,
                format,
                BridgeStatuses.VerifyFailed,
                domain: bundle.Domain,
                bundlePath: fromBundle,
                bundleSha256: bundleSha256,
                bodyPath: fromBody,
                bodySha256: null,
                approvalMarker: approval,
                approvalMarkerKind: null,
                errors: TaskingPublishReviewedBridgeAnalyzer.ExtractErrorMessages(verifyChecks),
                verifyValid: false,
                failedCheckIds: TaskingPublishReviewedBridgeAnalyzer.ExtractFailedCheckIds(verifyChecks));
            return 1;
        }

        // Step 4: read body file.
        var resolvedFromBody = Path.GetFullPath(fromBody);
        if (!File.Exists(resolvedFromBody))
        {
            WriteFailure(
                writer,
                format,
                BridgeStatuses.MissingBody,
                domain: bundle.Domain,
                bundlePath: fromBundle,
                bundleSha256: bundleSha256,
                bodyPath: fromBody,
                bodySha256: null,
                approvalMarker: approval,
                approvalMarkerKind: null,
                errors: new[]
                {
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Body path does not exist: {0}",
                        fromBody)
                },
                verifyValid: true,
                failedCheckIds: Array.Empty<string>());
            return 1;
        }

        byte[] bodyBytes;
        try
        {
            bodyBytes = File.ReadAllBytes(resolvedFromBody);
        }
        catch (Exception exception)
        {
            WriteFailure(
                writer,
                format,
                BridgeStatuses.MissingBody,
                domain: bundle.Domain,
                bundlePath: fromBundle,
                bundleSha256: bundleSha256,
                bodyPath: fromBody,
                bodySha256: null,
                approvalMarker: approval,
                approvalMarkerKind: null,
                errors: new[]
                {
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Body path could not be read: {0} ({1})",
                        fromBody,
                        exception.Message)
                },
                verifyValid: true,
                failedCheckIds: Array.Empty<string>());
            return 1;
        }

        var bodySha256 = IssuePrepareCommand.ComputeSha256Hex(bodyBytes);
        var bodyContent = Encoding.UTF8.GetString(bodyBytes);

        // Step 5: reuse G183 body validator.
        var bodyValidation = IssueValidateBodyValidator.Validate(fromBody, bodyContent);
        if (!bodyValidation.IsValid)
        {
            var errors = new List<string>();
            foreach (var missing in bodyValidation.MissingHeadings)
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Missing required heading: {0}",
                    missing));
            }

            if (bodyValidation.RelatedLinksInvalid && bodyValidation.RelatedLinksReason is not null)
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Related Links invalid: {0}",
                    bodyValidation.RelatedLinksReason));
            }

            WriteFailure(
                writer,
                format,
                BridgeStatuses.BodyContractInvalid,
                domain: bundle.Domain,
                bundlePath: fromBundle,
                bundleSha256: bundleSha256,
                bodyPath: fromBody,
                bodySha256: bodySha256,
                approvalMarker: approval,
                approvalMarkerKind: null,
                errors: errors,
                verifyValid: true,
                failedCheckIds: Array.Empty<string>(),
                bodyValidation: bodyValidation);
            return 1;
        }

        // Step 6: validate approval marker.
        var approvalKind = TaskingPublishReviewedBridgeAnalyzer.ClassifyApprovalMarker(approval);
        if (approvalKind is null)
        {
            WriteFailure(
                writer,
                format,
                BridgeStatuses.ApprovalMarkerInvalid,
                domain: bundle.Domain,
                bundlePath: fromBundle,
                bundleSha256: bundleSha256,
                bodyPath: fromBody,
                bodySha256: bodySha256,
                approvalMarker: approval,
                approvalMarkerKind: null,
                errors: new[]
                {
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Approval marker '{0}' is not one of the accepted shapes (literal 'approved', literal 'reviewed-by-operator', or any string starting with 'approved:').",
                        approval)
                },
                verifyValid: true,
                failedCheckIds: Array.Empty<string>(),
                bodyValidation: bodyValidation);
            return 1;
        }

        // Step 7: build artifact + write.
        var resolvedOut = Path.GetFullPath(outPath);
        var generatedAt = IssuePrepareCommand.FormatUtcTimestamp(TimestampFactory());

        var artifact = TaskingPublishReviewedBridgeAnalyzer.BuildArtifact(
            sourceBundlePath: fromBundle,
            sourceBundleSha256: bundleSha256,
            sourceBodyPath: fromBody,
            sourceBodySha256: bodySha256,
            domain: bundle.Domain,
            approvalMarker: approval,
            approvalMarkerKind: approvalKind,
            bodyValidation: bodyValidation,
            generatedAtUtc: generatedAt,
            artifactPath: resolvedOut);

        var artifactJson = JsonSerializer.Serialize(artifact, JsonOutputOptions);
        var outDir = Path.GetDirectoryName(resolvedOut);
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        File.WriteAllText(resolvedOut, artifactJson);

        WriteSuccess(writer, artifact, format);
        return 0;
    }

    private static void WriteFailure(
        TextWriter writer,
        string format,
        string status,
        string? domain,
        string bundlePath,
        string? bundleSha256,
        string bodyPath,
        string? bodySha256,
        string approvalMarker,
        string? approvalMarkerKind,
        IReadOnlyList<string> errors,
        bool verifyValid,
        IReadOnlyList<string> failedCheckIds,
        IssueValidateBodyResult? bodyValidation = null)
    {
        // Build a "failure projection" of the artifact JSON. We deliberately
        // do not write the artifact to disk on the failure path — only emit
        // it to stdout so callers can parse status and error fields.
        var failureBodyValidation = bodyValidation is null
            ? new TaskingPublishReviewedBridgeBodyValidation
            {
                IsValid = false,
                MissingHeadings = Array.Empty<string>(),
                RelatedLinksInvalid = false,
                RelatedLinksReason = null
            }
            : TaskingPublishReviewedBridgeAnalyzer.ProjectBodyValidation(bodyValidation);

        var summaryLine = TaskingPublishReviewedBridgeAnalyzer.BuildSummaryLine(domain, status);

        var projection = new FailureProjection
        {
            Status = status,
            SourceBundlePath = bundlePath,
            SourceBundleSha256 = bundleSha256,
            SourceBodyPath = bodyPath,
            SourceBodySha256 = bodySha256,
            Domain = domain,
            ApprovalMarker = approvalMarker,
            ApprovalMarkerKind = approvalMarkerKind,
            Errors = errors,
            VerifySummary = new TaskingPublishReviewedBridgeVerifySummary
            {
                Valid = verifyValid,
                FailedCheckIds = failedCheckIds
            },
            BodyContractValidation = failureBodyValidation,
            SummaryLine = summaryLine
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(projection, JsonOutputOptions));
        }
        else
        {
            WriteFailureText(writer, projection);
        }
    }

    private static void WriteFailureText(TextWriter writer, FailureProjection projection)
    {
        writer.WriteLine(projection.SummaryLine);
        writer.WriteLine();
        writer.WriteLine($"Bundle path: {projection.SourceBundlePath}");
        writer.WriteLine($"Bundle sha256: {projection.SourceBundleSha256 ?? "(unavailable)"}");
        writer.WriteLine($"Body path: {projection.SourceBodyPath}");
        writer.WriteLine($"Body sha256: {projection.SourceBodySha256 ?? "(unavailable)"}");
        writer.WriteLine($"Domain: {projection.Domain ?? "(unavailable)"}");
        writer.WriteLine($"Status: {projection.Status}");
        writer.WriteLine($"Approval marker: {projection.ApprovalMarker}");
        writer.WriteLine($"Approval marker kind: {projection.ApprovalMarkerKind ?? "(unavailable)"}");
        writer.WriteLine($"Verify valid: {projection.VerifySummary.Valid}");

        if (projection.VerifySummary.FailedCheckIds.Count > 0)
        {
            writer.WriteLine("Failed verify check ids:");
            foreach (var id in projection.VerifySummary.FailedCheckIds)
            {
                writer.WriteLine($"- {id}");
            }
        }

        writer.WriteLine($"Body valid: {projection.BodyContractValidation.IsValid}");
        if (projection.BodyContractValidation.MissingHeadings.Count > 0)
        {
            writer.WriteLine("Missing headings:");
            foreach (var heading in projection.BodyContractValidation.MissingHeadings)
            {
                writer.WriteLine($"- {heading}");
            }
        }

        if (projection.BodyContractValidation.RelatedLinksInvalid)
        {
            writer.WriteLine($"Related links invalid: {projection.BodyContractValidation.RelatedLinksReason ?? "(unspecified)"}");
        }

        if (projection.Errors.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Errors:");
            foreach (var err in projection.Errors)
            {
                writer.WriteLine($"- {err}");
            }
        }
    }

    private static void WriteSuccess(
        TextWriter writer,
        TaskingPublishReviewedBridgeArtifact artifact,
        string format)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(artifact, JsonOutputOptions));
            return;
        }

        writer.WriteLine(artifact.SummaryLine);
        writer.WriteLine();
        writer.WriteLine($"Bundle path: {artifact.SourceBundlePath}");
        writer.WriteLine($"Bundle sha256: {artifact.SourceBundleSha256}");
        writer.WriteLine($"Body path: {artifact.SourceBodyPath}");
        writer.WriteLine($"Body sha256: {artifact.SourceBodySha256}");
        writer.WriteLine($"Domain: {artifact.Domain}");
        writer.WriteLine("Status: ok");
        writer.WriteLine($"Approval marker: {artifact.ApprovalMarker}");
        writer.WriteLine($"Approval marker kind: {artifact.ApprovalMarkerKind}");
        writer.WriteLine($"Reviewed bridge status: {artifact.ReviewedBridgeStatus}");
        writer.WriteLine($"Is published: {artifact.IsPublished}");
        writer.WriteLine($"Is automation visible: {artifact.IsAutomationVisible}");
        writer.WriteLine($"Verify valid: {artifact.VerifySummary.Valid}");
        writer.WriteLine($"Body valid: {artifact.BodyContractValidation.IsValid}");
        writer.WriteLine($"Generated at (UTC): {artifact.GeneratedAtUtc}");
        writer.WriteLine($"Artifact path: {artifact.ArtifactPath}");
    }

    private static bool TryParseArguments(
        string[] args,
        out string fromBundle,
        out string fromBody,
        out string approval,
        out string outPath,
        out string format,
        out string error)
    {
        fromBundle = string.Empty;
        fromBody = string.Empty;
        approval = string.Empty;
        outPath = string.Empty;
        format = FormatText;
        error = string.Empty;

        var sawApproval = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--from-bundle":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--from-bundle requires a non-empty value.";
                        return false;
                    }

                    fromBundle = args[index + 1];
                    index++;
                    break;

                case "--from-body":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--from-body requires a non-empty value.";
                        return false;
                    }

                    fromBody = args[index + 1];
                    index++;
                    break;

                case "--approval":
                    // Approval is allowed to be an empty string here so the
                    // command can flow through to the marker validator and
                    // emit status=approval_marker_invalid. We still require
                    // the flag itself to be present and to consume one value
                    // slot.
                    if (index + 1 >= args.Length)
                    {
                        error = "--approval requires a value.";
                        return false;
                    }

                    approval = args[index + 1];
                    sawApproval = true;
                    index++;
                    break;

                case "--out":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--out requires a non-empty value.";
                        return false;
                    }

                    outPath = args[index + 1];
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (text or json).";
                        return false;
                    }

                    var requestedFormat = args[index + 1];
                    if (!string.Equals(requestedFormat, FormatText, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'text' or 'json' (got '{requestedFormat}').";
                        return false;
                    }

                    format = requestedFormat;
                    index++;
                    break;

                default:
                    error =
                        $"Unknown argument '{argument}'. Supported: --from-bundle, --from-body, --approval, --out, --format.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(fromBundle))
        {
            error = "--from-bundle is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(fromBody))
        {
            error = "--from-body is required.";
            return false;
        }

        if (!sawApproval)
        {
            error = "--approval is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(outPath))
        {
            error = "--out is required.";
            return false;
        }

        return true;
    }

    private sealed record FailureProjection
    {
        [System.Text.Json.Serialization.JsonPropertyName("source_bundle_path")]
        public required string SourceBundlePath { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("source_bundle_sha256")]
        public required string? SourceBundleSha256 { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("source_body_path")]
        public required string SourceBodyPath { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("source_body_sha256")]
        public required string? SourceBodySha256 { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("domain")]
        public required string? Domain { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public required string Status { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("approval_marker")]
        public required string ApprovalMarker { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("approval_marker_kind")]
        public required string? ApprovalMarkerKind { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("errors")]
        public required IReadOnlyList<string> Errors { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("verify_summary")]
        public required TaskingPublishReviewedBridgeVerifySummary VerifySummary { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("body_contract_validation")]
        public required TaskingPublishReviewedBridgeBodyValidation BodyContractValidation { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("summary_line")]
        public required string SummaryLine { get; init; }
    }
}
