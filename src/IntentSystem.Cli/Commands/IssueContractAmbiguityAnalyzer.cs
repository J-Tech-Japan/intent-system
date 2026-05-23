using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G389: pure detector for an ambiguous child-implementation contract — an
/// issue that declares more than one execution-unit / packet identity as its
/// primary scope. The observed failure: a child loop selected an issue that
/// bundled multiple packets, made partial local commits, and released the
/// lease without a PR. <c>issue-to-pr</c> requires exactly one self-contained
/// unit of work, so <c>worker complete</c> uses this to refuse with
/// <c>ambiguous-contract</c> rather than completing a multi-unit issue.
///
/// Precision matters: a normal issue lists several prior units under a
/// <c>## Related Links</c> section (e.g. <c>- G300 ...</c>, <c>- G311 ...</c>),
/// which must NOT be treated as the issue's own scope. Only PRIMARY identity
/// declarations count:
/// <list type="bullet">
/// <item><description>the leading <c>G&lt;n&gt;</c> of the issue title;</description></item>
/// <item><description>the leading <c>G&lt;n&gt;</c> of any H1 (<c>#</c>) heading.</description></item>
/// </list>
/// Incidental references in body prose, bullet lists, and <c>##</c>/lower
/// sections (Related Links, etc.) are ignored. More than one distinct primary
/// unit ⇒ ambiguous.
///
/// No I/O — the command layer supplies the title/body strings, so the detector
/// is fully unit-testable.
/// </summary>
internal static partial class IssueContractAmbiguityAnalyzer
{
    public static IssueContractAmbiguityResult Analyze(string? title, string? body)
    {
        var units = new SortedSet<string>(StringComparer.Ordinal);

        var titleUnit = ExtractLeadingUnit(title);
        if (titleUnit is not null)
        {
            units.Add(titleUnit);
        }

        var normalized = (body ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var rawLine in normalized.Split('\n'))
        {
            var trimmed = rawLine.TrimStart();
            // H1 only: starts with "# " but not "## " (lower headings and
            // bullet lists like Related Links are intentionally excluded).
            if (!trimmed.StartsWith("# ", StringComparison.Ordinal)
                || trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                continue;
            }

            var headingUnit = ExtractLeadingUnit(trimmed[2..]);
            if (headingUnit is not null)
            {
                units.Add(headingUnit);
            }
        }

        var isAmbiguous = units.Count > 1;
        var unitList = units.ToArray();
        return new IssueContractAmbiguityResult
        {
            IsAmbiguous = isAmbiguous,
            PrimaryUnits = unitList,
            Diagnostic = isAmbiguous
                ? $"issue declares multiple execution-unit / packet identities ({string.Join(", ", unitList)}); "
                  + "issue-to-pr requires a single self-contained unit of work. Split the issue into one packet "
                  + "per execution unit on the host/design side, then re-select."
                : string.Empty,
        };
    }

    private static string? ExtractLeadingUnit(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = LeadingUnitRegex().Match(text.Trim());
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"^(G\d+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingUnitRegex();
}

/// <summary>G389: the verdict from <see cref="IssueContractAmbiguityAnalyzer.Analyze"/>.</summary>
internal sealed record IssueContractAmbiguityResult
{
    /// <summary>True when more than one distinct primary execution-unit identity was declared.</summary>
    public required bool IsAmbiguous { get; init; }

    /// <summary>The distinct primary execution-unit identities found (title + H1 headings).</summary>
    public required IReadOnlyList<string> PrimaryUnits { get; init; }

    /// <summary>Operator-facing diagnostic (set only when <see cref="IsAmbiguous"/>).</summary>
    public required string Diagnostic { get; init; }
}
