using System.Globalization;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Pure parsing shared by the G855 observer and the solo-conductor approval
/// gate. It keeps the historical observer result alongside the REST facts and
/// body classification needed for approval-specific recovery.
/// </summary>
internal static class IndependentReviewEvidence
{
    public static IndependentReviewRowEvidence ParseRow(
        JsonElement item,
        string unit,
        string domain,
        string team,
        string repo,
        int pullRequest,
        CrossRuntimeReviewReadResult localReviews)
    {
        ArgumentNullException.ThrowIfNull(localReviews);

        var isObject = item.ValueKind == JsonValueKind.Object;
        var idValid = TryReadLong(item, "id", out var id);
        var stateValid = TryReadString(item, "state", out var state);
        var commitIdValid = TryReadNullableString(item, "commit_id", out var commitId);
        var bodyValid = TryReadNullableString(item, "body", out var body);
        var reviewUrlValid = TryReadOptionalNullableString(item, "html_url", out var reviewUrl);
        var login = string.Empty;
        var loginValid = isObject
            && item.TryGetProperty("user", out var user)
            && TryReadString(user, "login", out login);
        var reviewer = loginValid ? login : null;

        var submittedAtPresent = isObject && item.TryGetProperty("submitted_at", out _);
        DateTimeOffset? submittedAtValue = null;
        var submittedAtValid = submittedAtPresent
            && TryReadNullableDateTime(item, "submitted_at", out submittedAtValue);
        DateTimeOffset? submittedAt = submittedAtValid ? submittedAtValue : null;

        var bodyEnvelopeReadable = bodyValid;
        var bodyText = bodyValid ? body : null;
        var bodyEvidence = !string.IsNullOrWhiteSpace(bodyText) ? ParseBody(bodyText) : null;
        var citationValidatedForExpected = bodyEvidence?.CitedRecord is { } expectedCitation
            && TryValidateCitedReviewRecord(localReviews, expectedCitation, unit, domain, team, repo, pullRequest, bodyEvidence, out _, bodyEvidence.Kind);
        var citationValidatedForDeclaredUnit = bodyEvidence?.CitedRecord is { } declaredCitation
            && bodyEvidence.ExecutionUnit is { } declaredUnit
            && TryValidateCitedReviewRecord(localReviews, declaredCitation, declaredUnit, domain, team, repo, pullRequest, bodyEvidence, out _, bodyEvidence.Kind);
        string? inventoryFailure = !isObject
            ? "Pull-request review response row is not a JSON object."
            : !bodyEnvelopeReadable
                ? "Pull-request review response body is missing or is not a string or null."
                : null;

        var evidence = new IndependentReviewRowEvidence
        {
            IsObject = isObject,
            BodyEnvelopeReadable = bodyEnvelopeReadable,
            InventoryFailureDetail = inventoryFailure,
            ReviewId = idValid ? id : null,
            ReviewIdFieldValid = idValid,
            State = stateValid ? state : null,
            StateFieldValid = stateValid,
            CommitId = commitIdValid ? commitId : null,
            CommitIdFieldValid = commitIdValid,
            Body = bodyText,
            ReviewerLogin = reviewer,
            ReviewerLoginFieldValid = loginValid,
            SubmittedAt = submittedAt,
            SubmittedAtPresent = submittedAtPresent,
            SubmittedAtFieldValid = submittedAtValid,
            ReviewUrl = reviewUrlValid ? reviewUrl : null,
            ReviewUrlFieldValid = reviewUrlValid,
            BodyEvidence = bodyEvidence,
            CitedRecordValidatedForExpected = citationValidatedForExpected,
            CitedRecordValidatedForDeclaredUnit = citationValidatedForDeclaredUnit,
            RawPayload = item.GetRawText(),
        };

        return ParseForObserver(evidence, unit, domain, team, repo, pullRequest, localReviews);
    }

