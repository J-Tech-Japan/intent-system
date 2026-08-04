using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G609: the 0.11.1 release-prep is documentation-only and must describe the
/// exact post-0.11.0 pair without retargeting version policy.
/// </summary>
public sealed class ReleaseNotesV0111DocsTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReleaseNotes_CoverExactlyG607AndG608WithVerifiedMerges_G609(string language)
    {
        var notes = Read(language, "release-notes-v0.11.1.md");

        foreach (var text in new[] { "G607", "#1318", "764905194ee1", "G608", "#1320", "a138e32b82a7" })
        {
            Assert.Contains(text, notes, StringComparison.Ordinal);
        }

        Assert.Contains("release-notes-v0.11.0.md", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("DRAFT", notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stub", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReleaseNotes_ExplainTheBehaviourNeutralPatchAndPrepareOnlyBoundary_G609(string language)
    {
        var notes = Read(language, "release-notes-v0.11.1.md");

        foreach (var command in new[] { "GuideModelCommand", "GuideOnboardingCommand", "GuideCommandsListCommand" })
        {
            Assert.Contains(command, notes, StringComparison.Ordinal);
        }

        Assert.Contains("0.11.1", notes, StringComparison.Ordinal);
        Assert.Contains("JTechJapan.IntentSystem.Cli --version 0.11.1", notes, StringComparison.Ordinal);
        Assert.Contains("releases/tag/v0.11.1", notes, StringComparison.Ordinal);
        Assert.Contains("prepare-only", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("command surface", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleasedPatchNotesRemainHistoricalAfterTheVersionRoll_G609()
    {
        foreach (var language in new[] { "en", "ja" })
        {
            var notes = Read(language, "release-notes-v0.11.1.md");
            Assert.Contains("0.11.1", notes, StringComparison.Ordinal);
            Assert.Contains("G607", notes, StringComparison.Ordinal);
            Assert.Contains("G608", notes, StringComparison.Ordinal);
            Assert.DoesNotContain("DRAFT", notes, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string Read(string language, string path) =>
        File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, path));
}
