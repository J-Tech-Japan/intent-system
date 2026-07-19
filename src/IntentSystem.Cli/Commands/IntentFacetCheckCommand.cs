using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G531: read-only <c>intent facet-check</c> scaffold. Extracts candidate
/// command/event terms from a change proposal — either a packet's
/// <c>github-body.md</c>/<c>implementation.md</c> (<c>--packet</c>) or an
/// explicit <c>--terms</c> list — and reports, per term, the related G529
/// facet nodes (vocabulary/invariant prioritized, then decider/
/// acceptance-property, matching <see cref="IntentNodeFacets.AllowedValues"/>
/// canonical order), collision candidates against existing vocabulary, and
/// an unmatched flag. In <c>--packet</c> mode it also reports
/// acceptance-property coverage for the packet's own <c>intent_references</c>
/// scope.
///
/// This is explicitly NOT a semantic verifier: matching is lexical (a
/// full-token match once a term and a node's own id/title are both case/
/// punctuation normalized — never a substring search) — false negatives are
/// expected and acceptable, silent claims of verification are not. It never
/// mutates state, never blocks on findings: every output carries a
/// <see cref="FacetCheckResult.Disclaimer"/> field and the command always
/// exits 0 regardless of findings. A genuine input/IO failure (e.g. an
/// unreadable packet source file) is a different thing — that is a real
/// execution error (non-zero exit), not a "finding".
/// </summary>
internal static class IntentFacetCheckCommand
{
    private const string FormatMarkdown = "markdown";
    private const string FormatJson = "json";

    private const string Disclaimer =
        "Lexical scaffolding only — not semantic verification. Findings guide human/reviewer judgment; this command never blocks and false negatives are expected.";

    private const string UsageLine =
        "Usage: intent-cli intent facet-check --domain <name> (--packet <execution-unit> | --terms <comma-list>) [--format markdown|json]";

    private const string EvidenceFieldId = "id";
    private const string EvidenceFieldTitle = "title";
    private const string MatchKindExact = "exact";
    private const string MatchKindNormalized = "normalized";

    private const string ScopeStatusValidEmpty = "valid-empty";
    private const string ScopeStatusValidNonEmpty = "valid-non-empty";
    private const string ScopeStatusMissing = "missing";
    private const string ScopeStatusMalformed = "malformed";
    private const string ScopeStatusWrongShape = "wrong-shape";

    // G531 review repair: a backtick preceded by a backslash is an escaped
    // literal, never a real inline-code delimiter — the negative lookbehind
    // on the OPENING backtick keeps "the \`x\` is escaped" from being
    // misread as a span.
    private static readonly Regex BacktickSpanRegex = new(@"(?<!\\)`([^`\n]+)`", RegexOptions.Compiled);
    private static readonly Regex WordTokenRegex = new(@"\b[A-Za-z][A-Za-z0-9]*\b", RegexOptions.Compiled);
    private static readonly Regex BareIdentifierRegex = new(@"^[A-Za-z][A-Za-z0-9_-]*$", RegexOptions.Compiled);
    private static readonly Regex CaseTransitionRegex = new(@"[a-z][A-Z]", RegexOptions.Compiled);
    private static readonly string[] CommandEventSuffixes = { "Command", "Event", "Query" };

    // G531 review repair round 4: a fenced code block's OPENING line, per
    // CommonMark's actual boundary — AT MOST 3 leading spaces (a tab, or 4+
    // spaces, is never a fence; that is either plain prose or a DIFFERENT
    // construct, an indented code block, which this scaffold does not
    // attempt to mask), then 3+ of the SAME fence character (backtick or
    // tilde), then the rest of the line as the info string. A backtick
    // fence's info string additionally may never itself contain a backtick
    // (ambiguous with inline code) — that extra check happens in code (see
    // <see cref="MaskFencedCodeBlocks"/>) since it depends on WHICH fence
    // character matched. The matching CLOSE line is found procedurally
    // (same char, count >= the opener's, same 0-3-space rule, CRLF/LF-
    // tolerant) — a single static regex can't express "closing fence
    // length >= this specific opening fence's length" without a
    // dynamically-sized quantifier, so that part is built per fence found.
    private static readonly Regex FenceOpenRegex = new(@"(?m)^ {0,3}(`{3,}|~{3,})([^\r\n]*)(?:\r?\n|$)", RegexOptions.Compiled);

    // G531 review repair round 4: a "link reference definition" line (e.g.
    // `[ref]: docs/path.md "title"`) — pure destination/title metadata,
    // never proposal prose, so the WHOLE line is noise. This is distinct
    // from a reference-style link's USAGE (`[label][ref]`, `[label][]`, or
    // a bare `[label]`), which has no adjacent `(...)`/`: ...` destination
    // of its own and is therefore left untouched — its label is exactly as
    // extractable as any other visible text.
    private static readonly Regex ReferenceDefinitionRegex = new(@"(?m)^ {0,3}\[[^\]\n]+\]:[ \t]*\S[^\r\n]*(?:\r?\n|$)", RegexOptions.Compiled);

    // G531 review repair round 4: a CommonMark autolink, `<scheme://...>` —
    // unlike `[label](url)`, an autolink has no separate visible label; the
    // whole angle-bracketed span IS the destination, so it is noise in its
    // entirety (not "target masked, label preserved").
    private static readonly Regex AutolinkRegex = new(@"<[a-zA-Z][a-zA-Z0-9+.-]*://[^<>\s]*>", RegexOptions.Compiled);

