using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G785: bounded, deterministic inspection of acceptance criteria that require
/// durable PR-body evidence. This deliberately recognizes only the authored
/// phrases in <see cref="RecognizedPhrases"/> and only unordered bullets under
/// the <c>## Acceptance Criteria</c> section. It does not infer evidence from
/// verification prose, test counts, block position, or later body text.
/// </summary>
internal static class WorkerEvidencePasteAnalyzer
{
    /// <summary>
    /// The design-side phrases that opt an acceptance criterion into the
    /// PR-body evidence contract. Keep this list narrow: broader natural
    /// language matching is explicitly outside G785's contract.
    /// </summary>
    internal static readonly IReadOnlyList<string> RecognizedPhrases = new[]
    {
        "actual output pasted",
        "actual counts pasted",
    };

    private static readonly IReadOnlyList<string> CriterionNameMarkers = new[]
    {
        "criterion",
        "criteria",
        "ac",
    };

    /// <summary>
    /// Measures the supplied PR body against the source issue's explicitly
    /// evidence-bearing acceptance criteria. A fence satisfies a criterion
    /// only when its immediately preceding Markdown heading or non-empty line,
    /// or its first line, names that criterion using one of the packet-defined
    /// forms.
    /// </summary>
    public static WorkerEvidencePasteAnalysisResult Analyze(string issueBody, string prBody)
    {
        ArgumentNullException.ThrowIfNull(issueBody);
        ArgumentNullException.ThrowIfNull(prBody);

        var scan = FindRequiredCriteria(issueBody);
        var required = scan.Required;
        if (required.Count == 0)
        {
            return WorkerEvidencePasteAnalysisResult.Empty;
        }

        var namedOrdinals = FindNamedFenceOrdinals(prBody, required, scan.LabelValues);
        var present = required.Where(criterion => namedOrdinals.Contains(criterion.Ordinal)).ToArray();
        var gap = required.Where(criterion => !namedOrdinals.Contains(criterion.Ordinal)).ToArray();

        return new WorkerEvidencePasteAnalysisResult
        {
            EvidenceRequired = required,
            EvidenceBlocksPresent = present,
            EvidenceGap = gap,
        };
    }

