using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G646 pins the operator-approved post-release transition. The preparation
/// line is minor, but its immediate follow-up is the next patch, not a skipped
/// minor line. Read the live policy so this guard survives the required roll.
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
        var policy = RepoVersionPolicySource.Read();

        Assert.Contains(
            language == "en"
                ? $"### Next release readiness (v{policy.NextVersion})"
                : $"### 次リリース準備(v{policy.NextVersion})",
            reference,
            StringComparison.Ordinal);
        Assert.Contains($"stableVersion {policy.StableVersion}", reference, StringComparison.Ordinal);
        Assert.Contains($"nextVersion {policy.NextVersion}", reference, StringComparison.Ordinal);
        Assert.DoesNotContain($"nextVersion → {NextPatch(policy.NextVersion)}", reference, StringComparison.Ordinal);
        Assert.DoesNotContain($"nextVersion → {NextMinor(policy.NextVersion)}", reference, StringComparison.Ordinal);
    }

    private static string NextPatch(string version)
    {
        var parsed = Version.Parse(version);
        return $"{parsed.Major}.{parsed.Minor}.{parsed.Build + 1}";
    }

    private static string NextMinor(string version)
    {
        var parsed = Version.Parse(version);
        return $"{parsed.Major}.{parsed.Minor + 1}.0";
    }
}