    private static readonly Regex UrlRegex = new(@"https?://\S+", RegexOptions.Compiled);
    private static readonly Regex PathLikeRegex = new(@"[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)+", RegexOptions.Compiled);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
        => Execute(context, args, writer, File.ReadAllText);

    /// <summary>
    /// G531 review repair: the <c>readAllText</c> seam exists so tests can
    /// deterministically fault-inject an I/O failure on a specific packet
    /// source path (a fake reader that throws) WITHOUT relying on chmod or
    /// platform/user identity, which are fragile and non-portable (chmod
    /// permission bits are bypassed when tests run as root, and behave
    /// differently on Windows). The public zero-seam overload always passes
    /// the real <see cref="File.ReadAllText(string)"/>.
    /// </summary>
    internal static int Execute(CliContext context, string[] args, TextWriter writer, Func<string, string> readAllText)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(readAllText);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseArguments(args, out var domainOverride, out var executionUnit, out var explicitTerms, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride!;

        IReadOnlyList<string> candidateTerms;
        FacetCheckPacketSource? packetSource = null;

        if (executionUnit is not null)
        {
            var packetDirectory = Path.Combine(context.RepoRoot, ".intent-cli", "issues", executionUnit);
            if (!Directory.Exists(packetDirectory))
            {
                writer.WriteLine($"No packet directory found for execution-unit '{executionUnit}' at {packetDirectory}.");
                return 1;
            }

            var githubBodyPath = Path.Combine(packetDirectory, "github-body.md");
            var implementationPath = Path.Combine(packetDirectory, "implementation.md");
            var packetYamlPath = Path.Combine(packetDirectory, "packet.yaml");

            string sourceText;
            PacketScopeReadResult scopeResult;
            try
            {
                // G531 review repair: github-body.md/implementation.md now
                // go through the SAME readAllText seam (and the same
                // FacetCheckPacketIoException boundary) as packet.yaml — an
                // unreadable/racing source file used to throw uncaught
                // straight out of File.ReadAllText instead of producing the
                // documented controlled exit 1.
                sourceText = string.Join(
                    "\n",
                    new[] { githubBodyPath, implementationPath }
                        .Where(File.Exists)
                        .Select(path => ReadFileOrThrowIo(path, readAllText)));
                scopeResult = ReadPacketScope(packetYamlPath, readAllText);
            }
            catch (FacetCheckPacketIoException exception)
            {
                writer.WriteLine($"Failed to read packet source for execution-unit '{executionUnit}': {exception.Message}");
                return 1;
            }

            candidateTerms = ExtractCandidateTerms(sourceText);
            packetSource = new FacetCheckPacketSource
            {
                ExecutionUnit = executionUnit,
                ScopeResult = scopeResult,
            };
        }
        else
        {
            candidateTerms = explicitTerms!;
        }

        var facetDomainRoot = ResolveFacetDomainRoot(context, domain);
        var result = Analyze(domain, facetDomainRoot, candidateTerms, packetSource);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        return 0;
    }

    private static string ReadFileOrThrowIo(string path, Func<string, string> readAllText)
    {
        try
        {
            return readAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new FacetCheckPacketIoException($"could not read {path}: {exception.Message}", exception);
        }
    }

    internal static FacetCheckResult Analyze(
        string domain,
        string facetDomainRoot,
        IReadOnlyList<string> candidateTerms,
        FacetCheckPacketSource? packetSource)
    {
        var wholeDomain = FacetContextSelector.Select(facetDomainRoot, domain, scopeHints: null, facetFilter: null);

        // A node carrying more than one facet appears once per matching
        // group in wholeDomain.Groups (e.g. once under "vocabulary", again
        // under "invariant") — dedupe by id here, keeping the first
        // occurrence, so a term match reports that node ONCE rather than
        // once per facet it happens to carry. Groups is already iterated in
        // AllowedValues canonical order, so the first occurrence is always
        // the highest-priority one.
        var allNodesInCanonicalOrder = wholeDomain.Groups
            .SelectMany(group => group.Nodes)
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        var terms = candidateTerms
            .Select(term => AnalyzeTerm(term, allNodesInCanonicalOrder))
            .ToArray();

        FacetCheckCoverage? coverage = null;
        if (packetSource is not null)
        {
            var scopeResult = packetSource.ScopeResult;
            var coverageSelection = FacetContextSelector.Select(
                facetDomainRoot, domain, scopeResult.IntentReferences, facetFilter: [IntentNodeFacets.AcceptanceProperty]);
            var coverageNodes = coverageSelection.Groups
                .SingleOrDefault(group => group.Facet == IntentNodeFacets.AcceptanceProperty)
                ?.Nodes
                ?? Array.Empty<FacetContextNodeRef>();
            coverage = new FacetCheckCoverage
            {
                Nodes = coverageNodes,
                Gap = coverageNodes.Count == 0,
                ScopeStatus = scopeResult.Status,
                ScopeStatusDetail = scopeResult.Detail,
                ScopeWarnings = coverageSelection.ScopeWarnings,
            };
        }

        return new FacetCheckResult
        {
            Domain = domain,
            Disclaimer = Disclaimer,
            NoFacetData = !wholeDomain.DomainHasAnyFacetNodes,
            Terms = terms,
            Coverage = coverage,
            Warnings = wholeDomain.Warnings,
        };
    }

