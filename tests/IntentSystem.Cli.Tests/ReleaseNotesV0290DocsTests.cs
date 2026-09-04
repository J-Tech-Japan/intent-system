using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G778: v0.29.0 is a measured, prepare-only release line. These guards keep
/// its mirrored inventory and three identity statements auditable.
/// </summary>
public sealed class ReleaseNotesV0290DocsTests
{
    private const string Base = "65e02d86d5e9e415d1fe934b0d5e8bad87af9ccf";
    private const string NormalBaseIdentity = "intent-cli 0.28.1-65e02d8-G772";
    private const string ExplicitReleaseIdentity = "intent-cli 0.29.0-65e02d8-G772";

    private static readonly (string Unit, string Pr, string Issue, string Merge)[] Units =
    [
        ("G773", "#1686", "#1685", "370cfd3ad6b008503fc38d11822a31617949c372"),
        ("G774", "#1690", "#1687", "9f124d86b0cc76366d2bb8cfcdcffed17a9eca66"),
        ("G775", "#1691", "#1688", "75216283875b08ade3d100de7ddabe3fad0bd21c"),
        ("G776", "#1692", "#1689", "b766f2d0961c665a2d6216c7ed24755556560626"),
        ("G777", "#1694", "#1693", "65e02d86d5e9e415d1fe934b0d5e8bad87af9ccf"),
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCoverExactlyTheFiveGitDerivedUnits(string language)
    {
        var notes = ReadNotes(language);
        var listed = Regex.Matches(notes, @"(?m)^- (G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(Units.Select(unit => unit.Unit), listed);
        Assert.Equal(5, listed.Length);

        foreach (var unit in Units)
        {
            var entry = FindEntry(notes, unit.Unit);
            Assert.Contains($"PR {unit.Pr} / issue {unit.Issue};", entry, StringComparison.Ordinal);
            Assert.Contains($"merge commit `{unit.Merge}`", entry, StringComparison.Ordinal);
            Assert.Contains("Operator-observable outcome", entry, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EnglishAndJapaneseMirrorsHaveIdenticalUnitPrIssueAndMergeTuples()
    {
        var english = ParseInventory(ReadNotes("en"));
        var japanese = ParseInventory(ReadNotes("ja"));

        Assert.Equal(Units, english);
        Assert.Equal(english, japanese);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesPinTheThreeMeasuredIdentitiesAndMinorDecision(string language)
    {
        var notes = ReadNotes(language);
        var normalized = Regex.Replace(notes, @"\s+", " ");

        Assert.Contains(Base, notes, StringComparison.Ordinal);
        Assert.Contains("dotnet build IntentSystem.sln --configuration Release", notes, StringComparison.Ordinal);
        Assert.Contains("-p:Version=0.29.0", notes, StringComparison.Ordinal);
        Assert.Contains(NormalBaseIdentity, notes, StringComparison.Ordinal);
        Assert.Contains(ExplicitReleaseIdentity, notes, StringComparison.Ordinal);
        Assert.Contains("release.yml", notes, StringComparison.Ordinal);
        Assert.Contains("RAW", notes, StringComparison.Ordinal);
        Assert.Contains("VERSION", notes, StringComparison.Ordinal);
        Assert.Contains("eng/version.json", notes, StringComparison.Ordinal);
        Assert.Contains("local builds", notes, StringComparison.Ordinal);
        Assert.Contains("dry runs", notes, StringComparison.Ordinal);
        Assert.Contains("Unknown argument 'repair-unreadable'", notes, StringComparison.Ordinal);
        Assert.Contains("notify supervise repair-unreadable", notes, StringComparison.Ordinal);
        Assert.Contains("option-level", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--wake-command", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "not counted" : "数えません", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "**not** v0.29.0" : "**v0.29.0 ではありません**", normalized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesPinEveryFirstParentCommitAndClassification(string language)
    {
        var notes = ReadNotes(language);
        const string range = "v0.28.0..65e02d86d5e9e415d1fe934b0d5e8bad87af9ccf";

        Assert.Contains($"git rev-list --first-parent --reverse {range}", notes, StringComparison.Ordinal);
        Assert.Contains($"git rev-list --first-parent --count {range}", notes, StringComparison.Ordinal);
        Assert.Contains("# 5", notes, StringComparison.Ordinal);

        foreach (var unit in Units)
        {
            Assert.Contains(unit.Merge, notes, StringComparison.Ordinal);
            Assert.Contains($"{unit.Unit} / PR {unit.Pr} / issue {unit.Issue}", notes, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesPinTheThreeTruthfulnessBoundaries(string language)
    {
        var notes = ReadNotes(language);
        var normalized = Regex.Replace(notes, @"\s+", " ");

        Assert.Contains("repair-unreadable", notes, StringComparison.Ordinal);
        Assert.Contains("verbatim", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "no reconstruction claim" : "reconstruction を claim せず", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "never automatic" : "automatic ではなく", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "never performed on read" : "read 時に", normalized, StringComparison.Ordinal);
        Assert.Contains("zero changed bytes", notes, StringComparison.Ordinal);
        Assert.Contains("9 records", notes, StringComparison.Ordinal);
        Assert.Contains("6279ad14", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "not a fleet-cleanliness claim" : "fleet-cleanliness claim ではありません", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionPolicyAndPreparedNotesAreExact()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        Assert.Equal(
            "{\n  \"stableVersion\": \"0.32.0\",\n  \"nextVersion\": \"0.32.1\"\n}\n",
            File.ReadAllText(Path.Combine(root, "eng", "version.json")));

        var policy = RepoVersionPolicySource.Read();
        Assert.Equal("0.32.0", policy.StableVersion);
        Assert.Equal("0.32.1", policy.NextVersion);

        foreach (var language in new[] { "en", "ja" })
        {
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.29.0.md")));
            var stub = File.ReadAllText(Path.Combine(root, "docs", language, "release-notes-v0.29.1.md"));
            Assert.Contains("DRAFT", stub, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("replaceable", stub, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(language == "en" ? "not a changelog" : "changelog ではありません", stub, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("- G", stub, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReadinessMirrorsDocumentTheCurrentPreparedLine(string language)
    {
        var reference = File.ReadAllText(Path.Combine(
            RepoVersionPolicySource.RepoRoot(), "docs", language, "09-developer-reference.md"));

        Assert.Contains(
            language == "en" ? "### Next release readiness (v0.32.1)" : "### 次リリース準備(v0.32.1)",
            reference,
            StringComparison.Ordinal);
        Assert.Contains(NormalBaseIdentity, reference, StringComparison.Ordinal);
        Assert.Contains(ExplicitReleaseIdentity, reference, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.29.0.md", reference, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.29.1.md", reference, StringComparison.Ordinal);
        Assert.Contains("ReleaseNotesV0290DocsTests", reference, StringComparison.Ordinal);
        Assert.Contains("ReleasePackageMetadataTests", reference, StringComparison.Ordinal);
        Assert.Contains("JapaneseTerminologyGuardG613Tests", reference, StringComparison.Ordinal);
    }

    private static string FindEntry(string notes, string unit)
    {
        var match = Regex.Match(notes, $@"(?ms)^- {Regex.Escape(unit)} —.*?(?=^- |^## |\z)");
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
        RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.29.0.md"));
}
