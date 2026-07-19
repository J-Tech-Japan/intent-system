using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G405: <c>intent-cli intent analyze-tree</c> — analyze a flat intent domain and
/// produce a proposed tree-v1 restructuring plan with a reference map.
///
/// <para>Reads all Markdown files directly under <c>intents/&lt;domain&gt;/</c>
/// (flat files — not inside sub-folders). For each file it:</para>
/// <list type="bullet">
///   <item>Extracts H2/H3 headings as content blocks to relocate.</item>
///   <item>Suggests a tree-v1 destination path per heading based on keyword heuristics.</item>
///   <item>Detects references: markdown links, heading anchors, execution-unit IDs (e.g. G123),
///         packet paths, and GitHub issue/PR URLs.</item>
///   <item>Produces a migration reference map from old path + heading to proposed new path.</item>
/// </list>
///
/// <para>Without <c>--write</c> the command is a dry-run (analysis only, no file changes).</para>
/// <para>With <c>--write</c> the command writes a <c>.restructure-backup/</c> copy of the
/// analyzed flat files and creates the proposed destination files. Existing files are preserved
/// (not overwritten).</para>
///
/// <para>Refuses to run inside an automation child worktree.</para>
/// </summary>
internal static class IntentAnalyzeTreeCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli intent analyze-tree --domain <name> [--write] [--format markdown|json]";

    /// <summary>Maximum flat-file size (bytes) before the file is flagged as large.</summary>
    internal const int LargeFlatFileBytesThreshold = 8_192;

    private static readonly (string[] Keywords, string Category, string Subfolder)[] CategoryHeuristics =
    [
        (["mission", "vision", "value", "principle", "glossary", "term"], "identity", "identity/"),
        (["feature", "auth", "login", "signup", "dashboard", "search", "notification", "payment"], "features", "features/"),
        (["architecture", "technology", "stack", "library", "framework", "language", "database", "cloud", "security", "deploy", "pipeline", "observ", "infra"], "technology", "technology/"),
        (["product", "user", "journey", "persona", "goal", "non-goal", "scope"], "product", "product/"),
        (["operation", "loop", "release", "runbook", "recovery", "incident"], "operations", "operations/"),
        (["decision", "adr", "chose", "trade", "rationale"], "decisions", "decisions/"),
        (["question", "clarif", "open", "unresolved", "blocking"], "clarifications", "clarifications/"),
        (["packet", "execution", "unit", "wave", "backlog", "roadmap"], "packets", "packets/"),
        (["link", "reference", "related", "external"], "links", "links/")
    ];

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(UsageLine);
            writer.WriteLine("Analyzes a flat intent domain and produces a proposed tree-v1 restructuring plan.");
            writer.WriteLine();
            writer.WriteLine("Without --write: dry-run analysis only (no file changes).");
            writer.WriteLine("With --write:    creates backup copies and proposed destination files (non-destructive).");
            return 0;
        }

        if (!TryParseArguments(args, out var request, out var parseError))
        {
            writer.WriteLine(parseError);
            writer.WriteLine(UsageLine);
            return 1;
        }

        try
        {
            var result = ExecuteCore(context.RepoRoot, request);
            WriteOutput(writer, request.Format, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static IntentAnalyzeTreeResult ExecuteCore(string hostRepoRoot, IntentAnalyzeTreeRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostRepoRoot);
        ArgumentNullException.ThrowIfNull(request);

        EnsureNotChildWorktree(hostRepoRoot);

        var domainRoot = Path.Combine(hostRepoRoot, "intents", request.Domain);
        var flatFiles = DiscoverFlatFiles(domainRoot);

        var proposals = new List<RestructureProposal>();
        var referenceMap = new List<ReferenceMapEntry>();
        var detectedRefs = new List<DetectedReference>();
        var backups = new List<string>();
        var written = new List<string>();

        foreach (var flatFile in flatFiles)
        {
            var relativeFlatPath = MakeRelative(hostRepoRoot, flatFile);
            var content = File.ReadAllText(flatFile);

            // Extract references from this file
            var fileRefs = ExtractReferences(content, relativeFlatPath);
            detectedRefs.AddRange(fileRefs);

            // Extract headings and produce proposals
            var headings = ExtractHeadings(content);
            foreach (var heading in headings)
            {
                var destination = SuggestDestination(request.Domain, heading);
                var destRelative = $"intents/{request.Domain}/{destination}";
                proposals.Add(new RestructureProposal(
                    SourceFile: relativeFlatPath,
                    Heading: heading.Text,
                    HeadingLevel: heading.Level,
                    SuggestedDestination: destRelative,
                    Category: destination.Split('/')[0]
                ));
                referenceMap.Add(new ReferenceMapEntry(
                    OldReference: $"{relativeFlatPath}#{SlugifyHeading(heading.Text)}",
                    NewReference: $"{destRelative}#{SlugifyHeading(heading.Text)}"
                ));
            }

            // Backup + write if requested
            if (request.Write)
            {
                var backupPath = Path.Combine(
                    domainRoot, ".restructure-backup",
                    Path.GetFileName(flatFile));
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                if (!File.Exists(backupPath))
                {
                    File.Copy(flatFile, backupPath);
                    backups.Add(MakeRelative(hostRepoRoot, backupPath));
                }

                // Write destination stub files (non-destructive)
                foreach (var proposal in proposals.Where(p =>
                    string.Equals(p.SourceFile, relativeFlatPath, StringComparison.Ordinal)))
                {
                    var absDestination = Path.Combine(
                        hostRepoRoot, proposal.SuggestedDestination.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(absDestination))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(absDestination)!);
                        File.WriteAllText(absDestination, RenderDestinationStub(proposal, request.Domain));
                        written.Add(proposal.SuggestedDestination);
                    }
                }
            }
        }

        var nextSteps = BuildNextSteps(request, flatFiles, proposals, backups.Count, written.Count);
        var facetCoverage = ComputeFacetCoverage(domainRoot);

        return new IntentAnalyzeTreeResult
        {
            Domain = request.Domain,
            WriteApplied = request.Write,
            FlatFilesAnalyzed = flatFiles.Select(f => MakeRelative(hostRepoRoot, f)).ToList(),
            Proposals = proposals,
            ReferenceMap = referenceMap,
            DetectedReferences = detectedRefs,
            BackupPaths = backups,
            WrittenPaths = written,
            FacetCoverage = facetCoverage,
            NextSteps = nextSteps
        };
    }

    // ── Facet coverage (G529) ─────────────────────────────────────────────────

    /// <summary>
    /// Counts, across every Markdown file in the whole domain tree (not just
    /// the top-level flat files this command otherwise analyzes), how many
    /// nodes carry each facet. Only facets with at least one node are
    /// included — an unannotated node is legitimate, so there is no
    /// "facet-less nodes" list, and an unused facet is simply absent rather
    /// than reported as zero.
    /// </summary>
    private static IReadOnlyDictionary<string, int> ComputeFacetCoverage(string domainRoot)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!Directory.Exists(domainRoot))
        {
            return counts;
        }

        foreach (var file in Directory.GetFiles(domainRoot, "*.md", SearchOption.AllDirectories))
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

            foreach (var facet in IntentNodeFacets.ExtractRawFacets(content).Distinct(StringComparer.Ordinal))
            {
                if (!IntentNodeFacets.IsAllowedValue(facet))
                {
                    // Invalid values are `lint-layout`'s concern, not counted here.
                    continue;
                }
                counts[facet] = counts.GetValueOrDefault(facet) + 1;
            }
        }

        return counts;
    }

    // ── Discovery ────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds Markdown files directly under <paramref name="domainRoot"/> (not in sub-folders).
    /// Also flags files above the <see cref="LargeFlatFileBytesThreshold"/> threshold.
    /// </summary>
    internal static IReadOnlyList<string> DiscoverFlatFiles(string domainRoot)
    {
        if (!Directory.Exists(domainRoot))
        {
            return [];
        }

        return Directory
            .GetFiles(domainRoot, "*.md", SearchOption.TopDirectoryOnly)
            .Where(f => !string.Equals(Path.GetFileName(f), "README.md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    // ── Heading extraction ────────────────────────────────────────────────────

    internal sealed record HeadingInfo(string Text, int Level, int LineIndex);

    internal static IReadOnlyList<HeadingInfo> ExtractHeadings(string content)
    {
        var result = new List<HeadingInfo>();
        var lines = content.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                result.Add(new HeadingInfo(line[3..].Trim(), 2, i));
            }
            else if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                result.Add(new HeadingInfo(line[4..].Trim(), 3, i));
            }
        }
        return result;
    }

    // ── Destination suggestion ────────────────────────────────────────────────

    internal static string SuggestDestination(string domain, HeadingInfo heading)
    {
        var lower = heading.Text.ToLowerInvariant();
        foreach (var (keywords, _, subfolder) in CategoryHeuristics)
        {
            if (keywords.Any(k => lower.Contains(k, StringComparison.Ordinal)))
            {
                var slug = SlugifyHeading(heading.Text);
                return $"{subfolder}{slug}.md";
            }
        }
        // Default: identity
        var defaultSlug = SlugifyHeading(heading.Text);
        return $"identity/{defaultSlug}.md";
    }

    // ── Reference detection ───────────────────────────────────────────────────

    internal static IReadOnlyList<DetectedReference> ExtractReferences(string content, string sourcePath)
    {
        var refs = new List<DetectedReference>();

        // Markdown links: [text](url)
        foreach (Match m in Regex.Matches(content, @"\[([^\]]+)\]\(([^)]+)\)"))
        {
            refs.Add(new DetectedReference(
                Kind: "markdown-link",
                Value: m.Groups[2].Value,
                SourcePath: sourcePath));
        }

        // Heading anchors: # Heading (extract as #slug)
        foreach (Match m in Regex.Matches(content, @"^#{1,6}\s+(.+)$", RegexOptions.Multiline))
        {
            refs.Add(new DetectedReference(
                Kind: "heading-anchor",
                Value: "#" + SlugifyHeading(m.Groups[1].Value.Trim()),
                SourcePath: sourcePath));
        }

        // Execution-unit IDs: G123, G404, etc.
        foreach (Match m in Regex.Matches(content, @"\bG\d{3,4}\b"))
        {
            refs.Add(new DetectedReference(
                Kind: "execution-unit-id",
                Value: m.Value,
                SourcePath: sourcePath));
        }

        // Packet paths: intents/.../packets/...
        foreach (Match m in Regex.Matches(content, @"intents/[^\s\)\]""]+"))
        {
            refs.Add(new DetectedReference(
                Kind: "packet-path",
                Value: m.Value,
                SourcePath: sourcePath));
        }

        // GitHub issue/PR URLs
        foreach (Match m in Regex.Matches(content, @"https://github\.com/[^\s\)\]""]+/(issues|pull)/\d+"))
        {
            refs.Add(new DetectedReference(
                Kind: "github-url",
                Value: m.Value,
                SourcePath: sourcePath));
        }

        return refs;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static string SlugifyHeading(string heading)
    {
        var lower = heading.ToLowerInvariant();
        var result = new System.Text.StringBuilder();
        foreach (var c in lower)
        {
            if (char.IsLetterOrDigit(c))
            {
                result.Append(c);
            }
            else if (c == ' ' || c == '-' || c == '_')
            {
                if (result.Length > 0 && result[^1] != '-')
                {
                    result.Append('-');
                }
            }
        }
        return result.ToString().Trim('-');
    }

    private static string RenderDestinationStub(RestructureProposal proposal, string domain)
    {
        return $"""
        # {proposal.Heading}

        > **Restructured from:** `{proposal.SourceFile}`
        > Run `intent-cli guide intent-work setup --kind restructure --domain {domain} --format markdown`
        > for the design-AI-assisted restructuring workflow.

        _[Paste or move the content from the source section here. Verify all links and references.]_
        """;
    }

    private static IReadOnlyList<string> BuildNextSteps(
        IntentAnalyzeTreeRequest request,
        IReadOnlyList<string> flatFiles,
        IReadOnlyList<RestructureProposal> proposals,
        int backupCount,
        int writtenCount)
    {
        var steps = new List<string>();

        if (flatFiles.Count == 0)
        {
            steps.Add($"No flat files found in intents/{request.Domain}/. Domain may already be tree-structured.");
            return steps;
        }

        steps.Add($"Analyzed {flatFiles.Count} flat file(s); proposed {proposals.Count} relocation(s).");

        if (!request.Write)
        {
            steps.Add($"Re-run with --write to create backup copies and destination stubs: "
                + $"intent-cli intent analyze-tree --domain {request.Domain} --write");
        }
        else
        {
            steps.Add($"Backed up {backupCount} file(s) to intents/{request.Domain}/.restructure-backup/.");
            steps.Add($"Created {writtenCount} destination stub file(s). Fill in content from the source sections.");
        }

        steps.Add($"Run `intent-cli guide intent-work setup --kind restructure --domain {request.Domain} "
            + "--target-repo <owner/repo> --format markdown` for the design-AI-assisted workflow.");
        steps.Add($"Run `intent-cli intent lint-layout --domain {request.Domain}` to check the result after restructuring.");
        return steps;
    }

    private static void EnsureNotChildWorktree(string hostRepoRoot)
    {
        var normalized = Path.GetFullPath(hostRepoRoot).Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i + 1 < segments.Length; i++)
        {
            if (string.Equals(segments[i], ".intent-cli", StringComparison.Ordinal)
                && string.Equals(segments[i + 1], "worktrees", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Refusing to run 'intent analyze-tree' inside an automation child worktree at '{hostRepoRoot}'. "
                    + "Run this command from the parent host repository.");
            }
        }
    }

    private static string MakeRelative(string repoRoot, string absolutePath)
    {
        var normalized = Path.GetFullPath(absolutePath);
        var root = Path.GetFullPath(repoRoot);
        if (normalized.StartsWith(root, StringComparison.Ordinal))
        {
            return normalized[(root.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/');
        }
        return normalized.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static bool TryParseArguments(
        string[] args,
        out IntentAnalyzeTreeRequest request,
        out string error)
    {
        request = default!;
        error = string.Empty;

        string? domain = null;
        var write = false;
        var format = FormatMarkdown;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--domain":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = "Missing value for '--domain'.";
                        return false;
                    }
                    domain = args[++i].Trim();
                    break;
                case "--write":
                    write = true;
                    break;
                case "--format":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = "Missing value for '--format'.";
                        return false;
                    }
                    var fmt = args[++i].Trim();
                    if (!string.Equals(fmt, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(fmt, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{fmt}').";
                        return false;
                    }
                    format = fmt;
                    break;
                default:
                    error = $"Unknown option '{args[i]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            error = "intent analyze-tree requires '--domain <name>'.";
            return false;
        }

        if (!IntentInitTreeCommand.IsValidSlug(domain))
        {
            error = $"intent analyze-tree '--domain' value '{domain}' must be a slug (letters, digits, '-', '_').";
            return false;
        }

        request = new IntentAnalyzeTreeRequest
        {
            Domain = domain!,
            Write = write,
            Format = format
        };
        return true;
    }

    private static void WriteOutput(TextWriter writer, string format, IntentAnalyzeTreeResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
            return;
        }

        WriteMarkdown(writer, result);
    }

    private static void WriteMarkdown(TextWriter writer, IntentAnalyzeTreeResult result)
    {
        var mode = result.WriteApplied ? "write" : "dry-run";
        writer.WriteLine($"# intent analyze-tree — {result.Domain} ({mode})");
        writer.WriteLine();
        writer.WriteLine($"- write-applied: {result.WriteApplied}");
        writer.WriteLine($"- flat-files-analyzed: {result.FlatFilesAnalyzed.Count}");
        writer.WriteLine($"- proposals: {result.Proposals.Count}");
        writer.WriteLine($"- detected-references: {result.DetectedReferences.Count}");
        writer.WriteLine();

        if (result.Proposals.Count > 0)
        {
            writer.WriteLine("## Proposed relocations");
            writer.WriteLine();
            foreach (var p in result.Proposals)
            {
                writer.WriteLine($"- `{p.SourceFile}` → `{p.SuggestedDestination}` (heading: {p.Heading})");
            }
            writer.WriteLine();
        }

        if (result.ReferenceMap.Count > 0)
        {
            writer.WriteLine("## Migration reference map");
            writer.WriteLine();
            foreach (var r in result.ReferenceMap)
            {
                writer.WriteLine($"- old: `{r.OldReference}` → new: `{r.NewReference}`");
            }
            writer.WriteLine();
        }

        if (result.BackupPaths.Count > 0)
        {
            writer.WriteLine("## Backups created");
            writer.WriteLine();
            foreach (var b in result.BackupPaths)
            {
                writer.WriteLine($"- {b}");
            }
            writer.WriteLine();
        }

        if (result.WrittenPaths.Count > 0)
        {
            writer.WriteLine("## Destination stubs created");
            writer.WriteLine();
            foreach (var w in result.WrittenPaths)
            {
                writer.WriteLine($"- {w}");
            }
            writer.WriteLine();
        }

        if (result.FacetCoverage.Count > 0)
        {
            writer.WriteLine("## Facet coverage");
            writer.WriteLine();
            foreach (var facet in IntentNodeFacets.AllowedValues)
            {
                if (result.FacetCoverage.TryGetValue(facet, out var count))
                {
                    writer.WriteLine($"- {facet}: {count}");
                }
            }
            writer.WriteLine();
        }

        writer.WriteLine("## Next steps");
        writer.WriteLine();
        foreach (var step in result.NextSteps)
        {
            writer.WriteLine($"- {step}");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    internal sealed record IntentAnalyzeTreeRequest
    {
        public required string Domain { get; init; }
        public required bool Write { get; init; }
        public required string Format { get; init; }
    }
}

internal sealed record IntentAnalyzeTreeResult
{
    public required string Domain { get; init; }
    public required bool WriteApplied { get; init; }
    public required IReadOnlyList<string> FlatFilesAnalyzed { get; init; }
    public required IReadOnlyList<RestructureProposal> Proposals { get; init; }
    public required IReadOnlyList<ReferenceMapEntry> ReferenceMap { get; init; }
    public required IReadOnlyList<DetectedReference> DetectedReferences { get; init; }
    public required IReadOnlyList<string> BackupPaths { get; init; }
    public required IReadOnlyList<string> WrittenPaths { get; init; }
    public required IReadOnlyDictionary<string, int> FacetCoverage { get; init; }
    public required IReadOnlyList<string> NextSteps { get; init; }
}

internal sealed record RestructureProposal(
    string SourceFile,
    string Heading,
    int HeadingLevel,
    string SuggestedDestination,
    string Category);

internal sealed record ReferenceMapEntry(
    string OldReference,
    string NewReference);

internal sealed record DetectedReference(
    string Kind,
    string Value,
    string SourcePath);
