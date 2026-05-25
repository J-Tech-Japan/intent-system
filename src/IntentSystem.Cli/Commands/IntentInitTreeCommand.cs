using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G404: <c>intent-cli intent init-tree</c> — initialize a new intent domain
/// with the flexible tree-v1 layout (G403). Creates <c>manifest.yaml</c>,
/// recommended category folders with <c>.gitkeep</c> placeholders, a domain
/// <c>README.md</c>, and an identity starter file.
///
/// Idempotent: existing files are preserved (reported as <c>existing</c>);
/// missing files are created only when <c>--write</c> is supplied. Without
/// <c>--write</c> the command is a dry-run that shows what would be created.
///
/// Refuses to run inside an automation child worktree to match the safety
/// posture of <see cref="IntentInitCommand"/>.
/// </summary>
internal static class IntentInitTreeCommand
{
    internal const string ProjectTypeProductApp = "product-app";
    internal const string ProjectTypeLibraryTool = "library-tool";
    internal const string ProjectTypeInfrastructure = "infrastructure";
    internal const string ProjectTypeResearchPrototype = "research-prototype";

    private static readonly string[] KnownProjectTypes =
    [
        ProjectTypeProductApp,
        ProjectTypeLibraryTool,
        ProjectTypeInfrastructure,
        ProjectTypeResearchPrototype
    ];

    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli intent init-tree --domain <name> --target-repo <owner/repo> "
        + "[--project-type product-app|library-tool|infrastructure|research-prototype] "
        + "[--write] [--format markdown|json]";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(UsageLine);
            writer.WriteLine("Initializes a new intent domain with the tree-v1 flexible layout (G403).");
            writer.WriteLine();
            writer.WriteLine("Project types:");
            foreach (var pt in KnownProjectTypes)
            {
                writer.WriteLine($"  {pt}");
            }
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

    internal static IntentInitTreeResult ExecuteCore(string hostRepoRoot, IntentInitTreeRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostRepoRoot);
        ArgumentNullException.ThrowIfNull(request);

        EnsureNotChildWorktree(hostRepoRoot);

        var categories = ResolveCategories(request.ProjectType);

        // Build the planned file list: manifest, README, category .gitkeeps, identity starter
        var planned = new List<string>();
        planned.Add($"intents/{request.Domain}/manifest.yaml");
        planned.Add($"intents/{request.Domain}/README.md");

        foreach (var (_, path) in categories)
        {
            planned.Add($"intents/{request.Domain}/{path}.gitkeep");
        }

        planned.Add($"intents/{request.Domain}/identity/mission.md");

        var written = new List<string>();
        var existing = new List<string>();

        foreach (var relativePath in planned)
        {
            var absolutePath = ResolvePath(hostRepoRoot, relativePath);
            if (File.Exists(absolutePath))
            {
                existing.Add(relativePath);
                continue;
            }

            if (!request.Write)
            {
                continue;
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(absolutePath)
                ?? throw new InvalidOperationException($"No parent directory for '{absolutePath}'."));
            File.WriteAllText(absolutePath, RenderContent(relativePath, request, categories));
            written.Add(relativePath);
        }

        var nextSteps = BuildNextSteps(request, written.Count, existing.Count);

