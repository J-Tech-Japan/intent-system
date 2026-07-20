using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G542: shared, read-only per-line inspection of a raw <c>runs.jsonl</c>
/// row against the RunEvent required-field contract (<c>ts</c>,
/// <c>event</c>, <c>execution_unit</c>, <c>by</c>), plus the two documented
/// lossless within-record derivations (design ruling, 2026-07-20):
/// <c>ts</c> ← the record's own <c>timestamp</c> field; <c>execution_unit</c>
/// ← <c>wip[0].eu</c> for <c>skip-next-slice-due-to-wip</c> rows, or
/// <c>stage1.eu</c> for <c>pr-merged-closeout</c> rows. Used by BOTH
/// <see cref="AutomationRunsAuditCommand"/> (the audit/repair surface) and
/// <see cref="PublishDurableArtifactAnalyzer"/> (domain-scoped durable-state
/// validation), so the two surfaces can never disagree about which rows are
/// malformed or which domain owns them.
///
/// Never throws: an unparseable line (not valid JSON, or valid JSON that is
/// not an object) is reported as malformed with every required field
/// missing, never propagated as an exception.
/// </summary>
internal static class RunsLogRowInspector
{
    public const string FieldTs = "ts";
    public const string FieldEvent = "event";
    public const string FieldExecutionUnit = "execution_unit";
    public const string FieldBy = "by";

    public static readonly IReadOnlyList<string> RequiredFields = new[]
    {
        FieldTs, FieldEvent, FieldExecutionUnit, FieldBy,
    };

    public const string EventSkipNextSliceDueToWip = "skip-next-slice-due-to-wip";
    public const string EventPrMergedCloseout = "pr-merged-closeout";

    public const string DerivationWithinRecord = "within-record";
    public const string DerivationInferredPeerConvention = "inferred-peer-convention";

    public const string SourceTimestampField = "timestamp";
    public const string SourceWipFirstEu = "wip[0].eu";
    public const string SourceStage1Eu = "stage1.eu";