    public static UnitStatusEvidencePointer LegacyReviewEvidence(JsonElement item, string repo, int pullRequest)
    {
        var recordId = TryReadLong(item, "id", out var id) ? id.ToString(CultureInfo.InvariantCulture) : null;
        var url = TryReadNullableString(item, "html_url", out var parsedUrl) ? parsedUrl : null;
        return new UnitStatusEvidencePointer
        {
            Kind = "github-pr-review",
            Url = url ?? $"https://github.com/{repo}/pull/{pullRequest}",
            RecordId = recordId,
            Repo = repo,
            Pr = pullRequest,
            ReviewDisposition = "legacy-review-identity-unrecorded",
            Provenance = "named-unstructured-github-pr-review",
        };
    }

    internal static UnitStatusReviewBodyParse ParseBody(string body)
    {
        static bool IsNamedIndependentHeading(string? value) => value is not null
            && (value.StartsWith("Independent subagent review:", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("Independent same-runtime subagent review:", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("Cross-runtime review:", StringComparison.OrdinalIgnoreCase));

        static string? NamedIndependentVerdict(string? value) => IsNamedIndependentHeading(value)
            ? value![(value!.IndexOf(':') + 1)..].Trim()
            : null;

        static bool IsExplicitVerdict(string? value) => value is not null
            && CrossRuntimeReviewVerdict.VerdictValues.Contains(value, StringComparer.Ordinal);

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var leadingLine = lines.Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);
        var headings = lines
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => line[3..].Trim())
            .ToArray();
        var heading = headings.FirstOrDefault();
        string? citedRecord = null;
        var hasCitationAssertion = false;
        var conflicting = false;
        var inMetadataHeader = true;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("Recorded as", StringComparison.OrdinalIgnoreCase))
            {
                hasCitationAssertion = true;
            }
            if (line.StartsWith("Recorded as `", StringComparison.Ordinal)
                && line.EndsWith("` by `intent-cli review cross-runtime record`.", StringComparison.Ordinal))
            {
                var close = line.IndexOf('`', "Recorded as `".Length);
                if (close > "Recorded as `".Length)
                {
                    var citation = line["Recorded as `".Length..close];
                    if (citedRecord is not null && !string.Equals(citedRecord, citation, StringComparison.Ordinal)) conflicting = true;
                    else citedRecord = citation;
                }
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                inMetadataHeader = false;
                continue;
            }

