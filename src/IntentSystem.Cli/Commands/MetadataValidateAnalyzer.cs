using System.Text.Json;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G207: Pure deterministic mechanical metadata validator. Given the raw
/// file contents that the command read off disk, runs the validation rules
/// listed in #519 and returns a <see cref="MetadataValidateResult"/>.
///
/// Pure: no I/O, no Process.Start, no GitHub network, no provider launch.
/// Tests inject <see cref="MetadataValidateInputs"/> directly to avoid
/// touching the filesystem.
/// </summary>
internal static class MetadataValidateAnalyzer
{
    private static readonly Regex MarkdownHeadingLineRegex = new(
        @"^[ \t]*#{1,6}[ \t]+(?<text>.+?)[ \t]*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex YamlScalarKeyRegex = new(
        // Top-level scalar key: "key:" possibly with a value on the same line.
        @"^(?<key>[A-Za-z_][A-Za-z0-9_\-]*)\s*:\s*(?<value>.*)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static MetadataValidateResult Analyze(MetadataValidateInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputs.ExecutionUnit);

        var errors = new List<MetadataValidateFinding>();
        var warnings = new List<MetadataValidateFinding>();
        var checkedFiles = new List<string>();

        // ---- packet.yaml (required) ----------------------------------------
        var packetPath = $".intent-cli/issues/{inputs.ExecutionUnit}/packet.yaml";
        checkedFiles.Add(packetPath);
        IReadOnlyDictionary<string, string>? packetFields = null;
        if (inputs.PacketYaml is null)
        {
            errors.Add(new MetadataValidateFinding
            {
                Code = MetadataValidateConstants.Codes.PacketFileMissing,
                Message = $"required packet file not found: {packetPath}",
                Path = packetPath,
            });
        }
        else
        {
            try
            {
                packetFields = ParseYamlScalars(inputs.PacketYaml);
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException)
            {
                errors.Add(new MetadataValidateFinding
                {
                    Code = MetadataValidateConstants.Codes.PacketYamlUnparseable,
                    Message = $"could not parse {packetPath}: {exception.Message}",
                    Path = packetPath,
                });
            }

            if (packetFields is not null)
            {
                if (!HasNonEmptyValue(packetFields, "execution_unit")
                    && !HasNonEmptyValue(packetFields, "executionUnit"))
                {
                    errors.Add(new MetadataValidateFinding
                    {
                        Code = MetadataValidateConstants.Codes.PacketMissingExecutionUnit,
                        Message = "packet.yaml is missing execution_unit / executionUnit.",
                        Path = packetPath,
                    });
                }
                if (!HasNonEmptyValue(packetFields, "title"))
                {
                    errors.Add(new MetadataValidateFinding
                    {
                        Code = MetadataValidateConstants.Codes.PacketMissingTitle,
                        Message = "packet.yaml is missing title.",
                        Path = packetPath,
                    });
                }
            }
        }

        // ---- github-body.md (required + standalone sections) ----------------
        var bodyPath = $".intent-cli/issues/{inputs.ExecutionUnit}/github-body.md";
        checkedFiles.Add(bodyPath);
        if (inputs.GithubBodyMarkdown is null)
        {
            errors.Add(new MetadataValidateFinding
            {
                Code = MetadataValidateConstants.Codes.GithubBodyMissing,
                Message = $"required github-body file not found: {bodyPath}",
                Path = bodyPath,
            });
        }
        else
        {
            var headings = ExtractHeadings(inputs.GithubBodyMarkdown);
            foreach (var section in MetadataValidateConstants.RequiredGithubBodySections)
            {
                if (!HasMatchingHeading(headings, section))
                {
                    errors.Add(new MetadataValidateFinding
                    {
                        Code = MetadataValidateConstants.Codes.GithubBodyMissingSection,
                        Message = $"github-body.md is missing required section '{section}'.",
                        Path = bodyPath,
                    });
                }
            }
        }

        // ---- review-context.md (required + sections) ------------------------
        var reviewPath = $".intent-cli/issues/{inputs.ExecutionUnit}/review-context.md";
        checkedFiles.Add(reviewPath);
        if (inputs.ReviewContextMarkdown is null)
        {
            errors.Add(new MetadataValidateFinding
            {
                Code = MetadataValidateConstants.Codes.ReviewContextMissing,
                Message = $"required review-context file not found: {reviewPath}",
                Path = reviewPath,
            });
        }
        else
        {
            var headings = ExtractHeadings(inputs.ReviewContextMarkdown);
            foreach (var section in MetadataValidateConstants.RequiredReviewContextSections)
            {
                if (!HasMatchingHeading(headings, section))
                {
                    warnings.Add(new MetadataValidateFinding
                    {
                        Code = MetadataValidateConstants.Codes.ReviewContextMissingSection,
                        Message = $"review-context.md is missing recommended section '{section}'.",
                        Path = reviewPath,
                    });
                }
            }
        }

        // ---- implementation.md (recommended; warning only) ------------------
        var implPath = $".intent-cli/issues/{inputs.ExecutionUnit}/implementation.md";
        checkedFiles.Add(implPath);
        if (inputs.ImplementationMarkdown is null)
        {
            warnings.Add(new MetadataValidateFinding
            {
                Code = MetadataValidateConstants.Codes.ImplementationFileMissing,
                Message = $"recommended implementation file not found: {implPath}",
                Path = implPath,
            });
        }

        // ---- publish.yaml (optional, but if present must be coherent) -------
        var publishPath = $".intent-cli/issues/{inputs.ExecutionUnit}/publish.yaml";
        IReadOnlyDictionary<string, string>? publishFields = null;
        if (inputs.PublishYaml is not null)
        {
            checkedFiles.Add(publishPath);
            try
            {
                publishFields = ParseYamlScalars(inputs.PublishYaml);
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException)
            {
                errors.Add(new MetadataValidateFinding
                {
                    Code = MetadataValidateConstants.Codes.PublishYamlUnparseable,
                    Message = $"could not parse {publishPath}: {exception.Message}",
                    Path = publishPath,
                });
            }

            if (publishFields is not null)
            {
                if (!HasNonEmptyValue(publishFields, "issue_number")
                    && !HasNonEmptyValue(publishFields, "issueNumber"))
                {
                    errors.Add(new MetadataValidateFinding
                    {
                        Code = MetadataValidateConstants.Codes.PublishMissingIssueNumber,
                        Message = "publish.yaml is missing issue_number / issueNumber.",
                        Path = publishPath,
                    });
                }
                if (!HasNonEmptyValue(publishFields, "issue_url")
                    && !HasNonEmptyValue(publishFields, "issueUrl"))
                {
                    errors.Add(new MetadataValidateFinding
                    {
                        Code = MetadataValidateConstants.Codes.PublishMissingIssueUrl,
                        Message = "publish.yaml is missing issue_url / issueUrl.",
                        Path = publishPath,
                    });
                }
            }
        }

        // ---- queue-state.json (required) -----------------------------------
        const string queueStatePath = ".intent-cli/queue-state.json";
        checkedFiles.Add(queueStatePath);
        QueueStateEntry? queueEntry = null;
        if (inputs.QueueStateJson is null)
        {
            errors.Add(new MetadataValidateFinding
            {
                Code = MetadataValidateConstants.Codes.QueueStateMissing,
                Message = $"required queue-state file not found: {queueStatePath}",
                Path = queueStatePath,
            });
        }
        else
        {
            try
            {
                queueEntry = FindQueueEntry(inputs.QueueStateJson, inputs.ExecutionUnit);
            }
            catch (JsonException exception)
            {
                errors.Add(new MetadataValidateFinding
                {
                    Code = MetadataValidateConstants.Codes.QueueStateUnparseable,
                    Message = $"could not parse {queueStatePath}: {exception.Message}",
                    Path = queueStatePath,
                });
            }

            if (queueEntry is null && inputs.QueueStateJson.Trim().Length > 0)
            {
                errors.Add(new MetadataValidateFinding
                {
                    Code = MetadataValidateConstants.Codes.QueueEntryMissing,
                    Message = $"queue-state.json has no entry for execution unit '{inputs.ExecutionUnit}'.",
                    Path = queueStatePath,
                });
            }
        }

        // ---- cross-file consistency ----------------------------------------
        if (publishFields is not null && queueEntry is not null)
        {
            var publishIssue = TryGetInt(publishFields, "issue_number")
                ?? TryGetInt(publishFields, "issueNumber");
            if (publishIssue.HasValue
                && queueEntry.LinkedIssue.HasValue
                && publishIssue.Value != queueEntry.LinkedIssue.Value)
            {
                errors.Add(new MetadataValidateFinding
                {
                    Code = MetadataValidateConstants.Codes.PublishQueueIssueMismatch,
                    Message = $"publish.yaml issue_number ({publishIssue}) does not match queue-state linked_issue ({queueEntry.LinkedIssue}).",
                    Path = publishPath,
                });
            }
        }

        if (queueEntry is not null
            && string.Equals(queueEntry.Status, "completed", StringComparison.OrdinalIgnoreCase)
            && queueEntry.LinkedPr is null)
        {
            errors.Add(new MetadataValidateFinding
            {
                Code = MetadataValidateConstants.Codes.CompletedMissingClosure,
                Message = $"queue-state entry '{inputs.ExecutionUnit}' is marked completed but has no linked_pr or closeout evidence.",
                Path = queueStatePath,
            });
        }

        // Dependency consistency between packet and queue-state, when both
        // expose a comma-separated dependency list.
        if (packetFields is not null && queueEntry is not null && queueEntry.Dependencies is not null)
        {
            var packetDeps = ParseListLiteral(
                ValueFor(packetFields, "dependencies") ?? string.Empty);
            if (packetDeps.Count > 0
                && !packetDeps.SetEquals(queueEntry.Dependencies))
            {
                errors.Add(new MetadataValidateFinding
                {
                    Code = MetadataValidateConstants.Codes.PacketQueueDependencyMismatch,
                    Message = "packet.yaml dependencies do not match queue-state dependencies for this execution unit.",
                    Path = packetPath,
                });
            }
        }

        // ---- label-policy warnings -----------------------------------------
        // If publish metadata claims intent-pr-created on the PR position,
        // surface a warning per the existing G205/G206 policy invariant.
        if (publishFields is not null
            && string.Equals(
                ValueFor(publishFields, "pr_label") ?? ValueFor(publishFields, "prLabel"),
                "intent-pr-created",
                StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(new MetadataValidateFinding
            {
                Code = MetadataValidateConstants.Codes.LabelPolicyMisplacedPrCreated,
                Message = "publish.yaml records 'intent-pr-created' as a PR label; it belongs on the source issue, not the PR.",
                Path = publishPath,
            });
        }

        return new MetadataValidateResult
        {
            Valid = errors.Count == 0,
            ExecutionUnit = inputs.ExecutionUnit,
            Errors = errors,
            Warnings = warnings,
            CheckedFiles = checkedFiles,
        };
    }

    /// <summary>
    /// G207: parse YAML top-level scalar keys. We deliberately do NOT pull in
    /// a full YAML library — the validator only needs to recognize whether
    /// well-known top-level keys are present and have a non-empty value.
    /// Anything more structured (lists, nested maps) is read as the literal
    /// trailing-of-line text and downstream consumers parse further if
    /// needed (see <see cref="ParseListLiteral"/>).
    /// </summary>
    private static IReadOnlyDictionary<string, string> ParseYamlScalars(string yaml)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in YamlScalarKeyRegex.Matches(yaml))
        {
            // Only accept top-level keys (no leading whitespace).
            var line = match.Value;
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                continue;
            }
            var key = match.Groups["key"].Value;
            var value = match.Groups["value"].Value.Trim();
            // Strip surrounding quotes if present.
            if (value.Length >= 2
                && (value[0] == '"' && value[^1] == '"'
                    || value[0] == '\'' && value[^1] == '\''))
            {
                value = value.Substring(1, value.Length - 2);
            }
            fields[key] = value;
        }
        return fields;
    }

    private static bool HasNonEmptyValue(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);

    private static string? ValueFor(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value : null;

    private static int? TryGetInt(IReadOnlyDictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return int.TryParse(value,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    private static HashSet<string> ParseListLiteral(string value)
    {
        // Accept either "[a, b, c]" or "a,b,c" or empty.
        var trimmed = value.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            trimmed = trimmed[1..^1];
        }
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
        return trimmed.Split(',')
            .Select(s => s.Trim().Trim('"', '\''))
            .Where(s => s.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> ExtractHeadings(string markdown)
    {
        var headings = new List<string>();
        foreach (Match match in MarkdownHeadingLineRegex.Matches(markdown))
        {
            headings.Add(match.Groups["text"].Value.Trim());
        }
        return headings;
    }

    private static bool HasMatchingHeading(IReadOnlyList<string> headings, string section)
    {
        // Loose match: substring + case-insensitive. Accommodates
        // variations like "Target Repo / Path / Part" matching "Target Repo".
        foreach (var heading in headings)
        {
            if (heading.Contains(section, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static QueueStateEntry? FindQueueEntry(string queueStateJson, string executionUnit)
    {
        using var doc = JsonDocument.Parse(queueStateJson);
        var root = doc.RootElement;

        // Two common shapes: array of entries, OR object with "entries" array.
        JsonElement entries;
        if (root.ValueKind == JsonValueKind.Array)
        {
            entries = root;
        }
        else if (root.ValueKind == JsonValueKind.Object
            && (root.TryGetProperty("entries", out entries)
                || root.TryGetProperty("items", out entries))
            && entries.ValueKind == JsonValueKind.Array)
        {
            // entries assigned by TryGetProperty out arg.
        }
        else
        {
            return null;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var unit = TryGetString(entry, "execution_unit") ?? TryGetString(entry, "executionUnit");
            if (string.Equals(unit, executionUnit, StringComparison.Ordinal))
            {
                return new QueueStateEntry(
                    Status: TryGetString(entry, "status"),
                    LinkedIssue: TryGetIntProperty(entry, "linked_issue") ?? TryGetIntProperty(entry, "linkedIssue"),
                    LinkedPr: TryGetIntProperty(entry, "linked_pr") ?? TryGetIntProperty(entry, "linkedPr"),
                    Dependencies: TryGetStringArray(entry, "dependencies"));
            }
        }

        return null;
    }

    private static string? TryGetString(JsonElement obj, string name)
    {
        if (obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }

    private static int? TryGetIntProperty(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var prop))
        {
            return null;
        }
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var n))
        {
            return n;
        }
        if (prop.ValueKind == JsonValueKind.String
            && int.TryParse(prop.GetString(),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed))
        {
            return parsed;
        }
        return null;
    }

    private static HashSet<string>? TryGetStringArray(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
            {
                set.Add(s);
            }
        }
        return set;
    }

    private sealed record QueueStateEntry(
        string? Status,
        int? LinkedIssue,
        int? LinkedPr,
        HashSet<string>? Dependencies);
}

/// <summary>
/// G207: Bundle of file contents the analyzer needs. Anything <c>null</c>
/// represents "file not present on disk"; the analyzer raises the
/// appropriate missing-file finding. Empty strings represent present-but-
/// empty files.
/// </summary>
internal sealed record MetadataValidateInputs
{
    public required string ExecutionUnit { get; init; }
    public string? PacketYaml { get; init; }
    public string? GithubBodyMarkdown { get; init; }
    public string? ReviewContextMarkdown { get; init; }
    public string? ImplementationMarkdown { get; init; }
    public string? PublishYaml { get; init; }
    public string? QueueStateJson { get; init; }
}
