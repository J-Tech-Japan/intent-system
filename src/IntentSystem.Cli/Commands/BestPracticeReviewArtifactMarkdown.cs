namespace IntentSystem.Cli.Commands;

internal sealed record BestPracticeReviewArtifact
{
    public required string Domain { get; init; }

    public required IReadOnlyList<string> ParentRuleSpecRefs { get; init; }

    public required IReadOnlyList<string> RecommendedClarifications { get; init; }

    public required IReadOnlyList<string> ReturnToIntentPaths { get; init; }
}

internal static class BestPracticeReviewArtifactMarkdown
{
    private static readonly string[] RequiredSections =
    [
        "reconstructed_artifact_paths",
        "reviewed_dimensions",
        "model_refs",
        "knowledge_refs",
        "parent_rule_spec_refs",
        "recommended_intent_additions",
        "recommended_clarifications",
        "developer_confirmation_items",
        "return_to_intent_paths",
        "confidence_deltas",
        "skipped_stages"
    ];

    public static BestPracticeReviewArtifact Deserialize(string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.None);
        var sawTitle = false;
        var expectingDomain = false;
        string? domain = null;
        string? currentSection = null;
        var sections = RequiredSections.ToDictionary(section => section, _ => new List<string>(), StringComparer.Ordinal);
        var seenSections = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (string.Equals(line, "# Best Practice Review", StringComparison.Ordinal))
            {
                sawTitle = true;
                currentSection = null;
                continue;
            }

            if (string.Equals(line, "## Domain", StringComparison.Ordinal))
            {
                expectingDomain = true;
                currentSection = null;
                continue;
            }

            if (expectingDomain)
            {
                if (line.StartsWith('`') && line.EndsWith('`') && line.Length >= 2)
                {
                    domain = line[1..^1];
                    expectingDomain = false;
                    continue;
                }

                throw new InvalidOperationException("Best-practice review artifact must contain a backticked domain line.");
            }

            if (line.EndsWith(":", StringComparison.Ordinal) && sections.ContainsKey(line[..^1]))
            {
                currentSection = line[..^1];
                seenSections.Add(currentSection);
                continue;
            }

            if (!line.StartsWith("- ", StringComparison.Ordinal) || currentSection is null)
            {
                continue;
            }

            var value = line[2..];
            if (!string.Equals(value, "none", StringComparison.Ordinal))
            {
                sections[currentSection].Add(value);
            }
        }

        if (!sawTitle)
        {
            throw new InvalidOperationException("Best-practice review artifact must start with '# Best Practice Review'.");
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new InvalidOperationException("Best-practice review artifact must contain a domain.");
        }

        var missingSection = RequiredSections.FirstOrDefault(section => !seenSections.Contains(section));
        if (missingSection is not null)
        {
            throw new InvalidOperationException(
                $"Best-practice review artifact must contain section '{missingSection}:'.");
        }

        return new BestPracticeReviewArtifact
        {
            Domain = domain,
            ParentRuleSpecRefs = sections["parent_rule_spec_refs"].ToArray(),
            RecommendedClarifications = sections["recommended_clarifications"].ToArray(),
            ReturnToIntentPaths = sections["return_to_intent_paths"].ToArray()
        };
    }
}
