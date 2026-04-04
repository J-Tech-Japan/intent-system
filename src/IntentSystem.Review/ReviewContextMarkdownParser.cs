using IntentSystem.Review.Models;

namespace IntentSystem.Review;

public static class ReviewContextMarkdownParser
{
    public static ReviewContextSnapshot Parse(string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);

        var executionUnit = ParseExecutionUnit(markdown);
        var sections = ParseSections(markdown);

        return new ReviewContextSnapshot
        {
            SourceExecutionUnit = executionUnit,
            AcceptanceCriteria = GetRequiredListSection(sections, "Acceptance Criteria"),
            DeterministicReviewChecks = GetRequiredListSection(sections, "Deterministic Review Checks"),
            ExpectedEvidence = GetOptionalListSection(sections, "Expected Evidence")
        };
    }

    private static string ParseExecutionUnit(string markdown)
    {
        const string prefix = "- **execution-unit**: `";
        using var reader = new StringReader(markdown);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal) || !line.EndsWith('`'))
            {
                continue;
            }

            return line[prefix.Length..^1];
        }

        throw new InvalidOperationException("Review context markdown must contain an execution-unit header line.");
    }

    private static Dictionary<string, List<string>> ParseSections(string markdown)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        List<string>? currentItems = null;

        using var reader = new StringReader(markdown);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var heading = line[3..];
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