        return new IntentInitTreeResult
        {
            Domain = request.Domain,
            TargetRepo = request.TargetRepo,
            ProjectType = request.ProjectType,
            WriteApplied = request.Write,
            PlannedPaths = planned,
            WrittenPaths = written,
            ExistingPaths = existing,
            NextSteps = nextSteps,
            Categories = categories.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };
    }

    /// <summary>Returns the ordered category map for a given project type.</summary>
    internal static IReadOnlyList<KeyValuePair<string, string>> ResolveCategories(string projectType)
    {
        return projectType switch
        {
            ProjectTypeLibraryTool => new[]
            {
                new KeyValuePair<string, string>("identity", "identity/"),
                new KeyValuePair<string, string>("api", "api/"),
                new KeyValuePair<string, string>("users", "users/"),
                new KeyValuePair<string, string>("features", "features/"),
                new KeyValuePair<string, string>("technology", "technology/"),
                new KeyValuePair<string, string>("operations", "operations/"),
                new KeyValuePair<string, string>("decisions", "decisions/"),
                new KeyValuePair<string, string>("clarifications", "clarifications/"),
                new KeyValuePair<string, string>("packets", "packets/")
            },
            ProjectTypeInfrastructure => new[]
            {
                new KeyValuePair<string, string>("identity", "identity/"),
                new KeyValuePair<string, string>("environments", "environments/"),
                new KeyValuePair<string, string>("features", "features/"),
                new KeyValuePair<string, string>("technology", "technology/"),
                new KeyValuePair<string, string>("operations", "operations/"),
                new KeyValuePair<string, string>("runbooks", "runbooks/"),
                new KeyValuePair<string, string>("decisions", "decisions/"),
                new KeyValuePair<string, string>("clarifications", "clarifications/"),
                new KeyValuePair<string, string>("packets", "packets/")
            },
            ProjectTypeResearchPrototype => new[]
            {
                new KeyValuePair<string, string>("identity", "identity/"),
                new KeyValuePair<string, string>("hypothesis", "hypothesis/"),
                new KeyValuePair<string, string>("features", "features/"),
                new KeyValuePair<string, string>("technology", "technology/"),
                new KeyValuePair<string, string>("experiments", "experiments/"),
                new KeyValuePair<string, string>("decisions", "decisions/"),
                new KeyValuePair<string, string>("clarifications", "clarifications/"),
                new KeyValuePair<string, string>("packets", "packets/")
            },
            _ /* product-app */ => new[]
            {
                new KeyValuePair<string, string>("identity", "identity/"),
                new KeyValuePair<string, string>("product", "product/"),
                new KeyValuePair<string, string>("features", "features/"),
                new KeyValuePair<string, string>("technology", "technology/"),
                new KeyValuePair<string, string>("operations", "operations/"),
                new KeyValuePair<string, string>("decisions", "decisions/"),
                new KeyValuePair<string, string>("clarifications", "clarifications/"),
                new KeyValuePair<string, string>("packets", "packets/"),
                new KeyValuePair<string, string>("links", "links/")
            }
        };
    }

    private static string RenderContent(
        string relativePath,
        IntentInitTreeRequest request,
        IReadOnlyList<KeyValuePair<string, string>> categories)
    {
        if (relativePath.EndsWith("manifest.yaml", StringComparison.Ordinal))
        {
            return RenderManifest(request, categories);
        }

        if (relativePath.EndsWith($"intents/{request.Domain}/README.md", StringComparison.Ordinal))
        {
            return RenderDomainReadme(request, categories);
        }

        if (relativePath.EndsWith(".gitkeep", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (relativePath.EndsWith("identity/mission.md", StringComparison.Ordinal))
        {
            return RenderMissionStarter(request);
        }

        throw new InvalidOperationException($"intent init-tree has no template for '{relativePath}'.");
    }

    private static string RenderManifest(
        IntentInitTreeRequest request,
        IReadOnlyList<KeyValuePair<string, string>> categories)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("version: \"1\"");
        sb.AppendLine("layout_version: tree-v1");
        sb.AppendLine($"project_type: {request.ProjectType}");
        if (!string.IsNullOrWhiteSpace(request.TargetRepo))
        {
            sb.AppendLine($"target_repo: {request.TargetRepo}");
        }
        sb.AppendLine("branch_policy: direct-main");
        sb.AppendLine("metadata_policy: host-metadata");
        sb.AppendLine("entrypoints:");
        sb.AppendLine("  - identity/mission.md");
        sb.AppendLine("  - README.md");
        sb.AppendLine("categories:");
        foreach (var (key, path) in categories)
        {
            sb.AppendLine($"  {key}: {path}");
        }
        return sb.ToString();
    }

    private static string RenderDomainReadme(
        IntentInitTreeRequest request,
        IReadOnlyList<KeyValuePair<string, string>> categories)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# {request.Domain} intent tree");
        sb.AppendLine();
        sb.AppendLine("> **Ask intent-cli first:** `intent-cli guide intent-work setup --kind tree-layout "
            + $"--domain {request.Domain}"
            + (string.IsNullOrWhiteSpace(request.TargetRepo) ? string.Empty : $" --target-repo {request.TargetRepo}")
            + " --format markdown`");
        sb.AppendLine();
        sb.AppendLine($"Layout: tree-v1 · Project type: {request.ProjectType}");
        sb.AppendLine();
        sb.AppendLine("## Categories");
        sb.AppendLine();
        foreach (var (key, path) in categories)
        {
            sb.AppendLine($"- [{key}/]({path})");
        }
        sb.AppendLine();
        sb.AppendLine("## Cross-linking rules");
        sb.AppendLine();
        sb.AppendLine("- Feature pages must link to related decisions, clarifications, packets, and GitHub issues.");
        sb.AppendLine("- Decision records must link to the feature(s) that motivated them.");
        sb.AppendLine("- Clarification entries must link back to the feature or decision they block.");
        sb.AppendLine("- Packet pages must link to the GitHub issue once published.");
        return sb.ToString();
    }

    private static string RenderMissionStarter(IntentInitTreeRequest request)
    {
        return $"""
        # Mission

        > Ask intent-cli for guidance before editing:
        > `intent-cli guide intent-work setup --kind tree-layout --domain {request.Domain} --format markdown`

        ## Mission statement

        _[Describe the mission of this domain in one or two sentences.]_

        ## Vision

        _[Describe the desired future state this domain is working toward.]_

        ## Values / principles

        _[List the guiding principles that constrain how this domain operates.]_

        ## Glossary

        _[Define key terms specific to this domain.]_
        """;
    }

    private static IReadOnlyList<string> BuildNextSteps(
        IntentInitTreeRequest request,
        int writtenCount,
        int existingCount)
    {
        var verb = request.Write
            ? (writtenCount == 0 ? "Already initialized" : "Initialized")
            : "Plan";

        var steps = new List<string>
        {
            $"{verb}: domain '{request.Domain}' tree-v1 (written: {writtenCount}, existing: {existingCount})."
        };

        if (!request.Write)
        {
            steps.Add(
                $"Re-run with --write to create the planned files: "
                + $"intent-cli intent init-tree --domain {request.Domain}"
                + (string.IsNullOrWhiteSpace(request.TargetRepo) ? string.Empty : $" --target-repo {request.TargetRepo}")
                + $" --project-type {request.ProjectType} --write");
        }

        steps.Add($"Edit `intents/{request.Domain}/identity/mission.md` with the domain's mission and vision.");
        steps.Add($"Add a feature: intent-cli intent add-feature --domain {request.Domain} --name <feature> --write");
        steps.Add($"Run `intent-cli guide intent-work setup --kind tree-layout --domain {request.Domain} --format markdown` for tree authoring guidance.");
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
                    $"Refusing to run 'intent init-tree' inside an automation child worktree at '{hostRepoRoot}'. "
                    + "Run this command from the parent host repository.");
            }
        }
    }

    private static string ResolvePath(string hostRepoRoot, string relativePath) =>
        Path.Combine(hostRepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static bool TryParseArguments(
        string[] args,
        out IntentInitTreeRequest request,
        out string error)
    {
        request = default!;
        error = string.Empty;

        string? domain = null;
        string? targetRepo = null;
        var projectType = ProjectTypeProductApp;
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
                case "--target-repo":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = "Missing value for '--target-repo'.";
                        return false;
                    }
                    targetRepo = args[++i].Trim();
                    break;
                case "--project-type":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = "Missing value for '--project-type'.";
                        return false;
                    }
                    var pt = args[++i].Trim();
                    if (!Array.Exists(KnownProjectTypes, p => string.Equals(p, pt, StringComparison.Ordinal)))
                    {
                        error = $"--project-type must be one of: {string.Join(", ", KnownProjectTypes)} (got '{pt}').";
                        return false;
                    }
                    projectType = pt;
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
            error = "intent init-tree requires '--domain <name>'.";
            return false;
        }

        if (!IsValidSlug(domain))
        {
            error = $"intent init-tree '--domain' value '{domain}' must be a slug (letters, digits, '-', '_').";
            return false;
        }

        request = new IntentInitTreeRequest
        {
            Domain = domain!,
            TargetRepo = targetRepo,
            ProjectType = projectType,
            Write = write,
            Format = format
        };
        return true;
    }

    /// <summary>
    /// Validates that a value is a safe slug: letters, digits, hyphens, and underscores only.
    /// This mirrors the slug guard in <see cref="IntentInitCommand"/> and prevents path-traversal
    /// attacks when domain or feature values are interpolated into file system paths.
    /// </summary>
    internal static bool IsValidSlug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var character in value)
        {
            var isValid = char.IsLetterOrDigit(character)
                || character == '-'
                || character == '_';
            if (!isValid)
            {
                return false;
            }
        }

        return true;
    }

    private static void WriteOutput(TextWriter writer, string format, IntentInitTreeResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
            return;
        }

        WriteMarkdown(writer, result);
    }

    private static void WriteMarkdown(TextWriter writer, IntentInitTreeResult result)
    {
        var verb = result.WriteApplied ? "Written" : "Planned (dry-run)";
        writer.WriteLine($"# intent init-tree — {result.Domain}");
        writer.WriteLine();
        writer.WriteLine($"- project-type: {result.ProjectType}");
        if (!string.IsNullOrWhiteSpace(result.TargetRepo))
        {
            writer.WriteLine($"- target-repo: {result.TargetRepo}");
        }
        writer.WriteLine($"- write-applied: {result.WriteApplied}");
        writer.WriteLine();

        writer.WriteLine($"## {verb} ({result.WrittenPaths.Count} written, {result.ExistingPaths.Count} existing)");
        writer.WriteLine();
        foreach (var p in result.WrittenPaths)
        {
            writer.WriteLine($"- [created] {p}");
        }
        foreach (var p in result.ExistingPaths)
        {
            writer.WriteLine($"- [existing] {p}");
        }
        foreach (var p in result.PlannedPaths
            .Where(p => !result.WrittenPaths.Contains(p) && !result.ExistingPaths.Contains(p)))
        {
            writer.WriteLine($"- [planned] {p}");
        }
        writer.WriteLine();

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

    internal sealed record IntentInitTreeRequest
    {
        public required string Domain { get; init; }
        public string? TargetRepo { get; init; }
        public required string ProjectType { get; init; }
        public required bool Write { get; init; }
        public required string Format { get; init; }
    }
}

internal sealed record IntentInitTreeResult
{
    public required string Domain { get; init; }
    public string? TargetRepo { get; init; }
    public required string ProjectType { get; init; }
    public required bool WriteApplied { get; init; }
    public required IReadOnlyList<string> PlannedPaths { get; init; }
    public required IReadOnlyList<string> WrittenPaths { get; init; }
    public required IReadOnlyList<string> ExistingPaths { get; init; }
    public required IReadOnlyList<string> NextSteps { get; init; }
    public required IReadOnlyDictionary<string, string> Categories { get; init; }
}
