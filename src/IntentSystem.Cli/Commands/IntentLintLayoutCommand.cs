using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G405: <c>intent-cli intent lint-layout</c> — detect layout problems in an intent domain.
///
/// <para>Checks performed:</para>
/// <list type="bullet">
///   <item>Missing <c>manifest.yaml</c> (for tree-v1 domains).</item>
///   <item>Missing category index files referenced in the manifest.</item>
///   <item>Broken relative Markdown links within the domain tree.</item>
///   <item>Overly large flat Markdown files (above threshold).</item>
///   <item>Missing <c>features/index.md</c> when feature sub-folders exist.</item>
///   <item>Feature sub-folders missing <c>overview.md</c>.</item>
/// </list>
///
/// <para>Supports mixed flat/tree domains: a domain without a manifest is treated as flat
/// and receives only the large-file and missing-feature-index checks.</para>
///
/// <para>Refuses to run inside an automation child worktree.</para>
/// </summary>
internal static class IntentLintLayoutCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli intent lint-layout --domain <name> [--format markdown|json]";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(UsageLine);
            writer.WriteLine("Lints an intent domain layout: missing manifest, broken links, large flat files, missing indexes.");
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
            return result.Warnings.Count > 0 ? 0 : 0; // always exit 0; warnings surfaced in output
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static IntentLintLayoutResult ExecuteCore(string hostRepoRoot, IntentLintLayoutRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostRepoRoot);
        ArgumentNullException.ThrowIfNull(request);

        EnsureNotChildWorktree(hostRepoRoot);

        var domainRoot = Path.Combine(hostRepoRoot, "intents", request.Domain);
        var warnings = new List<LintWarning>();

        if (!Directory.Exists(domainRoot))
        {
            warnings.Add(new LintWarning(
                Code: "MISSING-DOMAIN",
                Message: $"Domain directory 'intents/{request.Domain}/' does not exist.",
                Fix: $"Run `intent-cli intent init-tree --domain {request.Domain} --write` to initialize."));
            return BuildResult(request, warnings, isTreeDomain: false);
        }

        // Determine if this is a tree-v1 domain
        var manifestPath = Path.Combine(domainRoot, "manifest.yaml");
        var isTreeDomain = File.Exists(manifestPath);

        if (!isTreeDomain)
        {
            warnings.Add(new LintWarning(
                Code: "MISSING-MANIFEST",
                Message: $"No manifest.yaml found in 'intents/{request.Domain}/'. Domain is flat.",
                Fix: $"Run `intent-cli intent init-tree --domain {request.Domain} --write` to add tree structure, "
                    + "or `intent-cli intent analyze-tree --domain "
                    + $"{request.Domain}` to plan a restructure."));
        }
        else
        {
            // Parse manifest for category paths
            var manifestContent = File.ReadAllText(manifestPath);
            var categories = ParseManifestCategories(manifestContent);
            CheckMissingCategoryFolders(domainRoot, request.Domain, categories, warnings);
        }

        // Check for large flat files (both flat and tree domains)
        CheckLargeFlatFiles(domainRoot, request.Domain, warnings);

        // Check broken relative links in all markdown files
        CheckBrokenRelativeLinks(domainRoot, request.Domain, warnings);

        // Check features/index.md
        CheckFeaturesIndex(domainRoot, request.Domain, warnings);

        // G529: validate any facets: frontmatter against the closed value set
        CheckFacetValues(domainRoot, request.Domain, warnings);

        return BuildResult(request, warnings, isTreeDomain);
    }

    // ── Category checks ───────────────────────────────────────────────────────

    private static Dictionary<string, string> ParseManifestCategories(string manifestContent)
    {
        var categories = new Dictionary<string, string>(StringComparer.Ordinal);
        var inCategories = false;

        foreach (var line in manifestContent.Split('\n'))
        {
            var trimmed = line.TrimEnd();
            if (string.Equals(trimmed, "categories:", StringComparison.Ordinal))
            {
                inCategories = true;
                continue;
            }

            if (inCategories)
            {
                if (trimmed.StartsWith("  ", StringComparison.Ordinal) && trimmed.Contains(':'))
                {
                    var colonIdx = trimmed.IndexOf(':', StringComparison.Ordinal);
                    var key = trimmed[..colonIdx].Trim();
                    var value = trimmed[(colonIdx + 1)..].Trim();
                    categories[key] = value;
                }
                else if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    inCategories = false;
                }
            }
        }

        return categories;
    }

    private static void CheckMissingCategoryFolders(
        string domainRoot,
        string domain,
        Dictionary<string, string> categories,
        List<LintWarning> warnings)
    {
        foreach (var (key, path) in categories)
        {
            var catDir = Path.Combine(domainRoot, path.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(catDir))
            {
                warnings.Add(new LintWarning(
                    Code: "MISSING-CATEGORY-FOLDER",
                    Message: $"Category '{key}' folder 'intents/{domain}/{path}' listed in manifest.yaml does not exist.",
                    Fix: $"Create the folder or run `intent-cli intent init-tree --domain {domain} --write` to scaffold it."));
            }
        }
    }

    // ── Large flat file check ─────────────────────────────────────────────────

    private static void CheckLargeFlatFiles(
        string domainRoot,
        string domain,
        List<LintWarning> warnings)
    {
        var flatFiles = Directory
            .GetFiles(domainRoot, "*.md", SearchOption.TopDirectoryOnly)
            .Where(f => !string.Equals(Path.GetFileName(f), "README.md", StringComparison.OrdinalIgnoreCase));

        foreach (var file in flatFiles)
        {
            var info = new FileInfo(file);
            if (info.Length > IntentAnalyzeTreeCommand.LargeFlatFileBytesThreshold)
            {
                var rel = $"intents/{domain}/{Path.GetFileName(file)}";
                warnings.Add(new LintWarning(
                    Code: "LARGE-FLAT-FILE",
                    Message: $"Flat file '{rel}' is {info.Length:N0} bytes (threshold: {IntentAnalyzeTreeCommand.LargeFlatFileBytesThreshold:N0} bytes).",
                    Fix: $"Consider restructuring into tree-v1 categories. "
                        + $"Run `intent-cli intent analyze-tree --domain {domain}` to see a proposed plan."));
            }
        }
    }

    // ── Broken link check ─────────────────────────────────────────────────────

    private static void CheckBrokenRelativeLinks(
        string domainRoot,
        string domain,
        List<LintWarning> warnings)
    {
        var allMarkdown = Directory.GetFiles(domainRoot, "*.md", SearchOption.AllDirectories);

        foreach (var mdFile in allMarkdown)
        {
            var content = File.ReadAllText(mdFile);
            var dir = Path.GetDirectoryName(mdFile)!;

            foreach (Match m in Regex.Matches(content, @"\[([^\]]+)\]\(([^)#]+)(?:#[^)]*)?\)"))
            {
                var linkTarget = m.Groups[2].Value.Trim();

                // Skip absolute URLs
                if (linkTarget.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || linkTarget.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    || linkTarget.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Skip empty
                if (string.IsNullOrWhiteSpace(linkTarget))
                {
                    continue;
                }

                var resolved = Path.GetFullPath(Path.Combine(dir, linkTarget));
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                {
                    var relSource = $"intents/{domain}/" + Path.GetRelativePath(domainRoot, mdFile).Replace(Path.DirectorySeparatorChar, '/');
                    warnings.Add(new LintWarning(
                        Code: "BROKEN-RELATIVE-LINK",
                        Message: $"Broken link in '{relSource}': '{linkTarget}' does not resolve.",
                        Fix: "Update the link target or create the missing file."));
                }
            }
        }
    }

    // ── Feature index check ───────────────────────────────────────────────────

    private static void CheckFeaturesIndex(
        string domainRoot,
        string domain,
        List<LintWarning> warnings)
    {
        var featuresDir = Path.Combine(domainRoot, "features");
        if (!Directory.Exists(featuresDir))
        {
            return;
        }

        // Check for features/index.md
        var indexPath = Path.Combine(featuresDir, "index.md");
        if (!File.Exists(indexPath))
        {
            warnings.Add(new LintWarning(
                Code: "MISSING-FEATURES-INDEX",
                Message: $"'intents/{domain}/features/' exists but 'features/index.md' is missing.",
                Fix: $"Run `intent-cli intent add-feature --domain {domain} --name <feature> --write` "
                    + "to create the first feature and the index."));
        }

        // Check for feature sub-folders missing overview.md
        foreach (var featureDir in Directory.GetDirectories(featuresDir))
        {
            var featureName = Path.GetFileName(featureDir);
            var overviewPath = Path.Combine(featureDir, "overview.md");
            if (!File.Exists(overviewPath))
            {
                warnings.Add(new LintWarning(
                    Code: "MISSING-FEATURE-OVERVIEW",
                    Message: $"Feature folder 'intents/{domain}/features/{featureName}/' is missing 'overview.md'.",
                    Fix: $"Add 'intents/{domain}/features/{featureName}/overview.md' with goals and acceptance criteria."));
            }
        }
    }

    // ── Facet check (G529) ────────────────────────────────────────────────────

    /// <summary>
    /// A node's optional <c>facets:</c> frontmatter must draw only from the
    /// closed set (<see cref="IntentNodeFacets.AllowedValues"/>). A node with
    /// no frontmatter, or frontmatter with no <c>facets:</c> line, is
    /// unaffected — unannotated nodes are legitimate and stay lint-clean.
    /// </summary>
    private static void CheckFacetValues(
        string domainRoot,
        string domain,
        List<LintWarning> warnings)
    {
        var allMarkdown = Directory.GetFiles(domainRoot, "*.md", SearchOption.AllDirectories);

        foreach (var mdFile in allMarkdown)
        {
            string content;
            try
            {
                content = File.ReadAllText(mdFile);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            var rawFacets = IntentNodeFacets.ExtractRawFacets(content);
            if (rawFacets.Count == 0)
            {
                continue;
            }

            var rel = $"intents/{domain}/" + Path.GetRelativePath(domainRoot, mdFile).Replace(Path.DirectorySeparatorChar, '/');
            var allowed = string.Join(", ", IntentNodeFacets.AllowedValues);

            foreach (var value in rawFacets.Distinct(StringComparer.Ordinal))
            {
                if (!IntentNodeFacets.IsAllowedValue(value))
                {
                    warnings.Add(new LintWarning(
                        Code: "INVALID-FACET",
                        Message: $"Node '{rel}' has invalid facet '{value}' (allowed: {allowed}).",
                        Fix: $"Use one of: {allowed}, or remove '{value}' from the 'facets:' line in '{rel}'."));
                }
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IntentLintLayoutResult BuildResult(
        IntentLintLayoutRequest request,
        List<LintWarning> warnings,
        bool isTreeDomain) =>
        new()
        {
            Domain = request.Domain,
            IsTreeDomain = isTreeDomain,
            Warnings = warnings,
            Clean = warnings.Count == 0
        };

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
                    $"Refusing to run 'intent lint-layout' inside an automation child worktree at '{hostRepoRoot}'. "
                    + "Run this command from the parent host repository.");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out IntentLintLayoutRequest request,
        out string error)
    {
        request = default!;
        error = string.Empty;

        string? domain = null;
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
            error = "intent lint-layout requires '--domain <name>'.";
            return false;
        }

        if (!IntentInitTreeCommand.IsValidSlug(domain))
        {
            error = $"intent lint-layout '--domain' value '{domain}' must be a slug (letters, digits, '-', '_').";
            return false;
        }

        request = new IntentLintLayoutRequest { Domain = domain!, Format = format };
        return true;
    }

    private static void WriteOutput(TextWriter writer, string format, IntentLintLayoutResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
            return;
        }

        WriteMarkdown(writer, result);
    }

    private static void WriteMarkdown(TextWriter writer, IntentLintLayoutResult result)
    {
        var status = result.Clean ? "clean" : $"{result.Warnings.Count} warning(s)";
        writer.WriteLine($"# intent lint-layout — {result.Domain} ({status})");
        writer.WriteLine();
        writer.WriteLine($"- domain: {result.Domain}");
        writer.WriteLine($"- tree-domain: {result.IsTreeDomain}");
        writer.WriteLine($"- warnings: {result.Warnings.Count}");
        writer.WriteLine();

        if (result.Clean)
        {
            writer.WriteLine("No layout problems detected.");
            return;
        }

        writer.WriteLine("## Warnings");
        writer.WriteLine();
        foreach (var w in result.Warnings)
        {
            writer.WriteLine($"### [{w.Code}] {w.Message}");
            writer.WriteLine();
            writer.WriteLine($"**Fix:** {w.Fix}");
            writer.WriteLine();
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    internal sealed record IntentLintLayoutRequest
    {
        public required string Domain { get; init; }
        public required string Format { get; init; }
    }
}

internal sealed record IntentLintLayoutResult
{
    public required string Domain { get; init; }
    public required bool IsTreeDomain { get; init; }
    public required bool Clean { get; init; }
    public required IReadOnlyList<LintWarning> Warnings { get; init; }
}

internal sealed record LintWarning(
    string Code,
    string Message,
    string Fix);