            if (!inMetadataHeader) continue;
            if (line.StartsWith("- ", StringComparison.Ordinal)) line = line[2..].Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim().ToLowerInvariant() switch
            {
                "head sha" => "head_sha",
                "execution unit" => "execution_unit",
                "conductor runtime" => "conductor_runtime",
                "runtime version" => "runtime_version",
                var other => other,
            };
            if (key is not ("reviewer" or "runtime" or "runtime_version" or "model" or "effort"
                or "conductor_runtime" or "head_sha" or "kind" or "execution_unit" or "verdict")) continue;
            var value = line[(colon + 1)..].Trim().Trim('`');
            if (fields.TryGetValue(key, out var previous) && !string.Equals(previous, value, StringComparison.Ordinal))
            {
                conflicting = true;
            }
            else
            {
                fields[key] = value;
            }
        }

        var relation = heading?.StartsWith("Cross-runtime review:", StringComparison.Ordinal) == true
            ? "cross-runtime"
            : heading?.StartsWith("Independent same-runtime subagent review:", StringComparison.Ordinal) == true
                ? "same-runtime"
                : null;
        var reviewer = fields.GetValueOrDefault("reviewer");
        var namedIndependentHeading = IsNamedIndependentHeading(heading);
        var leadingReviewLine = leadingLine?.StartsWith("## ", StringComparison.Ordinal) == true
            ? leadingLine[3..].Trim()
            : leadingLine;
        var namedIndependentLead = IsNamedIndependentHeading(leadingReviewLine);
        var headingVerdict = NamedIndependentVerdict(heading);
        var leadingVerdict = NamedIndependentVerdict(leadingReviewLine);
        var namedIndependent = relation is not null
            || namedIndependentHeading
            || namedIndependentLead
            || string.Equals(reviewer, "independent same-runtime subagent review", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reviewer, "independent subagent review", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reviewer, "cross-runtime review", StringComparison.OrdinalIgnoreCase);
        var verdict = fields.GetValueOrDefault("verdict");
        var headingVerdictConflict = IsExplicitVerdict(headingVerdict)
            && !string.IsNullOrWhiteSpace(verdict)
            && !string.Equals(headingVerdict, verdict, StringComparison.Ordinal);
        var leadingVerdictConflict = IsExplicitVerdict(leadingVerdict)
            && !string.IsNullOrWhiteSpace(verdict)
            && !string.Equals(leadingVerdict, verdict, StringComparison.Ordinal);
        var headingAndLeadingVerdictConflict = IsExplicitVerdict(headingVerdict)
            && IsExplicitVerdict(leadingVerdict)
            && !string.Equals(headingVerdict, leadingVerdict, StringComparison.Ordinal);
        conflicting |= headingVerdictConflict || leadingVerdictConflict || headingAndLeadingVerdictConflict;
        var approvalHeadingVerdicts = headings
            .Select(NamedIndependentVerdict)
            .Where(IsExplicitVerdict)
            .Cast<string>()
            .ToArray();
        var approvalEvidenceConflicting = approvalHeadingVerdicts
            .Distinct(StringComparer.Ordinal)
            .Skip(1)
            .Any()
            || IsExplicitVerdict(verdict)
            && approvalHeadingVerdicts.Any(headingAssertion => !string.Equals(headingAssertion, verdict, StringComparison.Ordinal));
        var canonicalRelationMatchesReviewer = relation switch
        {
            "cross-runtime" => string.Equals(reviewer, "cross-runtime review", StringComparison.OrdinalIgnoreCase),
            "same-runtime" => string.Equals(reviewer, "independent same-runtime subagent review", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
        var structured = !conflicting
            && namedIndependent
            && canonicalRelationMatchesReviewer
            && !string.IsNullOrWhiteSpace(fields.GetValueOrDefault("execution_unit"))
            && !string.IsNullOrWhiteSpace(fields.GetValueOrDefault("head_sha"))
            && !string.IsNullOrWhiteSpace(fields.GetValueOrDefault("kind"))
            && !string.IsNullOrWhiteSpace(verdict)
            && (!IsExplicitVerdict(headingVerdict) || string.Equals(headingVerdict, verdict, StringComparison.Ordinal))
            && (!IsExplicitVerdict(leadingVerdict) || string.Equals(leadingVerdict, verdict, StringComparison.Ordinal))
            && (relation is null
                || !string.IsNullOrWhiteSpace(fields.GetValueOrDefault("runtime"))
                && !string.IsNullOrWhiteSpace(fields.GetValueOrDefault("conductor_runtime")));

        return new UnitStatusReviewBodyParse(
            structured,
            namedIndependent,
            relation,
            fields.GetValueOrDefault("execution_unit"),
            fields.GetValueOrDefault("head_sha"),
            fields.GetValueOrDefault("kind"),
            verdict,
            fields.GetValueOrDefault("runtime"),
            fields.GetValueOrDefault("conductor_runtime"),
            citedRecord,
            conflicting)
        {
            Reviewer = reviewer,
            HeadingVerdict = headingVerdict,
            LeadingVerdict = leadingVerdict,
            HasCitationAssertion = hasCitationAssertion,
            HasStructuredMetadata = fields.Keys.Any(key => key is "head_sha" or "execution_unit" or "kind" or "verdict" or "runtime" or "conductor_runtime"),
            ApprovalEvidenceConflicting = approvalEvidenceConflicting,
            ExplicitRequestChangesAssertion = string.Equals(verdict, CrossRuntimeReviewVerdict.RequestChanges, StringComparison.Ordinal)
                || string.Equals(headingVerdict, CrossRuntimeReviewVerdict.RequestChanges, StringComparison.Ordinal)
                || string.Equals(leadingVerdict, CrossRuntimeReviewVerdict.RequestChanges, StringComparison.Ordinal)
                || approvalHeadingVerdicts.Contains(CrossRuntimeReviewVerdict.RequestChanges, StringComparer.Ordinal),
        };
    }

    private static IndependentReviewRowEvidence ParseForObserver(
        IndependentReviewRowEvidence evidence,
        string unit,
        string domain,
        string team,
        string repo,
        int pullRequest,
        CrossRuntimeReviewReadResult localReviews)
    {
        IndependentReviewRowEvidence WithFailure(ReviewParseFailure kind, string detail, IndependentReviewEvidenceKind classification = IndependentReviewEvidenceKind.Malformed) =>
            evidence with
            {
                ObserverFailureKind = kind,
                ObserverFailureDetail = detail,
                Classification = classification,
            };

        if (!evidence.ReviewIdFieldValid
            || !evidence.StateFieldValid
            || !evidence.CommitIdFieldValid
            || !evidence.BodyEnvelopeReadable
            || !evidence.ReviewUrlFieldValid)
        {
            return WithFailure(ReviewParseFailure.Malformed,
                "Pull-request review response is missing id, state, commit_id, or body, or has a non-null, non-string html_url.");
        }

        var state = evidence.State!;
        if (state is not ("PENDING" or "COMMENTED" or "APPROVED" or "CHANGES_REQUESTED" or "DISMISSED"))
        {
            return WithFailure(ReviewParseFailure.Malformed, "Pull-request review response has an unknown state.");
        }

        if (evidence.SubmittedAtPresent && !evidence.SubmittedAtFieldValid
            || !evidence.SubmittedAtPresent && state != "PENDING")
        {
            return WithFailure(ReviewParseFailure.Malformed, "Pull-request review response has an invalid submitted_at field.");
        }
        if (state is "COMMENTED" or "APPROVED" or "CHANGES_REQUESTED" or "DISMISSED"
            && (string.IsNullOrWhiteSpace(evidence.ReviewerLogin) || evidence.SubmittedAt is null))
        {
            return WithFailure(ReviewParseFailure.Malformed,
                "A submitted pull-request review is missing its reviewer login or valid submitted_at timestamp.");
        }
        if (string.IsNullOrWhiteSpace(evidence.Body))
        {
            return evidence with { Classification = IndependentReviewEvidenceKind.Unrelated };
        }

        var parsed = evidence.BodyEvidence!;
        if (!parsed.NamedIndependent)
        {
            return evidence with { Classification = IndependentReviewEvidenceKind.Unrelated };
        }
        if (parsed.Conflicting)
        {
            return WithFailure(ReviewParseFailure.IdentityConflict,
                "The structured review body contains conflicting repeated identity fields or contradictory heading/verdict assertions.",
                IndependentReviewEvidenceKind.IdentityConflict);
        }
        if (!parsed.IsStructured)
        {
            return WithFailure(ReviewParseFailure.ProvenanceLimit, "", IndependentReviewEvidenceKind.Provenance);
        }

        var id = evidence.ReviewId!.Value;
        var reviewUrl = evidence.ReviewUrl;
        var submittedAt = evidence.SubmittedAt;
        if (!string.Equals(parsed.ExecutionUnit, unit, StringComparison.Ordinal)
            || !string.Equals(parsed.Kind, CrossRuntimeReviewRecord.KindImplementation, StringComparison.Ordinal))
        {
            var foreignReview = new UnitStatusObservedReview
            {
                Source = "github-pr-review",
                HeadSha = parsed.HeadSha!,
                Verdict = parsed.Verdict!,
                ReviewState = state,
                Reviewer = evidence.ReviewerLogin,
                Runtime = parsed.Runtime,
                Relation = parsed.Relation,
                At = submittedAt,
                RecordId = id.ToString(CultureInfo.InvariantCulture),
                Url = reviewUrl,
                Qualification = !string.Equals(parsed.ExecutionUnit, unit, StringComparison.Ordinal)
                    ? "nonqualifying-other-unit-review"
                    : "nonqualifying-design-review",
                Dismissed = string.Equals(state, "DISMISSED", StringComparison.OrdinalIgnoreCase),
            };
            return evidence with
            {
                Classification = IndependentReviewEvidenceKind.NonQualifying,
                ObserverAccepted = true,
                ObserverReview = foreignReview,
            };
        }

        var valid = parsed.HeadSha is not null
            && IsObjectId(parsed.HeadSha)
            && string.Equals(parsed.HeadSha, evidence.CommitId, StringComparison.OrdinalIgnoreCase)
            && parsed.Verdict is CrossRuntimeReviewVerdict.Approve or CrossRuntimeReviewVerdict.RequestChanges
            && (parsed.Relation is null
                || CrossRuntimeReviewRuntimes.IsSupported(parsed.Runtime)
                && CrossRuntimeReviewRuntimes.IsSupported(parsed.ConductorRuntime)
                && (parsed.Relation == "same-runtime"
                    ? string.Equals(parsed.Runtime, parsed.ConductorRuntime, StringComparison.Ordinal)
                    : !string.Equals(parsed.Runtime, parsed.ConductorRuntime, StringComparison.Ordinal)));
        if (!valid)
        {
            return WithFailure(ReviewParseFailure.IdentityConflict, "", IndependentReviewEvidenceKind.IdentityConflict);
        }

        string? citedRecordPath = null;
        if (parsed.CitedRecord is { } citedRecord
            && !TryValidateCitedReviewRecord(localReviews, citedRecord, unit, domain, team, repo, pullRequest, parsed, out citedRecordPath))
        {
            return WithFailure(ReviewParseFailure.IdentityConflict, "", IndependentReviewEvidenceKind.IdentityConflict);
        }

        var review = new UnitStatusObservedReview
        {
            Source = "github-pr-review",
            HeadSha = parsed.HeadSha!,
            Verdict = parsed.Verdict!,
            ReviewState = state,
            Reviewer = evidence.ReviewerLogin,
            Runtime = parsed.Runtime,
            Relation = parsed.Relation,
            At = submittedAt,
            RecordId = id.ToString(CultureInfo.InvariantCulture),
            CitedRecordPath = citedRecordPath,
            Url = reviewUrl,
            Qualification = "implementation-review",
            Dismissed = string.Equals(state, "DISMISSED", StringComparison.OrdinalIgnoreCase),
        };
        return evidence with
        {
            Classification = IndependentReviewEvidenceKind.Structured,
            ObserverAccepted = true,
            ObserverReview = review,
        };
    }

    private static bool TryValidateCitedReviewRecord(
        CrossRuntimeReviewReadResult localReviews,
        string citedRecord,
        string unit,
        string domain,
        string team,
        string repo,
        int pullRequest,
        UnitStatusReviewBodyParse parsed,
        out string? recordPath,
        string? expectedKind = null)
    {
        recordPath = null;
        if (localReviews.Unreadable.Any(item => string.Equals(item.RelativePath, citedRecord, StringComparison.Ordinal))) return false;
        var stored = localReviews.Records.FirstOrDefault(item => string.Equals(item.RelativePath, citedRecord, StringComparison.Ordinal));
        if (stored is null) return false;
        var record = stored.Record;
        if (!string.Equals(record.Repo, repo, StringComparison.OrdinalIgnoreCase)
            || record.Pr != pullRequest
            || !string.Equals(record.ExecutionUnit, unit, StringComparison.Ordinal)
            || !string.Equals(record.Domain, domain, StringComparison.Ordinal)
            || !string.Equals(record.Team, team, StringComparison.Ordinal)
            || !string.Equals(record.Kind, expectedKind ?? CrossRuntimeReviewRecord.KindImplementation, StringComparison.Ordinal)
            || !string.Equals(record.HeadSha, parsed.HeadSha, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(record.Runtime, parsed.Runtime, StringComparison.Ordinal)
            || !string.Equals(record.ConductorRuntime, parsed.ConductorRuntime, StringComparison.Ordinal)
            || !string.Equals(record.Verdict, parsed.Verdict, StringComparison.Ordinal))
        {
            return false;
        }

        recordPath = stored.RelativePath;
        return true;
    }

    private static bool TryReadString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var raw)
            && raw.ValueKind == JsonValueKind.String
            && raw.GetString() is { } text
            && !string.IsNullOrWhiteSpace(text)
            && Assign(text, out value);
    }

    private static bool TryReadNullableString(JsonElement root, string name, out string? value)
    {
        value = null;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var raw)) return false;
        if (raw.ValueKind == JsonValueKind.Null) return true;
        if (raw.ValueKind != JsonValueKind.String) return false;
        value = raw.GetString();
        return true;
    }

    private static bool TryReadOptionalNullableString(JsonElement root, string name, out string? value)
    {
        value = null;
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (!root.TryGetProperty(name, out _)) return true;
        return TryReadNullableString(root, name, out value);
    }

    private static bool TryReadLong(JsonElement root, string name, out long value)
    {
        value = 0;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var raw)
            && raw.ValueKind == JsonValueKind.Number
            && raw.TryGetInt64(out value);
    }

    private static bool TryReadNullableDateTime(JsonElement root, string name, out DateTimeOffset? value)
    {
        value = null;
        if (!TryReadNullableString(root, name, out var raw)) return false;
        if (raw is null) return true;
        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)) return false;
        value = parsed;
        return true;
    }

    private static bool IsObjectId(string value) => value.Length is 40 or 64 && value.All(Uri.IsHexDigit);

    private static bool Assign<T>(T value, out T target)
    {
        target = value;
        return true;
    }
}