    /// <summary>
    /// Inspects one raw runs.jsonl line (already known non-blank).
    /// Returns <c>null</c> when every required field is present as a
    /// non-empty JSON string — the row is valid, not a finding.
    /// </summary>
    public static RunsLogRowFinding? Inspect(int lineNumber, string rawLine)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawLine);
        }
        catch (JsonException exception)
        {
            return RunsLogRowFinding.CreateUnparseable(lineNumber, exception.Message);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return RunsLogRowFinding.CreateUnparseable(lineNumber, "line is valid JSON but not a JSON object");
            }

            var root = document.RootElement;
            var missing = new List<string>();
            foreach (var field in RequiredFields)
            {
                if (!TryGetNonEmptyString(root, field, out _))
                {
                    missing.Add(field);
                }
            }

            if (missing.Count == 0)
            {
                return null;
            }

            var repairs = new Dictionary<string, RunsLogFieldRepair>(StringComparer.Ordinal);

            if (missing.Contains(FieldTs) && TryGetRawPropertyValue(root, SourceTimestampField, out var timestampRaw))
            {
                repairs[FieldTs] = new RunsLogFieldRepair(FieldTs, timestampRaw, DerivationWithinRecord, SourceTimestampField);
            }

            if (missing.Contains(FieldExecutionUnit))
            {
                TryGetNonEmptyString(root, FieldEvent, out var eventForDerivation);
                if (string.Equals(eventForDerivation, EventSkipNextSliceDueToWip, StringComparison.Ordinal)
                    && TryGetWipFirstEu(root, out var wipEuRaw))
                {
                    repairs[FieldExecutionUnit] = new RunsLogFieldRepair(FieldExecutionUnit, wipEuRaw, DerivationWithinRecord, SourceWipFirstEu);
                }
                else if (string.Equals(eventForDerivation, EventPrMergedCloseout, StringComparison.Ordinal)
                    && TryGetStage1Eu(root, out var stage1EuRaw))
                {
                    repairs[FieldExecutionUnit] = new RunsLogFieldRepair(FieldExecutionUnit, stage1EuRaw, DerivationWithinRecord, SourceStage1Eu);
                }
            }

            TryGetNonEmptyString(root, FieldExecutionUnit, out var presentExecutionUnit);
            var effectiveExecutionUnit = presentExecutionUnit
                ?? (repairs.TryGetValue(FieldExecutionUnit, out var euRepair) ? TryUnquote(euRepair.RawValue) : null);
            TryGetNonEmptyString(root, FieldBy, out var byValue);
            TryGetNonEmptyString(root, FieldEvent, out var eventValue);

            return new RunsLogRowFinding
            {
                Line = lineNumber,
                MissingFields = missing,
                Repairs = repairs,
                ExecutionUnit = effectiveExecutionUnit,
                By = byValue,
                Event = eventValue,
                Unparseable = false,
                UnparseableDetail = null,
            };
        }
    }

    /// <summary>
    /// Resolves the domain that owns <paramref name="executionUnit"/>, best
    /// effort: (1) that unit's own <c>.intent-cli/issues/&lt;unit&gt;/packet.yaml</c>
    /// <c>domain:</c> field (nested <c>implementation_issue_packet.domain</c>
    /// preferred, top-level alias fallback) when the packet still exists on
    /// disk; (2) a unique match of <paramref name="executionUnit"/> against
    /// exactly one candidate domain's <c>execution_unit_regex</c>
    /// (<c>intents/&lt;domain&gt;/automation/bindings.md</c>); (3) a
    /// domain-like prefix in <paramref name="byValue"/> (e.g.
    /// <c>"&lt;domain&gt;/tool-name"</c>) that corroborates against a real
    /// domain directory. Returns <c>null</c> — "domain-underivable" — when
    /// none of these resolve, or when more than one candidate domain's regex
    /// matches (ambiguous). Never fails loud: this is advisory information
    /// for a report/warning, not a gate.
    /// </summary>
    public static string? ResolveOwningDomain(CliContext context, string? executionUnit, string? byValue, out string detail)
    {
        ArgumentNullException.ThrowIfNull(context);
        detail = "domain-underivable";

        if (!string.IsNullOrWhiteSpace(executionUnit))
        {
            var packetPath = Path.Combine(context.RepoRoot, ".intent-cli", "issues", executionUnit, "packet.yaml");
            if (File.Exists(packetPath))
            {
                try
                {
                    var fields = PreparedPacketYamlScalarParser.Parse(File.ReadAllText(packetPath));
                    if (fields.TryGetValue("implementation_issue_packet.domain", out var nested) && !string.IsNullOrWhiteSpace(nested))
                    {
                        detail = $"packet.yaml implementation_issue_packet.domain ({packetPath})";
                        return nested;
                    }
                    if (fields.TryGetValue("domain", out var top) && !string.IsNullOrWhiteSpace(top))
                    {
                        detail = $"packet.yaml domain ({packetPath})";
                        return top;
                    }
                }
                catch (FormatException)
                {
                    // Malformed packet.yaml — fall through to the other
                    // resolution paths rather than treating this as fatal;
                    // domain resolution here is advisory, not a gate.
                }
            }

            var candidates = DomainCandidateScanner.Scan(context);
            var matches = new List<string>();
            foreach (var candidate in candidates)
            {
                var resolution = NextSliceDomainBindingsExecutionUnitRegex.Resolve(context, candidate);
                if (resolution.Kind == ExecutionUnitRegexResolutionKind.Present
                    && resolution.Regex!.IsMatch(executionUnit))
                {
                    matches.Add(candidate);
                }
            }

            if (matches.Count == 1)
            {
                detail = $"execution_unit_regex match (intents/{matches[0]}/automation/bindings.md)";
                return matches[0];
            }

            if (matches.Count > 1)
            {
                detail = $"ambiguous: matched {matches.Count} domains via execution_unit_regex ({string.Join(", ", matches)})";
                return null;
            }
        }

        if (!string.IsNullOrWhiteSpace(byValue) && byValue.Contains('/', StringComparison.Ordinal))
        {
            var candidateDomain = byValue.Split('/', 2)[0];
            var candidates = DomainCandidateScanner.Scan(context);
            if (candidates.Contains(candidateDomain, StringComparer.Ordinal))
            {
                detail = $"'by' field domain-like prefix ('{byValue}')";
                return candidateDomain;
            }
        }

        return null;
    }

    private static bool TryGetNonEmptyString(JsonElement root, string key, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(key, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = element.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text;
        return true;
    }

    private static bool TryGetRawPropertyValue(JsonElement root, string key, out string rawValue)
    {
        rawValue = string.Empty;
        if (!root.TryGetProperty(key, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        rawValue = element.GetRawText();
        return true;
    }

    private static bool TryGetWipFirstEu(JsonElement root, out string rawValue)
    {
        rawValue = string.Empty;
        if (!root.TryGetProperty("wip", out var wip) || wip.ValueKind != JsonValueKind.Array || wip.GetArrayLength() == 0)
        {
            return false;
        }

        var first = wip[0];
        if (first.ValueKind != JsonValueKind.Object
            || !first.TryGetProperty("eu", out var eu)
            || eu.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(eu.GetString()))
        {
            return false;
        }

        rawValue = eu.GetRawText();
        return true;
    }

    private static bool TryGetStage1Eu(JsonElement root, out string rawValue)
    {
        rawValue = string.Empty;
        if (!root.TryGetProperty("stage1", out var stage1) || stage1.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!stage1.TryGetProperty("eu", out var eu)
            || eu.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(eu.GetString()))
        {
            return false;
        }

        rawValue = eu.GetRawText();
        return true;
    }

    private static string? TryUnquote(string rawJsonValue)
    {
        try
        {
            return JsonSerializer.Deserialize<string>(rawJsonValue);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>A within-record, lossless repair for one missing required field.</summary>
internal sealed record RunsLogFieldRepair(string Field, string RawValue, string Derivation, string Source);

/// <summary>
/// The result of inspecting one malformed runs.jsonl row. Never constructed
/// for a valid row (see <see cref="RunsLogRowInspector.Inspect"/>).
/// </summary>
internal sealed record RunsLogRowFinding
{
    public required int Line { get; init; }

    public required IReadOnlyList<string> MissingFields { get; init; }

    /// <summary>Within-record repairs only, keyed by field name. Never includes inferred (peer-convention) repairs.</summary>
    public required IReadOnlyDictionary<string, RunsLogFieldRepair> Repairs { get; init; }

    public string? ExecutionUnit { get; init; }

    public string? By { get; init; }

    public string? Event { get; init; }

    public required bool Unparseable { get; init; }

    public string? UnparseableDetail { get; init; }

    public IReadOnlyList<string> NonDerivableFields => MissingFields.Where(missingField => !Repairs.ContainsKey(missingField)).ToArray();

    public static RunsLogRowFinding CreateUnparseable(int line, string detail) => new()
    {
        Line = line,
        MissingFields = RunsLogRowInspector.RequiredFields,
        Repairs = new Dictionary<string, RunsLogFieldRepair>(StringComparer.Ordinal),
        Unparseable = true,
        UnparseableDetail = detail,
    };
}
