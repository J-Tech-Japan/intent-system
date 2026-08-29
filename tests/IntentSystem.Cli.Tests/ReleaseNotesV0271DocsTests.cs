using System.Security.Cryptography;
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
    private const string ShippedIdentity = "intent-cli 0.27.0-f43fbd1-G753";
    private const string ShippedHead = "f43fbd19f6e0cb7fa284ccd2f89d2932f63ca330";
    private const string StaleHostCheckout = "35c6d96a";
    private const string HostOriginMain = "209b1369";

    [Fact]
    public void VersionPolicyAndPlaceholderNotesAreExact()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var policyPath = Path.Combine(root, "eng", "version.json");

        Assert.Equal(
            "{\n  \"stableVersion\": \"0.27.0\",\n  \"nextVersion\": \"0.27.1\"\n}\n",
            File.ReadAllText(policyPath));

        var policy = RepoVersionPolicySource.Read();
        Assert.Equal("0.27.0", policy.StableVersion);
        Assert.Equal("0.27.1", policy.NextVersion);

        foreach (var language in new[] { "en", "ja" })
        {
            var stub = File.ReadAllText(Path.Combine(
                root, "docs", language, "release-notes-v0.27.1.md"));

            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.27.0.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.27.1.md")));
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
    public void ReadinessPinsShippedIdentityAndG725EvidenceBoundary(string language)
    {
        var readiness = ReadCurrentReadiness(language);
        var compact = Regex.Replace(readiness, @"\s+", " ");

        Assert.Contains(ShippedIdentity, readiness, StringComparison.Ordinal);
        Assert.Contains(ShippedHead, readiness, StringComparison.Ordinal);
        Assert.Contains("stableVersion", readiness, StringComparison.Ordinal);
        Assert.Contains("0.27.1", readiness, StringComparison.Ordinal);
        Assert.Contains("replaceable placeholder", readiness, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("release-notes-v0.27.1.md", readiness, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.27.0.md", readiness, StringComparison.Ordinal);
        Assert.Contains("byte-identical", readiness, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kind=version-roll-required", readiness, StringComparison.Ordinal);
        Assert.Contains("is_informational=false", readiness, StringComparison.Ordinal);
        Assert.Contains("released_version=0.27.0", readiness, StringComparison.Ordinal);
        Assert.Contains("expected_stable_version=0.27.0", readiness, StringComparison.Ordinal);
        Assert.Contains("expected_next_version=0.27.1", readiness, StringComparison.Ordinal);
        Assert.Contains("stalled=true", readiness, StringComparison.Ordinal);
        Assert.Contains(StaleHostCheckout, readiness, StringComparison.Ordinal);
        Assert.Contains(HostOriginMain, readiness, StringComparison.Ordinal);
        Assert.Contains("next-action=wait", readiness, StringComparison.Ordinal);
        Assert.Contains("execution-unit:G754", readiness, StringComparison.Ordinal);
        Assert.Contains("unheld", readiness, StringComparison.Ordinal);
        Assert.Contains("G725", readiness, StringComparison.Ordinal);
        Assert.Contains("HOST DUTY REQUEST", readiness, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "no `version-roll-required` item" : "`version-roll-required` item はなく",
            compact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en" ? "non-evidence" : "evidence ではなく",
            compact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en" ? "does not prove the roll" : "roll の証明にもなりません",
            compact,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShippedV0270NoteBytesRemainPinned()
    {
        var root = RepoVersionPolicySource.RepoRoot();

        Assert.Equal(
            "1b810139d4775fa4306bc571c150e49c4e882464c8228b82b65ed3a0ad0978cb",
            Sha256(Path.Combine(root, "docs", "en", "release-notes-v0.27.0.md")));
        Assert.Equal(
            "6973f0438bd0bb208fc22e6a6d31596ee455b8284c7edc345687e3f3016e414d",
            Sha256(Path.Combine(root, "docs", "ja", "release-notes-v0.27.0.md")));
    }

    private static string ReadCurrentReadiness(string language)
    {
        var content = File.ReadAllText(Path.Combine(
            RepoVersionPolicySource.RepoRoot(), "docs", language, "09-developer-reference.md"));
        var heading = language == "en"
            ? "### Next release readiness (v0.27.1)"
            : "### 次リリース準備(v0.27.1)";
        var endHeading = language == "en"
            ? "### Previous v0.27.0 release-prep evidence"
            : "### 以前の v0.27.0 release-prep evidence";
        var start = content.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing current readiness heading in {language}.");
        var end = content.IndexOf(endHeading, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing prior-readiness boundary in {language}.");
        return content[start..end];
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
