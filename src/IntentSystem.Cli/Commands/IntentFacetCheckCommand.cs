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
/// This is explicitly NOT a semantic verifier: matching is lexical (an exact
/// match once a term and a node's own id are both case/punctuation
/// normalized) — false negatives are expected and acceptable, silent claims
/// of verification are not. It never mutates state, never blocks: every
/// output carries a <see cref="FacetCheckResult.Disclaimer"/> field and the
/// command always exits 0, regardless of findings.
/// </summary>
internal static class IntentFacetCheckCommand
{
    private const string FormatMarkdown = "markdown";
    private const string FormatJson = "json";

    private const string Disclaimer =
        "Lexical scaffolding only — not semantic verification. Findings guide human/reviewer judgment; this command never blocks and false negatives are expected.";

    private const string UsageLine =
        "Usage: intent-cli intent facet-check --domain <name> (--packet <execution-unit> | --terms <comma-list>) [--format markdown|json]";

    private static readonly Regex BacktickSpanRegex = new(@"`([^`\n]+)`", RegexOptions.Compiled);
    private static readonly Regex WordTokenRegex = new(@"\b[A-Za-z][A-Za-z0-9]*\b", RegexOptions.Compiled);
    private static readonly Regex BareIdentifierRegex = new(@"^[A-Za-z][A-Za-z0-9_-]*$", RegexOptions.Compiled);
    private static readonly Regex CaseTransitionRegex = new(@"[a-z][A-Z]", RegexOptions.Compiled);
    private static readonly string[] CommandEventSuffixes = { "Command", "Event", "Query" };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

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

            var sourceText = string.Join(
                "\n",
                new[] { githubBodyPath, implementationPath }
                    .Where(File.Exists)
                    .Select(File.ReadAllText));

