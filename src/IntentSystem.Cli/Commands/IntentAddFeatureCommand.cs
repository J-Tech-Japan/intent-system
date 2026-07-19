using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G404: <c>intent-cli intent add-feature</c> — add a feature folder skeleton
/// under <c>features/&lt;slug&gt;/</c> inside an existing tree-v1 intent domain.
///
/// Creates cross-linked starter files:
/// <list type="bullet">
///   <item><c>overview.md</c> — goals, motivation, acceptance-criteria summary</item>
///   <item><c>requirements.md</c> — detailed requirements</item>
///   <item><c>acceptance.md</c> — acceptance criteria</item>
///   <item><c>decisions.md</c> — feature-specific design decisions</item>
///   <item><c>open-questions.md</c> — open questions (links to clarifications/)</item>
///   <item><c>packets.md</c> — execution units (links to packets/ or GitHub issues)</item>
///   <item><c>links.md</c> — reference links</item>
/// </list>
///
/// Idempotent: existing files are preserved; missing files are created only when
/// <c>--write</c> is supplied. Without <c>--write</c> the command is a dry-run.
///
/// Also creates or updates <c>features/index.md</c> to cross-link the new feature.
///
/// Refuses to run inside an automation child worktree.
/// </summary>
internal static class IntentAddFeatureCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli intent add-feature --domain <name> --name <feature-slug> "
        + "[--write] [--format markdown|json]";

    /// <summary>Starter file names created per feature folder.</summary>
    internal static readonly string[] FeatureFiles =
    [
        "overview.md",
        "requirements.md",
        "acceptance.md",
        "decisions.md",
        "open-questions.md",
        "packets.md",
        "links.md"
    ];

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(UsageLine);
            writer.WriteLine("Adds a feature folder skeleton to an existing tree-v1 intent domain.");
            writer.WriteLine();
            writer.WriteLine("Creates starter files: " + string.Join(", ", FeatureFiles));
            writer.WriteLine("Also creates or updates features/index.md with a cross-link.");
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

    internal static IntentAddFeatureResult ExecuteCore(string hostRepoRoot, IntentAddFeatureRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostRepoRoot);
        ArgumentNullException.ThrowIfNull(request);

        EnsureNotChildWorktree(hostRepoRoot);

        // Build planned file list: feature files + features index
        var planned = new List<string>();
        var featureBase = $"intents/{request.Domain}/features/{request.FeatureName}/";

        foreach (var fileName in FeatureFiles)
        {
            planned.Add($"{featureBase}{fileName}");
        }

        var featuresIndexPath = $"intents/{request.Domain}/features/index.md";
        planned.Add(featuresIndexPath);

        var written = new List<string>();
        var existing = new List<string>();
        var updated = new List<string>();

        // Feature starter files (idempotent: never overwrite)
        foreach (var relativePath in planned.Where(p => !string.Equals(p, featuresIndexPath, StringComparison.Ordinal)))
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
            File.WriteAllText(absolutePath, RenderFeatureFile(relativePath, request));
            written.Add(relativePath);
        }

        // features/index.md: create or append cross-link entry
        HandleFeaturesIndex(hostRepoRoot, featuresIndexPath, request, written, existing, updated);

        var nextSteps = BuildNextSteps(request, written.Count, existing.Count);

        return new IntentAddFeatureResult
        {
            Domain = request.Domain,
            FeatureName = request.FeatureName,
            WriteApplied = request.Write,
            PlannedPaths = planned,
            WrittenPaths = written,
            UpdatedPaths = updated,
            ExistingPaths = existing,
            NextSteps = nextSteps
        };
    }

    private static void HandleFeaturesIndex(
        string hostRepoRoot,
        string featuresIndexPath,
        IntentAddFeatureRequest request,
        List<string> written,
        List<string> existing,
        List<string> updated)
    {
        var absoluteIndex = ResolvePath(hostRepoRoot, featuresIndexPath);
        var featureLink = $"- [{request.FeatureName}]({request.FeatureName}/overview.md)";

        if (!File.Exists(absoluteIndex))
        {
            if (!request.Write)
            {
                return;
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(absoluteIndex)
                ?? throw new InvalidOperationException($"No parent directory for '{absoluteIndex}'."));
            File.WriteAllText(absoluteIndex, RenderFeaturesIndex(request, featureLink));
            written.Add(featuresIndexPath);
            return;
        }

        // Index exists — check if this feature is already listed
        var content = File.ReadAllText(absoluteIndex);
        if (content.Contains($"]({request.FeatureName}/", StringComparison.Ordinal))
        {
            existing.Add(featuresIndexPath);
            return;
        }

        if (!request.Write)
        {
            return;
        }

        // Append the feature link before the end of the file
        var trimmed = content.TrimEnd();
        var newContent = trimmed + Environment.NewLine + featureLink + Environment.NewLine;
        File.WriteAllText(absoluteIndex, newContent);
        updated.Add(featuresIndexPath);
    }

    private static string RenderFeatureFile(string relativePath, IntentAddFeatureRequest request)
    {
        var fileName = Path.GetFileName(relativePath);
        return fileName switch
        {
            "overview.md" => RenderOverview(request),
            "requirements.md" => RenderRequirements(request),
            "acceptance.md" => RenderAcceptance(request),
            "decisions.md" => RenderDecisions(request),
            "open-questions.md" => RenderOpenQuestions(request),
            "packets.md" => RenderPackets(request),
            "links.md" => RenderLinks(request),
            _ => throw new InvalidOperationException($"intent add-feature has no template for '{fileName}'.")
        };
    }

    private static string RenderOverview(IntentAddFeatureRequest r) =>
        $"""
        ---
        # Optional semantic facets (G529) — closed set, one line each:
        #   vocabulary            — event/command vocabulary: what counts as a fact
        #   invariant              — invariants and consistency boundaries
        #   decider                — decider judgments: what a command decides
        #   acceptance-property    — what must not break
        # Uncomment and edit to annotate this node, e.g.:
        # facets: [vocabulary]
        ---

        # {r.FeatureName} — overview

        > **Ask intent-cli first:** `intent-cli guide intent-work setup --kind tree-layout --domain {r.Domain} --format markdown`

        ## Goals

        _[Describe the goals and motivation for this feature.]_

        ## Acceptance criteria summary

        _[List high-level acceptance criteria here; see [acceptance.md](acceptance.md) for details.]_

        ## Related

        - [requirements.md](requirements.md)
        - [acceptance.md](acceptance.md)
        - [decisions.md](decisions.md)
        - [open-questions.md](open-questions.md)
        - [packets.md](packets.md)
        """;

    private static string RenderRequirements(IntentAddFeatureRequest r) =>
        $"""
        # {r.FeatureName} — requirements

        > See [overview.md](overview.md) for goals.

        ## Functional requirements

        _[List detailed functional requirements.]_

        ## Non-functional requirements

        _[List performance, security, reliability, and other non-functional requirements.]_
        """;

    private static string RenderAcceptance(IntentAddFeatureRequest r) =>
        $"""
        # {r.FeatureName} — acceptance criteria

        > See [overview.md](overview.md) for goals.

        ## Criteria

        - [ ] _[Criterion 1]_
        - [ ] _[Criterion 2]_
        """;

    private static string RenderDecisions(IntentAddFeatureRequest r) =>
        $"""
        # {r.FeatureName} — design decisions

        > See [overview.md](overview.md) for goals and [../../decisions/](../../decisions/) for cross-domain ADRs.

        ## Decisions

        _[Record feature-specific design decisions here. Link to cross-domain ADRs in decisions/ if applicable.]_
        """;

    private static string RenderOpenQuestions(IntentAddFeatureRequest r) =>
        $"""
        # {r.FeatureName} — open questions

        > See [../../clarifications/open.md](../../clarifications/open.md) for domain-level open questions.

        ## Open questions blocking this feature

        _[List unresolved questions. Link back to clarifications/ entries that block this feature.]_
        """;

    private static string RenderPackets(IntentAddFeatureRequest r) =>
        $"""
        # {r.FeatureName} — packets

        > See [../../packets/](../../packets/) for domain-level packet list.

        ## Execution units

        _[List implementation packets for this feature. Link to packets/ entries and GitHub issues once published.]_
        """;

    private static string RenderLinks(IntentAddFeatureRequest r) =>
        $"""
        # {r.FeatureName} — links

        > See [overview.md](overview.md) for context.

        ## Reference links

        _[List external references, related GitHub issues, prior art, and relevant documentation.]_
        """;

    private static string RenderFeaturesIndex(IntentAddFeatureRequest request, string firstFeatureLink) =>
        $"""
        # {request.Domain} — features

        > **Ask intent-cli first:** `intent-cli guide intent-work setup --kind tree-layout --domain {request.Domain} --format markdown`

        ## Feature list

        {firstFeatureLink}
        """;

    private static IReadOnlyList<string> BuildNextSteps(
        IntentAddFeatureRequest request,
        int writtenCount,
        int existingCount)
    {
        var verb = request.Write
            ? (writtenCount == 0 ? "Already added" : "Added")
            : "Plan";

        var steps = new List<string>
        {
            $"{verb}: feature '{request.FeatureName}' under domain '{request.Domain}' (written: {writtenCount}, existing: {existingCount})."
        };

        if (!request.Write)
        {
            steps.Add(
                $"Re-run with --write to create the planned files: "
                + $"intent-cli intent add-feature --domain {request.Domain} --name {request.FeatureName} --write");
        }

        steps.Add($"Edit `intents/{request.Domain}/features/{request.FeatureName}/overview.md` with goals and acceptance criteria.");
        steps.Add($"Fill requirements in `intents/{request.Domain}/features/{request.FeatureName}/requirements.md`.");
        steps.Add($"Link open questions to `intents/{request.Domain}/clarifications/open.md`.");
        steps.Add($"Run `intent-cli guide intent-work setup --kind tree-layout --domain {request.Domain} --format markdown` for authoring guidance.");
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
                    $"Refusing to run 'intent add-feature' inside an automation child worktree at '{hostRepoRoot}'. "
                    + "Run this command from the parent host repository.");
            }
        }
    }

    private static string ResolvePath(string hostRepoRoot, string relativePath) =>
        Path.Combine(hostRepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Validates that a value is a safe slug: letters, digits, hyphens, and underscores only.
    /// Mirrors the slug guard in <see cref="IntentInitCommand"/> and <see cref="IntentInitTreeCommand"/>
    /// to prevent path-traversal when domain/feature values are interpolated into file system paths.
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

    private static bool TryParseArguments(
        string[] args,
        out IntentAddFeatureRequest request,
        out string error)
    {
        request = default!;
        error = string.Empty;

        string? domain = null;
        string? featureName = null;
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
                case "--name":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = "Missing value for '--name'.";
                        return false;
                    }
                    featureName = args[++i].Trim();
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
            error = "intent add-feature requires '--domain <name>'.";
            return false;
        }

        if (!IsValidSlug(domain))
        {
            error = $"intent add-feature '--domain' value '{domain}' must be a slug (letters, digits, '-', '_').";
            return false;
        }

        if (string.IsNullOrWhiteSpace(featureName))
        {
            error = "intent add-feature requires '--name <feature-slug>'.";
            return false;
        }

        if (!IsValidSlug(featureName))
        {
            error = $"intent add-feature '--name' value '{featureName}' must be a slug (letters, digits, '-', '_').";
            return false;
        }

        request = new IntentAddFeatureRequest
        {
            Domain = domain!,
            FeatureName = featureName!,
            Write = write,
            Format = format
        };
        return true;
    }

    private static void WriteOutput(TextWriter writer, string format, IntentAddFeatureResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
            return;
        }

        WriteMarkdown(writer, result);
    }

    private static void WriteMarkdown(TextWriter writer, IntentAddFeatureResult result)
    {
        var verb = result.WriteApplied ? "Written" : "Planned (dry-run)";
        writer.WriteLine($"# intent add-feature — {result.Domain}/{result.FeatureName}");
        writer.WriteLine();
        writer.WriteLine($"- write-applied: {result.WriteApplied}");
        writer.WriteLine();

        writer.WriteLine($"## {verb} ({result.WrittenPaths.Count} written, {result.UpdatedPaths.Count} updated, {result.ExistingPaths.Count} existing)");
        writer.WriteLine();
        foreach (var p in result.WrittenPaths)
        {
            writer.WriteLine($"- [created] {p}");
        }
        foreach (var p in result.UpdatedPaths)
        {
            writer.WriteLine($"- [updated] {p}");
        }
        foreach (var p in result.ExistingPaths)
        {
            writer.WriteLine($"- [existing] {p}");
        }
        foreach (var p in result.PlannedPaths
            .Where(p => !result.WrittenPaths.Contains(p)
                     && !result.UpdatedPaths.Contains(p)
                     && !result.ExistingPaths.Contains(p)))
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

    internal sealed record IntentAddFeatureRequest
    {
        public required string Domain { get; init; }
        public required string FeatureName { get; init; }
        public required bool Write { get; init; }
        public required string Format { get; init; }
    }
}

internal sealed record IntentAddFeatureResult
{
    public required string Domain { get; init; }
    public required string FeatureName { get; init; }
    public required bool WriteApplied { get; init; }
    public required IReadOnlyList<string> PlannedPaths { get; init; }
    public required IReadOnlyList<string> WrittenPaths { get; init; }
    public required IReadOnlyList<string> UpdatedPaths { get; init; }
    public required IReadOnlyList<string> ExistingPaths { get; init; }
    public required IReadOnlyList<string> NextSteps { get; init; }
}
