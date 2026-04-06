namespace IntentSystem.Cli.Commands;

internal static class IntakeFoldinArtifactMarkdown
{
    private static readonly string[] RequiredSections =
    [
        "answered_question_ids",
        "recommended_updates",
        "return_to_intent_paths",
        "source_concept_refs"
    ];

    public static IntakePatchFoldinDraft Deserialize(string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);

        var lines = markdown
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None);

        var sawTitle = false;
        string? domain = null;
        var sections = RequiredSections.ToDictionary(section => section, _ => new List<string>(), StringComparer.Ordinal);
        var seenSections = new HashSet<string>(StringComparer.Ordinal);

        string? currentSection = null;
        var expectingDomain = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (string.Equals(line, "# Intake Fold-In Draft", StringComparison.Ordinal))
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

                throw new InvalidOperationException("Intake fold-in artifact must contain a backticked domain line.");
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
            throw new InvalidOperationException("Intake fold-in artifact must start with '# Intake Fold-In Draft'.");
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new InvalidOperationException("Intake fold-in artifact must contain a domain.");
        }

        var missingSection = RequiredSections.FirstOrDefault(section => !seenSections.Contains(section));
        if (missingSection is not null)
        {
            throw new InvalidOperationException($"Intake fold-in artifact must contain section '{missingSection}:'.");
        }

        return new IntakePatchFoldinDraft
        {
            Domain = domain,
            AnsweredQuestionIds = sections["answered_question_ids"].ToArray(),
            RecommendedUpdates = sections["recommended_updates"].ToArray(),
            ReturnToIntentPaths = sections["return_to_intent_paths"].ToArray(),
            SourceConceptRefs = sections["source_concept_refs"].ToArray()
        };
    }
}
