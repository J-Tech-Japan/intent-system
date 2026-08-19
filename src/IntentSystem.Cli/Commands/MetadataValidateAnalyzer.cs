using System.Text.Json;
using System.Text.RegularExpressions;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

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
        // Indented or top-level scalar key: "<indent>key:" possibly with a
        // value. We capture the leading whitespace so the parser can
        // reconstruct nesting depth.
        //
        // IMPORTANT: use [ \t]* between the colon and the inline value
        // (not \s*) — \s matches newlines, so on a key line whose value
        // continues on the next physical line (`key:\n  child: ...`) the
        // value capture would greedily slurp the next line. With [ \t]*
        // the value capture ends at end-of-line and the next line is
        // matched as its own regex match.
        @"^(?<indent>[ \t]*)(?<key>[A-Za-z_][A-Za-z0-9_\-]*)[ \t]*:[ \t]*(?<value>.*)$",
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
                // The parent-host packet schema nests fields under
                // `implementation_issue_packet:` with `source_execution_unit`
                // and `issue_title` keys. Accept both that shape and the
                // simpler flat shape used by the in-memory test fixtures.
                if (!HasAnyNonEmptyValue(packetFields,
                        "execution_unit",
                        "executionUnit",
                        "implementation_issue_packet.source_execution_unit",
                        "source_execution_unit"))
                {
                    errors.Add(new MetadataValidateFinding
                    {
                        Code = MetadataValidateConstants.Codes.PacketMissingExecutionUnit,
                        Message = "packet.yaml is missing execution_unit / source_execution_unit.",
                        Path = packetPath,
                    });
                }
                if (!HasAnyNonEmptyValue(packetFields,
                        "title",
                        "implementation_issue_packet.issue_title",
                        "issue_title"))
                {
                    errors.Add(new MetadataValidateFinding
                    {
                        Code = MetadataValidateConstants.Codes.PacketMissingTitle,
                        Message = "packet.yaml is missing title / issue_title.",
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
                // The parent-host publish schema nests issue data under
                // `issue:` (so the number is exposed as `issue.number`).
                // Accept both that shape and the simpler flat shape. The
                // shipped issue-publish workflow also produced
                // `created_issue_number` / `created_issue_url`; keep reading
                // those superseded keys so completed records remain valid
                // after a validator upgrade.
                var hasLegacyIssueNumber = HasNonEmptyValue(publishFields, "created_issue_number");
                var hasLegacyIssueUrl = HasNonEmptyValue(publishFields, "created_issue_url");
                if (hasLegacyIssueNumber || hasLegacyIssueUrl)
                {
                    warnings.Add(new MetadataValidateFinding
                    {
                        Code = MetadataValidateConstants.Codes.PublishLegacyIssueIdentity,
                        Message = "publish.yaml uses superseded created_issue_number / created_issue_url keys; accepted for backward compatibility.",
                        Path = publishPath,
                    });
                }
                if (!HasAnyNonEmptyValue(publishFields,
                        "issue_number", "issueNumber", "issue.number", "created_issue_number"))
                {
                    errors.Add(new MetadataValidateFinding
                    {
                        Code = MetadataValidateConstants.Codes.PublishMissingIssueNumber,
                        Message = "publish.yaml is missing issue_number / issue.number.",
                        Path = publishPath,
                    });
                }
                if (!HasAnyNonEmptyValue(publishFields,
                        "issue_url", "issueUrl", "issue.url", "created_issue_url"))
                {
                    errors.Add(new MetadataValidateFinding
                    {
                        Code = MetadataValidateConstants.Codes.PublishMissingIssueUrl,
                        Message = "publish.yaml is missing issue_url / issue.url.",
                        Path = publishPath,
                    });
                }
            }
        }

        QueueStateEntry? queueEntry = null;

        // ---- runs.jsonl closeout evidence (optional) ----------------------
        const string runsPath = ".intent-cli/runs.jsonl";
        var runEvents = Array.Empty<RunEvent>();
        if (inputs.RunsJsonl is not null)
        {
            checkedFiles.Add(runsPath);
            try
            {
                runEvents = RunLogSerializer.DeserializeAll(inputs.RunsJsonl).ToArray();
            }
            catch (Exception exception) when (
                exception is ArgumentException
                or InvalidOperationException
                or JsonException)
            {
                errors.Add(new MetadataValidateFinding
                {
                    Code = MetadataValidateConstants.Codes.RunsLogUnparseable,
                    Message = $"could not parse {runsPath}: {exception.Message}",
                    Path = runsPath,
                });
            }
        }

        var unitRunEvents = runEvents
            .Where(runEvent => string.Equals(
                runEvent.ExecutionUnit,
                inputs.ExecutionUnit,
                StringComparison.Ordinal))
            .ToArray();
        var hasPrMergedEvidence = unitRunEvents.Any(runEvent =>
            string.Equals(runEvent.Event, "pr-merged", StringComparison.Ordinal));
        var hasCloseoutRecordedEvidence = unitRunEvents.Any(runEvent =>
            string.Equals(runEvent.Event, "closeout-recorded", StringComparison.Ordinal));
        var hasLinkageRecoveryEvidence = unitRunEvents.Any(runEvent =>
            string.Equals(runEvent.Event, "linkage-recovery", StringComparison.Ordinal));
        var hasCloseoutEvidence = hasPrMergedEvidence && hasCloseoutRecordedEvidence;

        // ---- queue-state.json (required) -----------------------------------
        const string queueStatePath = ".intent-cli/queue-state.json";
        checkedFiles.Add(queueStatePath);
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

        if (hasCloseoutEvidence && queueEntry is not null && queueEntry.LinkedPr is null)
        {
            warnings.Add(new MetadataValidateFinding
            {
                Code = MetadataValidateConstants.Codes.RunsCloseoutEvidence,
                Message = hasLinkageRecoveryEvidence
                    ? "runs.jsonl contains the shipped pr-merged, closeout-recorded, and linkage-recovery events; accepted as closeout evidence for the superseded queue linkage shape."
                    : "runs.jsonl contains the shipped pr-merged and closeout-recorded events; accepted as closeout evidence for the superseded queue linkage shape.",
                Path = runsPath,
            });
        }

        // ---- cross-file consistency ----------------------------------------
        if (publishFields is not null && queueEntry is not null)
        {
            var publishIssue = TryGetInt(publishFields, "issue_number")
                ?? TryGetInt(publishFields, "issueNumber")
                ?? TryGetInt(publishFields, "issue.number")
                ?? TryGetInt(publishFields, "created_issue_number");
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

        if (queueEntry?.LinkedPrWasLegacyUrl == true)
        {
            warnings.Add(new MetadataValidateFinding
            {
                Code = MetadataValidateConstants.Codes.QueueLegacyLinkedPrUrl,
                Message = "queue-state linked_pr is a superseded GitHub URL string; accepted for backward compatibility.",
                Path = queueStatePath,
            });
        }

        if (queueEntry is not null
            && string.Equals(queueEntry.Status, "completed", StringComparison.OrdinalIgnoreCase)
            && queueEntry.LinkedPr is null
            && !hasCloseoutEvidence)
        {
            errors.Add(new MetadataValidateFinding
            {
                Code = MetadataValidateConstants.Codes.CompletedMissingClosure,
                Message = $"queue-state entry '{inputs.ExecutionUnit}' is genuinely incomplete: state=completed has no supported linked_pr reference or both shipped closeout runs events (pr-merged and closeout-recorded).",
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
    /// G207 follow-up: parse YAML scalar keys using indentation-tracked
    /// nesting so the validator can recognize the parent-host packet and
    /// publish schemas, which nest fields under outer maps like
    /// <c>implementation_issue_packet:</c> and <c>issue:</c>.
    ///
    /// Keys are returned both bare (e.g. <c>source_execution_unit</c>) and
    /// dotted (e.g. <c>implementation_issue_packet.source_execution_unit</c>)
    /// so callers can match either the flat or nested form. Bare keys at
    /// inner levels are also indexed so a nested <c>issue.number</c> is
    /// findable as just <c>number</c> when no top-level <c>number</c>
    /// shadows it.
    ///
    /// Lists, multi-line scalars, anchors, and other YAML features are
    /// out of scope — the validator only needs scalar values.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ParseYamlScalars(string yaml)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        // Stack of (indentWidth, keyName) pairs that describe the chain of
        // open mapping keys above the current line. We pop entries whose
        // indent is >= the current indent before pushing.
        var pathStack = new List<(int Indent, string Key)>();

        foreach (Match match in YamlScalarKeyRegex.Matches(yaml))
        {
            var indent = match.Groups["indent"].Value.Length;
            var key = match.Groups["key"].Value;
            var rawValue = match.Groups["value"].Value;
            var hadInlineValue = rawValue.Length > 0
                && !rawValue.StartsWith('#');
            var value = rawValue;
            // Strip trailing inline comments.
            var hashIndex = value.IndexOf(" #", StringComparison.Ordinal);
            if (hashIndex >= 0)
            {
                value = value.Substring(0, hashIndex);
            }
            value = value.Trim();
            if (value.Length >= 2
                && (value[0] == '"' && value[^1] == '"'
                    || value[0] == '\'' && value[^1] == '\''))
            {
                value = value.Substring(1, value.Length - 2);
            }

            // Pop the path stack until the top is shallower than the current
            // line; entries at the same or deeper indent are siblings or
            // children that already closed.
            while (pathStack.Count > 0 && pathStack[^1].Indent >= indent)
            {
                pathStack.RemoveAt(pathStack.Count - 1);
            }

            // Build dotted path for this key.
            var dottedPath = pathStack.Count == 0
                ? key
                : string.Join(".", pathStack.Select(e => e.Key)) + "." + key;

            // Always store the dotted path. Also store the bare key when no
            // earlier line already wrote that bare key — first writer wins
            // so a top-level `execution_unit` outranks a deeper one.
            if (hadInlineValue && !string.IsNullOrEmpty(value))
            {
                fields[dottedPath] = value;
                if (!fields.ContainsKey(key))
                {
                    fields[key] = value;
                }
            }
            else
            {
                // No inline value — this line opens a nested mapping.
                // Push it onto the path stack so subsequent lines see it
                // as a parent.
                pathStack.Add((indent, key));
            }
        }

        return fields;
    }

    private static bool HasNonEmptyValue(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);

    private static bool HasAnyNonEmptyValue(
        IReadOnlyDictionary<string, string> fields,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (HasNonEmptyValue(fields, key))
            {
                return true;
            }
        }
        return false;
    }

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

    /// <summary>
    /// PR #824 review repair #6: match required sections by EXACT
    /// heading text (case-insensitive, trimming trailing punctuation)
    /// instead of substring `Contains`. The legacy loose match
    /// accepted non-standalone headings like `## My Goal` or
    /// `## Goal - notes`, so the github-body contract could pass
    /// without the required exact sections. A heading is also
    /// accepted when it equals `<section> / ...` — a documented
    /// compound shape for `Target Repo / Path / Part` — but
    /// arbitrary prefix-followed-by-text is rejected.
    /// </summary>
    private static bool HasMatchingHeading(IReadOnlyList<string> headings, string section)
    {
        foreach (var heading in headings)
        {
            var normalized = heading.TrimEnd().TrimEnd(':', '.', '!', '?').TrimEnd();
            if (string.Equals(normalized, section, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            // Tolerate the documented compound shape
            // `<section> / <suffix>` so historical packets that use
            // `## Target Repo / Path / Part` keep matching the
            // required `Target Repo` section name without admitting
            // arbitrary `<section> - notes` augmentations.
            var compoundPrefix = section + " /";
            if (normalized.StartsWith(compoundPrefix, StringComparison.OrdinalIgnoreCase))
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
                // The host queue-state schema uses `state` (e.g. "queued",
                // "completed"); accept `status` as a synonym for the simpler
                // flat shape used in tests.
                var status = TryGetString(entry, "state") ?? TryGetString(entry, "status");

                // The host queue-state schema represents linked issue / PR
                // as objects `{ repo, number, url }`. Accept either the
                // object-with-number form or a flat int (legacy / tests).
                var linkedIssue = ResolveLinkedNumber(entry, false, "linked_issue", "linkedIssue");
                var linkedPr = ResolveLinkedNumber(entry, true, "linked_pr", "linkedPr");
                return new QueueStateEntry(
                    Status: status,
                    LinkedIssue: linkedIssue.Number,
                    LinkedPr: linkedPr.Number,
                    LinkedPrWasLegacyUrl: linkedPr.WasLegacyUrl,
                    Dependencies: TryGetStringArray(entry, "dependencies"));
            }
        }

        return null;
    }

    /// <summary>
    /// G207 follow-up: the host queue-state stores linked_issue / linked_pr
    /// as objects with a <c>number</c> property. Accept either the
    /// object-with-number form or a flat integer.
    /// </summary>
    private static LinkedNumber ResolveLinkedNumber(
        JsonElement entry,
        bool allowPullRequestUrl,
        params string[] candidateKeys)
    {
        foreach (var key in candidateKeys)
        {
            if (!entry.TryGetProperty(key, out var prop))
            {
                continue;
            }
            // Object form: prefer the `number` property.
            if (prop.ValueKind == JsonValueKind.Object
                && prop.TryGetProperty("number", out var numberProp))
            {
                if (numberProp.ValueKind == JsonValueKind.Number
                    && numberProp.TryGetInt32(out var n))
                {
                    return new LinkedNumber(n, WasLegacyUrl: false);
                }
                if (numberProp.ValueKind == JsonValueKind.String
                    && int.TryParse(numberProp.GetString(),
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var parsedString))
                {
                    return new LinkedNumber(parsedString, WasLegacyUrl: false);
                }
            }
            // Flat form (test fixtures / legacy):
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var direct))
            {
                return new LinkedNumber(direct, WasLegacyUrl: false);
            }
            if (prop.ValueKind == JsonValueKind.String
                && int.TryParse(prop.GetString(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var directParsed))
            {
                return new LinkedNumber(directParsed, WasLegacyUrl: false);
            }
            if (allowPullRequestUrl
                && prop.ValueKind == JsonValueKind.String
                && TryParseGitHubPullRequestUrl(prop.GetString(), out var urlNumber))
            {
                return new LinkedNumber(urlNumber, WasLegacyUrl: true);
            }
        }
        return new LinkedNumber(null, WasLegacyUrl: false);
    }

    private static bool TryParseGitHubPullRequestUrl(string? value, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 4
            && string.Equals(segments[2], "pull", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(
                segments[3],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out number)
            && number > 0;
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
        bool LinkedPrWasLegacyUrl,
        HashSet<string>? Dependencies);

    private sealed record LinkedNumber(int? Number, bool WasLegacyUrl);
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
    public string? RunsJsonl { get; init; }
}
