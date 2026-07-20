using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G542: read-only-by-default <c>intent-cli automation runs-audit</c>.
/// Reports EVERY malformed <c>runs.jsonl</c> row in one pass — never stops
/// at the first offender, unlike <see cref="RunLogSerializer.DeserializeAll"/>
/// / <see cref="PublishDurableArtifactAnalyzer"/>'s whole-file parse. Field
/// incident, 2026-07-20: publishing G539 was refused twice by legacy rows
/// belonging to an unrelated domain, each repair merely revealing the next
/// offender one at a time.
///
/// <c>--write</c> applies ONLY within-record repairs (see
/// <see cref="RunsLogRowInspector"/> for the two documented derivation
/// shapes) — never a field with no source inside the record itself (e.g.
/// <c>by</c>, which has no within-record source). Such fields are always
/// listed as <c>non_derivable</c> and refused by <c>--write</c>; a peer-
/// convention <c>inferred_suggestion</c> may be reported alongside its
/// evidence, but is only ever applied by the separate, explicit
/// <c>--apply-inferred</c> flag (never implied by <c>--write</c> alone),
/// recording <c>derivation: inferred-peer-convention</c> in its own audit
/// event so the trail distinguishes known-from-record facts from inferred
/// peer convention. Every repair — of either derivation class — appends its
/// own <c>runs-repair</c> audit event and preserves the rest of the file
/// (and the rest of the repaired line) byte-for-byte.
///
/// Never launches an AI provider; never mutates GitHub state; the only
/// mutation this command ever performs is to <c>runs.jsonl</c> itself, and
/// only under explicit <c>--write</c>.
/// </summary>
internal static class AutomationRunsAuditCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string RepairEventName = "runs-repair";
    private const string RepairEventBy = "intent-cli automation runs-audit";

    private const string UsageLine =
        "Usage: intent-cli automation runs-audit [--repo <owner/repo>] [--domain <name>] [--write] [--apply-inferred] [--format json|markdown]";

    /// <summary>Test seam: overrides the clock used for appended `runs-repair` events. Reset to null after each test.</summary>
    internal static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseArguments(args, out var repo, out var domain, out var write, out var applyInferred, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (applyInferred && !write)
        {
            writer.WriteLine("--apply-inferred requires --write (it is never implied by --write alone, but it never applies anything on its own either).");
            writer.WriteLine(UsageLine);
            return 1;
        }

        var runLogPath = context.GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            EmitResult(writer, format, BuildResult(repo, domain, runLogPath, totalRows: 0, rows: Array.Empty<RunsAuditRowReport>(), write, applyInferred, repairsApplied: 0));
            return 0;
        }

        var originalContent = File.ReadAllText(runLogPath);
        var rawLines = originalContent.Split('\n');

        var findings = new List<RunsLogRowFinding>();
        var peerByPerEvent = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var totalRows = 0;

        for (var index = 0; index < rawLines.Length; index++)
        {
            var (jsonPart, _) = SplitTrailingCarriageReturn(rawLines[index]);
            if (string.IsNullOrWhiteSpace(jsonPart))
            {
                continue;
            }

            var lineNumber = index + 1;

            RunEvent validEvent;
            try
            {
                validEvent = RunLogSerializer.DeserializeLine(jsonPart);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
            {
                totalRows++;
                var finding = RunsLogRowInspector.Inspect(lineNumber, jsonPart);
                if (finding is not null)
                {
                    findings.Add(finding);
                }
                continue;
            }

            totalRows++;
            if (!peerByPerEvent.TryGetValue(validEvent.Event, out var byValues))
            {
                byValues = new List<string>();
                peerByPerEvent[validEvent.Event] = byValues;
            }
            byValues.Add(validEvent.By);
        }

        var utcNow = UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow;
        var patchesByLine = new Dictionary<int, List<(string Field, string RawValue)>>();
        var repairEvents = new List<RunEvent>();
        var rows = new List<RunsAuditRowReport>();

        foreach (var finding in findings)
        {
            string? owningDomain = null;
            string? owningDomainDetail = null;
            if (!finding.Unparseable)
            {
                owningDomain = RunsLogRowInspector.ResolveOwningDomain(context, finding.ExecutionUnit, finding.By, out owningDomainDetail);
            }

            var withinRecordFields = RunsLogRowInspector.RequiredFields
                .Where(finding.Repairs.ContainsKey)
                .Select(field => finding.Repairs[field])
                .ToArray();

            RunsAuditInferredSuggestionReport? inferredSuggestion = null;
            string? inferredByValue = null;
            if (!finding.Unparseable
                && finding.MissingFields.Contains(RunsLogRowInspector.FieldBy)
                && !finding.Repairs.ContainsKey(RunsLogRowInspector.FieldBy)
                && !string.IsNullOrWhiteSpace(finding.Event)
                && TryComputeInferredBySuggestion(finding.Event, peerByPerEvent, out var suggestedBy, out var evidence))
            {
                inferredSuggestion = new RunsAuditInferredSuggestionReport { Field = RunsLogRowInspector.FieldBy, Value = suggestedBy, Evidence = evidence };
                inferredByValue = suggestedBy;
            }

            // `execution_unit` itself missing with no within-record source
            // (event isn't one of the two documented derivable shapes)
            // leaves no safe anchor to attribute a `runs-repair` audit event
            // to — refuse ALL repair for this row rather than fabricate an
            // "unknown" execution unit on a durable audit trail.
            var hasAnchor = !string.IsNullOrWhiteSpace(finding.ExecutionUnit);

            if (write && !finding.Unparseable && hasAnchor)
            {
                if (withinRecordFields.Length > 0)
                {
                    AddPatch(patchesByLine, finding.Line, withinRecordFields.Select(r => (r.Field, r.RawValue)));
                    repairEvents.Add(BuildRepairEvent(finding.Line, finding.ExecutionUnit!, withinRecordFields, RunsLogRowInspector.DerivationWithinRecord, utcNow));
                }

                if (applyInferred && inferredByValue is not null)
                {
                    var rawValue = JsonSerializer.Serialize(inferredByValue);
                    AddPatch(patchesByLine, finding.Line, new[] { (RunsLogRowInspector.FieldBy, rawValue) });
                    repairEvents.Add(BuildRepairEvent(
                        finding.Line,
                        finding.ExecutionUnit!,
                        new[] { new RunsLogFieldRepair(RunsLogRowInspector.FieldBy, rawValue, RunsLogRowInspector.DerivationInferredPeerConvention, "peer-convention") },
                        RunsLogRowInspector.DerivationInferredPeerConvention,
                        utcNow));
                }
            }

            rows.Add(new RunsAuditRowReport
            {
                Line = finding.Line,
                MissingFields = finding.MissingFields,
                OwningDomain = owningDomain,
                OwningDomainDetail = owningDomainDetail,
                Unparseable = finding.Unparseable,
                UnparseableDetail = finding.UnparseableDetail,
                Repairs = withinRecordFields.Select(r => new RunsAuditRepairReport
                {
                    Field = r.Field,
                    Value = DisplayValue(r.RawValue),
                    Derivation = r.Derivation,
                    Source = r.Source,
                }).ToArray(),
                NonDerivableFields = finding.NonDerivableFields,
                InferredSuggestions = inferredSuggestion is null
                    ? Array.Empty<RunsAuditInferredSuggestionReport>()
                    : new[] { inferredSuggestion.Value },
            });
        }

        var repairsApplied = repairEvents.Count;
        if (write && repairsApplied > 0)
        {
            var patchedLines = new string[rawLines.Length];
            for (var index = 0; index < rawLines.Length; index++)
            {
                var lineNumber = index + 1;
                if (patchesByLine.TryGetValue(lineNumber, out var insertions))
                {
                    var (jsonPart, trailingCr) = SplitTrailingCarriageReturn(rawLines[index]);
                    patchedLines[index] = PatchLine(jsonPart, insertions) + trailingCr;
                }
                else
                {
                    patchedLines[index] = rawLines[index];
                }
            }

            var patchedContent = string.Join('\n', patchedLines);
            var appended = string.Concat(repairEvents.Select(evt => RunLogSerializer.SerializeLine(evt) + "\n"));
            var separator = patchedContent.Length > 0 && !patchedContent.EndsWith('\n') ? "\n" : string.Empty;
            File.WriteAllText(runLogPath, patchedContent + separator + appended);
        }

        EmitResult(writer, format, BuildResult(repo, domain, runLogPath, totalRows, rows, write, applyInferred, repairsApplied));
        return 0;
    }

    private static void AddPatch(Dictionary<int, List<(string Field, string RawValue)>> patches, int line, IEnumerable<(string Field, string RawValue)> fields)
    {
        if (!patches.TryGetValue(line, out var list))
        {
            list = new List<(string, string)>();
            patches[line] = list;
        }
        list.AddRange(fields);
    }

    private static string PatchLine(string jsonPart, IReadOnlyList<(string Field, string RawValue)> insertions)
    {
        if (insertions.Count == 0)
        {
            return jsonPart;
        }

        var braceIndex = jsonPart.IndexOf('{');
        var insertText = string.Concat(insertions.Select(kv => $"\"{kv.Field}\":{kv.RawValue},"));
        return jsonPart[..(braceIndex + 1)] + insertText + jsonPart[(braceIndex + 1)..];
    }

    private static (string JsonPart, string TrailingCr) SplitTrailingCarriageReturn(string rawLine)
    {
        return rawLine.EndsWith('\r') ? (rawLine[..^1], "\r") : (rawLine, string.Empty);
    }

    private static RunEvent BuildRepairEvent(int line, string executionUnit, IReadOnlyList<RunsLogFieldRepair> repairs, string derivation, DateTimeOffset utcNow)
    {
        var fieldNames = string.Join(",", repairs.Select(r => r.Field));
        var sources = string.Join(",", repairs.Select(r => r.Source));
        return new RunEvent
        {
            Ts = utcNow,
            ExecutionUnit = executionUnit,
            Event = RepairEventName,
            By = RepairEventBy,
            Reason = $"line {line}: repaired {fieldNames} (derivation: {derivation}; source: {sources})",
        };
    }

    private static bool TryComputeInferredBySuggestion(string eventName, IReadOnlyDictionary<string, List<string>> peerByPerEvent, out string value, out string evidence)
    {
        value = string.Empty;
        evidence = string.Empty;
        if (!peerByPerEvent.TryGetValue(eventName, out var byValues) || byValues.Count == 0)
        {
            return false;
        }

        var grouped = byValues
            .GroupBy(v => v, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ToList();
        var top = grouped[0];
        var topCount = top.Count();
        if (topCount * 2 <= byValues.Count)
        {
            // Not a majority — no clear peer convention to suggest.
            return false;
        }

        value = top.Key;
        evidence = topCount == byValues.Count
            ? $"all {byValues.Count} peer record(s) of event '{eventName}' use by={top.Key}"
            : $"{topCount} of {byValues.Count} peer record(s) of event '{eventName}' use by={top.Key}";
        return true;
    }

    private static string DisplayValue(string rawJsonValue)
    {
        try
        {
            return JsonSerializer.Deserialize<string>(rawJsonValue) ?? rawJsonValue;
        }
        catch (JsonException)
        {
            return rawJsonValue;
        }
    }

    private static RunsAuditResult BuildResult(
        string? repo,
        string? domain,
        string runLogPath,
        int totalRows,
        IReadOnlyList<RunsAuditRowReport> rows,
        bool write,
        bool applyInferred,
        int repairsApplied)
    {
        return new RunsAuditResult
        {
            Repo = repo,
            Domain = domain,
            RunLogPath = runLogPath,
            TotalRows = totalRows,
            MalformedRowCount = rows.Count,
            MalformedRows = rows,
            Write = write,
            ApplyInferred = applyInferred,
            RepairsApplied = repairsApplied,
            Clean = rows.Count == 0,
        };
    }

    private static void EmitResult(TextWriter writer, string format, RunsAuditResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
            return;
        }

        WriteMarkdown(writer, result);
    }

    private static void WriteMarkdown(TextWriter writer, RunsAuditResult result)
    {
        writer.WriteLine("# Runs-log audit (G542)");
        writer.WriteLine();
        writer.WriteLine($"- run log path: {result.RunLogPath}");
        if (result.Repo is not null)
        {
            writer.WriteLine($"- repo: {result.Repo}");
        }
        if (result.Domain is not null)
        {
            writer.WriteLine($"- domain: {result.Domain}");
        }
        writer.WriteLine($"- total rows: {result.TotalRows}");
        writer.WriteLine($"- malformed rows: {result.MalformedRowCount}");
        writer.WriteLine($"- write: {(result.Write ? "yes" : "no")}");
        writer.WriteLine($"- apply-inferred: {(result.ApplyInferred ? "yes" : "no")}");
        writer.WriteLine($"- repairs applied: {result.RepairsApplied}");
        writer.WriteLine();

        if (result.Clean)
        {
            writer.WriteLine("Clean — every row satisfies the RunEvent required-field contract.");
            return;
        }

        writer.WriteLine("## Malformed rows");
        foreach (var row in result.MalformedRows)
        {
            writer.WriteLine();
            writer.WriteLine($"### line {row.Line}");
            writer.WriteLine();
            if (row.Unparseable)
            {
                writer.WriteLine($"- unparseable: {row.UnparseableDetail}");
                continue;
            }

            writer.WriteLine($"- missing fields: {string.Join(", ", row.MissingFields)}");
            writer.WriteLine($"- owning domain: {row.OwningDomain ?? "(underivable)"}" + (row.OwningDomainDetail is null ? string.Empty : $" ({row.OwningDomainDetail})"));
            if (row.Repairs.Count > 0)
            {
                foreach (var repair in row.Repairs)
                {
                    writer.WriteLine($"- repair: {repair.Field} <- \"{repair.Value}\" (derivation: {repair.Derivation}; source: {repair.Source})");
                }
            }
            else
            {
                writer.WriteLine("- repair: (none within-record)");
            }
            if (row.NonDerivableFields.Count > 0)
            {
                writer.WriteLine($"- non-derivable fields: {string.Join(", ", row.NonDerivableFields)}");
            }
            foreach (var suggestion in row.InferredSuggestions)
            {
                writer.WriteLine($"- inferred suggestion: {suggestion.Field} -> \"{suggestion.Value}\" ({suggestion.Evidence})");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out string? domain,
        out bool write,
        out bool applyInferred,
        out string format,
        out string error)
    {
        repo = null;
        domain = null;
        write = false;
        applyInferred = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value.";
                        return false;
                    }
                    repo = args[index + 1];
                    index++;
                    break;

                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }
                    domain = args[index + 1];
                    index++;
                    break;

                case "--write":
                    write = true;
                    break;

                case "--apply-inferred":
                    applyInferred = true;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requested = args[index + 1];
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }
                    format = requested;
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("automation runs-audit");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only by default: reports EVERY malformed runs.jsonl row in one pass (line, missing");
        writer.WriteLine("fields, owning domain, and a within-record repair when one is losslessly derivable).");
        writer.WriteLine("--write applies only within-record repairs and appends a runs-repair audit event per");
        writer.WriteLine("repair; a field with no within-record source (e.g. by) is always left non-derivable.");
        writer.WriteLine("--apply-inferred (requires --write) additionally applies peer-convention suggestions,");
        writer.WriteLine("recording derivation: inferred-peer-convention.");
    }
}

