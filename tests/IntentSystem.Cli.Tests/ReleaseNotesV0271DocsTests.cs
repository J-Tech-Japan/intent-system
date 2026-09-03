using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G754: the post-release roll records the shipped v0.27.0 identity, creates
/// replaceable v0.27.1 placeholders, and keeps the G725 evidence boundary
/// explicit without changing the detector.
/// </summary>
public sealed class ReleaseNotesV0271DocsTests
{

    [Fact]
    public void VersionPolicyAndPlaceholderNotesAreExact()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var policyPath = Path.Combine(root, "eng", "version.json");

        Assert.Equal(
            "{\n  \"stableVersion\": \"0.31.0\",\n  \"nextVersion\": \"0.31.1\"\n}\n",
            File.ReadAllText(policyPath));

        var policy = RepoVersionPolicySource.Read();
        Assert.Equal("0.31.0", policy.StableVersion);
        Assert.Equal("0.31.1", policy.NextVersion);

        foreach (var language in new[] { "en", "ja" })
        {
            var stub = File.ReadAllText(Path.Combine(
                root, "docs", language, "release-notes-v0.27.1.md"));

            Assert.False(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.27.0.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.27.1.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.28.0.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.28.1.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.29.0.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.29.1.md")));
            Assert.DoesNotContain("- G", stub, StringComparison.Ordinal);

            if (language == "en")
            {
                Assert.Contains("DRAFT / UNRELEASED", stub, StringComparison.Ordinal);
                Assert.Contains("replaceable planning scaffold", stub, StringComparison.Ordinal);
                Assert.Contains("not a changelog", stub, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("release-prep packet will replace", stub, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("DRAFT / 未リリース", stub, StringComparison.Ordinal);
                Assert.Contains("replaceable planning scaffold", stub, StringComparison.Ordinal);
                Assert.Contains("changelog ではありません", stub, StringComparison.Ordinal);
                Assert.Contains("replace", stub, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReadinessPinsCurrentPreparedLineAndClaimBoundary(string language)
    {
        var readiness = ReadCurrentReadiness(language);
        Assert.Contains("intent-cli 0.28.0-565530e-G769", readiness, StringComparison.Ordinal);
        Assert.Contains("565530e5c965d55335790c9446ef0686988d14c8", readiness, StringComparison.Ordinal);
        Assert.Contains("stableVersion", readiness, StringComparison.Ordinal);
        Assert.Contains("0.28.1", readiness, StringComparison.Ordinal);
        Assert.Contains("replaceable", readiness, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("release-notes-v0.28.0.md", readiness, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.28.1.md", readiness, StringComparison.Ordinal);
        Assert.DoesNotContain("release-notes-v0.27.0.md", readiness, StringComparison.Ordinal);
        Assert.Contains("canonical-unavailable", readiness, StringComparison.Ordinal);
        Assert.Contains("none/unheld", readiness, StringComparison.Ordinal);
        Assert.Contains("missing-target-declaration", readiness, StringComparison.Ordinal);
        Assert.Contains("G768", readiness, StringComparison.Ordinal);
        Assert.Contains("G771", readiness, StringComparison.Ordinal);
    }

    [Fact]
    public void RetiredV0270NoteMirrorsAreAbsent()
    {
        var root = RepoVersionPolicySource.RepoRoot();

        Assert.False(File.Exists(Path.Combine(root, "docs", "en", "release-notes-v0.27.0.md")));
        Assert.False(File.Exists(Path.Combine(root, "docs", "ja", "release-notes-v0.27.0.md")));
    }

    private static string ReadCurrentReadiness(string language)
    {
        var content = File.ReadAllText(Path.Combine(
            RepoVersionPolicySource.RepoRoot(), "docs", language, "09-developer-reference.md"));
        var heading = language == "en"
            ? "### Previous v0.28.0 release-prep evidence (retained in history only)"
            : "### previous v0.28.0 release-prep evidence (history のみ)";
        var endHeading = language == "en"
            ? "### Retired v0.27.0 release-prep evidence (retained in history only)"
            : "### retired v0.27.0 release-prep evidence (history のみ)";
        var start = content.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing current readiness heading in {language}.");
        var end = content.IndexOf(endHeading, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing prior-readiness boundary in {language}.");
        return content[start..end];
    }

}
