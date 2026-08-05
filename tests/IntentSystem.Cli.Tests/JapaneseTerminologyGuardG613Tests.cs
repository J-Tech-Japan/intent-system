using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G613 keeps the already-reviewed Japanese reader path readable as Japanese.
/// The manifest grows only when a later documentation pass has been reviewed.
/// </summary>
public sealed class JapaneseTerminologyGuardG613Tests
{
    private static readonly IReadOnlySet<string> ReviewedReaderPath = new HashSet<string>(StringComparer.Ordinal)
    {
        "docs/ja/README.md",
        "docs/ja/01-install.md",
        "docs/ja/02-project-start.md",
        "docs/ja/02a-getting-started-orchestration.md",
        "docs/ja/02b-separate-host-brand-new.md",
        "docs/ja/02c-separate-host-existing.md",
        "docs/ja/02d-same-repo-brand-new.md",
        "docs/ja/02e-same-repo-existing.md",
        "docs/ja/03-intents.md",
        "docs/ja/04-packets-issues.md",
        "docs/ja/07-recovery.md",
        "docs/ja/09-developer-reference.md",
    };

    // Closed list from the G613 measured reader-path sweep. Do not make this a
    // docs-wide exception: later passes add their reviewed page to the manifest.
    private static readonly string[] OrdinaryEnglishPhrases =
    [
        "self-contained",
        "brand-new",
        "pattern",
        "collocate",
        "metadata branch",
        "maturity note",
        "supported choice",
        "host metadata",
        "initial prompt",
        "separate host repository",
        "same repository",
        "authority",
    ];

    private static readonly Regex Fence = new(@"^\s*(```|~~~)", RegexOptions.Compiled);
    private static readonly Regex InlineCode = new(@"(?<tick>`+).*?\k<tick>", RegexOptions.Compiled);
    private static readonly Regex InlineDestination = new(@"(?<=\])\((?:<[^>]+>|[^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex ReferenceDestination = new(@"^(?<label>\s{0,3}\[[^\]]+\]:\s*)(?:<[^>]+>|\S+)", RegexOptions.Compiled);
    private static readonly Regex BareUrl = new(@"\b(?:https?|mailto):\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HtmlId = new("""\bid\s*=\s*(['\"]).*?\1""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HtmlComment = new(@"<!--.*?-->", RegexOptions.Compiled);

    [Fact]
    public void ReviewedJapaneseReaderPath_HasNoClosedListOrdinaryEnglish_G613()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var failures = new List<string>();

        Assert.Equal(12, ReviewedReaderPath.Count);
        foreach (var relativePath in ReviewedReaderPath.OrderBy(path => path, StringComparer.Ordinal))
        {
            var path = Path.Combine(root, relativePath);
            Assert.True(File.Exists(path), $"G613 manifest entry does not exist: {relativePath}");
            failures.AddRange(FindOrdinaryEnglish(relativePath, File.ReadAllLines(path)));
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void GuardChecksProseTableCellsAndLinkLabels_ButSkipsCodeAndUrlDestinations_G613()
    {
        var failures = FindOrdinaryEnglish("docs/ja/example.md",
        [
            "普通の prose に brand-new を置く。",
            "| 見出し | pattern |",
            "[supported choice](https://example.invalid/brand-new)",
            "`self-contained` は梱包用語として説明する。",
            "[日本語ラベル](https://example.invalid/metadata-branch)",
            "```text",
            "metadata branch",
            "```",
        ]);

        Assert.Equal(3, failures.Count);
        Assert.Contains(failures, failure => failure.Contains(":1:", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains(":2:", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains(":3:", StringComparison.Ordinal));
    }

    [Fact]
    public void DeveloperReferenceG616_LeavesDurableOnlyAsGlossedConceptOrIdentifiers_G616()
    {
        const int measuredOccurrences = 25;
        const int keptOccurrences = 8;
        const int translatedOccurrences = 17;
        var path = Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", "ja", "09-developer-reference.md");
        var content = File.ReadAllText(path);
        var prose = InlineCode.Replace(content, string.Empty);
        var unglossedProse = prose.Replace("durable state（永続状態）", string.Empty, StringComparison.Ordinal);

        Assert.Equal(measuredOccurrences, keptOccurrences + translatedOccurrences);
        Assert.Equal(keptOccurrences, Regex.Matches(content, "durable", RegexOptions.IgnoreCase).Count);
        Assert.Contains("### operator attention の永続状態 (G596)", content, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("durable", RegexOptions.IgnoreCase), unglossedProse);
        Assert.Contains("durable state（永続状態）", content, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> FindOrdinaryEnglish(string relativePath, IReadOnlyList<string> lines)
    {
        var failures = new List<string>();
        var fenced = false;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (Fence.IsMatch(line))
            {
                fenced = !fenced;
                continue;
            }

            if (fenced)
            {
                continue;
            }

            var prose = InlineCode.Replace(line, string.Empty);
            prose = InlineDestination.Replace(prose, string.Empty);
            prose = ReferenceDestination.Replace(prose, "${label}");
            prose = BareUrl.Replace(prose, string.Empty);
            prose = HtmlId.Replace(prose, string.Empty);
            prose = HtmlComment.Replace(prose, string.Empty);

            foreach (var phrase in OrdinaryEnglishPhrases)
            {
                if (ContainsPhrase(prose, phrase))
                {
                    failures.Add($"{relativePath}:{index + 1}: ordinary English phrase '{phrase}' must be translated in reviewed Japanese prose.");
                }
            }
        }

        return failures;
    }

    private static bool ContainsPhrase(string prose, string phrase)
    {
        var start = 0;
        while (true)
        {
            var index = prose.IndexOf(phrase, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var beforeIsWord = index > 0 && IsAsciiWord(prose[index - 1]);
            var end = index + phrase.Length;
            var afterIsWord = end < prose.Length && IsAsciiWord(prose[end]);
            if (!beforeIsWord && !afterIsWord)
            {
                return true;
            }

            start = end;
        }
    }

    private static bool IsAsciiWord(char value) => char.IsAsciiLetterOrDigit(value) || value == '_';
}
