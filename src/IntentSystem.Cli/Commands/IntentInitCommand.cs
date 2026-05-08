using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G293: chat-first host-domain initialization command. Bootstraps an
/// <c>.intent-cli/config.toml</c> and a minimal <c>intents/&lt;domain&gt;</c>
/// scaffold in a parent host repository so a fresh AI agent can ask
/// <c>intent-cli</c> to set up a new domain without reading
/// <c>intents/rules/**</c>, copied prompt files, or local skills.
///
/// Idempotent: existing files are preserved (reported as <c>existing</c>),
/// missing files are created when <c>--write</c> is supplied. Without
/// <c>--write</c> the command is a dry-run.
///
/// Refuses to run inside an automation child worktree
/// (<c>.intent-cli/worktrees/**</c>) because host bootstrap belongs in the
/// parent host repo, not in a child checkout.
/// </summary>
internal static class IntentInitCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli intent init --domain <name> [--target-repo <owner/repo>] [--write] [--format markdown|json]";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(UsageLine);
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

    internal static IntentInitResult ExecuteCore(string hostRepoRoot, IntentInitRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostRepoRoot);
        ArgumentNullException.ThrowIfNull(request);

        EnsureNotChildWorktree(hostRepoRoot);

        var planned = new List<string>
        {
            $"{CliRuntimeContracts.IntentCliDirectoryName}/{CliRuntimeContracts.ConfigFileName}",
            $"intents/{request.Domain}/README.md",
            $"intents/{request.Domain}/clarifications/open.md",
            $"intents/{request.Domain}/intent-tree/00-map.md"
        };

        var written = new List<string>();
        var existing = new List<string>();

        foreach (var relativePath in planned)
        {
            var absolutePath = ResolveHostPath(hostRepoRoot, relativePath);
            if (File.Exists(absolutePath))
            {
                existing.Add(relativePath);
                continue;
            }

            if (!request.Write)
            {
                continue;
            }

            var directoryPath = Path.GetDirectoryName(absolutePath)
                ?? throw new InvalidOperationException(
                    $"Intent init target '{absolutePath}' did not contain a directory.");
            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(absolutePath, RenderContent(relativePath, request));
            written.Add(relativePath);
        }

        return new IntentInitResult
        {
            Domain = request.Domain,
            TargetRepo = request.TargetRepo,
            HostRepoRoot = Path.GetFullPath(hostRepoRoot),
            WriteApplied = request.Write,
            PlannedPaths = planned,
            WrittenPaths = written,
            ExistingPaths = existing,
            NextSteps = BuildNextSteps(request, written.Count, existing.Count)
        };
    }

    private static void EnsureNotChildWorktree(string hostRepoRoot)
    {
        var normalized = Path.GetFullPath(hostRepoRoot)
            .Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var index = 0; index + 1 < segments.Length; index++)
        {
            if (string.Equals(segments[index], ".intent-cli", StringComparison.Ordinal)
                && string.Equals(segments[index + 1], "worktrees", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Refusing to run 'intent init' inside an automation child worktree at '{hostRepoRoot}'. Run this command from the parent host repository (the directory that owns '.intent-cli/').");
            }
        }
    }

    private static IReadOnlyList<string> BuildNextSteps(
        IntentInitRequest request,
        int writtenCount,
        int existingCount)
    {
        var domain = request.Domain;
        var verb = request.Write
            ? (writtenCount == 0 ? "Already initialized" : "Initialized")
            : "Plan";

        var steps = new List<string>
        {
            $"{verb}: domain '{domain}' (written: {writtenCount}, existing: {existingCount})."
        };

        if (!request.Write)
        {
            steps.Add($"Re-run with --write to create the planned files: intent-cli intent init --domain {domain}{TargetArg(request)} --write");
        }

        steps.Add($"Open `intents/{domain}/intent-tree/00-map.md` and capture the initial domain shape.");
        steps.Add($"Use `intent-cli interview record-answer --write` (chat-first) to durably record durable Q/A for '{domain}'.");
        steps.Add($"Use `intent-cli intent next-slice --domain {domain} --dry-run` to plan the first publishable slice.");
        steps.Add("Run this command from the parent host repository, never inside `.intent-cli/worktrees/**`.");

        return steps;
    }

    private static string TargetArg(IntentInitRequest request)
    {
        return string.IsNullOrWhiteSpace(request.TargetRepo)
            ? string.Empty
            : $" --target-repo {request.TargetRepo}";
    }

    private static string ResolveHostPath(string hostRepoRoot, string relativePath)
    {
        return Path.Combine(hostRepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string RenderContent(string relativePath, IntentInitRequest request)
    {
        return relativePath switch
        {
            var path when path.EndsWith($"/{CliRuntimeContracts.ConfigFileName}", StringComparison.Ordinal)
                => RenderConfigToml(request),
            var path when path.EndsWith("/README.md", StringComparison.Ordinal)
                => RenderDomainReadme(request),
            var path when path.EndsWith("/clarifications/open.md", StringComparison.Ordinal)
                => RenderOpenClarifications(),
            var path when path.EndsWith("/intent-tree/00-map.md", StringComparison.Ordinal)
                => RenderIntentMap(request),
            _ => throw new InvalidOperationException(
                $"Intent init has no template for '{relativePath}'.")
        };
    }

    private static string RenderConfigToml(IntentInitRequest request)
    {
        return $$"""
        [project]
        domain = "{{EscapeTomlString(request.Domain)}}"
        artifact_root = ".intent-cli"
        worktree_root = ".intent-cli/worktrees"
        """;
    }

    private static string RenderDomainReadme(IntentInitRequest request)
    {
        var targetLine = string.IsNullOrWhiteSpace(request.TargetRepo)
            ? string.Empty
            : $"Target repo: `{request.TargetRepo}`" + Environment.NewLine + Environment.NewLine;

        return $$"""
        # {{request.Domain}}

        {{targetLine}}Bootstrapped by `intent-cli intent init`.

        Treat this as the parent host record for the `{{request.Domain}}` domain. Add
        upstream references and the canonical domain shape under
        `intent-tree/`, durable Q/A under `interviews/`, and open
        clarifications under `clarifications/open.md`.
        """;
    }

    private static string RenderOpenClarifications()
    {
        return """
        # Open Clarifications

        - none
        """;
    }

    private static string RenderIntentMap(IntentInitRequest request)
    {
        var targetLine = string.IsNullOrWhiteSpace(request.TargetRepo)
            ? string.Empty
            : $"- Target repo: `{request.TargetRepo}`" + Environment.NewLine;

        return $$"""
        # Intent Map

        - Domain: `{{request.Domain}}`
        {{targetLine}}- Initial map: pending

        Capture the canonical domain shape here. Each child intent should
        link back to this map and the host data under `.intent-cli/`.
        """;
    }

    private static string EscapeTomlString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static bool TryParseArguments(
        string[] args,
        out IntentInitRequest request,
        out string error)
    {
        request = default!;
        error = string.Empty;

        string? domain = null;
        string? targetRepo = null;
        var write = false;
        var format = FormatMarkdown;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "Missing value for '--domain'.";
                        return false;
                    }
                    domain = args[++index].Trim();
                    break;
                case "--target-repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "Missing value for '--target-repo'.";
                        return false;
                    }
                    targetRepo = args[++index].Trim();
                    break;
                case "--write":
                    write = true;
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "Missing value for '--format'.";
                        return false;
                    }
                    var nextValue = args[++index].Trim();
                    if (!string.Equals(nextValue, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(nextValue, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"Unsupported format '{nextValue}'. Expected 'markdown' or 'json'.";
                        return false;
                    }
                    format = nextValue;
                    break;
                default:
                    error = $"Unknown intent init option '{args[index]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            error = "Intent init requires '--domain <name>'.";
            return false;
        }

        if (!IsValidDomainSlug(domain))
        {
            error = $"Intent init '--domain' value '{domain}' must be a slug (letters, digits, '-', '_').";
            return false;
        }

        request = new IntentInitRequest
        {
            Domain = domain!,
            TargetRepo = targetRepo,
            Write = write,
            Format = format
        };
        return true;
    }

    private static bool IsValidDomainSlug(string? value)
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

    private static void WriteOutput(TextWriter writer, string format, IntentInitResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
            return;
        }

        IntentInitRenderer.WriteMarkdown(writer, result);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    internal sealed record IntentInitRequest
    {
        public required string Domain { get; init; }

        public string? TargetRepo { get; init; }

        public required bool Write { get; init; }

        public required string Format { get; init; }
    }
}
