using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G451: resolves the <see cref="ReviewStandingPolicy"/> for a domain from an
/// OPTIONAL host policy file at <c>.intent-cli/review-policy.json</c>.
///
/// Resolution is fail-closed and backward compatible:
/// <list type="bullet">
///   <item>no file → the built-in safe defaults (source
///         <c>built-in-default</c>); identical to prior behavior.</item>
///   <item>valid file → the file's sections override the defaults; any omitted
///         section keeps its default (source <c>domain-file</c>).</item>
///   <item>invalid/unparseable file → the built-in defaults plus a warning
///         (source <c>invalid-fallback-default</c>); the command NEVER crashes
///         and never silently drops operator clarification.</item>
/// </list>
///
/// The loader is read-only — it never writes the policy file and never mutates
/// host state.
/// </summary>
internal static class ReviewStandingPolicyRegistry
{
    internal const string PolicyFileName = "review-policy.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        // Accept snake_case keys (device_gated_evidence, approve_with_recorded_gap_allowed)
        // and, via case-insensitivity, camelCase/PascalCase too.
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ReviewStandingPolicy Resolve(CliContext context, string? domain)
    {
        ArgumentNullException.ThrowIfNull(context);

        var path = Path.Combine(context.GetIntentCliDirectoryPath(), PolicyFileName);
        if (!File.Exists(path))
        {
            return ReviewStandingPolicy.Default(domain);
        }

        string raw;
        try
        {
            raw = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fallback(domain, $"could not read review-policy.json ({exception.Message}); using built-in defaults.");
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return Fallback(domain, "review-policy.json is empty; using built-in defaults.");
        }

        ReviewPolicyFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ReviewPolicyFile>(raw, ReadOptions);
        }
        catch (JsonException exception)
        {
            return Fallback(domain, $"review-policy.json is not valid JSON ({exception.Message}); using built-in defaults.");
        }

        if (file is null)
        {
            return Fallback(domain, "review-policy.json deserialized to null; using built-in defaults.");
        }

        return Merge(file, domain);
    }

    private static ReviewStandingPolicy Fallback(string? domain, string warning)
    {
        var defaults = ReviewStandingPolicy.Default(domain);
        return defaults with
        {
            Source = ReviewStandingPolicySources.InvalidFallbackDefault,
            Warnings = [warning],
        };
    }

    private static ReviewStandingPolicy Merge(ReviewPolicyFile file, string? domain)
    {
        var defaults = ReviewStandingPolicy.Default(domain);
        var warnings = new List<string>();

        // Each section is optional; an omitted (or empty) section keeps the
        // safe default so a partial policy file never removes guidance.
        var device = defaults.DeviceGatedEvidence;
        if (file.DeviceGatedEvidence is { } deviceFile)
        {
            device = new ReviewDeviceGatedEvidencePolicy
            {
                ApproveWithRecordedGapAllowed = deviceFile.ApproveWithRecordedGapAllowed
                    ?? defaults.DeviceGatedEvidence.ApproveWithRecordedGapAllowed,
                HardBlockCategories = NonEmpty(deviceFile.HardBlockCategories)
                    ?? defaults.DeviceGatedEvidence.HardBlockCategories,
                Rules = NonEmpty(deviceFile.Rules) ?? defaults.DeviceGatedEvidence.Rules,
            };
        }

        return defaults with
        {
            Source = ReviewStandingPolicySources.DomainFile,
            Domain = string.IsNullOrWhiteSpace(file.Domain) ? domain : file.Domain,
            Warnings = warnings,
            DeviceGatedEvidence = device,
            DraftHandling = Section(file.DraftHandling, defaults.DraftHandling),
            ExternalArtifactIntake = Section(file.ExternalArtifactIntake, defaults.ExternalArtifactIntake),
            TestEvidenceSufficiency = Section(file.TestEvidenceSufficiency, defaults.TestEvidenceSufficiency),
            FollowUpTracking = Section(file.FollowUpTracking, defaults.FollowUpTracking),
        };
    }

    private static ReviewPolicySection Section(ReviewPolicySectionFile? file, ReviewPolicySection fallback)
    {
        var rules = file is null ? null : NonEmpty(file.Rules);
        return rules is null ? fallback : new ReviewPolicySection { Rules = rules };
    }

    private static IReadOnlyList<string>? NonEmpty(IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return null;
        }

        var cleaned = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        return cleaned.Length == 0 ? null : cleaned;
    }

    // ── on-disk schema (all fields optional) ────────────────────────────────

    private sealed record ReviewPolicyFile
    {
        public string? Domain { get; init; }
        public ReviewDeviceGatedEvidenceFile? DeviceGatedEvidence { get; init; }
        public ReviewPolicySectionFile? DraftHandling { get; init; }
        public ReviewPolicySectionFile? ExternalArtifactIntake { get; init; }
        public ReviewPolicySectionFile? TestEvidenceSufficiency { get; init; }
        public ReviewPolicySectionFile? FollowUpTracking { get; init; }
    }

    private sealed record ReviewDeviceGatedEvidenceFile
    {
        public bool? ApproveWithRecordedGapAllowed { get; init; }
        public IReadOnlyList<string>? HardBlockCategories { get; init; }
        public IReadOnlyList<string>? Rules { get; init; }
    }

    private sealed record ReviewPolicySectionFile
    {
        public IReadOnlyList<string>? Rules { get; init; }
    }
}
