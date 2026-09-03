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
        "565530e5c965d55335790c9446ef0686988d14c8";
    private const string BuiltDisplayIdentity = "intent-cli 0.28.0-565530e-G769";
    private const string InstalledDisplayIdentity = "intent-cli 0.27.0-f43fbd1-G753";
    private const string StrandedUsage =
        "claim stranded";

    private static readonly (string Unit, string Pr, string Merge)[] Units =
    [
        ("G770", "#1680", "b111fc644dfca24b911c26eef6bad9c784ad6cd4"),
        ("G771", "#1682", "565530e5c965d55335790c9446ef0686988d14c8"),
    ];

    private static readonly (string Unit, string Merge)[] FirstParentCommits =
    [
        ("G754", "6ea81ac85e5fc104d5cd954766c916445f751183"),
        ("G755", "9a30e95accc9d92d56ba0bdb62b1974ec7ab8302"),
        ("G756", "ec261ec4c16454d122a3baec0d48393a4245f513"),
        ("G757", "071ccf2c988e6244633c0971c8098fbd31b17093"),
        ("G758", "145c5a43c031353a5e5ad4d7ea9eb3fb7365304c"),
        ("G759", "c6e6922e8ca89520465adfa8f69375eefd5d4fa6"),
        ("G760", "5d553b7a0aeecf8d9939080eada9772963fe35c8"),
        ("G761", "5a6e850412beb5cd515991b3486022e457726f6a"),
        ("G762", "ff11a355377fe2b1698cce1e14f39d8c79c20bd5"),
        ("G763", "6cc2b05127f7dc8c9080e425eb5af8e0e099ace7"),
        ("G764", "642a86626f95fe271be663fca9d79240a58e6fd7"),
        ("G765", "db5394d75e267e17606f9a5fb96b3607ec58b435"),
        ("G766", "7adb2b5cac8090865d19c864842dbed48ffab7d2"),
        ("G767", "4dcf1916a94dfb871a1249fd60a3a4569b0a032c"),
        ("G768", "af8b82c37c27ff319c7468084b8ac59590f887fb"),
        ("G769", "a92a53fda2f8901e49b0e60d5d7c00d5c2a6c324"),
        ("G770", "b111fc644dfca24b911c26eef6bad9c784ad6cd4"),
        ("G771", "565530e5c965d55335790c9446ef0686988d14c8"),
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

        Assert.Equal(18, listed.Length);
        Assert.DoesNotContain("- G752 —", notes, StringComparison.Ordinal);
        Assert.Contains("- G754 —", notes, StringComparison.Ordinal);
        Assert.Contains("- G771 —", notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void FirstParentAccountingIncludesTheCurrentRange(string language)
    {
        var notes = ReadNotes(language);

        Assert.Contains(
            "git rev-list --first-parent --reverse v0.27.0..565530e5c965d55335790c9446ef0686988d14c8",
            notes,
            StringComparison.Ordinal);
        Assert.Contains(
            "git rev-list --first-parent --count v0.27.0..565530e5c965d55335790c9446ef0686988d14c8",
            notes,
            StringComparison.Ordinal);
        Assert.Contains("18", notes, StringComparison.Ordinal);

        foreach (var commit in FirstParentCommits)
        {
            Assert.Contains(commit.Merge, notes, StringComparison.Ordinal);
        }

        Assert.Contains(language == "en" ? "separate release unit" : "release unit として", notes, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("74", notes, StringComparison.Ordinal);
        Assert.Contains("72", notes, StringComparison.Ordinal);
        Assert.Contains("104", notes, StringComparison.Ordinal);
        Assert.Contains("106", notes, StringComparison.Ordinal);
        Assert.Contains(StrandedUsage, notes, StringComparison.Ordinal);
        Assert.Contains("notify supervise liveness", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "unchanged" : "変わらない", notes, StringComparison.OrdinalIgnoreCase);
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

        Assert.Contains("G768", notes, StringComparison.Ordinal);
        Assert.Contains("G771", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "does not repair" : "repair しません",
            notes,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("9 unreadable", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("250 ms", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1.8 s", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "limitations" : "limitation", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesStayPrepareOnly(string language)
    {
        var notes = ReadNotes(language);

        Assert.Contains("PREPARED / NOT PUBLISHED", notes, StringComparison.Ordinal);
        Assert.Contains(
            "tag",
            notes,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GitHub Release", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("publish", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("post-release roll", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source", notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("G743", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionPolicyAndNoteInventoryAreExact()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        Assert.Equal(
            "{\n  \"stableVersion\": \"0.31.0\",\n  \"nextVersion\": \"0.31.1\"\n}\n",
            File.ReadAllText(Path.Combine(root, "eng", "version.json")));

        var policy = RepoVersionPolicySource.Read();
        Assert.Equal("0.31.0", policy.StableVersion);
        Assert.Equal("0.31.1", policy.NextVersion);

        foreach (var language in new[] { "en", "ja" })
        {
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.25.0.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.26.0.md")));
            Assert.False(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.26.1.md")));
            Assert.False(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.27.0.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.27.1.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.28.0.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.28.1.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.29.0.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.29.1.md")));
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
                ? "### Previous v0.28.0 release-prep evidence (retained in history only)"
                : "### previous v0.28.0 release-prep evidence (history のみ)",
            reference,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            language == "en"
                ? "### Next release readiness (v0.26.1)"
                : "### 次リリース準備(v0.26.1)",
            reference,
            StringComparison.Ordinal);
        Assert.Contains(InstalledDisplayIdentity, reference, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.28.0.md", reference, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.28.1.md", reference, StringComparison.Ordinal);
        Assert.DoesNotContain("release-notes-v0.27.0.md", reference, StringComparison.Ordinal);
        Assert.Contains("intent-cli 0.28.0-565530e-G769", reference, StringComparison.Ordinal);
        Assert.Contains(PreparedFunctionalHead, reference, StringComparison.Ordinal);
        Assert.Contains("104 usages", reference, StringComparison.Ordinal);
        Assert.Contains("106 usages", reference, StringComparison.Ordinal);
        Assert.Contains("111.5MB", reference, StringComparison.Ordinal);
        Assert.Contains("3.6 records/second", reference, StringComparison.Ordinal);
        Assert.Contains("12.00/hour", reference, StringComparison.Ordinal);

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
        RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.28.0.md"));

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
