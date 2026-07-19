using System.Text;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G529 (+ rereview repair): shared reader for the optional <c>facets:</c>
/// frontmatter field on intent-tree node Markdown files. Facets name which
/// of the four "human-retained understanding" surfaces (Operator input,
/// issue #1159) a node documents — event/command vocabulary, invariants and
/// consistency boundaries, decider judgments, and acceptance properties.
/// The value set is closed for now; extending it is future design work.
///
/// A node opts in with a <c>---</c>-delimited frontmatter block at the very
/// start of the file, using a genuine YAML list — either flow form:
/// <code>
/// ---
/// facets: [vocabulary, invariant]
/// ---
/// </code>
/// or block form:
/// <code>
/// ---
/// facets:
///   - vocabulary
///   - invariant
/// ---
/// </code>
///
/// <see cref="ParseFacets"/> supports both forms, quoted scalars
/// (<c>"vocabulary"</c> / <c>'vocabulary'</c>), inline <c>#</c> comments, a
/// flow list spanning multiple physical lines, and duplicate values
/// (deduplicated once, centrally, preserving first-seen order — every
/// caller sees the same deduplicated list, so lint/search/analyze can never
/// disagree on how many times a facet "counts"). A <c>facets:</c>
/// declaration that is present but does not parse as a YAML list (a bare
/// scalar, an unterminated flow list, an unbalanced quote, tab indentation)
/// is reported as <see cref="FacetsParseKind.Malformed"/> — it is never
/// silently treated as absent. Only a genuinely missing <c>facets:</c> key
/// (or no frontmatter block at all) is <see cref="FacetsParseKind.Absent"/>,
/// which is fully backward compatible: the node is simply unannotated.
///
/// This reader is intentionally narrow: it extracts and validates ONLY the
/// top-level <c>facets:</c> key. Any other frontmatter fields a node file
/// may carry (e.g. <c>intent_id</c>, <c>intent_type</c>) are untouched —
/// parsing and validating those is out of scope for this slice.
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

    private const string FacetsKeyPrefix = "facets:";

    /// <summary>
    /// Parses the node's <c>facets:</c> frontmatter field. See the type
    /// doc-comment for the exact forms supported and the absent/malformed/
    /// present distinction.
    /// </summary>
    public static FacetsParseResult ParseFacets(string fileContent)
    {
        ArgumentNullException.ThrowIfNull(fileContent);

        if (!TryExtractFrontmatterBlock(fileContent, out var frontmatter))
        {
            return FacetsParseResult.AbsentResult;
        }

        var lines = frontmatter.Split('\n');

        // Top-level key only: must start at column 0. A "facets:" appearing
        // under some other key's indentation is out of scope (narrow reader).
        var facetsLineIndex = Array.FindIndex(lines, line => line.StartsWith(FacetsKeyPrefix, StringComparison.Ordinal));
        if (facetsLineIndex < 0)
        {
            return FacetsParseResult.AbsentResult;
        }

        if (lines[facetsLineIndex].Contains('\t'))
        {
            return FacetsParseResult.Malformed("tab character is invalid YAML indentation");
        }

        var rest = lines[facetsLineIndex][FacetsKeyPrefix.Length..];
        var (restBeforeComment, _) = StripInlineComment(rest);
        var trimmedRest = restBeforeComment.Trim();

        if (trimmedRest.Length == 0)
        {
            return ParseBlockForm(lines, facetsLineIndex + 1);
        }

        if (trimmedRest[0] == '[')
        {
            return ParseFlowForm(lines, facetsLineIndex, trimmedRest);
        }

        return FacetsParseResult.Malformed(
            $"expected a YAML list after 'facets:' (flow '[item, ...]' or block '- item' form), found scalar '{trimmedRest}'");
    }

    // ── Block form: "facets:" alone, followed by indented "- item" lines ──

    private static FacetsParseResult ParseBlockForm(string[] frontmatterLines, int startIndex)
    {
        var items = new List<string>();

        for (var i = startIndex; i < frontmatterLines.Length; i++)
        {
            var rawLine = frontmatterLines[i];
            if (rawLine.Contains('\t'))
            {
                return FacetsParseResult.Malformed("tab character is invalid YAML indentation");
            }

            var trimmed = rawLine.TrimStart();
            if (trimmed.Length == 0)
            {
                continue; // blank line inside the block list is allowed
            }
            if (trimmed.StartsWith('#'))
            {
                continue; // whole-line comment
            }
            if (!rawLine.StartsWith(' '))
            {
                // Not indented under the key: a new top-level entry — the
                // block list has ended (not an error, just its boundary).
                break;
            }
            if (!(trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed == "-"))
            {
                return FacetsParseResult.Malformed($"expected a '- item' list entry, found '{trimmed}'");
            }

            var rawItem = trimmed == "-" ? string.Empty : trimmed[2..];
            var (beforeComment, _) = StripInlineComment(rawItem);
            var token = beforeComment.Trim();
            if (!TryUnquote(token, out var unquoted))
            {
                return FacetsParseResult.Malformed($"unbalanced quote in list item '{token}'");
            }
            if (unquoted.Length == 0)
            {
                return FacetsParseResult.Malformed("empty list item under 'facets:'");
            }
            items.Add(unquoted);
        }

        if (items.Count == 0)
        {
            return FacetsParseResult.Malformed(
                "'facets:' has no value — use '[]' for an explicitly empty list, or list values with '- item' entries");
        }

        return FacetsParseResult.Present(Deduplicate(items));
    }

    // ── Flow form: "facets: [a, b]", possibly spanning multiple lines ─────

    private static FacetsParseResult ParseFlowForm(string[] frontmatterLines, int facetsLineIndex, string firstSegment)
    {
        var inner = new StringBuilder();
        var depth = 0;
        var inSingle = false;
        var inDouble = false;
        var closed = false;

        bool ScanSegment(string segment)
        {
            foreach (var c in segment)
            {
                if (inSingle)
                {
                    inner.Append(c);
                    if (c == '\'')
                    {
                        inSingle = false;
                    }
                    continue;
                }
                if (inDouble)
                {
                    inner.Append(c);
                    if (c == '"')
                    {
                        inDouble = false;
                    }
                    continue;
                }
                if (c == '\'')
                {
                    inSingle = true;
                    inner.Append(c);
                    continue;
                }
                if (c == '"')
                {
                    inDouble = true;
                    inner.Append(c);
                    continue;
                }
                if (c == '[')
                {
                    depth++;
                    if (depth == 1)
                    {
                        continue; // exclude the outermost opening bracket
                    }
                    inner.Append(c);
                    continue;
                }
                if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        closed = true;
                        return true;
                    }
                    inner.Append(c);
                    continue;
                }
                inner.Append(c);
            }
            return false;
        }

        if (!ScanSegment(firstSegment))
        {
            inner.Append(' ');

            var lineIndex = facetsLineIndex;
            while (!closed)
            {
                lineIndex++;
                if (lineIndex >= frontmatterLines.Length)
                {
                    return FacetsParseResult.Malformed("unterminated '[' list under 'facets:' (missing closing ']')");
                }
                if (frontmatterLines[lineIndex].Contains('\t'))
                {
                    return FacetsParseResult.Malformed("tab character is invalid YAML indentation");
                }

                var (beforeComment, _) = StripInlineComment(frontmatterLines[lineIndex]);
                if (ScanSegment(beforeComment))
                {
                    break;
                }
                inner.Append(' ');
            }
        }

        if (inSingle || inDouble)
        {
            return FacetsParseResult.Malformed("unterminated quoted string in 'facets:' list");
        }

        return FinishFlowForm(inner.ToString());
    }

    private static FacetsParseResult FinishFlowForm(string inner)
    {
        var items = new List<string>();
        foreach (var rawToken in SplitTopLevelCommas(inner))
        {
            var token = rawToken.Trim();
            if (token.Length == 0)
            {
                continue; // tolerate a trailing comma, e.g. "[a, b,]"
            }
            if (!TryUnquote(token, out var unquoted))
            {
                return FacetsParseResult.Malformed($"unbalanced quote in list item '{token}'");
            }
            if (unquoted.Length == 0)
            {
                return FacetsParseResult.Malformed("empty list item in 'facets:' list");
            }
            items.Add(unquoted);
        }

        return FacetsParseResult.Present(Deduplicate(items));
    }

    // ── Shared token helpers ────────────────────────────────────────────────

    /// <summary>Splits on top-level commas only — a comma inside a quoted scalar does not split.</summary>
    private static IReadOnlyList<string> SplitTopLevelCommas(string text)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inSingle = false;
        var inDouble = false;

        foreach (var c in text)
        {
            if (inSingle)
            {
                current.Append(c);
                if (c == '\'')
                {
                    inSingle = false;
                }
                continue;
            }
            if (inDouble)
            {
                current.Append(c);
                if (c == '"')
                {
                    inDouble = false;
                }
                continue;
            }
            if (c == '\'')
            {
                inSingle = true;
                current.Append(c);
                continue;
            }
            if (c == '"')
            {
                inDouble = true;
                current.Append(c);
                continue;
            }
            if (c == ',')
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        result.Add(current.ToString());
        return result;
    }

    /// <summary>
    /// Strips a matching pair of surrounding quotes from a token. Returns
    /// <see langword="false"/> when the token opens a quote but does not
    /// close it with the same delimiter (a genuine syntax error, never
    /// silently treated as a literal value).
    /// </summary>
    private static bool TryUnquote(string token, out string value)
    {
        value = token;
        if (token.Length == 0)
        {
            return true;
        }

        var quote = token[0];
        if (quote != '"' && quote != '\'')
        {
            return true; // unquoted plain scalar
        }
        if (token.Length < 2 || token[^1] != quote)
        {
            return false;
        }

        value = token[1..^1];
        return true;
    }

    /// <summary>
    /// Strips a <c>#</c> comment from a single physical line, respecting
    /// quotes — a <c>#</c> inside an open quote is literal content, not a
    /// comment marker.
    /// </summary>
    private static (string Before, bool HadComment) StripInlineComment(string line)
    {
        var inSingle = false;
        var inDouble = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
            }
            else if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
            }
            else if (c == '#' && !inSingle && !inDouble)
            {
                return (line[..i], true);
            }
        }
        return (line, false);
    }

    /// <summary>Stable dedup: first-seen order preserved, ordinal comparison — the single dedup point every caller shares.</summary>
    private static IReadOnlyList<string> Deduplicate(IReadOnlyList<string> items)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(items.Count);
        foreach (var item in items)
        {
            if (seen.Add(item))
            {
                result.Add(item);
            }
        }
        return result;
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

