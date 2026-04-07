using System.Text.Json;

namespace IntentSystem.Cli.Commands;

internal static class ReconstructedInterviewArtifactMarkdown
{
    private static readonly string[] RequiredSections =
    [
        "selected_altitudes",
        "root_near_intent_candidates",
        "execution_near_update_candidates",
        "confidence_by_altitude",
        "source_concept_refs",
        "recommended_follow_up_questions",
        "bridge_questions",
        "return_to_intent_paths",
        "gaps"
    ];

    public static ReconstructedInterviewArtifact Deserialize(string markdown)
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

            if (string.Equals(line, "# Reconstructed Interview", StringComparison.Ordinal))
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

                throw new InvalidOperationException("Reconstructed interview artifact must contain a backticked domain line.");
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
            throw new InvalidOperationException("Reconstructed interview artifact must start with '# Reconstructed Interview'.");
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new InvalidOperationException("Reconstructed interview artifact must contain a domain.");
        }

        var missingSection = RequiredSections.FirstOrDefault(section => !seenSections.Contains(section));
        if (missingSection is not null)
        {
            throw new InvalidOperationException(
                $"Reconstructed interview artifact must contain section '{missingSection}:'.");
        }

        return new ReconstructedInterviewArtifact
        {
            Domain = domain,
            SelectedAltitudes = sections["selected_altitudes"].ToArray(),
            RootNearIntentCandidates = sections["root_near_intent_candidates"].ToArray(),
            ExecutionNearUpdateCandidates = sections["execution_near_update_candidates"].ToArray(),
            ConfidenceByAltitude = sections["confidence_by_altitude"].ToArray(),
            SourceConceptRefs = sections["source_concept_refs"].ToArray(),
            RecommendedFollowUpQuestions = sections["recommended_follow_up_questions"].ToArray(),
            BridgeQuestions = sections["bridge_questions"].Select(ParseBridgeQuestion).ToArray(),
            ReturnToIntentPaths = sections["return_to_intent_paths"].ToArray(),
            Gaps = sections["gaps"].ToArray()
        };
    }

    private static ReconstructedBridgeQuestion ParseBridgeQuestion(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            var root = document.RootElement;
            var affectsElement = root.GetProperty("affects");

            return new ReconstructedBridgeQuestion
            {
                QuestionId = root.GetProperty("question_id").GetString()
                    ?? throw new InvalidOperationException("Bridge question must contain question_id."),
                QuestionText = root.GetProperty("question_text").GetString()
                    ?? throw new InvalidOperationException("Bridge question must contain question_text."),
                Reason = root.GetProperty("reason").GetString()
                    ?? throw new InvalidOperationException("Bridge question must contain reason."),
                Affects = affectsElement.EnumerateArray()
                    .Select(element => element.GetString()
                        ?? throw new InvalidOperationException("Bridge question affects entries must be strings."))
                    .ToArray(),
                BlockingOrNonblocking = root.GetProperty("blocking_or_nonblocking").GetString()
                    ?? throw new InvalidOperationException("Bridge question must contain blocking_or_nonblocking.")
            };
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException("Reconstructed interview artifact bridge_questions entries must be valid JSON objects.", exception);
        }
    }
}
