using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G608 keeps the field-tested minimal start visible without turning document
/// 02a into a replacement transport contract. It also makes the default
/// reading trail mechanically observable in each language.
/// </summary>
public sealed class OnboardingTransportPresentationG608Tests
{
    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void PatternChooser_IsTheFirstOnboardingDecision_G608(string language)
    {
        var root = language == "en"
            ? File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "README.md"))
            : ReadDoc(language, "README.md");
        var gettingStarted = ReadDoc(language, "02a-getting-started-orchestration.md");

        foreach (var path in PatternPaths)
        {
            Assert.Contains($"]({path})", root + gettingStarted, StringComparison.Ordinal);
        }

        Assert.Contains("Separate host", gettingStarted, StringComparison.Ordinal);
        Assert.Contains("Same repo", gettingStarted, StringComparison.Ordinal);
        Assert.Contains("brand-new", gettingStarted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("existing", gettingStarted, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void EveryPattern_IsSelfContainedAndHasExactlyTwoInitialPrompts_G608(string language)
    {
        foreach (var path in PatternPaths)
        {
            var doc = ReadDoc(language, path);

            Assert.Contains("<owner>/<implementation-repo>", doc, StringComparison.Ordinal);
            Assert.Contains("guide onboarding", doc, StringComparison.Ordinal);
            Assert.Contains("guide model", doc, StringComparison.Ordinal);
            Assert.Contains("intent init", doc, StringComparison.Ordinal);
            Assert.Contains("--write", doc, StringComparison.Ordinal);
            Assert.Contains(language == "en" ? "nine files" : "9 files", doc, StringComparison.Ordinal);
            Assert.Contains("base-branch", doc, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("agent kind", doc, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("herdr-only", doc, StringComparison.Ordinal);
            Assert.Contains("agmsg", doc, StringComparison.Ordinal);
            Assert.Equal(2, CountOccurrences(doc, "### "));
            Assert.Equal(2, CountOccurrences(doc, "\n> "));
            Assert.Contains("](02a-getting-started-orchestration.md)", doc, StringComparison.Ordinal);
        }

        var fieldTestPattern = ReadDoc(language, "02b-separate-host-brand-new.md");
        Assert.Contains(language == "en" ? "two empty repositories" : "空の", fieldTestPattern, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "only the empty host repository" : "host repository だけ", fieldTestPattern, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DefaultHeaderAndNextTrail_ReachesDocument12WithoutTimerLoop_G608(string language)
    {
        var trail = new[]
        {
            ("01-install.md", "02-project-start.md"),
            ("02-project-start.md", "02a-getting-started-orchestration.md"),
            ("02a-getting-started-orchestration.md", "03-intents.md"),
            ("03-intents.md", "04-packets-issues.md"),
            ("04-packets-issues.md", "12-agent-message-orchestration.md"),
        };

        foreach (var (page, next) in trail)
        {
            var doc = ReadDoc(language, page);
            var header = string.Join('\n', doc.Split('\n').Take(5));
            var nextHeading = language == "en" ? "## Next" : "## 次へ";
            var footerStart = doc.LastIndexOf(nextHeading, StringComparison.Ordinal);

            Assert.Contains($"]({next})", header, StringComparison.Ordinal);
            Assert.True(footerStart >= 0, $"{language}/{page} must retain its Next footer.");
            Assert.Contains($"]({next})", doc[footerStart..], StringComparison.Ordinal);
        }

        var terminalHeader = string.Join('\n', ReadDoc(language, "12-agent-message-orchestration.md").Split('\n').Take(5));
        Assert.Contains("](04-packets-issues.md)", terminalHeader, StringComparison.Ordinal);
        var defaultLinks = string.Join('\n', trail.Select(step =>
        {
            var doc = ReadDoc(language, step.Item1);
            var header = string.Join('\n', doc.Split('\n').Take(5));
            var nextHeading = language == "en" ? "## Next" : "## 次へ";
            return header + "\n" + doc[doc.LastIndexOf(nextHeading, StringComparison.Ordinal)..];
        }));
        Assert.DoesNotContain("05-implementation-loop.md", defaultLinks, StringComparison.Ordinal);
        Assert.DoesNotContain("06-review-next-slice-loop.md", defaultLinks, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "visible-generated-mode-markers", "#### Visible, generated mode markers")]
    [InlineData("ja", "可視な生成済み-mode-marker", "#### 可視な生成済み mode marker")]
    public void LanguageSpecificMarkerAnchor_ResolvesWithinItsOwnDocument_G608(
        string language,
        string anchor,
        string heading)
    {
        Assert.Contains(
            $"12-agent-message-orchestration.md#{anchor}",
            ReadDoc(language, "02a-getting-started-orchestration.md"),
            StringComparison.Ordinal);
        Assert.Contains(heading, ReadDoc(language, "12-agent-message-orchestration.md"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void TransportChooser_IsConditionalAndPrimaryBelongsToTheModel_G608(string language)
    {
        var presentation = string.Join('\n', new[]
        {
            ReadDoc(language, "README.md"),
            ReadDoc(language, "02a-getting-started-orchestration.md"),
            ReadDoc(language, "02b-separate-host-brand-new.md"),
            ReadDoc(language, "02c-separate-host-existing.md"),
            ReadDoc(language, "02d-same-repo-brand-new.md"),
            ReadDoc(language, "02e-same-repo-existing.md"),
            ReadDoc(language, "09-developer-reference.md").Split(language == "en"
                ? "Semantics:"
                : "セマンティクス:", StringSplitOptions.None)[0],
            ReadDoc(language, "12-agent-message-orchestration.md").Split("## Canonical notify workflow", StringSplitOptions.None)[0],
        });

        Assert.Contains("herdr-only", presentation, StringComparison.Ordinal);
        Assert.Contains("agmsg", presentation, StringComparison.Ordinal);
        Assert.Contains("PREVIEW", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("agmsg (PRIMARY)", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("`agmsg` is PRIMARY", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("`agmsg` が PRIMARY", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("primary transport", presentation, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void LiveDocumentation_DoesNotLabelAnyTransportPrimary_G608(string language)
    {
        var docsDirectory = Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language);
        var liveDocs = Directory.GetFiles(docsDirectory, "*.md")
            .Where(path => !Path.GetFileName(path).StartsWith("release-notes-", StringComparison.Ordinal))
            .Select(File.ReadAllText);

        foreach (var doc in liveDocs)
        {
            Assert.DoesNotContain("agmsg (PRIMARY)", doc, StringComparison.Ordinal);
            Assert.DoesNotContain("`agmsg` is PRIMARY", doc, StringComparison.Ordinal);
            Assert.DoesNotContain("`agmsg` が PRIMARY", doc, StringComparison.Ordinal);
            Assert.DoesNotContain("primary transport", doc, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string ReadDoc(string language, string path) =>
        File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, path));

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += needle.Length;
        }

        return count;
    }

    private static readonly string[] PatternPaths =
    [
        "02b-separate-host-brand-new.md",
        "02c-separate-host-existing.md",
        "02d-same-repo-brand-new.md",
        "02e-same-repo-existing.md",
    ];
}