internal enum IndependentReviewEvidenceKind
{
    Unrelated,
    Provenance,
    Malformed,
    IdentityConflict,
    NonQualifying,
    Structured,
}

internal sealed record IndependentReviewRowEvidence
{
    public bool IsObject { get; init; }
    public bool BodyEnvelopeReadable { get; init; }
    public string? InventoryFailureDetail { get; init; }
    public long? ReviewId { get; init; }
    public bool ReviewIdFieldValid { get; init; }
    public string? State { get; init; }
    public bool StateFieldValid { get; init; }
    public string? CommitId { get; init; }
    public bool CommitIdFieldValid { get; init; }
    public string? Body { get; init; }
    public string? ReviewerLogin { get; init; }
    public bool ReviewerLoginFieldValid { get; init; }
    public DateTimeOffset? SubmittedAt { get; init; }
    public bool SubmittedAtPresent { get; init; }
    public bool SubmittedAtFieldValid { get; init; }
    public string? ReviewUrl { get; init; }
    public bool ReviewUrlFieldValid { get; init; }
    public UnitStatusReviewBodyParse? BodyEvidence { get; init; }
    public bool CitedRecordValidatedForExpected { get; init; }
    public bool CitedRecordValidatedForDeclaredUnit { get; init; }
    public string RawPayload { get; init; } = "";
    public IndependentReviewEvidenceKind Classification { get; init; } = IndependentReviewEvidenceKind.Unrelated;
    public ReviewParseFailure ObserverFailureKind { get; init; }
    public string ObserverFailureDetail { get; init; } = "";
    public bool ObserverAccepted { get; init; }
    public UnitStatusObservedReview? ObserverReview { get; init; }

    public bool IsNamedIndependent => BodyEvidence?.NamedIndependent == true;
    public bool IsExplicitlyExcludedState => State is "PENDING" or "DISMISSED";
    public bool HasTrustedRestEnvelope => ReviewId is > 0
        && !string.IsNullOrWhiteSpace(ReviewerLogin)
        && SubmittedAt is not null
        && SubmittedAtFieldValid;
}
