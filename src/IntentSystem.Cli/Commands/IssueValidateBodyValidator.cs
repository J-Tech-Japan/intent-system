namespace IntentSystem.Cli.Commands;

/// <summary>
/// Pure validator for standalone Markdown child issue bodies (G183). Checks
/// the 10 host-review-loop Child Issue Contract headings and ensures the
/// <c>Related Links</c> section contains at least one non-placeholder list
/// item. Read-only and side-effect-free.
/// </summary>
internal static class IssueValidateBodyValidator
{
    /// <summary>
    /// Required Child Issue Contract headings, in canonical order.
    /// "Target Repo / Path / Part" is the canonical form; the hyphen variant
    /// "Target Repo-Path-Part" is also accepted because both shapes are in
    /// active use across the host workflow.
    /// </summary>
    public static IReadOnlyList<string> RequiredHeadings { get; } =
    [
        "Goal",
        "Why This Slice Exists Now",
        "Current Observed State",
        "Accepted Baseline You May Assume",
        "Target Repo / Path / Part",
        "In Scope",
        "Out Of Scope",
        "Acceptance Criteria",
        "Verification",
        "Related Links",
        // G347: child implementation agents must know the expected PR base branch
        // from the issue body alone (no host metadata access in child-cwd mode).
        "Base Branch Policy"
    ];

    private static readonly IReadOnlyDictionary<string, string[]> HeadingAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Target Repo / Path / Part"] = ["Target Repo-Path-Part"]
        };

    /// <summary>
    /// Trimmed bullet contents that count as placeholders rather than real
    /// links. Match is case-insensitive and applies to the full trimmed item
    /// (after stripping leading list markers and trailing punctuation).
    /// </summary>
    private static readonly HashSet<string> PlaceholderTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "todo",
        "fixme",
        "tbd",
        "n/a",
        "na",
        "none",
        "placeholder",
        "...",
        "-"
    };

    public static IssueValidateBodyResult Validate(string sourcePath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(content);

        var sections = ExtractSections(content);
        var missing = new List<string>();
        foreach (var heading in RequiredHeadings)
        {
            if (!sections.ContainsKey(heading) && !TryFindAlias(heading, sections))
            {
                missing.Add(heading);
            }
        }

        var relatedLinksInvalid = false;
        string? relatedLinksReason = null;
        if (sections.TryGetValue("Related Links", out var relatedLinksBody))
        {
            (relatedLinksInvalid, relatedLinksReason) = ValidateRelatedLinks(relatedLinksBody);
        }

        var isValid = missing.Count == 0 && !relatedLinksInvalid;

        return new IssueValidateBodyResult
        {
            SourcePath = sourcePath,
            IsValid = isValid,
            MissingHeadings = missing,
            RelatedLinksInvalid = relatedLinksInvalid,
            RelatedLinksReason = relatedLinksReason
        };
    }

    private static bool TryFindAlias(string canonicalHeading, IReadOnlyDictionary<string, string> sections)
    {
        if (!HeadingAliases.TryGetValue(canonicalHeading, out var aliases))
        {
            return false;
        }

        foreach (var alias in aliases)
        {
            if (sections.ContainsKey(alias))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyDictionary<string, string> ExtractSections(string content)
    {
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        string? currentHeading = null;
        var currentBody = new List<string>();

        void Flush()
        {
            if (currentHeading is null)
            {
                return;
            }

            sections[currentHeading] = string.Join("\n", currentBody);
        }

        foreach (var rawLine in lines)
        {
            var trimmedLine = rawLine.TrimEnd();
            if (trimmedLine.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                currentBody.Clear();
                currentHeading = trimmedLine[3..].Trim();
                continue;
            }

            if (currentHeading is not null)
            {
                currentBody.Add(rawLine);
            }
        }

        Flush();
        return sections;
    }

    private static (bool Invalid, string? Reason) ValidateRelatedLinks(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (true, "Related Links section is empty.");
        }

        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var foundAnyItem = false;
        var hasMeaningfulItem = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimStart();
            if (!line.StartsWith("- ", StringComparison.Ordinal)
                && !line.StartsWith("* ", StringComparison.Ordinal))
            {
                continue;
            }

            foundAnyItem = true;
            var item = line[2..].Trim().TrimEnd('.', ',', ';', ':');

            // Strip surrounding parentheses/brackets that operators sometimes
            // add around placeholders, e.g. "(TODO)" or "[N/A]".
            item = StripWrappers(item);

            if (item.Length == 0)
            {
                continue;
            }

            if (PlaceholderTokens.Contains(item))
            {
                continue;
            }

            hasMeaningfulItem = true;
            break;
        }

        if (!foundAnyItem)
        {
            return (true, "Related Links section has no list items.");
        }

        if (!hasMeaningfulItem)
        {
            return (true, "Related Links section contains only TODO/FIXME/placeholder entries.");
        }

        return (false, null);
    }

    private static string StripWrappers(string value)
    {
        var trimmed = value.Trim();
        while (trimmed.Length >= 2)
        {
            var first = trimmed[0];
            var last = trimmed[^1];
            if ((first == '(' && last == ')')
                || (first == '[' && last == ']')
                || (first == '"' && last == '"'))
            {
                trimmed = trimmed[1..^1].Trim();
                continue;
            }

            break;
        }

        return trimmed;
    }
}
