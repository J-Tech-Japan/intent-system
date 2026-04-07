using System.Text.Json;

namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentCommand
{
    private static readonly string[] SupportedAltitudes =
    [
        "purpose",
        "user-context",
        "means",
        "rules",
        "specs",
        "execution"
    ];

    private static readonly HashSet<string> TextFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".csproj",
        ".fs",
        ".json",
        ".md",
        ".props",
        ".sln",
        ".targets",
        ".toml",
        ".txt",
        ".xml",
        ".yaml",
        ".yml"
    };

    public static Func<IGitHubCommandRunner> GitHubCommandRunnerFactory { get; set; } = () => new GhCommandRunner();

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length > 0
            && string.Equals(args[0], "advance", StringComparison.Ordinal))
        {
            return GenerateFromCurrentAdvanceCommand.Execute(context, args[1..], writer);
        }

        if (args.Length > 0
            && string.Equals(args[0], "submit", StringComparison.Ordinal))
        {
            return GenerateFromCurrentSubmitCommand.Execute(context, args[1..], writer);
        }

        if (args.Length > 0
            && string.Equals(args[0], "review", StringComparison.Ordinal))
        {
            return GenerateFromCurrentReviewCommand.Execute(context, args[1..], writer);
        }

        if (args.Length > 0
            && string.Equals(args[0], "accept", StringComparison.Ordinal))
        {
            return GenerateFromCurrentAcceptCommand.Execute(context, args[1..], writer);
        }

        if (args.Length > 0
            && string.Equals(args[0], "comment", StringComparison.Ordinal))
        {
            return GenerateFromCurrentCommentCommand.Execute(context, args[1..], writer);
        }

        if (args.Length > 0
            && string.Equals(args[0], "fix", StringComparison.Ordinal))
        {
            return GenerateFromCurrentFixCommand.Execute(context, args[1..], writer);
        }

        if (args.Length > 0
            && string.Equals(args[0], "resubmit", StringComparison.Ordinal))
        {
            return GenerateFromCurrentResubmitCommand.Execute(context, args[1..], writer);
        }

        if (args.Length > 0
            && string.Equals(args[0], "rereview", StringComparison.Ordinal))
        {
            return GenerateFromCurrentRereviewCommand.Execute(context, args[1..], writer);
        }

        if (args.Length > 0
            && string.Equals(args[0], "implement", StringComparison.Ordinal))
        {
            return GenerateFromCurrentImplementCommand.Execute(context, args[1..], writer);
        }

        if (args.Length > 0
            && string.Equals(args[0], "bridge", StringComparison.Ordinal))
        {
            return GenerateFromCurrentBridgeCommand.Execute(context, args[1..], writer);
        }

        if (!args.Contains("--from-path", StringComparer.Ordinal))
        {
            return GenerateFromCurrentReconstructionCommand.Execute(context, args, writer);
        }

        try
        {
            var result = ExecuteSourceBundleCore(context, args);
            GenerateFromCurrentRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentResult ExecuteSourceBundleCore(CliContext context, string[] args)
    {
        var options = ParseOptions(args);
        var sourceRootRelativePath = ResolvePathWithinRepo(context.RepoRoot, options.FromPath);
        var selectedPaths = new List<string>();
        var sourceRefs = new List<string>();
        var samplingNotes = new List<string>();
        var gaps = new List<string>();

        CollectCodeSignals(
            context.RepoRoot,
            sourceRootRelativePath,
            options.MaxFiles,
            selectedPaths,
            sourceRefs,
            samplingNotes,
            gaps);

        if (options.IncludeReadme)
        {
            CollectReadmeSignals(context.RepoRoot, selectedPaths, sourceRefs, samplingNotes, gaps);
        }

        if (options.IncludeDocs)
        {
            CollectDocSignals(context.RepoRoot, selectedPaths, sourceRefs, samplingNotes, gaps);
        }

        if (options.IncludeTests)
        {
            CollectTestSignals(context.RepoRoot, selectedPaths, sourceRefs, samplingNotes, gaps);
        }

        var runner = GitHubCommandRunnerFactory();
        CollectIssueSignals(options.IssueScope, runner, sourceRefs, samplingNotes, gaps);
        CollectPullRequestSignals(options.PrScope, runner, sourceRefs, samplingNotes, gaps);

        var artifact = new CurrentSourcesArtifact
        {
            DomainSlug = options.Domain,
            SourceRoot = sourceRootRelativePath,
            SelectedAltitudes = options.SelectedAltitudes,
            SelectedIssueScope = options.IssueScope.NormalizedValue,
            SelectedPrScope = options.PrScope.NormalizedValue,
            SelectedPaths = selectedPaths,
            SourceRefs = sourceRefs,
            SamplingNotes = samplingNotes,
            Gaps = gaps
        };

        var artifactPath = CurrentSourcesArtifactWriter.Write(context.RepoRoot, options.Domain, artifact);
        return new GenerateFromCurrentResult
        {
            Domain = options.Domain,
            ArtifactPath = ToRelativePath(context.RepoRoot, artifactPath),
            SourceRoot = sourceRootRelativePath,
            SelectedIssueScope = options.IssueScope.NormalizedValue,
            SelectedPrScope = options.PrScope.NormalizedValue,
            SelectedAltitudes = options.SelectedAltitudes,
            SelectedPaths = selectedPaths,
            SourceRefs = sourceRefs,
            SamplingNotes = samplingNotes,
            Gaps = gaps
        };
    }

    private static GenerateFromCurrentOptions ParseOptions(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current command requires a domain.");
        }

        var domain = args[0].Trim();
        string? fromPath = null;
        var issueScope = ScopeSelection.None("issues");
        var prScope = ScopeSelection.None("pull requests");
        IReadOnlyList<string> selectedAltitudes = SupportedAltitudes;
        var includeReadme = false;
        var includeDocs = false;
        var includeTests = false;
        var maxFiles = 20;

        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--from-path":
                    fromPath = ReadRequiredOptionValue(args, ref index, "--from-path");
                    break;
                case "--issues":
                    issueScope = ScopeSelection.Parse(ReadRequiredOptionValue(args, ref index, "--issues"), "issues");
                    break;
                case "--prs":
                    prScope = ScopeSelection.Parse(ReadRequiredOptionValue(args, ref index, "--prs"), "pull requests");
                    break;
                case "--altitudes":
                    selectedAltitudes = ParseAltitudes(ReadRequiredOptionValue(args, ref index, "--altitudes"));
                    break;
                case "--include-readme":
                    includeReadme = true;
                    break;
                case "--include-docs":
                    includeDocs = true;
                    break;
                case "--include-tests":
                    includeTests = true;
                    break;
                case "--max-files":
                    maxFiles = ParsePositiveInteger(ReadRequiredOptionValue(args, ref index, "--max-files"), "--max-files");
                    break;
                default:
                    throw new InvalidOperationException($"Unknown generate-from-current option '{argument}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(fromPath))
        {
            throw new InvalidOperationException("Generate-from-current command requires --from-path <path>.");
        }

        return new GenerateFromCurrentOptions
        {
            Domain = domain,
            FromPath = fromPath,
            IssueScope = issueScope,
            PrScope = prScope,
            SelectedAltitudes = selectedAltitudes,
            IncludeReadme = includeReadme,
            IncludeDocs = includeDocs,
            IncludeTests = includeTests,
            MaxFiles = maxFiles
        };
    }

    private static string ReadRequiredOptionValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw new InvalidOperationException($"{optionName} requires a value.");
        }

        index++;
        return args[index];
    }

    private static IReadOnlyList<string> ParseAltitudes(string value)
    {
        var selected = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0)
        {
            throw new InvalidOperationException("--altitudes must include at least one altitude.");
        }

        var unknownAltitude = selected.FirstOrDefault(altitude => !SupportedAltitudes.Contains(altitude, StringComparer.Ordinal));
        if (unknownAltitude is not null)
        {
            throw new InvalidOperationException($"Unsupported altitude '{unknownAltitude}'.");
        }

        return SupportedAltitudes.Where(selected.Contains).ToArray();
    }

    private static int ParsePositiveInteger(string value, string optionName)
    {
        if (!int.TryParse(value, out var parsed) || parsed <= 0)
        {
            throw new InvalidOperationException($"{optionName} must be a positive integer.");
        }

        return parsed;
    }

    private static string ResolvePathWithinRepo(string repoRoot, string fromPath)
    {
        var fullRepoRoot = Path.GetFullPath(repoRoot);
        var absolutePath = Path.IsPathRooted(fromPath)
            ? Path.GetFullPath(fromPath)
            : Path.GetFullPath(Path.Combine(repoRoot, fromPath));

        if (!absolutePath.StartsWith(fullRepoRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(absolutePath, fullRepoRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Generate-from-current path '{fromPath}' must resolve within repo root '{repoRoot}'.");
        }

        if (!File.Exists(absolutePath) && !Directory.Exists(absolutePath))
        {
            throw new InvalidOperationException($"Generate-from-current path was not found at {absolutePath}");
        }

        return ToRelativePath(repoRoot, absolutePath);
    }

    private static void CollectCodeSignals(
        string repoRoot,
        string sourceRootRelativePath,
        int maxFiles,
        List<string> selectedPaths,
        List<string> sourceRefs,
        List<string> samplingNotes,
        List<string> gaps)
    {
        var sourceRootAbsolutePath = Path.Combine(repoRoot, sourceRootRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var eligibleFiles = EnumerateEligibleFiles(sourceRootAbsolutePath, repoRoot).ToArray();
        if (eligibleFiles.Length == 0)
        {
            gaps.Add($"No readable files were found under selected path '{sourceRootRelativePath}'.");
            return;
        }

        var sampledFiles = eligibleFiles.Take(maxFiles).ToArray();
        if (eligibleFiles.Length > sampledFiles.Length)
        {
            samplingNotes.Add(
                $"code scope truncated to first {sampledFiles.Length} files out of {eligibleFiles.Length} eligible files under '{sourceRootRelativePath}'.");
        }
        else
        {
            samplingNotes.Add($"code scope sampled {sampledFiles.Length} files under '{sourceRootRelativePath}'.");
        }

        foreach (var relativePath in sampledFiles)
        {
            AddSourceFile("code", repoRoot, relativePath, selectedPaths, sourceRefs, samplingNotes);
        }
    }

    private static void CollectReadmeSignals(
        string repoRoot,
        List<string> selectedPaths,
        List<string> sourceRefs,
        List<string> samplingNotes,
        List<string> gaps)
    {
        var readmePath = Path.Combine(repoRoot, "README.md");
        if (!File.Exists(readmePath))
        {
            gaps.Add("README.md was requested but not found.");
            return;
        }

        AddSourceFile("readme", repoRoot, "README.md", selectedPaths, sourceRefs, samplingNotes);
    }

    private static void CollectDocSignals(
        string repoRoot,
        List<string> selectedPaths,
        List<string> sourceRefs,
        List<string> samplingNotes,
        List<string> gaps)
    {
        var topLevelMarkdownFiles = Directory.GetFiles(repoRoot, "*.md", SearchOption.TopDirectoryOnly)
            .Select(path => ToRelativePath(repoRoot, path))
            .Where(path => !string.Equals(path, "README.md", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var docsDirectory = Path.Combine(repoRoot, "docs");
        var docsDirectoryMarkdownFiles = Directory.Exists(docsDirectory)
            ? Directory.GetFiles(docsDirectory, "*.md", SearchOption.AllDirectories)
                .Select(path => ToRelativePath(repoRoot, path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray()
            : [];

        var docFiles = topLevelMarkdownFiles
            .Concat(docsDirectoryMarkdownFiles)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (docFiles.Length == 0)
        {
            gaps.Add("Repo docs were requested but no doc markdown files were found.");
            return;
        }

        samplingNotes.Add($"repo docs included {docFiles.Length} markdown files.");
        foreach (var relativePath in docFiles)
        {
            AddSourceFile("doc", repoRoot, relativePath, selectedPaths, sourceRefs, samplingNotes);
        }
    }

    private static void CollectTestSignals(
        string repoRoot,
        List<string> selectedPaths,
        List<string> sourceRefs,
        List<string> samplingNotes,
        List<string> gaps)
    {
        var testsRoot = Path.Combine(repoRoot, "tests");
        if (!Directory.Exists(testsRoot))
        {
            gaps.Add("Tests were requested but the tests directory was not found.");
            return;
        }

        var testFiles = EnumerateEligibleFiles(testsRoot, repoRoot)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (testFiles.Length == 0)
        {
            gaps.Add("Tests were requested but no readable test files were found.");
            return;
        }

        samplingNotes.Add($"test scope included {testFiles.Length} files from 'tests'.");
        foreach (var relativePath in testFiles)
        {
            AddSourceFile("test", repoRoot, relativePath, selectedPaths, sourceRefs, samplingNotes);
        }
    }

    private static void CollectIssueSignals(
        ScopeSelection scope,
        IGitHubCommandRunner runner,
        List<string> sourceRefs,
        List<string> samplingNotes,
        List<string> gaps)
    {
        if (scope.Mode == ScopeMode.None)
        {
            return;
        }

        var issueNumbers = ResolveGitHubNumbers(scope, "issue", runner);
        if (issueNumbers.Count == 0)
        {
            gaps.Add("Selected issue scope resolved to no issues.");
            return;
        }

        foreach (var issueNumber in issueNumbers)
        {
            var result = RunGitHub(
                runner,
                ["issue", "view", issueNumber.ToString(), "--comments", "--json", "number,title,body,url,state,comments"],
                "gh issue view failed.");

            using var document = JsonDocument.Parse(result.StdOut);
            var root = document.RootElement;
            var title = root.GetProperty("title").GetString() ?? string.Empty;
            var url = root.GetProperty("url").GetString() ?? string.Empty;
            var body = root.GetProperty("body").GetString() ?? string.Empty;
            var comments = root.TryGetProperty("comments", out var commentsElement) && commentsElement.ValueKind == JsonValueKind.Array
                ? commentsElement.GetArrayLength()
                : 0;

            sourceRefs.Add($"issue:{issueNumber} {url} {title}");
            samplingNotes.Add(
                $"issue:{issueNumber} state={root.GetProperty("state").GetString()} body={SummarizeText(body)} comments={comments}");
            AppendIssueCommentSignals(issueNumber, root, sourceRefs, samplingNotes);
            if (string.IsNullOrWhiteSpace(body) && comments == 0)
            {
                gaps.Add($"Issue {issueNumber} has sparse signal.");
            }
        }
    }

    private static void CollectPullRequestSignals(
        ScopeSelection scope,
        IGitHubCommandRunner runner,
        List<string> sourceRefs,
        List<string> samplingNotes,
        List<string> gaps)
    {
        if (scope.Mode == ScopeMode.None)
        {
            return;
        }

        var pullNumbers = ResolveGitHubNumbers(scope, "pr", runner);
        if (pullNumbers.Count == 0)
        {
            gaps.Add("Selected PR scope resolved to no pull requests.");
            return;
        }

        foreach (var pullNumber in pullNumbers)
        {
            var result = RunGitHub(
                runner,
                ["pr", "view", pullNumber.ToString(), "--comments", "--json", "number,title,body,url,state,isDraft,mergeStateStatus,comments,reviews"],
                "gh pr view failed.");

            using var document = JsonDocument.Parse(result.StdOut);
            var root = document.RootElement;
            var title = root.GetProperty("title").GetString() ?? string.Empty;
            var url = root.GetProperty("url").GetString() ?? string.Empty;
            var body = root.GetProperty("body").GetString() ?? string.Empty;
            var comments = root.TryGetProperty("comments", out var commentsElement) && commentsElement.ValueKind == JsonValueKind.Array
                ? commentsElement.GetArrayLength()
                : 0;
            var reviews = root.TryGetProperty("reviews", out var reviewsElement) && reviewsElement.ValueKind == JsonValueKind.Array
                ? reviewsElement.GetArrayLength()
                : 0;

            sourceRefs.Add($"pr:{pullNumber} {url} {title}");
            samplingNotes.Add(
                $"pr:{pullNumber} state={root.GetProperty("state").GetString()} merge_state={root.GetProperty("mergeStateStatus").GetString()} body={SummarizeText(body)} comments={comments} reviews={reviews}");
            AppendPullRequestCommentSignals(pullNumber, root, sourceRefs, samplingNotes);
            AppendPullRequestReviewSignals(pullNumber, root, sourceRefs, samplingNotes);
            if (string.IsNullOrWhiteSpace(body) && comments == 0 && reviews == 0)
            {
                gaps.Add($"PR {pullNumber} has sparse signal.");
            }
        }
    }

    private static IReadOnlyList<int> ResolveGitHubNumbers(ScopeSelection scope, string resourceKind, IGitHubCommandRunner runner)
    {
        return scope.Mode switch
        {
            ScopeMode.None => [],
            ScopeMode.All => ListAllNumbers(resourceKind, runner),
            ScopeMode.Explicit => scope.Numbers,
            _ => throw new InvalidOperationException($"Unsupported scope mode '{scope.Mode}'.")
        };
    }

    private static IReadOnlyList<int> ListAllNumbers(string resourceKind, IGitHubCommandRunner runner)
    {
        var result = RunGitHub(
            runner,
            [resourceKind, "list", "--state", "all", "--limit", "1000", "--json", "number"],
            $"gh {resourceKind} list failed.");

        using var document = JsonDocument.Parse(result.StdOut);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"gh {resourceKind} list response must be an array.");
        }

        return document.RootElement
            .EnumerateArray()
            .Select(element => element.GetProperty("number").GetInt32())
            .OrderBy(number => number)
            .ToArray();
    }

    private static GitHubCommandResult RunGitHub(
        IGitHubCommandRunner runner,
        IReadOnlyList<string> arguments,
        string defaultError)
    {
        var result = runner.Run(arguments);
        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StdErr)
                ? defaultError
                : result.StdErr.Trim();
            throw new InvalidOperationException(error);
        }

        return result;
    }

    private static void AppendIssueCommentSignals(
        int issueNumber,
        JsonElement issue,
        List<string> sourceRefs,
        List<string> samplingNotes)
    {
        if (!issue.TryGetProperty("comments", out var commentsElement)
            || commentsElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var commentIndex = 0;
        foreach (var comment in commentsElement.EnumerateArray())
        {
            var body = GetOptionalText(comment, "body");
            if (string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            commentIndex++;
            var summary = SummarizeText(body);
            sourceRefs.Add($"issue-comment:{issueNumber}#{commentIndex} {summary}");
            samplingNotes.Add($"issue-comment:{issueNumber}#{commentIndex} body={summary}");
        }
    }

    private static void AppendPullRequestCommentSignals(
        int pullNumber,
        JsonElement pullRequest,
        List<string> sourceRefs,
        List<string> samplingNotes)
    {
        if (!pullRequest.TryGetProperty("comments", out var commentsElement)
            || commentsElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var commentIndex = 0;
        foreach (var comment in commentsElement.EnumerateArray())
        {
            var body = GetOptionalText(comment, "body");
            if (string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            commentIndex++;
            var summary = SummarizeText(body);
            sourceRefs.Add($"pr-comment:{pullNumber}#{commentIndex} {summary}");
            samplingNotes.Add($"pr-comment:{pullNumber}#{commentIndex} body={summary}");
        }
    }

    private static void AppendPullRequestReviewSignals(
        int pullNumber,
        JsonElement pullRequest,
        List<string> sourceRefs,
        List<string> samplingNotes)
    {
        if (!pullRequest.TryGetProperty("reviews", out var reviewsElement)
            || reviewsElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var reviewIndex = 0;
        foreach (var review in reviewsElement.EnumerateArray())
        {
            var body = GetOptionalText(review, "body");
            var state = GetOptionalText(review, "state");
            if (string.IsNullOrWhiteSpace(body) && string.IsNullOrWhiteSpace(state))
            {
                continue;
            }

            reviewIndex++;
            var summary = string.IsNullOrWhiteSpace(body) ? "none" : SummarizeText(body);
            var normalizedState = string.IsNullOrWhiteSpace(state) ? "unknown" : state;
            sourceRefs.Add($"pr-review:{pullNumber}#{reviewIndex} state={normalizedState} {summary}");
            samplingNotes.Add($"pr-review:{pullNumber}#{reviewIndex} state={normalizedState} body={summary}");
        }
    }

    private static string? GetOptionalText(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.GetString();
    }

    private static IEnumerable<string> EnumerateEligibleFiles(string absolutePath, string repoRoot)
    {
        if (File.Exists(absolutePath))
        {
            var singleFile = ToRelativePath(repoRoot, absolutePath);
            return IsReadableTextFile(absolutePath) ? [singleFile] : [];
        }

        return Directory.GetFiles(absolutePath, "*", SearchOption.AllDirectories)
            .Where(path => !IsIgnoredPath(path))
            .Where(IsReadableTextFile)
            .Select(path => ToRelativePath(repoRoot, path))
            .OrderBy(path => path, StringComparer.Ordinal);
    }

    private static bool IsIgnoredPath(string absolutePath)
    {
        return absolutePath.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || absolutePath.Contains($"{Path.DirectorySeparatorChar}.intent-cli{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || absolutePath.Contains($"{Path.DirectorySeparatorChar}.takt{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || absolutePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || absolutePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool IsReadableTextFile(string absolutePath)
    {
        if (!TextFileExtensions.Contains(Path.GetExtension(absolutePath)))
        {
            return false;
        }

        return new FileInfo(absolutePath).Length <= 256 * 1024;
    }

    private static void AddSourceFile(
        string kind,
        string repoRoot,
        string relativePath,
        List<string> selectedPaths,
        List<string> sourceRefs,
        List<string> samplingNotes)
    {
        if (!selectedPaths.Contains(relativePath, StringComparer.Ordinal))
        {
            selectedPaths.Add(relativePath);
        }

        sourceRefs.Add($"{kind}:{relativePath}");
        var absolutePath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var summary = SummarizeText(File.ReadAllText(absolutePath));
        samplingNotes.Add($"{kind}:{relativePath} summary={summary}");
    }

    private static string SummarizeText(string text)
    {
        var line = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None)
            .Select(candidate => candidate.Trim())
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));

        if (string.IsNullOrWhiteSpace(line))
        {
            return "none";
        }

        return line.Length <= 120 ? line : line[..120];
    }

    private static string ToRelativePath(string repoRoot, string absolutePath)
    {
        return Path.GetRelativePath(repoRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
    }

    private sealed record GenerateFromCurrentOptions
    {
        public required string Domain { get; init; }

        public required string FromPath { get; init; }

        public required ScopeSelection IssueScope { get; init; }

        public required ScopeSelection PrScope { get; init; }

        public required IReadOnlyList<string> SelectedAltitudes { get; init; }

        public required bool IncludeReadme { get; init; }

        public required bool IncludeDocs { get; init; }

        public required bool IncludeTests { get; init; }

        public required int MaxFiles { get; init; }
    }

    internal enum ScopeMode
    {
        None,
        All,
        Explicit
    }

    internal sealed record ScopeSelection
    {
        public required ScopeMode Mode { get; init; }

        public required string NormalizedValue { get; init; }

        public required IReadOnlyList<int> Numbers { get; init; }

        public static ScopeSelection None(string label)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
            return new ScopeSelection
            {
                Mode = ScopeMode.None,
                NormalizedValue = "none",
                Numbers = []
            };
        }

        public static ScopeSelection Parse(string rawValue, string label)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(rawValue);
            ArgumentException.ThrowIfNullOrWhiteSpace(label);

            var value = rawValue.Trim();
            if (string.Equals(value, "none", StringComparison.Ordinal))
            {
                return None(label);
            }

            if (string.Equals(value, "all", StringComparison.Ordinal))
            {
                return new ScopeSelection
                {
                    Mode = ScopeMode.All,
                    NormalizedValue = "all",
                    Numbers = []
                };
            }

            var numbers = value.Contains('-', StringComparison.Ordinal)
                ? ParseRange(value, label)
                : ParseList(value, label);

            return new ScopeSelection
            {
                Mode = ScopeMode.Explicit,
                NormalizedValue = string.Join(',', numbers),
                Numbers = numbers
            };
        }

        private static IReadOnlyList<int> ParseRange(string value, string label)
        {
            var parts = value.Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length != 2
                || !int.TryParse(parts[0], out var start)
                || !int.TryParse(parts[1], out var end)
                || start <= 0
                || end < start)
            {
                throw new InvalidOperationException($"{label} scope '{value}' must be 'start-end'.");
            }

            return Enumerable.Range(start, end - start + 1).ToArray();
        }

        private static IReadOnlyList<int> ParseList(string value, string label)
        {
            var numbers = value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part =>
                {
                    if (!int.TryParse(part, out var parsed) || parsed <= 0)
                    {
                        throw new InvalidOperationException($"{label} scope '{value}' must contain positive integers.");
                    }

                    return parsed;
                })
                .Distinct()
                .OrderBy(number => number)
                .ToArray();

            if (numbers.Length == 0)
            {
                throw new InvalidOperationException($"{label} scope '{value}' must not be empty.");
            }

            return numbers;
        }
    }
}