internal sealed record RunsAuditResult
{
    [JsonPropertyName("repo")]
    public string? Repo { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("run_log_path")]
    public required string RunLogPath { get; init; }

    [JsonPropertyName("total_rows")]
    public required int TotalRows { get; init; }

    [JsonPropertyName("malformed_row_count")]
    public required int MalformedRowCount { get; init; }

    [JsonPropertyName("malformed_rows")]
    public required IReadOnlyList<RunsAuditRowReport> MalformedRows { get; init; }

    [JsonPropertyName("write")]
    public required bool Write { get; init; }

    [JsonPropertyName("apply_inferred")]
    public required bool ApplyInferred { get; init; }

    [JsonPropertyName("repairs_applied")]
    public required int RepairsApplied { get; init; }

    [JsonPropertyName("clean")]
    public required bool Clean { get; init; }
}

internal sealed record RunsAuditRowReport
{
    [JsonPropertyName("line")]
    public required int Line { get; init; }

    [JsonPropertyName("missing_fields")]
    public required IReadOnlyList<string> MissingFields { get; init; }

    [JsonPropertyName("owning_domain")]
    public string? OwningDomain { get; init; }

    [JsonPropertyName("owning_domain_detail")]
    public string? OwningDomainDetail { get; init; }

    [JsonPropertyName("unparseable")]
    public required bool Unparseable { get; init; }

    [JsonPropertyName("unparseable_detail")]
    public string? UnparseableDetail { get; init; }

    [JsonPropertyName("repairs")]
    public required IReadOnlyList<RunsAuditRepairReport> Repairs { get; init; }

    [JsonPropertyName("non_derivable_fields")]
    public required IReadOnlyList<string> NonDerivableFields { get; init; }

    [JsonPropertyName("inferred_suggestions")]
    public required IReadOnlyList<RunsAuditInferredSuggestionReport> InferredSuggestions { get; init; }
}

internal sealed record RunsAuditRepairReport
{
    [JsonPropertyName("field")]
    public required string Field { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }

    [JsonPropertyName("derivation")]
    public required string Derivation { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }
}

internal readonly record struct RunsAuditInferredSuggestionReport
{
    [JsonPropertyName("field")]
    public required string Field { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }

    [JsonPropertyName("evidence")]
    public required string Evidence { get; init; }
}
