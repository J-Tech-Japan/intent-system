using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G529: shared reader for the optional <c>facets:</c> frontmatter field on
/// intent-tree node Markdown files. Facets name which of the four
/// "human-retained understanding" surfaces (Operator input, issue #1159) a
/// node documents — event/command vocabulary, invariants and consistency
/// boundaries, decider judgments, and acceptance properties. The value set
/// is closed for now; extending it is future design work.
///
/// A node opts in with a <c>---</c>-delimited frontmatter block at the very
/// start of the file, e.g.:
/// <code>
/// ---
/// facets: [vocabulary, invariant]
/// ---
/// # Node title
/// ...
/// </code>
///
/// This reader is intentionally narrow: it extracts and validates ONLY the
/// <c>facets:</c> line. Any other frontmatter fields a node file may carry
/// (e.g. <c>intent_id</c>, <c>intent_type</c>) are untouched — parsing and
/// validating those is out of scope for this slice. A node with no
/// frontmatter block, or a frontmatter block with no <c>facets:</c> line,
/// is fully backward compatible: it simply has zero facets.
/// </summary>
internal static class IntentNodeFacets
{
    public const string Vocabulary = "vocabulary";
    public const string Invariant = "invariant";
    public const string Decider = "decider";
    public const string AcceptanceProperty = "acceptance-property";

    /// <summary>The closed set of recognized facet values, in the canonical order used for docs/scaffolds.</summary>
    public static readonly IReadOnlyList<string> AllowedValues = new[]
    {
        Vocabulary,
        Invariant,
        Decider,
        AcceptanceProperty,
    };

    private static readonly Regex FacetsLinePattern = new(
        @"^facets:\s*\[(?<values>[^\]]*)\]\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// Extracts the raw (unvalidated) facet tokens from a node's <c>facets:</c>
    /// frontmatter line, or an empty list when the file has no frontmatter
    /// block, or the block has no <c>facets:</c> line. Tokens are returned
    /// exactly as written (trimmed) — callers validate against
    /// <see cref="AllowedValues"/> via <see cref="IsAllowedValue"/>.
    /// </summary>
    public static IReadOnlyList<string> ExtractRawFacets(string fileContent)
    {
        ArgumentNullException.ThrowIfNull(fileContent);

        if (!TryExtractFrontmatterBlock(fileContent, out var frontmatter))
        {
            return Array.Empty<string>();
        }

        var match = FacetsLinePattern.Match(frontmatter);
        if (!match.Success)
        {
            return Array.Empty<string>();
        }

        return match.Groups["values"].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => value.Length > 0)
            .ToArray();
    }

    /// <summary>
    /// Extracts the raw text between an opening <c>---</c> line at the very
    /// start of the file and the next <c>---</c> line. Returns
    /// <see langword="false"/> when the file does not start with a
    /// frontmatter block at all (the common case for a node with no facets).
    /// </summary>
    public static bool TryExtractFrontmatterBlock(string fileContent, out string frontmatter)
    {
        frontmatter = string.Empty;
        if (string.IsNullOrEmpty(fileContent))
        {
            return false;
        }

        var normalized = fileContent.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return false;
        }

        var closingIndex = normalized.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (closingIndex < 0)
        {
            return false;
        }

        frontmatter = normalized[4..closingIndex];
        return true;
    }

    public static bool IsAllowedValue(string value) =>
        AllowedValues.Contains(value, StringComparer.Ordinal);
}
