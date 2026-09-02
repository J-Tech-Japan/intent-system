using System.Text.Json;
using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G772: the v0.28.0 preparation is a measured, prepare-only release line
/// with all first-parent units and both language mirrors accounted for.
/// </summary>
public sealed class ReleaseNotesV0280DocsTests
{
    private const string PreparedHead =
        "565530e5c965d55335790c9446ef0686988d14c8";
    private const string NormalCleanIdentity = "intent-cli 0.27.1-565530e-G769";
    private const string PreparedReleaseIdentity = "intent-cli 0.28.0-565530e-G769";
    private const string TaggedIdentity = "intent-cli 0.27.0-f43fbd1-G753";
    private const string InstalledIdentity = "intent-cli 0.27.1-5d553b7-G756";

    private static readonly (string Unit, string Pr, string Issue, string Merge)[] Units =
    [
        ("G754", "#1641", "#1640", "6ea81ac85e5fc104d5cd954766c916445f751183"),
        ("G755", "#1643", "#1642", "9a30e95accc9d92d56ba0bdb62b1974ec7ab8302"),
        ("G756", "#1646", "#1644", "ec261ec4c16454d122a3baec0d48393a4245f513"),
        ("G757", "#1648", "#1645", "071ccf2c988e6244633c0971c8098fbd31b17093"),
        ("G758", "#1652", "#1647", "145c5a43c031353a5e5ad4d7ea9eb3fb7365304c"),
        ("G759", "#1650", "#1649", "c6e6922e8ca89520465adfa8f69375eefd5d4fa6"),
        ("G760", "#1653", "#1651", "5d553b7a0aeecf8d9939080eada9772963fe35c8"),
        ("G761", "#1660", "#1655", "5a6e850412beb5cd515991b3486022e457726f6a"),
        ("G762", "#1659", "#1657", "ff11a355377fe2b1698cce1e14f39d8c79c20bd5"),
        ("G763", "#1667", "#1663", "6cc2b05127f7dc8c9080e425eb5af8e0e099ace7"),
        ("G764", "#1666", "#1664", "642a86626f95fe271be663fca9d79240a58e6fd7"),
        ("G765", "#1670", "#1665", "db5394d75e267e17606f9a5fb96b3607ec58b435"),
        ("G766", "#1671", "#1669", "7adb2b5cac8090865d19c864842dbed48ffab7d2"),
        ("G767", "#1673", "#1672", "4dcf1916a94dfb871a1249fd60a3a4569b0a032c"),
        ("G768", "#1676", "#1674", "af8b82c37c27ff319c7468084b8ac59590f887fb"),
        ("G769", "#1677", "#1675", "a92a53fda2f8901e49b0e60d5d7c00d5c2a6c324"),
        ("G770", "#1680", "#1678", "b111fc644dfca24b911c26eef6bad9c784ad6cd4"),
        ("G771", "#1682", "#1681", "565530e5c965d55335790c9446ef0686988d14c8"),
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCoverExactlyTheEighteenGitDerivedUnits(string language)
    {
        var notes = ReadNotes(language);
        var listed = Regex.Matches(notes, @"(?m)^- (G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(Units.Select(unit => unit.Unit), listed);
        Assert.Equal(18, listed.Length);

        foreach (var unit in Units)
        {
            var entry = FindEntry(notes, unit.Unit);
            Assert.NotEmpty(entry);
            Assert.Contains($"PR {unit.Pr} / issue {unit.Issue};", entry, StringComparison.Ordinal);
            Assert.Contains($"merge commit `{unit.Merge}`", entry, StringComparison.Ordinal);
            Assert.Contains("Operator-observable outcome", entry, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void EnglishAndJapaneseNotesHaveIdenticalUnitIssueAndMergeInventory()
    {
        var expected = Units
            .Select(unit => (unit.Unit, unit.Pr, unit.Issue, unit.Merge))
            .ToArray();
        var english = ParseInventory(ReadNotes("en"));
        var japanese = ParseInventory(ReadNotes("ja"));

        Assert.Equal(expected, english);
        Assert.Equal(expected, japanese);
        Assert.Equal(english, japanese);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesPinTheExactFirstParentRangeAndAllCommits(string language)
    {
        var notes = ReadNotes(language);
        const string range = "v0.27.0..565530e5c965d55335790c9446ef0686988d14c8";

        Assert.Contains($"git rev-list --first-parent --reverse {range}", notes, StringComparison.Ordinal);
        Assert.Contains($"git rev-list --first-parent --count {range}", notes, StringComparison.Ordinal);
        Assert.Contains("18", notes, StringComparison.Ordinal);

        foreach (var unit in Units)
        {
            Assert.Contains(unit.Merge, notes, StringComparison.Ordinal);
        }

        Assert.Contains("release inventory", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "separate release unit" : "release unit として", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesPinMeasuredIdentitiesAndOnlyTheTwoNewRoutes(string language)
    {
        var notes = ReadNotes(language);

        Assert.Contains(PreparedHead, notes, StringComparison.Ordinal);
        Assert.Contains(NormalCleanIdentity, notes, StringComparison.Ordinal);
        Assert.Contains(PreparedReleaseIdentity, notes, StringComparison.Ordinal);
        Assert.Contains(TaggedIdentity, notes, StringComparison.Ordinal);
        Assert.Contains(InstalledIdentity, notes, StringComparison.Ordinal);
        Assert.Contains("release.yml", notes, StringComparison.Ordinal);
        Assert.Contains("v0.28.0", notes, StringComparison.Ordinal);
        Assert.Contains("VERSION", notes, StringComparison.Ordinal);
        Assert.Contains("RAW", notes, StringComparison.Ordinal);
        Assert.Contains("eng/version.json", notes, StringComparison.Ordinal);
        Assert.Contains("local builds", notes, StringComparison.Ordinal);
        Assert.Contains("dry runs", notes, StringComparison.Ordinal);
        Assert.Contains("104 usages", notes, StringComparison.Ordinal);
        Assert.Contains("106 usages", notes, StringComparison.Ordinal);
        Assert.Contains("claim stranded", notes, StringComparison.Ordinal);
        Assert.Contains("notify supervise liveness", notes, StringComparison.Ordinal);
        Assert.Contains("absent", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("present", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "no removal" : "removal はありません", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("automation", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("worker", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("state-doctor", notes, StringComparison.Ordinal);
        Assert.Contains("closeout-drift-check", notes, StringComparison.Ordinal);
        Assert.Contains("byte-identical", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesPinTheTruthfulG768AndG771Limitations(string language)
    {
        var notes = ReadNotes(language);

        Assert.Contains("G768", notes, StringComparison.Ordinal);
        Assert.Contains("G771", notes, StringComparison.Ordinal);
        Assert.Contains("9 unreadable", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("250 ms", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1.8 s", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "does not repair" : "repair しません", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "make deletion more reliable" : "reliable に", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#1679", notes, StringComparison.Ordinal);
        Assert.Contains("#1662", notes, StringComparison.Ordinal);
        Assert.Contains("#1661", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionPolicyAndNoteInventoryAreExact()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var policyPath = Path.Combine(root, "eng", "version.json");

        Assert.Equal(
            "{\n  \"stableVersion\": \"0.30.0\",\n  \"nextVersion\": \"0.30.1\"\n}\n",
            File.ReadAllText(policyPath));

        var policy = RepoVersionPolicySource.Read();
        Assert.Equal("0.30.0", policy.StableVersion);
        Assert.Equal("0.30.1", policy.NextVersion);

        foreach (var language in new[] { "en", "ja" })
        {
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.28.0.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.28.1.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.29.0.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.29.1.md")));
            Assert.False(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.27.0.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.27.1.md")));

            var stub = File.ReadAllText(Path.Combine(root, "docs", language, "release-notes-v0.28.1.md"));
            Assert.Contains("DRAFT", stub, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("replaceable", stub, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(language == "en" ? "not a changelog" : "changelog ではありません", stub, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("- G", stub, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReadinessMirrorsPinTheCurrentPlaceholderAndBoundary(string language)
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var reference = File.ReadAllText(Path.Combine(
            root, "docs", language, "09-developer-reference.md"));

        Assert.Contains(
            language == "en"
                ? "Previous v0.28.0 release-prep evidence (retained in history only)"
                : "previous v0.28.0 release-prep evidence (history のみ)",
            reference,
            StringComparison.Ordinal);
        Assert.Contains("0.28.0", reference, StringComparison.Ordinal);
        Assert.Contains("0.28.1", reference, StringComparison.Ordinal);
        Assert.Contains("replaceable", reference, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(PreparedReleaseIdentity, reference, StringComparison.Ordinal);
        Assert.Contains(TaggedIdentity, reference, StringComparison.Ordinal);
        Assert.Contains(InstalledIdentity, reference, StringComparison.Ordinal);
        Assert.Contains("104 usages", reference, StringComparison.Ordinal);
        Assert.Contains("106 usages", reference, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.28.0.md", reference, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.28.1.md", reference, StringComparison.Ordinal);
        Assert.DoesNotContain("release-notes-v0.27.0.md", reference, StringComparison.Ordinal);
        Assert.Contains("canonical-unavailable", reference, StringComparison.Ordinal);
        Assert.Contains("none/unheld", reference, StringComparison.Ordinal);
        Assert.Contains("missing-target-declaration", reference, StringComparison.Ordinal);
        Assert.Contains("Target paths:", reference, StringComparison.Ordinal);
        Assert.Contains("execution-unit:G772", reference, StringComparison.Ordinal);
        Assert.Contains("child", reference, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("G768", reference, StringComparison.Ordinal);
        Assert.Contains("G771", reference, StringComparison.Ordinal);
        Assert.Contains("#1679", reference, StringComparison.Ordinal);
        Assert.Contains("#1662", reference, StringComparison.Ordinal);
        Assert.Contains("#1661", reference, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesAndReadinessRemainPrepareOnly(string language)
    {
        var notes = ReadNotes(language);
        var reference = File.ReadAllText(Path.Combine(
            RepoVersionPolicySource.RepoRoot(), "docs", language, "09-developer-reference.md"));

        foreach (var text in new[] { notes, reference })
        {
            Assert.Contains("0.28.1", text, StringComparison.Ordinal);
            Assert.Contains("placeholder", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("tag", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("publish", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("product", text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("release-notes-v0.27.0.md", notes, StringComparison.Ordinal);
        var stub = File.ReadAllText(Path.Combine(
            RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.28.1.md"));
        Assert.Contains(language == "en" ? "not a changelog" : "changelog ではありません", stub, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindEntry(string notes, string unit)
    {
        var match = Regex.Match(
            notes,
            $@"(?ms)^- {Regex.Escape(unit)} —.*?(?=^- |^## |\z)");
        return match.Success ? match.Value : string.Empty;
    }

    private static IReadOnlyList<(string Unit, string Pr, string Issue, string Merge)> ParseInventory(string notes) =>
        Regex.Matches(
                notes,
                @"(?ms)^- (G\d+) — PR (#\d+) / issue (#\d+); merge commit `([0-9a-f]{40})`.*?(?=^- |^## |\z)")
            .Select(match => (
                match.Groups[1].Value,
                match.Groups[2].Value,
                match.Groups[3].Value,
                match.Groups[4].Value))
            .ToArray();

    private static string ReadNotes(string language) => File.ReadAllText(Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.28.0.md"));
}