    /// <summary>
    /// G531 review repair: evidence is now a list of RECORDS, one per
    /// node-authored surface that matched — <c>id</c> (the node's own last
    /// domain-relative segment, filename-derived) and/or <c>title</c> (the
    /// extracted <c>Summary</c>, typically the node's H1 heading) — each
    /// naming the actual raw authored <c>value</c> that was compared and
    /// that SPECIFIC field's own <c>exact</c> (raw text identical to the
    /// term) vs <c>normalized</c> (equal only after case/punctuation/
    /// camelCase folding) classification. There is deliberately no
    /// aggregate match-kind on the whole match — a node whose id only
    /// normalized-matched but whose title matched exactly must show BOTH
    /// facts distinctly, not collapse to a single blended flag. Always
    /// full-token equality, never a substring search. <c>collisions</c> is
    /// the subset of matches whose node carries the <c>vocabulary</c> facet
    /// (visible on every match's own node.facets) — a proposal term
    /// duplicates or conflicts with an EXISTING named concept.
    /// </summary>
    private static FacetCheckTermReport AnalyzeTerm(string term, IReadOnlyList<FacetContextNodeRef> allNodes)
    {
        var normalizedTerm = NormalizeTerm(term);
        var relatedMatches = new List<FacetCheckNodeMatch>();

        foreach (var node in allNodes)
        {
            var evidence = new List<FacetCheckEvidence>();

            var idLastSegment = LastIdSegment(node.Id);
            if (string.Equals(NormalizeTerm(idLastSegment), normalizedTerm, StringComparison.Ordinal))
            {
                evidence.Add(new FacetCheckEvidence
                {
                    Field = EvidenceFieldId,
                    Value = idLastSegment,
                    MatchKind = string.Equals(idLastSegment, term, StringComparison.Ordinal) ? MatchKindExact : MatchKindNormalized,
                });
            }

            var title = node.Summary.Trim();
            if (string.Equals(NormalizeTerm(title), normalizedTerm, StringComparison.Ordinal))
            {
                evidence.Add(new FacetCheckEvidence
                {
                    Field = EvidenceFieldTitle,
                    Value = title,
                    MatchKind = string.Equals(title, term, StringComparison.Ordinal) ? MatchKindExact : MatchKindNormalized,
                });
            }

            if (evidence.Count > 0)
            {
                relatedMatches.Add(new FacetCheckNodeMatch { Node = node, Evidence = evidence });
            }
        }

        var collisions = relatedMatches
            .Where(match => match.Node.Facets.Contains(IntentNodeFacets.Vocabulary, StringComparer.Ordinal))
            .ToArray();

        return new FacetCheckTermReport
        {
            Term = term,
            RelatedNodes = relatedMatches,
            Collisions = collisions,
            Unmatched = relatedMatches.Count == 0,
        };
    }

    private static string LastIdSegment(string id)
    {
        var slashIndex = id.LastIndexOf('/');
        return slashIndex < 0 ? id : id[(slashIndex + 1)..];
    }

