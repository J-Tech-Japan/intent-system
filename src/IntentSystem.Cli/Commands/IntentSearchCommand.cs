using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G242: Read-only <c>intent-cli intent search</c> command. Performs a
/// case-insensitive substring search across the packet directory
/// (<c>.intent-cli/issues/</c>) and the per-domain intent tree
/// (<c>intents/&lt;domain&gt;/</c>) and emits the matching file paths,
/// 1-based line numbers, and a trimmed snippet. Never mutates state and
/// never launches an AI provider.
///
/// G529: an optional <c>--facet &lt;value&gt;</c> filter (one of
/// <see cref="IntentNodeFacets.AllowedValues"/>) restricts results to files
/// whose <c>facets:</c> frontmatter carries that value, combinable with
/// <c>--query</c>. When <c>--facet</c> is given without <c>--query</c>, each
/// matching file is reported once (its <c>facets:</c> line as the snippet)
/// rather than doing line-level substring matching.
/// </summary>
internal static class IntentSearchCommand
{
    private const string FormatMarkdown = "markdown";
    private const string FormatJson = "json";

    private const int MatchesPerFile = 3;
    private const int SnippetMaxLength = 240;

    private const string UsageLine =
        "Usage: intent-cli intent search [--query <text>] [--domain <name>] [--facet vocabulary|invariant|decider|acceptance-property] [--format markdown|json]";

    private static readonly string[] SearchableExtensions =
        new[] { ".md", ".yaml", ".yml", ".json", ".txt" };

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

        if (!TryParseArguments(args, out var query, out var domainOverride, out var facet, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var result = Search(context, query, domainOverride, facet);

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

    internal static IntentSearchResult Search(CliContext context, string? query, string? domainOverride, string? facet)
    {
        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride!;

        var hits = new List<IntentSearchHit>();

        var packetRoot = Path.Combine(context.RepoRoot, ".intent-cli", "issues");
        if (Directory.Exists(packetRoot))
        {
            CollectMatches(packetRoot, query, facet, hits);
        }

        var domainRoot = Path.Combine(context.RepoRoot, "intents", domain);
        if (Directory.Exists(domainRoot))
        {
            CollectMatches(domainRoot, query, facet, hits);
        }

        return new IntentSearchResult
        {
            Domain = domain,
            Query = query,
            Facet = facet,
            PacketRoot = packetRoot,
            DomainRoot = domainRoot,
            Hits = hits
        };
    }

    private static void CollectMatches(string root, string? query, string? facet, List<IntentSearchHit> hits)
    {
        var files = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => SearchableExtensions.Contains(
                Path.GetExtension(file),
                StringComparer.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.Ordinal);

        foreach (var file in files)
        {
            string content;
            try
            {
                content = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            // G529: when --facet is given, restrict to files that PARSE a
            // present, valid facets: list carrying that value. A malformed
            // facets: declaration is `lint-layout`'s concern to flag as an
            // error — search simply excludes it, the same as a node with no
            // facets: at all, since it cannot be confirmed to carry the
            // requested facet.
            if (facet is not null)
            {
                var parsed = IntentNodeFacets.ParseFacets(content);
                if (parsed.Kind != FacetsParseKind.Present
                    || !parsed.Values.Contains(facet, StringComparer.Ordinal))
                {
                    continue;
                }
            }

            if (string.IsNullOrEmpty(query))
            {
                // G529: facet-only mode — report the file once rather than
                // doing line-level substring matching with an empty query.
                // Skip past any frontmatter block so the snippet shows the
                // node's actual title/prose, not the "---" delimiter.
                var body = IntentNodeFacets.TryExtractFrontmatterBlock(content, out _, out var bodyAfterFrontmatter)
                    ? bodyAfterFrontmatter
                    : content;
                var firstLine = body
                    .Split('\n')
                    .Select(line => line.Trim())
                    .FirstOrDefault(line => line.Length > 0) ?? string.Empty;
                hits.Add(new IntentSearchHit
                {
                    Path = file,
                    Line = 1,
                    Snippet = TrimSnippet(firstLine)
                });
                continue;
            }

            var lines = content.Split('\n');
            var matchesInFile = 0;
            for (var index = 0; index < lines.Length && matchesInFile < MatchesPerFile; index++)
            {
                var line = lines[index];
                if (line.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                hits.Add(new IntentSearchHit
                {
                    Path = file,
                    Line = index + 1,
                    Snippet = TrimSnippet(line)
                });
                matchesInFile++;
            }
        }
    }

    private static string TrimSnippet(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length <= SnippetMaxLength)
        {
            return trimmed;
        }

        return trimmed[..SnippetMaxLength] + "…";
    }

    private static void WriteMarkdown(TextWriter writer, IntentSearchResult result)
    {
        writer.WriteLine($"# Intent search — {result.Domain}");
        writer.WriteLine();
        if (!string.IsNullOrEmpty(result.Query))
        {
            writer.WriteLine($"- query: `{result.Query}`");
        }
        if (!string.IsNullOrEmpty(result.Facet))
        {
            writer.WriteLine($"- facet: `{result.Facet}`");
        }
        writer.WriteLine($"- packet root: {result.PacketRoot}");
        writer.WriteLine($"- domain root: {result.DomainRoot}");
        writer.WriteLine($"- matches: {result.Hits.Count}");
        writer.WriteLine();

        if (result.Hits.Count == 0)
        {
            writer.WriteLine("No matches.");
            return;
        }

        writer.WriteLine("## Matches");
        foreach (var hit in result.Hits)
        {
            writer.WriteLine($"- {hit.Path}:{hit.Line} — {hit.Snippet}");
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? query,
        out string? domainOverride,
        out string? facet,
        out string format,
        out string error)
    {
        query = null;
        domainOverride = null;
        facet = null;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--query":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--query requires a value.";
                        return false;
                    }

                    query = args[index + 1];
                    index++;
                    break;

                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }

                    domainOverride = args[index + 1];
                    index++;
                    break;

                case "--facet":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--facet requires a value.";
                        return false;
                    }

                    var requestedFacet = args[index + 1];
                    if (!IntentNodeFacets.IsAllowedValue(requestedFacet))
                    {
                        error = $"--facet must be one of: {string.Join(", ", IntentNodeFacets.AllowedValues)} (got '{requestedFacet}').";
                        return false;
                    }

                    facet = requestedFacet;
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

        if (string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(facet))
        {
            error = "intent search requires --query and/or --facet.";
            return false;
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("intent search");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only substring search across packets and the per-domain intent tree.");
        writer.WriteLine("--facet filters to files whose facets: frontmatter carries that value; combinable with --query.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record IntentSearchResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("query")]
    public string? Query { get; init; }

    [JsonPropertyName("facet")]
    public string? Facet { get; init; }

    [JsonPropertyName("packet_root")]
    public required string PacketRoot { get; init; }

    [JsonPropertyName("domain_root")]
    public required string DomainRoot { get; init; }

    [JsonPropertyName("hits")]
    public required IReadOnlyList<IntentSearchHit> Hits { get; init; }
}

internal sealed record IntentSearchHit
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("line")]
    public required int Line { get; init; }

    [JsonPropertyName("snippet")]
    public required string Snippet { get; init; }
}
