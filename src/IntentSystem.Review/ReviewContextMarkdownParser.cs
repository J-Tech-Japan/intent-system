using IntentSystem.Review.Models;

namespace IntentSystem.Review;

public static class ReviewContextMarkdownParser
{
    public static ReviewContextSnapshot Parse(string markdown)
        => Parse(markdown, fallbackExecutionUnit: null);

    /// <summary>
    /// G373: parse a review-context markdown using a caller-supplied
    /// canonical fallback execution-unit identity (resolved from
    /// <c>packet.yaml</c> / the queue item / the <c>review run</c> command
    /// argument). When the markdown omits the legacy <c># Execution Unit</c>
    /// section — as newer, intentionally minimal packet review-context
    /// files do — the fallback is used instead of throwing. This keeps
    /// <c>review run</c> from failing solely because the section is absent
    /// while <c>closeout-plan</c> and <c>guide review</c> already resolve the
    /// unit from canonical packet artifacts. Passing a <c>null</c>/blank
    /// fallback preserves the original "section is required" behavior for
    /// callers that have no canonical identity to fall back on.
    /// </summary>
    public static ReviewContextSnapshot Parse(string markdown, string? fallbackExecutionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);

        var executionUnit = ParseExecutionUnit(markdown, fallbackExecutionUnit);
        var sections = ParseSections(markdown);

        return new ReviewContextSnapshot
        {
            SourceExecutionUnit = executionUnit,
            AcceptanceCriteria = GetOptionalListSection(sections, "Acceptance Criteria"),
            DeterministicReviewChecks = GetRequiredListSection(sections, "Deterministic Review Checks"),
            ExpectedEvidence = GetOptionalListSection(sections, "Expected Evidence")
        };
    }

    private static string ParseExecutionUnit(string markdown, string? fallbackExecutionUnit)
    {
        using var reader = new StringReader(markdown);
        string? line;
        var inExecutionUnitSection = false;

        while ((line = reader.ReadLine()) is not null)
        {
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                inExecutionUnitSection = string.Equals(
                    line[2..],
                    "Execution Unit",
                    StringComparison.Ordinal);
                continue;
            }

            if (!inExecutionUnitSection || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '`' && trimmed[^1] == '`')
            {
                return trimmed[1..^1];
            }
        }

        // G373: the legacy `# Execution Unit` section is absent. Fall back
        // to the canonical identity the caller resolved from packet.yaml /
        // the queue item / the command argument, when one was supplied.
        if (!string.IsNullOrWhiteSpace(fallbackExecutionUnit))
        {
            return fallbackExecutionUnit;
        }

        throw new InvalidOperationException("Review context markdown must contain an Execution Unit section.");
    }

    private static Dictionary<string, List<string>> ParseSections(string markdown)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        List<string>? currentItems = null;

        using var reader = new StringReader(markdown);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                var heading = line.TrimStart('#', ' ');
                currentItems = [];
                sections[heading] = currentItems;
                continue;
            }

            if (currentItems is null || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (string.Equals(line, "(none)", StringComparison.Ordinal))
            {
                continue;
            }

            if (!line.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            currentItems.Add(line[2..]);
        }

        return sections;
    }

    private static IReadOnlyList<string> GetRequiredListSection(
        IReadOnlyDictionary<string, List<string>> sections,
        string heading)
    {
        if (!sections.TryGetValue(heading, out var items))
        {
            throw new InvalidOperationException(
                $"Review context markdown must contain required section '{heading}'.");
        }

        return items;
    }

    private static IReadOnlyList<string> GetOptionalListSection(
        IReadOnlyDictionary<string, List<string>> sections,
        string heading)
    {
        return sections.TryGetValue(heading, out var items)
            ? items
            : [];
    }
}
