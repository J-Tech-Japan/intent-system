using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G646 pins the operator-approved post-release transition for v0.15.0. The
/// preparation line is minor, but its immediate follow-up is the next patch,
/// not a skipped 0.16.0 line.
/// </summary>
public sealed class ReleaseNotesV0150DocsTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DeveloperReference_PostReleaseRollTargetsImmediatePatchAfterV0150_G646(string language)
    {
        var path = Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, "09-developer-reference.md");
        var reference = File.ReadAllText(path);

        Assert.Contains(
            language == "en" ? "### Next release readiness (v0.15.0)" : "### 次リリース準備(v0.15.0)",
            reference,
            StringComparison.Ordinal);
        Assert.Contains("stableVersion → 0.15.0", reference, StringComparison.Ordinal);
        Assert.Contains("nextVersion → 0.15.1", reference, StringComparison.Ordinal);
        Assert.DoesNotContain("nextVersion → 0.16.0", reference, StringComparison.Ordinal);
    }
}
