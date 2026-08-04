namespace IntentSystem.Cli.Tests;

/// <summary>
/// G607: guards the concise EN/JA onboarding path. The full session-layer
/// contract remains in document 12; this protects the links, observed v0.11.0
/// success shapes, and the new-team ordering without turning the short guide
/// into a second normative operating procedure.
/// </summary>
public sealed class GettingStartedOrchestrationDocsG607Tests
{
    private const string GettingStartedPath = "02a-getting-started-orchestration.md";

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void GettingStartedPage_RecordsNewTeamInObservedV0110Order_G607(string language)
    {
        var doc = ReadDoc(language, GettingStartedPath);

        Assert.Contains("intent-cli 0.11.0-7b3800e-G606", doc, StringComparison.Ordinal);
        Assert.Contains("session-layer set --domain <domain> --team <team> --mode herdr-only --write --format json", doc, StringComparison.Ordinal);
        Assert.Contains("session-layer topology record --domain", doc, StringComparison.Ordinal);
        Assert.Contains("session-layer marker generate --domain <domain> --team <team> --file AGENTS.md --write --format json", doc, StringComparison.Ordinal);
        Assert.Contains("automation doctor --domain <domain> --team <team> --format json", doc, StringComparison.Ordinal);
        Assert.Contains("\"mode\": \"herdr-only\"", doc, StringComparison.Ordinal);
        Assert.Contains("\"verdict\": \"ready\"", doc, StringComparison.Ordinal);

        foreach (var role in new[] { "design", "orchestration", "implementation", "review" })
        {
            Assert.Contains($"`{role}`", doc, StringComparison.Ordinal);
        }

        var set = doc.IndexOf("session-layer set --domain", StringComparison.Ordinal);
        var record = doc.IndexOf("session-layer topology record --domain", StringComparison.Ordinal);
        var marker = doc.IndexOf("session-layer marker generate --domain", StringComparison.Ordinal);
        var doctor = doc.IndexOf("automation doctor --domain", StringComparison.Ordinal);
        Assert.True(set < record && record < marker && marker < doctor, "New-team commands must remain set → record → marker → doctor.");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void GettingStartedPage_LinksAuthoritiesAndKeepsSwitchChecklistOutOfLine_G607(string language)
    {
        var doc = ReadDoc(language, GettingStartedPath);
        var generatedMarkerAnchor = language == "en"
            ? "visible-generated-mode-markers"
            : "可視な生成済み-mode-marker";
        var generatedMarkerHeading = language == "en"
            ? "#### Visible, generated mode markers"
            : "#### 可視な生成済み mode marker";

        Assert.Contains("02-project-start.md", doc, StringComparison.Ordinal);
        Assert.Contains("12-agent-message-orchestration.md#session-layer-switch-checklist", doc, StringComparison.Ordinal);
        Assert.Contains($"12-agent-message-orchestration.md#{generatedMarkerAnchor}", doc, StringComparison.Ordinal);
        Assert.Contains(generatedMarkerHeading, ReadDoc(language, "12-agent-message-orchestration.md"), StringComparison.Ordinal);
        Assert.Contains("05-implementation-loop.md", doc, StringComparison.Ordinal);
        Assert.Contains("06-review-next-slice-loop.md", doc, StringComparison.Ordinal);

        // A new-team guide links the existing-team migration authority instead
        // of copying either directional checklist into a second normative text.
        Assert.DoesNotContain("**agmsg → herdr-only**", doc, StringComparison.Ordinal);
        Assert.DoesNotContain("**herdr-only → agmsg**", doc, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DocumentationIndex_PresentsOrchestrationPathBeforeTimerLoopAlternatives_G607(string language)
    {
        var index = ReadDoc(language, "README.md");

        var projectStart = index.IndexOf("02-project-start.md", StringComparison.Ordinal);
        // The updated G608 front-door paragraph links 02a before the numbered
        // list. The list ordering is the contract this G607 guard protects.
        var gettingStarted = index.LastIndexOf(GettingStartedPath, StringComparison.Ordinal);
        var intents = index.IndexOf("03-intents.md", StringComparison.Ordinal);
        var packets = index.IndexOf("04-packets-issues.md", StringComparison.Ordinal);
        var contract = index.IndexOf("12-agent-message-orchestration.md", StringComparison.Ordinal);
        var implementationLoop = index.IndexOf("05-implementation-loop.md", StringComparison.Ordinal);
        var reviewLoop = index.IndexOf("06-review-next-slice-loop.md", StringComparison.Ordinal);

        Assert.True(projectStart < gettingStarted && gettingStarted < intents && intents < packets && packets < contract);
        Assert.True(contract < implementationLoop && implementationLoop < reviewLoop);
    }

    [Fact]
    public void RootReadme_OffersCollocatedFourThreadPromptAlongsideExistingPrompts_G607()
    {
        var root = File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "README.md"));

        Assert.Contains("Choose your onboarding pattern", root, StringComparison.Ordinal);
        Assert.Contains("Separate host × brand-new", root, StringComparison.Ordinal);
        Assert.Contains("Same repo × existing", root, StringComparison.Ordinal);
        Assert.Contains("PREVIEW** label is a maturity note", root, StringComparison.Ordinal);
        Assert.Contains("Timer-loop alternative", root, StringComparison.Ordinal);
    }

    private static string ReadDoc(string language, string path) =>
        File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, path));
}
