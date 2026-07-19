using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G529 (+ rereview repairs): shared reader for the optional <c>facets:</c>
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
/// <see cref="ParseFacets"/> hands the <c>facets:</c> value off to
/// <b>YamlDotNet</b> — a real YAML parser — rather than a hand-rolled
/// scanner. Prior repair rounds each found a fresh correctness gap in a
/// bespoke tokenizer; a real parser resolves escaping, doubled-single-quote
/// semantics, comments, multi-line flow, and trailing-content rejection
/// correctly by construction instead of by ad hoc patching.
///
/// Extraction is narrow in BOTH directions: content before the top-level
/// <c>facets:</c> line is never fed to the parser at all (excluded from the
/// input text), and parsing STOPS the instant the facets value itself is
/// fully consumed — YamlDotNet's low-level event parser is lazy/streaming,
/// so a later, unrelated key's own malformed value is never even
/// tokenized, let alone attributed to <c>facets:</c>. (A duplicate
/// top-level <c>facets:</c> key is instead detected up front via a cheap
/// line-level scan, deterministically, before any parsing is attempted.)
///
/// A <c>facets:</c> declaration that is present but does not parse as a
/// YAML list (a bare scalar, an unterminated flow list, an unbalanced
/// quote, non-comment trailing content after the list, tab indentation) is
/// reported as <see cref="FacetsParseKind.Malformed"/> — it is never
/// silently treated as absent. Only a genuinely missing <c>facets:</c> key
/// (or no frontmatter block at all) is <see cref="FacetsParseKind.Absent"/>,
/// which is fully backward compatible: the node is simply unannotated.
///
/// This reader is intentionally narrow in scope too: it locates and
/// validates ONLY the top-level <c>facets:</c> key. Any other frontmatter
/// fields a node file may carry (e.g. <c>intent_id</c>, <c>intent_type</c>)
/// are untouched — parsing and validating those is out of scope for this
/// slice, and (per the above) their own syntax is never even inspected.
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
    private const string FacetsKeyName = "facets";

    /// <summary>
    /// Parses the node's <c>facets:</c> frontmatter field. See the type
    /// doc-comment for the exact forms supported and the absent/malformed/
    /// present distinction.
    /// </summary>
    public static FacetsParseResult ParseFacets(string fileContent)
    {
        ArgumentNullException.ThrowIfNull(fileContent);

        if (!TryExtractFrontmatterBlock(fileContent, out var frontmatter, out _))
        {
            return FacetsParseResult.AbsentResult;
        }

        var lines = frontmatter.Split('\n');

        // Top-level key only: must start at column 0 with no leading
        // whitespace at all. A "facets:" appearing under some other key's
        // indentation is out of scope (narrow reader) — UNLESS it was
        // clearly intended as the top-level declaration but mis-indented
        // with a tab, which is never silently absent (see below).
        var facetsLineIndices = new List<int>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith(FacetsKeyPrefix, StringComparison.Ordinal))
            {
                facetsLineIndices.Add(i);
            }
        }

        if (facetsLineIndices.Count == 0)
        {
            if (Array.Exists(lines, LooksLikeTabIndentedFacetsDeclaration))
            {
                return FacetsParseResult.Malformed("tab character is invalid YAML indentation");
            }
            return FacetsParseResult.AbsentResult;
        }

        if (facetsLineIndices.Count > 1)
        {
            // Deterministic, cheap, and parser-independent: a second
            // top-level "facets:" key is unambiguously a contract
            // violation regardless of whether either declaration is
            // otherwise well-formed YAML.
            //
            // `frontmatter` is everything BETWEEN the opening "---" line
            // and the closing one (see TryExtractFrontmatterBlock), so its
            // own line 0 is already file line 2 — translate a frontmatter
            // index back to a 1-based FILE line by adding 2, not 1, or the
            // reported location is one line short of the actual file.
            return FacetsParseResult.Malformed(
                $"multiple top-level 'facets:' keys found in frontmatter (lines {string.Join(", ", facetsLineIndices.Select(i => i + 2))}); only one is allowed");
        }

        // Hand YamlDotNet everything from the "facets:" line to the end of
        // the frontmatter block as the INPUT TEXT — but the low-level event
        // parser below only reads as far as needed to fully consume the
        // facets value itself, then stops. Content before this line (other
        // fields) is excluded from the input text entirely; content after
        // it is included in the input text but is never actually parsed,
        // so neither can surface as a facets: problem.
        var fragment = string.Join('\n', lines[facetsLineIndices[0]..]);
        return ParseFacetsFragment(fragment);
    }

    private static bool LooksLikeTabIndentedFacetsDeclaration(string line) =>
        line.Contains('\t') && line.TrimStart('\t', ' ').StartsWith(FacetsKeyPrefix, StringComparison.Ordinal);

    private static FacetsParseResult ParseFacetsFragment(string fragment)
    {
        try
        {
            using var reader = new StringReader(fragment);
            IParser parser = new Parser(reader);

            parser.Consume<StreamStart>();
            parser.Consume<DocumentStart>();
            if (!parser.TryConsume<MappingStart>(out _))
            {
                return FacetsParseResult.Malformed("'facets:' did not parse as a YAML mapping entry");
            }

            var key = parser.Consume<Scalar>();
            if (!string.Equals(key.Value, FacetsKeyName, StringComparison.Ordinal))
            {
                // Should not happen — the caller only reaches here after
                // locating a "facets:" line itself — but stay defensive.
                return FacetsParseResult.AbsentResult;
            }

            var result = ConsumeFacetsValue(parser);

            // One more look-ahead only: confirm the value's own line ends
            // cleanly (either the mapping ends here, or a new key starts).
            // This is what catches non-comment trailing content on the
            // SAME declaration (e.g. "facets: [a] junk") without ever
            // parsing — or risking an exception from — anything belonging
            // to a genuinely separate, later key.
            if (result.Kind == FacetsParseKind.Present
                && !parser.Accept<MappingEnd>(out _)
                && !parser.Accept<Scalar>(out _))
            {
                parser.MoveNext(); // force tokenization to surface the real diagnostic
                return FacetsParseResult.Malformed("unexpected content immediately after the 'facets:' declaration");
            }

            return result;
        }
        catch (YamlException exception)
        {
            // Covers every hand-rolled failure mode from prior rounds for
            // free: unterminated flow lists, unbalanced/unescaped quotes,
            // non-comment trailing content after a flow list's closing
            // bracket, and tab indentation — all genuine YAML syntax
            // errors that a real parser rejects by construction. The
            // message is sanitized: no absolute paths (none are ever
            // legitimately present, parsing an in-memory fragment, but
            // this is defense in depth against library internals) and
            // capped in length so a pathological input can't flood output.
            return FacetsParseResult.Malformed($"invalid YAML: {SanitizeYamlDiagnostic(exception)}");
        }
    }

    private static FacetsParseResult ConsumeFacetsValue(IParser parser)
    {
        if (parser.TryConsume<SequenceStart>(out _))
        {
            var items = new List<string>();
            while (!parser.Accept<SequenceEnd>(out _))
            {
                if (!parser.TryConsume<Scalar>(out var itemScalar) || itemScalar.Value.Length == 0)
                {
                    return FacetsParseResult.Malformed("'facets:' list contains a non-scalar or empty item");
                }
                items.Add(itemScalar.Value);
            }
            parser.Consume<SequenceEnd>();
            return FacetsParseResult.Present(Deduplicate(items));
        }

        if (parser.TryConsume<Scalar>(out var scalar))
        {
            if (scalar.Value.Length == 0)
            {
                // "facets:" with nothing after it and no block items either
                // (YAML null) — use "[]" for an intentionally empty list.
                return FacetsParseResult.Malformed(
                    "'facets:' has no value — use '[]' for an explicitly empty list, or list values with '- item' entries");
            }
            return FacetsParseResult.Malformed(
                $"expected a YAML list after 'facets:' (flow '[item, ...]' or block '- item' form), found scalar '{scalar.Value}'");
        }

        return FacetsParseResult.Malformed("'facets:' value did not parse as a YAML list or scalar");
    }

    private static readonly Regex PathLikeFragmentPattern = new(
        @"[A-Za-z]:\\[^\s""']+|/(?:[^\s""'/]+/)+[^\s""']*",
        RegexOptions.Compiled);

    private const int MaxDiagnosticLength = 300;

    private static string SanitizeYamlDiagnostic(YamlException exception)
    {
        var message = string.IsNullOrWhiteSpace(exception.Message) ? "malformed YAML" : exception.Message;
        message = PathLikeFragmentPattern.Replace(message, "<redacted>");
        return message.Length > MaxDiagnosticLength
            ? message[..MaxDiagnosticLength] + "…"
            : message;
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
    /// Extracts the raw text between an opening <c>---</c> delimiter line at
    /// the very start of the file and the next exact <c>---</c> delimiter
    /// line, plus the remaining body text after that closing delimiter.
    /// Delimiter lines must match exactly (trailing whitespace tolerated) —
    /// a line like <c>---junk</c> is never mistaken for the closing
    /// delimiter. Returns <see langword="false"/> when the file does not
    /// start with a frontmatter block at all (the common case for a node
    /// with no facets).
    /// </summary>
    public static bool TryExtractFrontmatterBlock(string fileContent, out string frontmatter, out string body)
    {
        frontmatter = string.Empty;
        body = fileContent ?? string.Empty;
        if (string.IsNullOrEmpty(fileContent))
        {
            return false;
        }

        var normalized = fileContent.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        if (lines.Length == 0 || lines[0].TrimEnd() != "---")
        {
            return false;
        }

        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd() != "---")
            {
                continue;
            }

            frontmatter = string.Join('\n', lines[1..i]);
            body = string.Join('\n', lines[(i + 1)..]);
            return true;
        }

        return false;
    }

    /// <summary>Convenience overload for callers that only need the frontmatter text itself.</summary>
    public static bool TryExtractFrontmatterBlock(string fileContent, out string frontmatter) =>
        TryExtractFrontmatterBlock(fileContent, out frontmatter, out _);

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