    private static RequiredCriteriaScan FindRequiredCriteria(string issueBody)
    {
        var lines = NormalizeLines(issueBody);
        var inAcceptanceCriteria = false;
        var ordinal = 0;
        var allCriteria = new List<ParsedCriterion>();

        for (var index = 0; index < lines.Length; index++)
        {
            if (TryReadHeading(lines[index], out var headingLevel, out var headingText))
            {
                if (headingLevel <= 2)
                {
                    inAcceptanceCriteria = headingLevel == 2
                        && string.Equals(headingText, "Acceptance Criteria", StringComparison.OrdinalIgnoreCase);
                }

                continue;
            }

            if (!inAcceptanceCriteria || !TryStripBullet(lines[index], out var text))
            {
                continue;
            }

            ordinal++;
            var parts = new List<string> { text };
            var continuationIndex = index + 1;
            while (continuationIndex < lines.Length)
            {
                var continuation = lines[continuationIndex];
                if (string.IsNullOrWhiteSpace(continuation)
                    || TryStripBullet(continuation, out _)
                    || (TryReadHeading(continuation, out var continuationHeadingLevel, out _)
                        && continuationHeadingLevel <= 2)
                    || !char.IsWhiteSpace(continuation[0]))
                {
                    break;
                }

                parts.Add(continuation.Trim());
                continuationIndex++;
            }

            index = continuationIndex - 1;
            var criterionText = string.Join(" ", parts);
            var isRequired = RecognizedPhrases.Any(phrase =>
                criterionText.Contains(phrase, StringComparison.OrdinalIgnoreCase));
            allCriteria.Add(new ParsedCriterion(
                ordinal,
                criterionText,
                isRequired,
                TryReadCriterionLabel(criterionText, out var labelValue) ? labelValue : null));
        }

        var labelCounts = allCriteria
            .Where(criterion => criterion.LabelValue is not null)
            .GroupBy(criterion => criterion.LabelValue!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var labelValues = labelCounts
            .Where(pair => pair.Value == 1)
            .Select(pair => pair.Key)
            .ToHashSet();
        var required = allCriteria
            .Where(criterion => criterion.IsRequired)
            .Select(criterion => new WorkerEvidenceCriterion
            {
                Ordinal = criterion.Ordinal,
                Text = criterion.Text,
                Label = criterion.LabelValue is { } value && labelValues.Contains(value)
                    ? $"AC{value}"
                    : null,
            })
            .ToArray();

        return new RequiredCriteriaScan(required, labelValues);
    }

    private static IReadOnlySet<int> FindNamedFenceOrdinals(
        string prBody,
        IReadOnlyList<WorkerEvidenceCriterion> required,
        IReadOnlySet<int> labelValues)
    {
        var lines = NormalizeLines(prBody);
        var namedOrdinals = new HashSet<int>();

        for (var index = 0; index < lines.Length; index++)
        {
            if (!TryReadFence(lines[index], out var fenceMarker, out var fenceLength))
            {
                continue;
            }

            var closingIndex = FindClosingFence(lines, index + 1, fenceMarker, fenceLength);
            if (closingIndex < 0)
            {
                continue;
            }

            var precedingName = FindImmediatelyPrecedingFenceName(lines, index);
            var firstLine = index + 1 < closingIndex ? lines[index + 1].Trim() : string.Empty;
            foreach (var criterion in required)
            {
                if (NamesCriterion(precedingName, criterion, labelValues)
                    || NamesCriterion(firstLine, criterion, labelValues))
                {
                    namedOrdinals.Add(criterion.Ordinal);
                }
            }

            index = closingIndex;
        }

        return namedOrdinals;
    }

    private static string[] NormalizeLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static bool TryReadHeading(string line, out int level, out string text)
    {
        var trimmed = line.TrimStart();
        level = 0;
        text = string.Empty;
        while (level < trimmed.Length && trimmed[level] == '#')
        {
            level++;
        }

        if (level == 0 || level == trimmed.Length || !char.IsWhiteSpace(trimmed[level]))
        {
            level = 0;
            return false;
        }

        text = trimmed[(level + 1)..].Trim().TrimEnd('#').TrimEnd();
        return true;
    }

    private static bool TryStripBullet(string line, out string text)
    {
        var trimmed = line.TrimStart();
        text = string.Empty;
        if (trimmed.Length < 2
            || (trimmed[0] is not ('-' or '*' or '+'))
            || !char.IsWhiteSpace(trimmed[1]))
        {
            return false;
        }

        text = trimmed[2..].Trim();
        if (text.Length >= 3
            && text[0] == '['
            && text[2] == ']'
            && (text[1] is ' ' or 'x' or 'X'))
        {
            text = text[3..].TrimStart();
        }

        return !string.IsNullOrWhiteSpace(text);
    }

    private static bool TryReadFence(string line, out char marker, out int length)
    {
        var trimmed = line.TrimStart();
        marker = default;
        length = 0;
        if (trimmed.Length < 3 || (trimmed[0] is not ('`' or '~')))
        {
            return false;
        }

        marker = trimmed[0];
        while (length < trimmed.Length && trimmed[length] == marker)
        {
            length++;
        }

        return length >= 3;
    }

    private static int FindClosingFence(string[] lines, int start, char marker, int minimumLength)
    {
        for (var index = start; index < lines.Length; index++)
        {
            var trimmed = lines[index].TrimStart();
            var length = 0;
            while (length < trimmed.Length && trimmed[length] == marker)
            {
                length++;
            }

            if (length >= minimumLength && trimmed[length..].Trim().Length == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static string? FindImmediatelyPrecedingFenceName(string[] lines, int fenceIndex)
    {
        for (var index = fenceIndex - 1; index >= 0; index--)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            return TryReadHeading(lines[index], out _, out var heading)
                ? heading
                : lines[index].Trim();
        }

        return null;
    }

    private static bool NamesCriterion(
        string? candidate,
        WorkerEvidenceCriterion criterion,
        IReadOnlySet<int> labelValues)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        foreach (var reference in FindCriterionReferences(candidate))
        {
            if (reference.IsAc)
            {
                if (labelValues.Contains(reference.Number))
                {
                    if (string.Equals(criterion.Label, $"AC{reference.Number}", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                else if (reference.Number == criterion.Ordinal)
                {
                    return true;
                }
            }
            else if (reference.Number == criterion.Ordinal)
            {
                return true;
            }
        }

        return ContainsDistinctiveCriterionPhrase(candidate, criterion.Text);
    }

    private static IReadOnlyList<CriterionReference> FindCriterionReferences(string candidate)
    {
        var references = new List<CriterionReference>();
        foreach (var marker in CriterionNameMarkers)
        {
            var isAcMarker = string.Equals(marker, "ac", StringComparison.Ordinal);
            var searchStart = 0;
            while (searchStart < candidate.Length)
            {
                var index = candidate.IndexOf(marker, searchStart, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    break;
                }

                var beforeIsWord = index > 0 && char.IsLetterOrDigit(candidate[index - 1]);
                var cursor = index + marker.Length;
                var afterIsWord = cursor < candidate.Length && char.IsLetterOrDigit(candidate[cursor]);
                var gluedAcNumber = isAcMarker
                    && cursor < candidate.Length
                    && char.IsDigit(candidate[cursor]);
                if (!beforeIsWord && (!afterIsWord || gluedAcNumber))
                {
                    while (cursor < candidate.Length && char.IsWhiteSpace(candidate[cursor]))
                    {
                        cursor++;
                    }

                    if (cursor < candidate.Length && candidate[cursor] == '#')
                    {
                        cursor++;
                        while (cursor < candidate.Length && char.IsWhiteSpace(candidate[cursor]))
                        {
                            cursor++;
                        }
                    }

                    var numberStart = cursor;
                    while (cursor < candidate.Length && char.IsDigit(candidate[cursor]))
                    {
                        cursor++;
                    }

                    if (numberStart < cursor
                        && int.TryParse(candidate[numberStart..cursor], out var namedNumber)
                        && (cursor == candidate.Length || !char.IsLetterOrDigit(candidate[cursor])))
                    {
                        references.Add(new CriterionReference(isAcMarker, namedNumber));
                    }
                }

                searchStart = index + marker.Length;
            }
        }

        return references;
    }

    private static bool TryReadCriterionLabel(string text, out int labelValue)
    {
        labelValue = default;
        var cursor = 0;
        while (cursor < text.Length
            && (char.IsWhiteSpace(text[cursor]) || text[cursor] is '*' or '_'))
        {
            cursor++;
        }

        if (cursor + 2 > text.Length
            || !text.AsSpan(cursor, 2).Equals("AC", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        cursor += 2;
        while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
        {
            cursor++;
        }

        if (cursor < text.Length && text[cursor] == '#')
        {
            cursor++;
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
            {
                cursor++;
            }
        }

        var numberStart = cursor;
        while (cursor < text.Length && IsAsciiDigit(text[cursor]))
        {
            cursor++;
        }

        if (numberStart == cursor
            || (cursor < text.Length && char.IsLetterOrDigit(text[cursor])))
        {
            return false;
        }

        return int.TryParse(
            text.AsSpan(numberStart, cursor - numberStart),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out labelValue);
    }

    private static bool IsAsciiDigit(char value) => value is >= '0' and <= '9';

    private sealed record ParsedCriterion(int Ordinal, string Text, bool IsRequired, int? LabelValue);

    private sealed record RequiredCriteriaScan(
        IReadOnlyList<WorkerEvidenceCriterion> Required,
        IReadOnlySet<int> LabelValues);

    private readonly record struct CriterionReference(bool IsAc, int Number);

    private static bool ContainsDistinctiveCriterionPhrase(string? candidate, string criterionText)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var candidateWords = ExtractWords(candidate);
        var criterionWords = ExtractWords(criterionText);
        const int minimumPhraseWords = 4;
        if (candidateWords.Count < minimumPhraseWords || criterionWords.Count < minimumPhraseWords)
        {
            return false;
        }

        for (var candidateStart = 0; candidateStart <= candidateWords.Count - minimumPhraseWords; candidateStart++)
        {
            for (var criterionStart = 0; criterionStart <= criterionWords.Count - minimumPhraseWords; criterionStart++)
            {
                var matchedWords = 0;
                while (candidateStart + matchedWords < candidateWords.Count
                    && criterionStart + matchedWords < criterionWords.Count
                    && string.Equals(
                        candidateWords[candidateStart + matchedWords],
                        criterionWords[criterionStart + matchedWords],
                        StringComparison.OrdinalIgnoreCase))
                {
                    matchedWords++;
                }

                if (matchedWords >= minimumPhraseWords)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IReadOnlyList<string> ExtractWords(string text)
    {
        var words = new List<string>();
        var start = -1;
        for (var index = 0; index <= text.Length; index++)
        {
            var isWordCharacter = index < text.Length && char.IsLetterOrDigit(text[index]);
            if (isWordCharacter && start < 0)
            {
                start = index;
            }
            else if (!isWordCharacter && start >= 0)
            {
                words.Add(text[start..index]);
                start = -1;
            }
        }

        return words;
    }
}

/// <summary>Shared G785 wording rendered by both worker guides.</summary>
internal static class WorkerEvidencePasteRule
{
    public const string Text =
        "Every Acceptance Criteria bullet that contains `actual output pasted` or `actual counts pasted` "
        + "requires a fenced block of collected output in the PR body. Name the matching `AC <ordinal>`, "
        + "`Criterion <ordinal>`, `Criteria <ordinal>`, or a distinctive four-or-more-word criterion phrase "
        + "in the block's immediately preceding Markdown heading or non-empty line, or its first line; paraphrased or expected values "
        + "are a request-update. A criterion whose text begins with `AC<n>` can be named by that label; `AC<n>` and `AC <n>` are equivalent, and the gate prints the label to use. "
        + "`intent-cli worker result-summary` measures this rule when given `--pr-body` or `--pr-body-file`.";
}

/// <summary>One acceptance criterion that is subject to the G785 evidence rule.</summary>
internal sealed record WorkerEvidenceCriterion
{
    [JsonPropertyName("ordinal")]
    public required int Ordinal { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; init; }
}

/// <summary>Shared text formatting for G785 required, present, and gap criteria.</summary>
internal static class WorkerEvidenceCriterionFormatter
{
    public static string Format(IReadOnlyList<WorkerEvidenceCriterion> criteria) =>
        string.Join(", ", criteria.Select(criterion => criterion.Label is null
            ? $"Criterion {criterion.Ordinal}: {criterion.Text}"
            : $"{criterion.Label} (Criterion {criterion.Ordinal}): {criterion.Text}"));
}

/// <summary>G785 measurement returned by result-summary and worker-complete.</summary>
internal sealed record WorkerEvidencePasteAnalysisResult
{
    public static WorkerEvidencePasteAnalysisResult Empty { get; } = new()
    {
        EvidenceRequired = Array.Empty<WorkerEvidenceCriterion>(),
        EvidenceBlocksPresent = Array.Empty<WorkerEvidenceCriterion>(),
        EvidenceGap = Array.Empty<WorkerEvidenceCriterion>(),
    };

    public required IReadOnlyList<WorkerEvidenceCriterion> EvidenceRequired { get; init; }

    public required IReadOnlyList<WorkerEvidenceCriterion> EvidenceBlocksPresent { get; init; }

    public required IReadOnlyList<WorkerEvidenceCriterion> EvidenceGap { get; init; }
}