/// <summary>Discriminates the three possible outcomes of <see cref="IntentNodeFacets.ParseFacets"/>.</summary>
internal enum FacetsParseKind
{
    /// <summary>No frontmatter block, or a frontmatter block with no <c>facets:</c> key — fully backward compatible.</summary>
    Absent,

    /// <summary>A <c>facets:</c> key is present but does not parse as a valid YAML list — a lint ERROR, never silently absent.</summary>
    Malformed,

    /// <summary>A <c>facets:</c> key parsed successfully; <see cref="Values"/> holds the deduplicated tokens (not yet validated against the closed set).</summary>
    Present,
}

internal sealed record FacetsParseResult
{
    public required FacetsParseKind Kind { get; init; }
    public required IReadOnlyList<string> Values { get; init; }
    public string? MalformedReason { get; init; }

    public static readonly FacetsParseResult AbsentResult = new()
    {
        Kind = FacetsParseKind.Absent,
        Values = Array.Empty<string>(),
    };

    public static FacetsParseResult Malformed(string reason) => new()
    {
        Kind = FacetsParseKind.Malformed,
        Values = Array.Empty<string>(),
        MalformedReason = reason,
    };

    public static FacetsParseResult Present(IReadOnlyList<string> values) => new()
    {
        Kind = FacetsParseKind.Present,
        Values = values,
    };
}
