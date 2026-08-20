using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G718: the prepared v0.23.0 notes are a bilingual inventory of exactly
/// G710 through G716, with the two new command surfaces and the G714/G716
/// guidance corrections made explicit.
/// </summary>
public sealed class ReleaseNotesV0230DocsTests
{
    private const string ReleaseIdentityEvidenceSourceRevision =
        "39611e5e0f024591cc961b20bc99ada2b8e22c38";
    private const string ReleaseIdentityEvidence = "intent-cli 0.23.0-39611e5-G718";

    private static readonly (string Unit, string[] Prs, string[] Merges)[] Units =
    [
        ("G710", ["#1537"], ["335bb686ba966368abbdadac149bc27d9aea7c6b"]),
        ("G711", ["#1539"], ["a4b4ecb7ac904b077f3cd75f2c738aa5c163ebc6"]),
        ("G712", ["#1541", "#1542"], [
            "037c4acc8a02401d4bdb58b0011e05d2026dafdb",
            "130c99f828a6574b822203072d03554cda6a1182",
        ]),
        ("G713", ["#1545"], ["553f963439b2e3a700c2acc5800679b78d86b325"]),
        ("G714", ["#1548"], ["c21c2c7e2e976914eed5231148cc1f1f6cf3c5e3"]),
        ("G715", ["#1554"], ["e25d770caacbcdafa2aa9bebea72e895dc22fcbb"]),
        ("G716", ["#1553"], ["4b0a1a31b075746927d0d73c6f9b370c531e9845"]),
    ];

    private static readonly string[] RangeCommits =
    [
        "c48a5635",
        "335bb686ba966368abbdadac149bc27d9aea7c6b",
        "a4b4ecb7ac904b077f3cd75f2c738aa5c163ebc6",
        "037c4acc8a02401d4bdb58b0011e05d2026dafdb",
        "130c99f828a6574b822203072d03554cda6a1182",
        "553f963439b2e3a700c2acc5800679b78d86b325",
        "c21c2c7e2e976914eed5231148cc1f1f6cf3c5e3",
        "4b0a1a31b075746927d0d73c6f9b370c531e9845",
        "e25d770caacbcdafa2aa9bebea72e895dc22fcbb",
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCoverExactlyG710ThroughG716WithVerifiedPrsAndMerges(string language)
    {
        var notes = Read(language);
        var listed = Regex.Matches(notes, @"(?m)^- (G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(Units.Select(unit => unit.Unit), listed);
        Assert.Equal(7, listed.Length);
        foreach (var unit in Units)
        {
            var bullet = Regex.Match(notes, $@"(?m)^- {Regex.Escape(unit.Unit)} —[^\r\n]*$");
            Assert.True(bullet.Success, $"{language} notes are missing the bullet for {unit.Unit}.");
            foreach (var pr in unit.Prs)
            {
                Assert.Contains($"PR {pr};", bullet.Value, StringComparison.Ordinal);
            }

            foreach (var merge in unit.Merges)
            {
                Assert.Contains($"merge commit `{merge}`", notes, StringComparison.Ordinal);
                Assert.Contains($"`{merge}`", notes, StringComparison.Ordinal);
            }
        }

        Assert.Contains("git log --first-parent v0.22.0..origin/main", notes, StringComparison.Ordinal);
        Assert.Contains("git log --first-parent v0.22.0..main", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "exactly seven merged feature units" : "正確に七件の merged feature unit",
            notes,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("twenty commits", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesAccountForTheFullFirstParentRange(string language)
    {
        var notes = Read(language);

        Assert.Equal(9, RangeCommits.Length);
        Assert.Equal(RangeCommits.Length, RangeCommits.Distinct(StringComparer.Ordinal).Count());
        foreach (var commit in RangeCommits)
        {
            Assert.Contains(commit, notes, StringComparison.Ordinal);
        }

        Assert.Contains(
            language == "en"
                ? "not a release execution unit"
                : "release execution unit ではありません",
            notes,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesMakeRetargetRationaleAndGuidanceCorrectionsExplicit(string language)
    {
        var notes = Read(language);
        var compact = Regex.Replace(notes, @"\s+", " ");

        Assert.Contains("0.23.0", notes, StringComparison.Ordinal);
        Assert.Contains("notify supervise reconcile|uninstall", notes, StringComparison.Ordinal);
        Assert.Contains("guide workflow task supervision-setup", notes, StringComparison.Ordinal);
        Assert.Contains("linkage-recovered", notes, StringComparison.Ordinal);
        Assert.Contains("G714", notes, StringComparison.Ordinal);
        Assert.Contains("G716", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "correction, not a feature" : "feature ではなく correction",
            compact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en"
                ? "earlier `.git` claim was retracted/corrected"
                : "以前の `.git` claim は retracted/corrected",
            compact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en" ? "190 of the 191" : "191 unit のうち 190 unit",
            compact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en" ? "prepare-only" : "release preparation",
            compact,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void EnglishAndJapaneseNotesShareTheSameInventory(string language)
    {
        var notes = Read(language);
        foreach (var unit in Units)
        {
            Assert.Contains($"- {unit.Unit} —", notes, StringComparison.Ordinal);
            foreach (var merge in unit.Merges)
            {
                Assert.Contains(merge, notes, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ReleaseIdentityEvidence_IsMirroredAndBoundToItsProducingRevision_G718Repair()
    {
        var notes = new[] { Read("en"), Read("ja") };
        var sourceRevisions = notes
            .Select(note => Regex.Match(
                note,
                @"(?m)^- \*\*Release identity evidence source revision:\*\*\s*`(?<sha>[0-9a-f]{40})`"))
            .ToArray();
        var identities = notes
            .Select(note => Regex.Match(
                note,
                @"intent-cli (?<version>0\.23\.0)-(?<sha>[0-9a-f]{7})-(?<unit>G\d+)"))
            .ToArray();

        Assert.All(sourceRevisions, match => Assert.True(match.Success, "Release identity evidence must name its source revision."));
        Assert.All(identities, match => Assert.True(match.Success, "Release identity evidence must include a display identity."));
        Assert.Equal(ReleaseIdentityEvidenceSourceRevision, sourceRevisions[0].Groups["sha"].Value);
        Assert.Equal(ReleaseIdentityEvidenceSourceRevision, sourceRevisions[1].Groups["sha"].Value);
        Assert.Equal(sourceRevisions[0].Groups["sha"].Value, sourceRevisions[1].Groups["sha"].Value);
        Assert.Equal(ReleaseIdentityEvidence, identities[0].Value);
        Assert.Equal(ReleaseIdentityEvidence, identities[1].Value);
        Assert.Equal(
            sourceRevisions[0].Groups["sha"].Value[..7],
            identities[0].Groups["sha"].Value);
        Assert.Equal("G718", identities[0].Groups["unit"].Value);
        Assert.All(notes, note => Assert.Contains("documentation-only repair", note, StringComparison.OrdinalIgnoreCase));
    }

    private static string Read(string language) =>
        File.ReadAllText(Path.Combine(
            RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.23.0.md"));
}