    /// <summary>
    /// G531: extends <c>GuideAutomationSetupAliasResolver.NormalizeKey</c>'s
    /// case/punctuation folding (lowercase, collapse any run of
    /// non-alphanumeric characters into a single hyphen, trim leading/
    /// trailing hyphens) with an explicit camelCase/PascalCase boundary
    /// split — a plain case-fold alone would leave <c>CreateOrder</c>
    /// (a proposal term, no punctuation) and <c>create-order</c> (a
    /// facet node's filename-derived id) unable to normalize to the same
    /// key, defeating the whole point of "case/-/_ normalized" matching
    /// between the two. A hyphen is inserted before any uppercase letter
    /// that immediately follows a lowercase letter or digit, before the
    /// rest of the fold runs — so <c>CreateOrder</c>, <c>create-order</c>,
    /// and <c>create_order</c> all converge on <c>create-order</c>.
    /// </summary>
    private static string NormalizeTerm(string raw)
    {
        var withCaseBoundaries = InsertCaseBoundaries(raw.Trim());
        var lower = withCaseBoundaries.ToLowerInvariant();
        var builder = new System.Text.StringBuilder(lower.Length);
        var lastWasSeparator = false;
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasSeparator = false;
            }
            else
            {
                if (!lastWasSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                }
                lastWasSeparator = true;
            }
        }
        if (builder.Length > 0 && builder[^1] == '-')
        {
            builder.Length--;
        }
        return builder.ToString();
    }

    private static string InsertCaseBoundaries(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 4);
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (index > 0 && char.IsUpper(ch) && (char.IsLower(value[index - 1]) || char.IsDigit(value[index - 1])))
            {
                builder.Append('-');
            }
            builder.Append(ch);
        }
        return builder.ToString();
    }

    /// <summary>
    /// G531 candidate-term extraction — deliberately simple, lexical, and
    /// honest about its limits (a scaffold, not a parser). Noise regions
    /// (fenced code blocks, Markdown/image link TARGETS, bare URLs,
    /// multi-segment paths — see <see cref="MaskNoise"/>) are blanked out
    /// BEFORE either extraction rule runs, so a class name inside a code
    /// fence or a CamelCase URL/path segment never becomes a candidate —
    /// while a link's VISIBLE label survives untouched, since that is
    /// intentional authored proposal text. A term is extracted when it is:
    /// <list type="bullet">
    /// <item>a bare identifier inside backticks (e.g. <c>`CreateOrder`</c>,
    /// <c>`create-order`</c>) — a backtick span containing whitespace or
    /// other punctuation (a command example, not a term) is skipped, and an
    /// escaped backtick (<c>\`</c>) never opens a span;</item>
    /// <item>a plain-text word token that is camelCase/PascalCase (contains
    /// an internal lowercase-then-uppercase transition, e.g.
    /// <c>CreateOrder</c>); or</item>
    /// <item>a plain-text word token ending in <c>Command</c>, <c>Event</c>,
    /// or <c>Query</c>.</item>
    /// </list>
    /// Both rules run over the SAME masked text and their matches are
    /// merged and sorted by source offset before deduplication — so
    /// extraction is appearance-ordered across the whole document (github-
    /// body content precedes implementation content, since the caller
    /// concatenates them in that order), not "all backtick hits, then all
    /// plain-word hits" regardless of where they actually appear.
    /// Deduplicated by <see cref="NormalizeTerm"/>, first-seen original
    /// casing kept. When a backtick-quoted term and its own bare-word
    /// occurrence (inside the same backticks) both match, the backtick
    /// entry — whose match position is the earlier opening backtick — wins
    /// the dedup, preserving the more intentional, explicitly-marked form.
    /// </summary>
    internal static IReadOnlyList<string> ExtractCandidateTerms(string text)
    {
        var masked = MaskNoise(text);
        var candidates = new List<(int Start, string Value)>();

        foreach (Match backtick in BacktickSpanRegex.Matches(masked))
        {
            var inner = backtick.Groups[1].Value.Trim();
            if (BareIdentifierRegex.IsMatch(inner))
            {
                candidates.Add((backtick.Index, inner));
            }
        }

        foreach (Match word in WordTokenRegex.Matches(masked))
        {
            var token = word.Value;
            var looksLikeCommandOrEventTerm =
                CaseTransitionRegex.IsMatch(token)
                || CommandEventSuffixes.Any(suffix =>
                    token.Length > suffix.Length && token.EndsWith(suffix, StringComparison.Ordinal));
            if (looksLikeCommandOrEventTerm)
            {
                candidates.Add((word.Index, token));
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var terms = new List<string>();
        foreach (var candidate in candidates.OrderBy(c => c.Start))
        {
            var key = NormalizeTerm(candidate.Value);
            if (key.Length == 0 || !seen.Add(key))
            {
                continue;
            }
            terms.Add(candidate.Value);
        }

        return terms;
    }

    /// <summary>
    /// G531 review repair: blanks every noise region in document order —
    /// fenced code blocks first (<see cref="MaskFencedCodeBlocks"/>), then
    /// reference-style link DEFINITION lines, then autolinks, then inline
    /// Markdown/image link/image TARGETS only (label/alt preserved,
    /// <see cref="MaskInlineLinksAndImages"/>), then bare URLs, then any
    /// remaining multi-segment path-like run (e.g.
    /// <c>src/Commands/CreateOrder.cs</c>) — so neither extraction regex
    /// can see identifier-shaped noise living inside any of them. Order
    /// matters: each pass only ever operates on text the PRIOR pass already
    /// left alone or blanked, so nothing is double-processed.
    /// </summary>
    private static string MaskNoise(string text)
    {
        var masked = text;
        masked = MaskFencedCodeBlocks(masked);
        masked = MaskRegionsOf(masked, ReferenceDefinitionRegex);
        masked = MaskRegionsOf(masked, AutolinkRegex);
        masked = MaskInlineLinksAndImages(masked);
        masked = MaskRegionsOf(masked, UrlRegex);
        masked = MaskRegionsOf(masked, PathLikeRegex);
        return masked;
    }

    /// <summary>
    /// G531 review repair round 4: a Markdown-aware fenced-code masker
    /// honoring CommonMark's ACTUAL fence boundary, replacing a prior
    /// scanner that over-accepted unlimited leading whitespace and any
    /// backtick-fence info string. Scans procedurally: find the next
    /// OPENING fence line (<see cref="FenceOpenRegex"/> — at most 3 leading
    /// spaces, 3+ of the same fence char). A backtick-fenced opener whose
    /// info string itself contains a backtick is REJECTED (ambiguous with
    /// inline code per CommonMark) — that candidate is copied through as
    /// plain text and scanning resumes just past it, so a false/malformed
    /// "opener" line never swallows real proposal text after it. For a
    /// genuine opener, search FORWARD for the first line that closes it —
    /// at most 3 leading spaces, the SAME fence character, run length
    /// &gt;= the opener's (CommonMark permits a longer closer), and nothing
    /// but trailing whitespace after the run. A wrong-character, too-short,
    /// or over-indented "closer" is not recognized and does not end the
    /// fence early. If no valid closing line exists before end-of-document,
    /// the fence is UNCLOSED and the command fails closed: everything from
    /// the opening fence to end-of-document is masked as code.
    /// </summary>
    private static string MaskFencedCodeBlocks(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        var cursor = 0;

        while (cursor < text.Length)
        {
            var openMatch = FenceOpenRegex.Match(text, cursor);
            if (!openMatch.Success)
            {
                builder.Append(text, cursor, text.Length - cursor);
                cursor = text.Length;
                break;
            }

            var fenceRun = openMatch.Groups[1].Value;
            var fenceChar = fenceRun[0];
            var fenceLength = fenceRun.Length;
            var infoString = openMatch.Groups[2].Value;

            if (fenceChar == '`' && infoString.Contains('`'))
            {
                // Not a valid opener — a backtick fence's info string can
                // never itself contain a backtick. Copy this line through
                // untouched and keep scanning from just past it.
                var throughLine = openMatch.Index + openMatch.Length;
                builder.Append(text, cursor, throughLine - cursor);
                cursor = throughLine;
                continue;
            }

            builder.Append(text, cursor, openMatch.Index - cursor);

            var contentStart = openMatch.Index + openMatch.Length;
            var closePattern = $@"(?m)^ {{0,3}}{Regex.Escape(fenceChar.ToString())}{{{fenceLength},}}[ \t]*(?:\r?\n|$)";
            var closeMatch = contentStart < text.Length
                ? Regex.Match(text[contentStart..], closePattern)
                : Match.Empty;

            var blankEnd = closeMatch.Success
                ? contentStart + closeMatch.Index + closeMatch.Length
                : text.Length; // unclosed fence: fail closed to end-of-document

            for (var i = openMatch.Index; i < blankEnd; i++)
            {
                builder.Append(text[i] is '\n' or '\r' ? text[i] : ' ');
            }

            cursor = blankEnd;
        }

        return builder.ToString();
    }

    /// <summary>
    /// G531 review repair round 4: a hand-written scanner for inline
    /// Markdown/image links — <c>[label](destination title)</c> /
    /// <c>![alt](destination title)</c> — replacing a naive regex that
    /// stopped a destination at the FIRST <c>)</c>, leaking the remainder
    /// of a destination containing balanced parentheses (plus any optional
    /// title) back into plain-word extraction. Only the destination/title
    /// is blanked; the bracketed label/alt text survives untouched, since
    /// it is intentional authored proposal text. A malformed/unparseable
    /// candidate (no matching closing paren, etc.) is left completely
    /// untouched rather than guessing.
    /// </summary>
    private static string MaskInlineLinksAndImages(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        var cursor = 0;

        while (cursor < text.Length)
        {
            var bracketStart = text.IndexOf('[', cursor);
            if (bracketStart < 0)
            {
                builder.Append(text, cursor, text.Length - cursor);
                cursor = text.Length;
                break;
            }

            builder.Append(text, cursor, bracketStart - cursor);

            var labelEnd = -1;
            for (var i = bracketStart + 1; i < text.Length; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length) { i++; continue; }
                if (text[i] == ']') { labelEnd = i; break; }
                if (text[i] == '\n') { break; }
            }

            if (labelEnd < 0)
            {
                builder.Append(text[bracketStart]);
                cursor = bracketStart + 1;
                continue;
            }

            var afterLabel = labelEnd + 1;
            if (afterLabel >= text.Length || text[afterLabel] != '(')
            {
                // Not an inline link/image — e.g. a reference-style
                // [label][ref] or shortcut [label] — copy the label
                // through untouched; its text remains fully extractable.
                builder.Append(text, bracketStart, afterLabel - bracketStart);
                cursor = afterLabel;
                continue;
            }

            var parsed = TryParseLinkDestinationAndTitle(text, afterLabel + 1);
            if (parsed is null)
            {
                builder.Append(text, bracketStart, afterLabel + 1 - bracketStart);
                cursor = afterLabel + 1;
                continue;
            }

            builder.Append(text, bracketStart, afterLabel + 1 - bracketStart); // "[label]("
            for (var i = afterLabel + 1; i < parsed.Value.MatchEnd - 1; i++)
            {
                builder.Append(text[i] == '\n' ? '\n' : ' ');
            }
            builder.Append(')');
            cursor = parsed.Value.MatchEnd;
        }

        return builder.ToString();
    }

    private readonly record struct LinkDestinationParse(int MatchEnd);

    /// <summary>
    /// Parses a link destination + optional title starting right after the
    /// opening <c>(</c> (i.e. <paramref name="start"/> points at the first
    /// character of the destination), returning the index one PAST the
    /// closing <c>)</c> on success, or <see langword="null"/> if the
    /// syntax is not well-formed (no matching close found before EOF/
    /// newline). Handles: an angle-bracket destination <c>&lt;...&gt;</c>
    /// (may contain spaces, ends at the first unescaped <c>&gt;</c>); a
    /// bare destination with BALANCED, escapable parentheses (an unescaped
    /// <c>(</c> increases nesting depth, a matching <c>)</c> decreases it —
    /// only a <c>)</c> at depth 0 ends the destination) and ending at the
    /// first depth-0 whitespace or the closing <c>)</c>; and an optional
    /// title in double quotes, single quotes, or parentheses (each may
    /// contain an escaped instance of its own delimiter).
    /// </summary>
    private static LinkDestinationParse? TryParseLinkDestinationAndTitle(string text, int start)
    {
        var i = start;

        if (i < text.Length && text[i] == '<')
        {
            i++;
            while (i < text.Length && text[i] != '>' && text[i] != '\n')
            {
                if (text[i] == '\\' && i + 1 < text.Length) { i += 2; continue; }
                i++;
            }
            if (i >= text.Length || text[i] != '>')
            {
                return null;
            }
            i++;
        }
        else
        {
            var depth = 0;
            while (i < text.Length)
            {
                var ch = text[i];
                if (ch == '\\' && i + 1 < text.Length) { i += 2; continue; }
                if (ch == '(') { depth++; i++; continue; }
                if (ch == ')')
                {
                    if (depth == 0) { break; }
                    depth--; i++; continue;
                }
                if (ch == '\n') { break; }
                if (char.IsWhiteSpace(ch) && depth == 0) { break; }
                i++;
            }
        }

        while (i < text.Length && text[i] is ' ' or '\t') { i++; }

        if (i < text.Length && text[i] is '"' or '\'' or '(')
        {
            var quote = text[i];
            var close = quote == '(' ? ')' : quote;
            i++;
            var titleClosed = false;
            while (i < text.Length)
            {
                var ch = text[i];
                if (ch == '\\' && i + 1 < text.Length) { i += 2; continue; }
                if (ch == close) { i++; titleClosed = true; break; }
                if (ch == '\n') { break; }
                i++;
            }
            if (!titleClosed)
            {
                return null;
            }
            while (i < text.Length && text[i] is ' ' or '\t') { i++; }
        }

        if (i >= text.Length || text[i] != ')')
        {
            return null;
        }
        i++;

        return new LinkDestinationParse(i);
    }

    private static string MaskRegionsOf(string text, Regex noiseRegex)
    {
        return noiseRegex.Replace(text, match => new string(
            match.Value.Select(c => c == '\n' ? '\n' : ' ').ToArray()));
    }

    /// <summary>
    /// G531 review repair: distinguishes an AUTHORED empty
    /// <c>intent_references: []</c> (<see cref="ScopeStatusValidEmpty"/> —
    /// a deliberate "this packet references nothing yet") from a
    /// <c>packet.yaml</c> that is missing entirely, has no
    /// <c>implementation_issue_packet.intent_references</c> key at all
    /// (<see cref="ScopeStatusMissing"/>), fails to parse as YAML
    /// (<see cref="ScopeStatusMalformed"/>), or has that key present but
    /// not shaped as a sequence (<see cref="ScopeStatusWrongShape"/>) — the
    /// prior behavior silently folded ALL of these into an empty reference
    /// list, making a genuinely missing/broken packet scope indistinguishable
    /// from a deliberately empty one; both now report the same computed
    /// <c>gap: true</c> (an empty/degraded scope hint list still narrows
    /// coverage to nothing, per G530 semantics) but are tagged with the
    /// real reason via <see cref="FacetCheckCoverage.ScopeStatus"/>. An
    /// INDIVIDUAL malformed/out-of-domain reference INSIDE an otherwise
    /// well-shaped list is a separate, already-handled concern — it still
    /// surfaces via <see cref="FacetContextSelection.ScopeWarnings"/>
    /// unchanged. A genuine I/O failure reading an EXISTING file (not
    /// "missing", an actual read error) throws
    /// <see cref="FacetCheckPacketIoException"/> — that is a real execution
    /// error, not a finding, and the caller must not fold it into any of
    /// the above statuses.
    /// </summary>
    private static PacketScopeReadResult ReadPacketScope(string packetYamlPath, Func<string, string> readAllText)
    {
        if (!File.Exists(packetYamlPath))
        {
            return new PacketScopeReadResult
            {
                Status = ScopeStatusMissing,
                Detail = $"packet.yaml not found at {packetYamlPath}",
                IntentReferences = Array.Empty<string>(),
            };
        }

        var content = ReadFileOrThrowIo(packetYamlPath, readAllText);

        YamlDotNet.RepresentationModel.YamlStream yaml;
        try
        {
            yaml = new YamlDotNet.RepresentationModel.YamlStream();
            using var reader = new StringReader(content);
            yaml.Load(reader);
        }
        catch (YamlDotNet.Core.YamlException exception)
        {
            return new PacketScopeReadResult
            {
                Status = ScopeStatusMalformed,
                Detail = $"packet.yaml failed to parse as YAML: {exception.Message}",
                IntentReferences = Array.Empty<string>(),
            };
        }

        if (yaml.Documents.Count == 0
            || yaml.Documents[0].RootNode is not YamlDotNet.RepresentationModel.YamlMappingNode root
            || !root.Children.TryGetValue(
                new YamlDotNet.RepresentationModel.YamlScalarNode("implementation_issue_packet"), out var implementationNode)
            || implementationNode is not YamlDotNet.RepresentationModel.YamlMappingNode implementationMapping
            || !implementationMapping.Children.TryGetValue(
                new YamlDotNet.RepresentationModel.YamlScalarNode("intent_references"), out var referencesNode))
        {
            return new PacketScopeReadResult
            {
                Status = ScopeStatusMissing,
                Detail = "packet.yaml has no implementation_issue_packet.intent_references key",
                IntentReferences = Array.Empty<string>(),
            };
        }

        if (referencesNode is not YamlDotNet.RepresentationModel.YamlSequenceNode referencesSequence)
        {
            return new PacketScopeReadResult
            {
                Status = ScopeStatusWrongShape,
                Detail = "implementation_issue_packet.intent_references is not a YAML sequence",
                IntentReferences = Array.Empty<string>(),
            };
        }

        var references = referencesSequence.Children
            .OfType<YamlDotNet.RepresentationModel.YamlScalarNode>()
            .Select(scalar => scalar.Value ?? string.Empty)
            .Where(value => value.Length > 0)
            .ToArray();

        return new PacketScopeReadResult
        {
            Status = references.Length == 0 ? ScopeStatusValidEmpty : ScopeStatusValidNonEmpty,
            Detail = null,
            IntentReferences = references,
        };
    }

    /// <summary>
    /// G531: same parent-host-aware domain root resolution G530's
    /// <c>context collect</c>/<c>packet draft</c> use — a change proposal
    /// checked from a child implementation workspace must be checked
    /// against the PARENT host's intent tree (where the real vocabulary/
    /// invariant facets live) when one is configured, else the local repo.
    /// </summary>
    private static string ResolveFacetDomainRoot(CliContext context, string domain)
    {
        var parentRoot = context.ResolveParentIntentRepoRootPath();
        var baseRoot = string.IsNullOrWhiteSpace(parentRoot) ? context.RepoRoot : parentRoot;
        return Path.Combine(baseRoot!, "intents", domain);
    }

    private static void WriteMarkdown(TextWriter writer, FacetCheckResult result)
    {
        writer.WriteLine($"# Facet check — {result.Domain}");
        writer.WriteLine();
        writer.WriteLine($"> {result.Disclaimer}");
        writer.WriteLine();
        // G531 review repair: this line is unconditional in every output
        // shape — Markdown must never omit no_facet_data when false, unlike
        // the prior only-print-prose-when-true behavior.
        writer.WriteLine($"- No facet data: {(result.NoFacetData ? "yes" : "no")}");
        if (result.NoFacetData)
        {
            writer.WriteLine("  (no facet-annotated nodes found in this domain — nothing to check against; not an error, facets are optional)");
        }
        writer.WriteLine();

        writer.WriteLine("## Terms");
        writer.WriteLine();
        if (result.Terms.Count == 0)
        {
            writer.WriteLine("No candidate terms extracted.");
        }
        else
        {
            foreach (var term in result.Terms)
            {
                writer.WriteLine($"### `{term.Term}`");
                writer.WriteLine();
                writer.WriteLine(term.RelatedNodes.Count == 0 ? "- Related facet nodes: (none)" : "- Related facet nodes:");
                foreach (var match in term.RelatedNodes)
                {
                    writer.WriteLine($"  - {string.Join(", ", match.Node.Facets)}: {match.Node.Id} — {match.Node.Summary}");
                    foreach (var evidence in match.Evidence)
                    {
                        writer.WriteLine($"    - evidence: {evidence.Field}=\"{evidence.Value}\" ({evidence.MatchKind})");
                    }
                }
                writer.WriteLine(term.Collisions.Count == 0 ? "- Collisions (vocabulary): (none)" : "- Collisions (vocabulary):");
                foreach (var match in term.Collisions)
                {
                    writer.WriteLine($"  - {match.Node.Id} — {match.Node.Summary}");
                    foreach (var evidence in match.Evidence)
                    {
                        writer.WriteLine($"    - evidence: {evidence.Field}=\"{evidence.Value}\" ({evidence.MatchKind})");
                    }
                }
                writer.WriteLine($"- Unmatched: {(term.Unmatched ? "yes" : "no")}");
                writer.WriteLine();
            }
        }

        writer.WriteLine("## Acceptance-property coverage");
        writer.WriteLine();
        if (result.Coverage is null)
        {
            writer.WriteLine("Not applicable — coverage is only computed in `--packet` mode (no packet scope to check).");
        }
        else
        {
            writer.WriteLine($"- Scope status: {result.Coverage.ScopeStatus}");
            if (result.Coverage.ScopeStatusDetail is not null)
            {
                writer.WriteLine($"  ({result.Coverage.ScopeStatusDetail})");
            }
            if (result.Coverage.Nodes.Count == 0)
            {
                writer.WriteLine("- (none overlapping this packet's intent_references)");
            }
            else
            {
                foreach (var node in result.Coverage.Nodes)
                {
                    writer.WriteLine($"- {node.Id} — {node.Summary}");
                }
            }
            writer.WriteLine($"- Gap: {(result.Coverage.Gap ? "yes" : "no")}");
            foreach (var warning in result.Coverage.ScopeWarnings)
            {
                writer.WriteLine($"- Scope warning: '{warning.Hint}' — {warning.Reason}");
            }
        }

        if (result.Warnings.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Warnings");
            foreach (var warning in result.Warnings)
            {
                writer.WriteLine($"- {warning.Path} — {warning.Reason}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? domainOverride,
        out string? executionUnit,
        out IReadOnlyList<string>? explicitTerms,
        out string format,
        out string error)
    {
        domainOverride = null;
        executionUnit = null;
        explicitTerms = null;
        format = FormatMarkdown;
        error = string.Empty;
        string? termsRaw = null;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }
                    domainOverride = args[index + 1];
                    index++;
                    break;

                case "--packet":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--packet requires a value (an execution-unit id).";
                        return false;
                    }
                    executionUnit = args[index + 1];
                    index++;
                    break;

                case "--terms":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--terms requires a value (a comma-separated list).";
                        return false;
                    }
                    termsRaw = args[index + 1];
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requested = args[index + 1];
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }
                    format = requested;
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        if (executionUnit is not null && termsRaw is not null)
        {
            error = "--packet and --terms are mutually exclusive; pass exactly one.";
            return false;
        }

        if (executionUnit is null && termsRaw is null)
        {
            error = "intent facet-check requires exactly one of --packet or --terms.";
            return false;
        }

        if (termsRaw is not null)
        {
            var split = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rawElement in termsRaw.Split(','))
            {
                var trimmed = rawElement.Trim();
                if (trimmed.Length == 0)
                {
                    error = "--terms must be a comma-separated list with no empty elements.";
                    return false;
                }
                if (seen.Add(trimmed))
                {
                    split.Add(trimmed);
                }
            }
            explicitTerms = split;
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("intent facet-check");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only lexical scaffold: checks a change proposal's candidate command/event terms against G529 facet nodes.");
        writer.WriteLine("--packet <execution-unit> extracts terms from that packet's github-body.md/implementation.md and reports acceptance-property coverage for its intent_references.");
        writer.WriteLine("--terms <comma-list> checks an explicit term list (no coverage section, since there is no packet scope).");
        writer.WriteLine("Never a gate: exit code is always 0 on findings, and every result carries a scaffold-not-verification disclaimer.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// G531 review repair: a genuine I/O failure reading an EXISTING packet
    /// source file — <c>github-body.md</c>, <c>implementation.md</c>, or
    /// <c>packet.yaml</c> (permissions, a race with concurrent deletion,
    /// etc.) — distinct from "file does not exist" (a finding, not an
    /// error: either silently excluded from extraction, for the two body
    /// files, or <see cref="ScopeStatusMissing"/> for packet.yaml) and from
    /// "file exists but fails to parse" (<see cref="ScopeStatusMalformed"/>,
    /// also a finding, packet.yaml only). This is the one packet-input
    /// failure mode the command must NOT silently fold into an empty
    /// result — it aborts the whole invocation with a non-zero exit
    /// instead.
    /// </summary>
    private sealed class FacetCheckPacketIoException(string message, Exception innerException)
        : Exception(message, innerException);
}

internal sealed record FacetCheckPacketSource
{
    public required string ExecutionUnit { get; init; }

    public required PacketScopeReadResult ScopeResult { get; init; }
}

internal sealed record PacketScopeReadResult
{
    public required string Status { get; init; }

    public string? Detail { get; init; }

    public required IReadOnlyList<string> IntentReferences { get; init; }
}

internal sealed record FacetCheckResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("disclaimer")]
    public required string Disclaimer { get; init; }

    [JsonPropertyName("no_facet_data")]
    public required bool NoFacetData { get; init; }

    [JsonPropertyName("terms")]
    public required IReadOnlyList<FacetCheckTermReport> Terms { get; init; }

    [JsonPropertyName("coverage")]
    public FacetCheckCoverage? Coverage { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<FacetContextWarning> Warnings { get; init; }
}

