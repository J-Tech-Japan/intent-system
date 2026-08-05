using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G611: local documentation links are a repository contract. This test has no
/// network client and does not add a CLI surface; it checks only Markdown
/// files already shipped by the repository.
/// </summary>
public sealed class MarkdownLocalLinkGuardG611Tests
{
    private static readonly IReadOnlySet<ExactException> NoExceptions = new HashSet<ExactException>();
    private static readonly Regex Fence = new(@"^\s*(```|~~~)", RegexOptions.Compiled);
    private static readonly Regex InlineLink = new(@"(?<!\!)\[(?<text>[^\]]+)\]\((?<target><[^>]+>|[^\s)]+)(?:\s+[^)]*)?\)", RegexOptions.Compiled);
    private static readonly Regex ReferenceDefinition = new(@"^\s{0,3}\[(?<label>[^\]]+)\]:\s*(?<target><[^>]+>|\S+)", RegexOptions.Compiled);
    private static readonly Regex ReferenceUse = new(@"(?<!\!)\[(?<text>[^\]]+)\]\[(?<label>[^\]]*)\]", RegexOptions.Compiled);
    private static readonly Regex ShortcutReference = new(@"(?<!\!)\[(?<label>[^\]]+)\](?![\[(])", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^\s{0,3}#{1,6}\s+(?<text>.+?)\s*#*\s*$", RegexOptions.Compiled);
    private static readonly Regex ExplicitId = new("""\bid\s*=\s*(['\"])(?<id>.*?)\1""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MarkdownLinkInHeading = new(@"\[(?<text>[^\]]+)\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex InlineCode = new(@"(?<tick>`+).*?\k<tick>", RegexOptions.Compiled);

    [Fact]
    public void RepositoryLocalMarkdownLinks_ResolveOrHaveAnExactExpiringException_G611()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var exceptions = ReadExceptions(root);
        var failures = Validate(root, exceptions);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void AnchorResolution_HandlesUnicodePercentEncodingDuplicatesExplicitIdsAndSameFileLinks_G611()
    {
        using var fixture = new TemporaryDocs();
        fixture.Write("README.md", """
            [unicode](docs/target.md#%E5%8F%AF%E8%A6%96%E3%81%AA%E7%94%9F%E6%88%90%E6%B8%88%E3%81%BF-mode-marker)
            [duplicate](docs/target.md#repeat-1)
            [html](docs/target.md#manual-anchor)
            [underscore](docs/target.md#field_name)
            [same](#root-heading)
            [reference][target]

            [target]: docs/target.md#repeat

            # Root heading
            """);
        fixture.Write("docs/target.md", """
            ## 可視な生成済み mode marker
            ## Repeat
            ## Repeat
            <span id="manual-anchor"></span>
            ## Field_name
            """);

        Assert.Empty(Validate(fixture.Root, NoExceptions));
    }

    [Fact]
    public void Guard_ReportsEveryBrokenPairWithSourceAndLine_G611()
    {
        using var fixture = new TemporaryDocs();
        fixture.Write("README.md", """
            [missing file](docs/missing.md)
            [missing anchor](docs/target.md#not-a-heading)
            [outside](../outside.md)
            """);
        fixture.Write("docs/target.md", "# Present");

        var failures = Validate(fixture.Root, NoExceptions);

        Assert.Equal(3, failures.Count);
        Assert.All(failures, failure => Assert.Contains("README.md:", failure, StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("missing.md", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("not-a-heading", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("outside.md", StringComparison.Ordinal));
    }

    [Fact]
    public void ExternalUrlsAndInlineCode_AreSkippedWithoutNetworkAccess_G611()
    {
        using var fixture = new TemporaryDocs();
        fixture.Write("README.md", """
            [web](https://example.invalid/not-checked)
            [mail](mailto:docs@example.invalid)
            `[sample](does-not-exist.md)`
            ```md
            [fenced](also-does-not-exist.md)
            ```
            """);

        Assert.Empty(Validate(fixture.Root, NoExceptions));
    }

    private static List<string> Validate(string root, IReadOnlySet<ExactException> exceptions)
    {
        var documents = EnumerateDocuments(root).ToDictionary(path => Relative(root, path), StringComparer.Ordinal);
        var parsed = documents.ToDictionary(
            pair => pair.Key,
            pair => ParseDocument(pair.Key, File.ReadAllLines(pair.Value)),
            StringComparer.Ordinal);
        var failures = new List<string>();

        foreach (var source in parsed.Values)
        {
            foreach (var link in source.Links)
            {
                if (IsExternal(link.Target))
                {
                    continue;
                }

                var exception = new ExactException(source.RelativePath, link.Line);
                if (exceptions.Contains(exception))
                {
                    continue;
                }

                ValidateLink(root, parsed, source, link, failures);
            }
        }

        return failures;
    }

    private static void ValidateLink(
        string root,
        IReadOnlyDictionary<string, ParsedDocument> documents,
        ParsedDocument source,
        MarkdownLink link,
        List<string> failures)
    {
        var (pathPart, fragment) = SplitTarget(link.Target);
        var targetPath = string.IsNullOrEmpty(pathPart)
            ? source.RelativePath
            : ResolveRelativePath(root, source.RelativePath, pathPart);

        if (targetPath is null)
        {
            failures.Add($"{source.RelativePath}:{link.Line}: local target escapes the repository: {link.Target}");
            return;
        }

        var targetAbsolutePath = Path.Combine(root, targetPath);
        if (!File.Exists(targetAbsolutePath))
        {
            failures.Add($"{source.RelativePath}:{link.Line}: local target does not exist: {link.Target}");
            return;
        }

        if (string.IsNullOrEmpty(fragment))
        {
            return;
        }

        if (!documents.TryGetValue(targetPath, out var target))
        {
            failures.Add($"{source.RelativePath}:{link.Line}: fragment target is not scanned Markdown: {link.Target}");
            return;
        }

        var decoded = Uri.UnescapeDataString(fragment);
        if (target.ExplicitIds.Contains(decoded)
            || target.Anchors.Contains(decoded.Trim().ToLowerInvariant()))
        {
            return;
        }

        failures.Add($"{source.RelativePath}:{link.Line}: fragment '#{fragment}' does not resolve in {targetPath}");
    }

    private static ParsedDocument ParseDocument(string relativePath, string[] lines)
    {
        var references = new Dictionary<string, string>(StringComparer.Ordinal);
        var nonFencedLines = new List<(int Number, string Content)>();
        var fenced = false;

        for (var index = 0; index < lines.Length; index++)
        {
            if (Fence.IsMatch(lines[index]))
            {
                fenced = !fenced;
                continue;
            }

            if (!fenced)
            {
                nonFencedLines.Add((index + 1, lines[index]));
                var definition = ReferenceDefinition.Match(lines[index]);
                if (definition.Success)
                {
                    references[ReferenceLabel(definition.Groups["label"].Value)] = CleanTarget(definition.Groups["target"].Value);
                }
            }
        }

        var links = new List<MarkdownLink>();
        foreach (var (number, originalLine) in nonFencedLines)
        {
            var line = InlineCode.Replace(originalLine, string.Empty);
            foreach (Match match in InlineLink.Matches(line))
            {
                links.Add(new MarkdownLink(number, CleanTarget(match.Groups["target"].Value)));
            }

            foreach (Match match in ReferenceUse.Matches(line))
            {
                var label = match.Groups["label"].Value;
                var key = ReferenceLabel(string.IsNullOrEmpty(label) ? match.Groups["text"].Value : label);
                links.Add(new MarkdownLink(number, references.TryGetValue(key, out var target) ? target : $"__undefined-reference__:{key}"));
            }

            foreach (Match match in ShortcutReference.Matches(line))
            {
                var key = ReferenceLabel(match.Groups["label"].Value);
                if (references.TryGetValue(key, out var target))
                {
                    links.Add(new MarkdownLink(number, target));
                }
            }
        }

        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var explicitIds = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, originalLine) in nonFencedLines)
        {
            foreach (Match id in ExplicitId.Matches(originalLine))
            {
                explicitIds.Add(Uri.UnescapeDataString(id.Groups["id"].Value));
            }

            var heading = Heading.Match(originalLine);
            if (!heading.Success)
            {
                continue;
            }

            var baseAnchor = GitHubAnchor(MarkdownLinkInHeading.Replace(heading.Groups["text"].Value, "${text}"));
            var suffix = duplicates.TryGetValue(baseAnchor, out var count) ? count : 0;
            duplicates[baseAnchor] = suffix + 1;
            anchors.Add(suffix == 0 ? baseAnchor : $"{baseAnchor}-{suffix}");
        }

        return new ParsedDocument(relativePath, links, anchors, explicitIds);
    }

    private static IEnumerable<string> EnumerateDocuments(string root)
    {
        yield return Path.Combine(root, "README.md");
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories))
        {
            yield return path;
        }
    }

    private static IReadOnlySet<ExactException> ReadExceptions(string root)
    {
        var ledger = Path.Combine(root, "docs", "markdown-link-exceptions.md");
        var exceptions = new HashSet<ExactException>();
        var lines = File.ReadAllLines(ledger);
        for (var index = 0; index < lines.Length; index++)
        {
            var cells = lines[index].Split('|', StringSplitOptions.TrimEntries);
            if (cells.Length != 7 || !int.TryParse(cells[2], NumberStyles.None, CultureInfo.InvariantCulture, out var line))
            {
                continue;
            }

            Assert.False(string.IsNullOrWhiteSpace(cells[3]), $"{Relative(root, ledger)}:{index + 1}: exception reason is required.");
            Assert.False(string.IsNullOrWhiteSpace(cells[4]), $"{Relative(root, ledger)}:{index + 1}: exception owner is required.");
            Assert.True(DateOnly.TryParseExact(cells[5], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry), $"{Relative(root, ledger)}:{index + 1}: exception expiry must be yyyy-MM-dd.");
            Assert.True(expiry >= DateOnly.FromDateTime(DateTime.UtcNow), $"{Relative(root, ledger)}:{index +1}: exception has expired.");
            exceptions.Add(new ExactException(cells[1], line));
        }

        return exceptions;
    }

    private static string? ResolveRelativePath(string root, string sourceRelativePath, string pathPart)
    {
        var rootFullPath = Path.GetFullPath(root);
        var sourceDirectory = Path.GetDirectoryName(Path.Combine(rootFullPath, sourceRelativePath))!;
        var resolved = Path.GetFullPath(Path.Combine(sourceDirectory, Uri.UnescapeDataString(pathPart)));
        if (!resolved.StartsWith(rootFullPath + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(resolved, rootFullPath, StringComparison.Ordinal))
        {
            return null;
        }

        return Relative(rootFullPath, resolved);
    }

    private static (string PathPart, string Fragment) SplitTarget(string target)
    {
        var separator = target.IndexOf('#', StringComparison.Ordinal);
        return separator < 0 ? (target, string.Empty) : (target[..separator], target[(separator + 1)..]);
    }

    private static string GitHubAnchor(string text)
    {
        var spaced = Whitespace.Replace(text.Trim().ToLowerInvariant(), "-");
        var builder = new StringBuilder();
        foreach (var character in spaced)
        {
            if (!char.IsPunctuation(character) || character is '-' or '_')
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static bool IsExternal(string target) =>
        target.StartsWith("//", StringComparison.Ordinal)
        || Uri.TryCreate(target, UriKind.Absolute, out _);

    private static string CleanTarget(string target) => target.Trim().Trim('<', '>');

    private static string ReferenceLabel(string label) => Whitespace.Replace(label.Trim(), " ").ToLowerInvariant();

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private sealed record MarkdownLink(int Line, string Target);

    private sealed record ParsedDocument(
        string RelativePath,
        IReadOnlyList<MarkdownLink> Links,
        IReadOnlySet<string> Anchors,
        IReadOnlySet<string> ExplicitIds);

    private sealed record ExactException(string SourceRelativePath, int Line);

    private sealed class TemporaryDocs : IDisposable
    {
        public TemporaryDocs()
        {
            Root = Path.Combine(Path.GetTempPath(), $"intent-system-g611-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Path.Combine(Root, "docs"));
        }

        public string Root { get; }

        public void Write(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