            candidateTerms = ExtractCandidateTerms(sourceText);
            packetSource = new FacetCheckPacketSource
            {
                ExecutionUnit = executionUnit,
                IntentReferences = ReadIntentReferences(packetYamlPath),
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
            var coverageSelection = FacetContextSelector.Select(
                facetDomainRoot, domain, packetSource.IntentReferences, facetFilter: [IntentNodeFacets.AcceptanceProperty]);
            var coverageNodes = coverageSelection.Groups
                .SingleOrDefault(group => group.Facet == IntentNodeFacets.AcceptanceProperty)
                ?.Nodes
                ?? Array.Empty<FacetContextNodeRef>();
            coverage = new FacetCheckCoverage
            {
                Nodes = coverageNodes,
                Gap = coverageNodes.Count == 0,
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

    private static FacetCheckTermReport AnalyzeTerm(string term, IReadOnlyList<FacetContextNodeRef> allNodes)
    {
        var normalizedTerm = NormalizeTerm(term);
        var relatedNodes = allNodes
            .Where(node => string.Equals(NormalizeTerm(LastIdSegment(node.Id)), normalizedTerm, StringComparison.Ordinal))
            .ToArray();
        var collisions = relatedNodes
            .Where(node => node.Facets.Contains(IntentNodeFacets.Vocabulary, StringComparer.Ordinal))
            .ToArray();

        return new FacetCheckTermReport
        {
            Term = term,
            RelatedNodes = relatedNodes,
            Collisions = collisions,
            Unmatched = relatedNodes.Length == 0,
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
    /// honest about its limits (a scaffold, not a parser):
    /// <list type="bullet">
    /// <item>a bare identifier inside backticks (e.g. <c>`CreateOrder`</c>,
    /// <c>`create-order`</c>) — backtick spans containing whitespace or
    /// other punctuation (command examples, not terms) are skipped;</item>
    /// <item>a plain-text word token that is camelCase/PascalCase (contains
    /// an internal lowercase-then-uppercase transition, e.g.
    /// <c>CreateOrder</c>); or</item>
    /// <item>a plain-text word token ending in <c>Command</c>, <c>Event</c>,
    /// or <c>Query</c>.</item>
    /// </list>
    /// Deduplicated by <see cref="NormalizeTerm"/>, first-seen original
    /// casing kept, first-seen order preserved.
    /// </summary>
    internal static IReadOnlyList<string> ExtractCandidateTerms(string text)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var terms = new List<string>();

        void Consider(string candidate)
        {
            var key = NormalizeTerm(candidate);
            if (key.Length == 0 || !seen.Add(key))
            {
                return;
            }
            terms.Add(candidate);
        }

        foreach (Match backtick in BacktickSpanRegex.Matches(text))
        {
            var inner = backtick.Groups[1].Value.Trim();
            if (BareIdentifierRegex.IsMatch(inner))
            {
                Consider(inner);
            }
        }

        foreach (Match word in WordTokenRegex.Matches(text))
        {
            var token = word.Value;
            var looksLikeCommandOrEventTerm =
                CaseTransitionRegex.IsMatch(token)
                || CommandEventSuffixes.Any(suffix =>
                    token.Length > suffix.Length && token.EndsWith(suffix, StringComparison.Ordinal));
            if (looksLikeCommandOrEventTerm)
            {
                Consider(token);
            }
        }

        return terms;
    }

    private static IReadOnlyList<string> ReadIntentReferences(string packetYamlPath)
    {
        if (!File.Exists(packetYamlPath))
        {
            return Array.Empty<string>();
        }

        try
        {
            var yaml = new YamlDotNet.RepresentationModel.YamlStream();
            using var reader = new StringReader(File.ReadAllText(packetYamlPath));
            yaml.Load(reader);

            if (yaml.Documents.Count == 0
                || yaml.Documents[0].RootNode is not YamlDotNet.RepresentationModel.YamlMappingNode root
                || !root.Children.TryGetValue(
                    new YamlDotNet.RepresentationModel.YamlScalarNode("implementation_issue_packet"), out var implementationNode)
                || implementationNode is not YamlDotNet.RepresentationModel.YamlMappingNode implementationMapping
                || !implementationMapping.Children.TryGetValue(
                    new YamlDotNet.RepresentationModel.YamlScalarNode("intent_references"), out var referencesNode)
                || referencesNode is not YamlDotNet.RepresentationModel.YamlSequenceNode referencesSequence)
            {
                return Array.Empty<string>();
            }

            return referencesSequence.Children
                .OfType<YamlDotNet.RepresentationModel.YamlScalarNode>()
                .Select(scalar => scalar.Value ?? string.Empty)
                .Where(value => value.Length > 0)
                .ToArray();
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return Array.Empty<string>();
        }
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

        if (result.NoFacetData)
        {
            writer.WriteLine("No facet-annotated nodes found in this domain — nothing to check against (not an error; facets are optional).");
            writer.WriteLine();
        }

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
                foreach (var node in term.RelatedNodes)
                {
                    writer.WriteLine($"  - {string.Join(", ", node.Facets)}: {node.Id} — {node.Summary}");
                }
                writer.WriteLine(term.Collisions.Count == 0 ? "- Collisions (vocabulary): (none)" : "- Collisions (vocabulary):");
                foreach (var node in term.Collisions)
                {
                    writer.WriteLine($"  - {node.Id} — {node.Summary}");
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
            if (result.Coverage.Nodes.Count == 0)
            {
                writer.WriteLine("(none overlapping this packet's intent_references)");
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
        writer.WriteLine("Never a gate: exit code is always 0, and every result carries a scaffold-not-verification disclaimer.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record FacetCheckPacketSource
{
    public required string ExecutionUnit { get; init; }

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
    public required IReadOnlyList<FacetContextNodeRef> RelatedNodes { get; init; }

    [JsonPropertyName("collisions")]
    public required IReadOnlyList<FacetContextNodeRef> Collisions { get; init; }

    [JsonPropertyName("unmatched")]
    public required bool Unmatched { get; init; }
}

internal sealed record FacetCheckCoverage
{
    [JsonPropertyName("nodes")]
    public required IReadOnlyList<FacetContextNodeRef> Nodes { get; init; }

    [JsonPropertyName("gap")]
    public required bool Gap { get; init; }

    [JsonPropertyName("scope_warnings")]
    public required IReadOnlyList<FacetScopeWarning> ScopeWarnings { get; init; }
}