internal sealed record FacetCheckTermReport
{
    [JsonPropertyName("term")]
    public required string Term { get; init; }

    [JsonPropertyName("related_nodes")]
    public required IReadOnlyList<FacetCheckNodeMatch> RelatedNodes { get; init; }

    [JsonPropertyName("collisions")]
    public required IReadOnlyList<FacetCheckNodeMatch> Collisions { get; init; }

    [JsonPropertyName("unmatched")]
    public required bool Unmatched { get; init; }
}

/// <summary>
/// G531 review repair: a matched node plus every piece of evidence that
/// justified the match — see <see cref="FacetCheckEvidence"/> for the
/// per-field breakdown. There is no aggregate match-kind: a caller that
/// wants "was ANY evidence exact" can compute it from the list, but the
/// command itself never blends a normalized-only id match and an
/// exact-title match into one flag.
/// </summary>
internal sealed record FacetCheckNodeMatch
{
    [JsonPropertyName("node")]
    public required FacetContextNodeRef Node { get; init; }

    [JsonPropertyName("evidence")]
    public required IReadOnlyList<FacetCheckEvidence> Evidence { get; init; }
}

/// <summary>
/// One node-authored surface that matched a candidate term: which
/// <c>field</c> (<c>id</c> or <c>title</c>), the actual raw authored
/// <c>value</c> compared, and whether THAT specific comparison was
/// <c>exact</c> (raw text identical to the term) or only <c>normalized</c>
/// (equal after case/punctuation/camelCase folding).
/// </summary>
internal sealed record FacetCheckEvidence
{
    [JsonPropertyName("field")]
    public required string Field { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }

    [JsonPropertyName("match_kind")]
    public required string MatchKind { get; init; }
}

internal sealed record FacetCheckCoverage
{
    [JsonPropertyName("nodes")]
    public required IReadOnlyList<FacetContextNodeRef> Nodes { get; init; }

    [JsonPropertyName("gap")]
    public required bool Gap { get; init; }

    /// <summary>
    /// One of <c>valid-empty</c> (an authored, deliberate empty
    /// <c>intent_references: []</c>), <c>valid-non-empty</c>,
    /// <c>missing</c> (no <c>packet.yaml</c>, or no
    /// <c>intent_references</c> key at all), <c>malformed</c> (invalid
    /// YAML), or <c>wrong-shape</c> (the key exists but is not a
    /// sequence) — see <see cref="IntentFacetCheckCommand.ReadPacketScope"/>.
    /// </summary>
    [JsonPropertyName("scope_status")]
    public required string ScopeStatus { get; init; }

    [JsonPropertyName("scope_status_detail")]
    public string? ScopeStatusDetail { get; init; }

    [JsonPropertyName("scope_warnings")]
    public required IReadOnlyList<FacetScopeWarning> ScopeWarnings { get; init; }
}
