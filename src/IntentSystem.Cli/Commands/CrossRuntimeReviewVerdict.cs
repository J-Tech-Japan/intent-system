using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G834: the reviewer verdict — the emitted JSON Schema and the hand-written
/// validator that enforces it, kept in one class so they cannot drift. There is
/// no JSON Schema library: the schema uses only <c>type</c> object / array /
/// string / integer, <c>enum</c>, <c>required</c>, <c>properties</c>, <c>items</c>,
/// and <c>additionalProperties: false</c>, with no conditional keywords, so
/// <c>codex exec --output-schema</c> and <c>claude --json-schema</c> accept it.
/// The approve / request-changes finding rules live in the validator.
/// </summary>
internal static class CrossRuntimeReviewVerdict
{
    public const string Approve = "approve";
    public const string RequestChanges = "request-changes";

    public const string FieldVerdict = "verdict";
    public const string FieldHeadSha = "head_sha";
    public const string FieldBlockingFindings = "blocking_findings";
    public const string FieldNotes = "notes";
    public const string FieldFile = "file";
    public const string FieldLine = "line";
    public const string FieldScenario = "scenario";

    public static readonly IReadOnlyList<string> VerdictValues = [Approve, RequestChanges];
    public static readonly IReadOnlyList<string> TopLevelFields = [FieldVerdict, FieldHeadSha, FieldBlockingFindings, FieldNotes];
    public static readonly IReadOnlyList<string> FindingFields = [FieldFile, FieldLine, FieldScenario];

    /// <summary>
    /// The schema text written to <c>verdict.schema.json</c>. It declares no
    /// <c>$schema</c> keyword: every keyword it uses means the same in draft
    /// 2020-12 and draft-07, and Claude Code 2.1.269 rejects
    /// <c>"$schema": "https://json-schema.org/draft/2020-12/schema"</c> in
    /// <c>--json-schema</c> ("no schema with key or ref"), measured 2026-09-14.
    /// </summary>
    public static string SchemaJson { get; } = BuildSchemaJson();

