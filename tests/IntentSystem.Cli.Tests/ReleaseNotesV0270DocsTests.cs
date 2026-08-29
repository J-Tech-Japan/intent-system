using System.Security.Cryptography;
using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G753/G754: frozen v0.27.0 release-prep evidence remains in lockstep while
/// the v0.27.1 post-release roll updates the current readiness mirrors.
/// </summary>
public sealed class ReleaseNotesV0270DocsTests
{
    private const string PreparedFunctionalHead =
        "bb9754859ac8055adbd504f294145b7494668c1a";
    private const string BuiltDisplayIdentity = "intent-cli 0.26.0-bb97548-G751";
    private const string InstalledDisplayIdentity = "intent-cli 0.26.0-93f07f8-G749";
    private const string RepairUsage =
        "notify supervise repair-cycle-history --domain <d> --team <t> [--dry-run|--write] [--format markdown|json]";

    private static readonly (string Unit, string Pr, string Merge)[] Units =
    [
        ("G750", "#1634", "b525191a24e361419b03f77e15e659110a22c395"),
        ("G751", "#1635", "bb9754859ac8055adbd504f294145b7494668c1a"),
    ];

    private static readonly (string Unit, string Merge)[] FirstParentCommits =
    [
        ("G750", "b525191a24e361419b03f77e15e659110a22c395"),
        ("G751", "bb9754859ac8055adbd504f294145b7494668c1a"),
        ("G752", "086344540d70a052555502971fa968aff6a252ac"),
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCoverExactlyTheTwoReleaseUnits(string language)
    {
        var notes = ReadNotes(language);
        var listed = Regex.Matches(notes, @"(?m)^- (G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(Units.Select(unit => unit.Unit), listed);
        Assert.Equal(2, listed.Length);
        Assert.DoesNotContain("- G752 —", notes, StringComparison.Ordinal);

        foreach (var unit in Units)
        {
            var entry = FindEntry(notes, unit.Unit);
            Assert.NotEmpty(entry);
            Assert.Contains($"PR {unit.Pr};", entry, StringComparison.Ordinal);
            Assert.Contains("merge commit", entry, StringComparison.Ordinal);
            Assert.Contains(unit.Merge, entry, StringComparison.Ordinal);
            Assert.Contains("Operator-observable outcome", entry, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void FirstParentAccountingIncludesG752RollWithoutCountingIt(string language)
    {
        var notes = ReadNotes(language);

        Assert.Contains(
            "git rev-list --first-parent --reverse v0.26.0..086344540d70a052555502971fa968aff6a252ac",
            notes,
            StringComparison.Ordinal);
        Assert.Contains(
            "git rev-list --first-parent --count v0.26.0..086344540d70a052555502971fa968aff6a252ac",
            notes,
            StringComparison.Ordinal);
        Assert.Contains("# 3", notes, StringComparison.Ordinal);

        foreach (var commit in FirstParentCommits)
        {
            Assert.Contains(commit.Merge, notes, StringComparison.Ordinal);
        }

        Assert.Contains("G752 post-v0.26.0 version roll", notes, StringComparison.Ordinal);
        Assert.Contains("not a release unit", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("classified only", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en"
                ? "Therefore the release inventory is exactly G750 and G751"
                : "したがって release inventory は G750、G751 の二つだけです",
            notes,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void OwnBuildIdentityAndMeasuredSurfaceDifferenceArePinned(string language)
    {
        var notes = ReadNotes(language);

        Assert.Contains(PreparedFunctionalHead, notes, StringComparison.Ordinal);
        Assert.Contains(BuiltDisplayIdentity, notes, StringComparison.Ordinal);
        Assert.Contains(InstalledDisplayIdentity, notes, StringComparison.Ordinal);
        Assert.Contains("32", notes, StringComparison.Ordinal);
        Assert.Contains("71", notes, StringComparison.Ordinal);
        Assert.Contains("72", notes, StringComparison.Ordinal);
        Assert.Contains("103", notes, StringComparison.Ordinal);
        Assert.Contains("104", notes, StringComparison.Ordinal);
        Assert.Contains("notify supervise repair-cycle-history", notes, StringComparison.Ordinal);
        Assert.Contains(RepairUsage, notes, StringComparison.Ordinal);
        Assert.Contains(
            "invalid-notification: Unknown argument 'repair-cycle-history'.",
            notes,
            StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "no\nremoval" : "removal は",
            notes,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("automation", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("claim", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("worker", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("state-doctor", notes, StringComparison.Ordinal);
        Assert.Contains("closeout-drift-check", notes, StringComparison.Ordinal);
        Assert.Contains("byte-identical", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void HonestThreeUnitChainCarriesAttributedMeasurements(string language)
    {
        var notes = ReadNotes(language);

        Assert.Contains("G744", notes, StringComparison.Ordinal);
        Assert.Contains("G750", notes, StringComparison.Ordinal);
        Assert.Contains("G751", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "did not reduce" : "量は減っていません",
            notes,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("111.5MB", notes, StringComparison.Ordinal);
        Assert.Contains("100MB", notes, StringComparison.Ordinal);
        Assert.Contains("3.6 records/second", notes, StringComparison.Ordinal);
        Assert.Contains("12.00/hour", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "attributed measurements" : "source を付けた measurement",
            notes,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesStayPrepareOnly(string language)
    {
        var notes = ReadNotes(language);

        Assert.Contains("PREPARED / NOT PUBLISHED", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "no tag" : "tag",
            notes,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GitHub Release", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("package publish", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("post-release roll", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source runtime", notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("G743", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionPolicyAndNoteInventoryAreExact()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        Assert.Equal(
            "{\n  \"stableVersion\": \"0.27.0\",\n  \"nextVersion\": \"0.27.1\"\n}\n",
            File.ReadAllText(Path.Combine(root, "eng", "version.json")));

        var policy = RepoVersionPolicySource.Read();
        Assert.Equal("0.27.0", policy.StableVersion);
        Assert.Equal("0.27.1", policy.NextVersion);

        foreach (var language in new[] { "en", "ja" })
        {
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.25.0.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.26.0.md")));
            Assert.False(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.26.1.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.27.0.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.27.1.md")));
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReadinessMirrorsTheMeasuredCurrentLine(string language)
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var reference = File.ReadAllText(Path.Combine(
            root, "docs", language, "09-developer-reference.md"));

        Assert.Contains(
            language == "en"
                ? "### Next release readiness (v0.27.1)"
                : "### 次リリース準備(v0.27.1)",
            reference,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            language == "en"
                ? "### Next release readiness (v0.26.1)"
                : "### 次リリース準備(v0.26.1)",
            reference,
            StringComparison.Ordinal);
        Assert.Contains(InstalledDisplayIdentity, reference, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.27.0.md", reference, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.27.1.md", reference, StringComparison.Ordinal);
        Assert.Contains("intent-cli 0.27.0-f43fbd1-G753", reference, StringComparison.Ordinal);
        Assert.Contains(PreparedFunctionalHead, reference, StringComparison.Ordinal);
        Assert.Contains("104 usages", reference, StringComparison.Ordinal);
        Assert.Contains("111.5MB", reference, StringComparison.Ordinal);
        Assert.Contains("3.6 records/second", reference, StringComparison.Ordinal);
        Assert.Contains("12.00/hour", reference, StringComparison.Ordinal);

        foreach (var commit in FirstParentCommits)
        {
            Assert.Contains(commit.Merge, reference, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ShippedV0260NoteBytesRemainPinned()
    {
        var root = RepoVersionPolicySource.RepoRoot();

        Assert.Equal(
            "b385042d2276067120d1e9412b3a65cbf0d725cee63a93940736ea11472f4cbe",
            Sha256(Path.Combine(root, "docs", "en", "release-notes-v0.26.0.md")));
        Assert.Equal(
            "11a859a307bf2d07c239e7c30f7db95ee78b57f72a3415fe0d047f8ce68e9f9f",
            Sha256(Path.Combine(root, "docs", "ja", "release-notes-v0.26.0.md")));
    }

    private static string FindEntry(string notes, string unit)
    {
        var match = Regex.Match(
            notes,
            $@"(?ms)^- {Regex.Escape(unit)} —.*?(?=^- |^## |\z)");
        return match.Success ? match.Value : string.Empty;
    }

    private static string ReadNotes(string language) => File.ReadAllText(Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "docs", language, $"release-notes-v0.27.0.md"));

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
