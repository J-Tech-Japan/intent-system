using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G633 keeps the v0.12.0 prepare-only notes complete, bilingual, and bounded
/// to the eighteen merged units selected for this minor release.
/// </summary>
public sealed class ReleaseNotesV0120DocsTests
{
    private static readonly (string Unit, string Pr, string Merge)[] ReleasedUnits =
    [
        ("G610", "#1324", "48204646"),
        ("G611", "#1328", "4f4106f947e5"),
        ("G612", "#1326", "1b1206a56e71"),
        ("G613", "#1330", "f3d0838a1da0"),
        ("G614", "#1334", "a260b63bd4a1"),
        ("G615", "#1332", "940997c6b767"),
        ("G616", "#1336", "21f6fb3c8a3b"),
        ("G617", "#1338", "207a3d2e20e0"),
        ("G618", "#1340", "7f2bb23bd4a5"),
        ("G619", "#1342", "36b89ac9fbfc"),
        ("G620", "#1344", "72878b63ff97"),
        ("G621", "#1346", "a1886218f56c"),
        ("G623", "#1350", "c04e137"),
        ("G624", "#1352", "ccd4f29"),
        ("G625", "#1354", "06f1a71"),
        ("G626", "#1356", "2bb20d3"),
        ("G627", "#1358", "5b86977"),
        ("G628", "#1360", "f464a04"),
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReleaseNotes_CoverExactlyEighteenUnits_WithVerifiedMerges_G633(string language)
    {
        var notes = Read(language);
        var listedUnits = Regex.Matches(notes, @"(?m)^- (G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(ReleasedUnits.Select(unit => unit.Unit), listedUnits);
        Assert.Equal(18, listedUnits.Length);
        foreach (var unit in ReleasedUnits)
        {
            Assert.Contains(unit.Pr, notes, StringComparison.Ordinal);
            Assert.Contains(unit.Merge, notes, StringComparison.Ordinal);
        }

        Assert.Contains("release-notes-v0.11.1.md", notes, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.11.0.md", notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReleaseNotes_StateTheMinorRationale_BehaviourChanges_AndPrepareOnlyBoundary_G622(string language)
    {
        var notes = Read(language);

        foreach (var surface in new[]
                 {
                     "topology update-kind",
                     "topology retire-legacy",
                     "topology update-field",
                     "delivery_method: file-backed",
                 })
        {
            Assert.Contains(surface, notes, StringComparison.Ordinal);
        }

        Assert.Contains("v0.11.1", notes, StringComparison.Ordinal);
        Assert.Contains("record", notes, StringComparison.Ordinal);
        Assert.Contains("inline delivery", notes, StringComparison.Ordinal);
        Assert.Contains("denial", notes, StringComparison.Ordinal);
        Assert.Contains("guard", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Release", notes, StringComparison.Ordinal);
        Assert.Contains("tag", notes, StringComparison.Ordinal);
        Assert.Contains("publish", notes, StringComparison.Ordinal);
        foreach (var surface in new[] { "judgment-wait", "execution-unit", "preview-through-1.x" })
        {
            Assert.Contains(surface, notes, StringComparison.Ordinal);
        }

        Assert.Contains("working-did-not-settle", notes, StringComparison.Ordinal);
        Assert.Contains("not-observed-within-bound", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "silently" : "silent", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("working_transition", notes, StringComparison.Ordinal);
        Assert.Contains("breaking", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("topology record", notes, StringComparison.Ordinal);
        Assert.Contains("topology retire-legacy", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleasePrep_RetargetsVersionAndRemovesSupersededV0112Stubs_G622()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var policy = VersionPolicy.TryReadFromRepo(root);

        Assert.NotNull(policy);
        Assert.Equal("0.11.1", policy!.StableVersion);
        Assert.Equal("0.12.0", policy.NextVersion);
        foreach (var language in new[] { "en", "ja" })
        {
            Assert.False(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.11.2.md")));
        }
    }

    private static string Read(string language) =>
        File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.12.0.md"));
}