    private static string BuildSchemaJson()
    {
        var finding = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = FindingFields,
            ["properties"] = new Dictionary<string, object>
            {
                [FieldFile] = new Dictionary<string, object> { ["type"] = "string" },
                [FieldLine] = new Dictionary<string, object> { ["type"] = "integer" },
                [FieldScenario] = new Dictionary<string, object> { ["type"] = "string" },
            },
        };

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = TopLevelFields,
            ["properties"] = new Dictionary<string, object>
            {
                [FieldVerdict] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = VerdictValues },
                [FieldHeadSha] = new Dictionary<string, object> { ["type"] = "string" },
                [FieldBlockingFindings] = new Dictionary<string, object> { ["type"] = "array", ["items"] = finding },
                [FieldNotes] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object> { ["type"] = "string" },
                },
            },
        };

        return JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>
    /// Extracts the verdict object from what <paramref name="runtime"/> wrote.
    /// codex: the bare object <c>-o</c> writes. claude: the measured
    /// <c>--output-format json</c> result envelope carrying
    /// <c>structured_output</c>. cursor: the <c>--output-format json</c> result
    /// envelope whose <c>result</c> text ends with the verdict JSON. A bare object is
    /// refused for claude and cursor.
    /// </summary>
    public static bool TryParse(
        string runtime,
        string content,
        out CrossRuntimeReviewVerdictValue verdict,
        out string error)
    {
        verdict = null!;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content);
        }
        catch (JsonException exception)
        {
            error = $"verdict file is not valid JSON: {exception.Message}";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "verdict file must contain a JSON object.";
                return false;
            }

            switch (runtime)
            {
                case CrossRuntimeReviewRuntimes.Codex:
                    return TryValidate(root, out verdict, out error);

                case CrossRuntimeReviewRuntimes.Claude:
                {
                    if (!TryReadResultEnvelope(root, runtime, out error))
                    {
                        return false;
                    }

                    if (!root.TryGetProperty("structured_output", out var structured)
                        || structured.ValueKind != JsonValueKind.Object)
                    {
                        error = "claude envelope has no 'structured_output' object; run the pinned invocation with --json-schema.";
                        return false;
                    }

                    return TryValidate(structured, out verdict, out error);
                }

                case CrossRuntimeReviewRuntimes.Cursor:
                {
                    if (!TryReadResultEnvelope(root, runtime, out error))
                    {
                        return false;
                    }

                    if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.String)
                    {
                        error = "cursor envelope has no 'result' string.";
                        return false;
                    }

                    if (!TryParseTrailingObject(result.GetString()!, out var inner))
                    {
                        error = "cursor envelope 'result' does not end with a verdict JSON object.";
                        return false;
                    }

                    using (inner)
                    {
                        if (inner.RootElement.ValueKind != JsonValueKind.Object)
                        {
                            error = "cursor envelope 'result' must be a verdict JSON object.";
                            return false;
                        }

                        return TryValidate(inner.RootElement, out verdict, out error);
                    }
                }

                default:
                    error = $"runtime '{runtime}' is not supported ({CrossRuntimeReviewRuntimes.Describe()}).";
                    return false;
            }
        }
    }

    /// <summary>
    /// cursor-agent 2026.09.10 has no schema flag, and its measured
    /// <c>--output-format json</c> envelope concatenates the agent's progress
    /// narration and its final answer into <c>result</c> (for example
    /// <c>"Running each probe ...{\"verdict\":...}"</c>). The verdict is the JSON
    /// object that ends the text: the earliest <c>{</c> from which the whole
    /// remainder parses as exactly one object. Nothing after it is tolerated.
    /// </summary>
    internal static bool TryParseTrailingObject(string text, out JsonDocument document)
    {
        document = null!;
        var trimmed = text.TrimEnd();
        if (!trimmed.EndsWith('}'))
        {
            return false;
        }

        for (var index = trimmed.IndexOf('{', StringComparison.Ordinal); index >= 0; index = trimmed.IndexOf('{', index + 1))
        {
            try
            {
                var candidate = JsonDocument.Parse(trimmed[index..]);
                if (candidate.RootElement.ValueKind == JsonValueKind.Object)
                {
                    document = candidate;
                    return true;
                }

                candidate.Dispose();
            }
            catch (JsonException)
            {
            }
        }

        return false;
    }

    private static bool TryReadResultEnvelope(JsonElement root, string runtime, out string error)
    {
        if (root.TryGetProperty(FieldVerdict, out _) && !root.TryGetProperty("type", out _))
        {
            error = $"{runtime} requires the runtime JSON envelope from the pinned invocation; a bare verdict object is refused.";
            return false;
        }

        if (!root.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String
            || !string.Equals(type.GetString(), "result", StringComparison.Ordinal))
        {
            error = $"{runtime} envelope must have \"type\": \"result\".";
            return false;
        }

        if (!root.TryGetProperty("is_error", out var isError) || isError.ValueKind != JsonValueKind.False)
        {
            error = $"{runtime} envelope must have \"is_error\": false.";
            return false;
        }

        if (!root.TryGetProperty("subtype", out var subtype)
            || subtype.ValueKind != JsonValueKind.String
            || !string.Equals(subtype.GetString(), "success", StringComparison.Ordinal))
        {
            error = $"{runtime} envelope must have \"subtype\": \"success\".";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Enforces the schema exactly (required fields, types, enum, no additional
    /// properties) plus the two rules the schema cannot express:
    /// <c>request-changes</c> needs at least one blocking finding and
    /// <c>approve</c> needs none.
    /// </summary>
    public static bool TryValidate(JsonElement element, out CrossRuntimeReviewVerdictValue verdict, out string error)
    {
        verdict = null!;
        if (element.ValueKind != JsonValueKind.Object)
        {
            error = "verdict must be a JSON object.";
            return false;
        }

        if (!TryCheckFields(element, TopLevelFields, "verdict", out error))
        {
            return false;
        }

        var verdictElement = element.GetProperty(FieldVerdict);
        if (verdictElement.ValueKind != JsonValueKind.String
            || !VerdictValues.Contains(verdictElement.GetString(), StringComparer.Ordinal))
        {
            error = $"'{FieldVerdict}' must be \"{Approve}\" or \"{RequestChanges}\".";
            return false;
        }

        var headElement = element.GetProperty(FieldHeadSha);
        if (headElement.ValueKind != JsonValueKind.String)
        {
            error = $"'{FieldHeadSha}' must be a string.";
            return false;
        }

        var findingsElement = element.GetProperty(FieldBlockingFindings);
        if (findingsElement.ValueKind != JsonValueKind.Array)
        {
            error = $"'{FieldBlockingFindings}' must be an array.";
            return false;
        }

        var findings = new List<CrossRuntimeReviewFinding>();
        var index = 0;
        foreach (var item in findingsElement.EnumerateArray())
        {
            var name = $"{FieldBlockingFindings}[{index}]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                error = $"'{name}' must be an object.";
                return false;
            }

            if (!TryCheckFields(item, FindingFields, name, out error))
            {
                return false;
            }

            var file = item.GetProperty(FieldFile);
            var line = item.GetProperty(FieldLine);
            var scenario = item.GetProperty(FieldScenario);
            if (file.ValueKind != JsonValueKind.String)
            {
                error = $"'{name}.{FieldFile}' must be a string.";
                return false;
            }

            if (line.ValueKind != JsonValueKind.Number || !line.TryGetInt32(out var lineNumber))
            {
                error = $"'{name}.{FieldLine}' must be an integer.";
                return false;
            }

            if (scenario.ValueKind != JsonValueKind.String)
            {
                error = $"'{name}.{FieldScenario}' must be a string.";
                return false;
            }

            findings.Add(new CrossRuntimeReviewFinding
            {
                File = file.GetString()!,
                Line = lineNumber,
                Scenario = scenario.GetString()!,
            });
            index++;
        }

        var notesElement = element.GetProperty(FieldNotes);
        if (notesElement.ValueKind != JsonValueKind.Array)
        {
            error = $"'{FieldNotes}' must be an array.";
            return false;
        }

        var notes = new List<string>();
        foreach (var note in notesElement.EnumerateArray())
        {
            if (note.ValueKind != JsonValueKind.String)
            {
                error = $"every '{FieldNotes}' entry must be a string.";
                return false;
            }

            notes.Add(note.GetString()!);
        }

        var value = verdictElement.GetString()!;
        if (string.Equals(value, RequestChanges, StringComparison.Ordinal) && findings.Count == 0)
        {
            error = $"'{RequestChanges}' requires at least one blocking finding.";
            return false;
        }

        if (string.Equals(value, Approve, StringComparison.Ordinal) && findings.Count > 0)
        {
            error = $"'{Approve}' requires '{FieldBlockingFindings}' to be empty.";
            return false;
        }

        verdict = new CrossRuntimeReviewVerdictValue
        {
            Verdict = value,
            HeadSha = headElement.GetString()!,
            BlockingFindings = findings,
            Notes = notes,
        };
        error = string.Empty;
        return true;
    }

    private static bool TryCheckFields(JsonElement element, IReadOnlyList<string> fields, string name, out string error)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!fields.Contains(property.Name, StringComparer.Ordinal))
            {
                error = $"'{name}' has additional property '{property.Name}'.";
                return false;
            }
        }

        foreach (var field in fields)
        {
            if (!element.TryGetProperty(field, out _))
            {
                error = $"'{name}' is missing required property '{field}'.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }
}

internal sealed record CrossRuntimeReviewVerdictValue
{
    public required string Verdict { get; init; }

    public required string HeadSha { get; init; }

    public required IReadOnlyList<CrossRuntimeReviewFinding> BlockingFindings { get; init; }

    public required IReadOnlyList<string> Notes { get; init; }
}

internal sealed record CrossRuntimeReviewFinding
{
    [JsonPropertyName("file")]
    public required string File { get; init; }

    [JsonPropertyName("line")]
    public required int Line { get; init; }

    [JsonPropertyName("scenario")]
    public required string Scenario { get; init; }
}
