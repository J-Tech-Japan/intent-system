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
    public void MinimalStart_UsesOneHostCheckoutOnePromptAndFourHumanDecisions_G608(string language)
    {
        var root = language == "en"
            ? File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "README.md"))
            : ReadDoc(language, "README.md");
        var gettingStarted = ReadDoc(language, "02a-getting-started-orchestration.md");

        foreach (var text in new[]
                 {
                     "<owner>/<implementation-repo>",
                     "guide onboarding",
                     "intent-cli --version",
                     "intent init",
                     "--write",
                     language == "en" ? "nine files" : "9 files",
                     "session-layer set",
                 })
        {
            Assert.Contains(text, root + gettingStarted, StringComparison.Ordinal);
        }

        Assert.Contains("repository topology", gettingStarted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("base-branch", gettingStarted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("transport", gettingStarted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("agent kind", gettingStarted, StringComparison.OrdinalIgnoreCase);
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
            ReadDoc(language, "12-agent-message-orchestration.md").Split("## Canonical notify workflow", StringSplitOptions.None)[0],
        });

        Assert.Contains("herdr-only", presentation, StringComparison.Ordinal);
        Assert.Contains("agmsg", presentation, StringComparison.Ordinal);
        Assert.Contains("PREVIEW", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("agmsg (PRIMARY)", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("primary transport", presentation, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadDoc(string language, string path) =>
        File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, path));
}
