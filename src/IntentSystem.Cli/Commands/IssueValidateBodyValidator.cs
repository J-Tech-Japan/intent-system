using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Pure validator for standalone Markdown child issue bodies (G183). Checks
/// the 10 host-review-loop Child Issue Contract headings and, when requested
/// by the drafter-facing command, ensures the <c>Target Repo / Path / Part</c>
/// section declares target paths. It also ensures the <c>Related Links</c>
/// section contains at least one non-placeholder list item. Read-only and
/// side-effect-free.
/// </summary>
internal static class IssueValidateBodyValidator
{
    private const string TargetRepoPathPartHeading = "Target Repo / Path / Part";

    /// <summary>
    /// Required Child Issue Contract headings, in canonical order.
    /// "Target Repo / Path / Part" is the canonical form; the hyphen variant
    /// "Target Repo-Path-Part" is also accepted because both shapes are in
    /// active use across the host workflow.
    /// </summary>
    // G482: shares the single source of truth with the packet scaffold so the
    // publish gate and the draft contract check never disagree.
    public static IReadOnlyList<string> RequiredHeadings => PublishContractSections.Required;

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

    // The strict drafter-facing check requires the canonical bullet form. The
    // packet-draft guide and scaffold emit this exact line; legacy consumers
    // do not opt into this check, so already-published bodies are not
    // retroactively invalidated.
    private static readonly Regex TargetPathsDeclarationRegex = new(
        @"^\s*-\s+Target paths:\s*(?<paths>\S.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // The strict target-path check is opt-in so legacy consumers that inspect
    // already-published bodies do not retroactively invalidate them. The
    // standalone `issue validate-body` command opts in before issue creation.
    public static IssueValidateBodyResult Validate(
        string sourcePath,
        string content,
        bool requireTargetPathsDeclaration = false)
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

        var targetPathsInvalid = false;
        string? targetPathsReason = null;
        if (requireTargetPathsDeclaration
            && TryGetSection(TargetRepoPathPartHeading, sections, out var targetSectionBody))
        {
            (targetPathsInvalid, targetPathsReason) = ValidateTargetPathsDeclaration(targetSectionBody);
        }

        var isValid = missing.Count == 0
            && !relatedLinksInvalid
            && !targetPathsInvalid;

        return new IssueValidateBodyResult
        {
            SourcePath = sourcePath,
            IsValid = isValid,
            MissingHeadings = missing,
            RelatedLinksInvalid = relatedLinksInvalid,
            RelatedLinksReason = relatedLinksReason,
            TargetPathsInvalid = targetPathsInvalid,
            TargetPathsReason = targetPathsReason
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

    private static bool TryGetSection(
        string canonicalHeading,
        IReadOnlyDictionary<string, string> sections,
        out string body)
    {
        if (sections.TryGetValue(canonicalHeading, out body!))
        {
            return true;
        }

        if (HeadingAliases.TryGetValue(canonicalHeading, out var aliases))
        {
            foreach (var alias in aliases)
            {
                if (sections.TryGetValue(alias, out body!))
                {
                    return true;
                }
            }
        }

        body = string.Empty;
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

    private static (bool Invalid, string? Reason) ValidateTargetPathsDeclaration(string body)
    {
        foreach (var rawLine in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (TargetPathsDeclarationRegex.IsMatch(rawLine))
            {
                return (false, null);
            }
        }

        return (
            true,
            "Target Repo / Path / Part is present, but the declaration is missing. "
                + "Add the literal `- Target paths: <path>` line (for example, "
                + "`- Target paths: src/IntentSystem.Cli/Commands`).");
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
