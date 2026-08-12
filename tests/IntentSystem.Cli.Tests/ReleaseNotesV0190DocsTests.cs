using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G677 keeps the v0.19.0 prepare-only notes bilingual, exactly bounded to
/// G666-G676, and reconciled with the full v0.18.0..main first-parent range.
/// </summary>
public sealed class ReleaseNotesV0190DocsTests
{
    private static readonly (string Unit, string Pr, string Merge)[] Units =
    [
        ("G666", "#1440", "1b7f8b718d9c22cfe67707ee9ca23a9a9e6f0b7b"),
        ("G667", "#1444", "2c253a01ea3b7d3836ad044eb5e9ffac38d46f77"),
        ("G668", "#1446", "e9d125ea45a163636323a7a0420476b7267cf94e"),
        ("G669", "#1448", "e1924405e6d0fcdfdccf8665abc7263dc9a0ee96"),
        ("G670", "#1450", "8a85262cd1e42f73d9ba1f438f783e394f8a3828"),
        ("G671", "#1452", "c4f2d66af72c278d0de1d38b0c2c4ea508b1be5f"),
        ("G672", "#1454", "cc60fc7ae94ddba7746caf2acdef53ecb29becaf"),
        ("G673", "#1456", "e6762a5151dc8f489dd5ba108a63adca4ee8c0a6"),
        ("G674", "#1458", "44c4a27befe458399777743ed5c8e16c0d5f3fe1"),
        ("G675", "#1460", "1c7cace56fdf29a834ee2de61df768e3b083a796"),
        ("G676", "#1462", "85a4d451d9a91daaf936e3997cf36f67b73766f1"),
    ];

    private static readonly string[] RangeCommits =
    [
        "478dd57b5de609e47dbe678c82f714fd0e463dd8",
        .. Units.Select(unit => unit.Merge),
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCoverExactlyG666ThroughG676WithVerifiedPrsAndMerges(string language)
    {
        var notes = Read(language);
        var listed = Regex.Matches(notes, @"(?m)^- (G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(Units.Select(unit => unit.Unit), listed);
        Assert.Equal(11, listed.Length);
        foreach (var unit in Units)
        {
            Assert.Contains(unit.Pr, notes, StringComparison.Ordinal);
            Assert.Contains($"merge commit `{unit.Merge}`", notes, StringComparison.Ordinal);
            Assert.Contains($"`{unit.Merge}`", notes, StringComparison.Ordinal);
        }

        Assert.Contains("git log v0.18.0..main --first-parent", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "eleven merged units" : "正確に十一件の merged unit", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "twelve commits" : "十二 commit", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesAccountForEveryFullFirstParentRangeCommit(string language)
    {
        var notes = Read(language);

        Assert.Equal(12, RangeCommits.Length);
        Assert.Equal(RangeCommits.Length, RangeCommits.Distinct(StringComparer.Ordinal).Count());
        foreach (var commit in RangeCommits)
        {
            Assert.Contains(commit, notes, StringComparison.Ordinal);
        }

        Assert.Contains(language == "en" ? "not a release execution unit" : "release execution unit ではありません", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void PreviewStatementPrecedesFeatureDescriptionAndLinksPromise(string language)
    {
        var notes = Read(language);
        var preview = notes.IndexOf("preview-through-1.x", StringComparison.Ordinal);
        var feature = notes.IndexOf(
            language == "en" ? "## The feedback loop closed at day scale" : "## day-scale で閉じた feedback loop",
            StringComparison.Ordinal);

        Assert.True(preview >= 0);
        Assert.True(feature > preview);
        Assert.Contains("[1.0 compatibility promise](1.0-compatibility-promise.md)", notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesPreserveAttributedOriginsMinorRationaleAndPrepareOnly(string language)
    {
        var notes = Read(language);
        var compact = Regex.Replace(notes, @"\s+", " ");

        foreach (var term in new[] { "G625", "#1441", "#1442", "remote-herdr", "2026-08-12", "G675", "G676" })
        {
            Assert.Contains(term, compact, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var term in new[]
                 {
                     "branch-lane registry", "routing snapshot", "pending-delegation", "quota-degraded",
                     "duplicate-supervisor", "v0.18.0",
                 })
        {
            Assert.Contains(term, compact, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("prepare-only", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "UNRELEASED" : "未リリース", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "no code or runtime behavior change" : "code と runtime behavior は変更しません", compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "creates no GitHub Release or tag" : "GitHub Release / tag を作成せず", compact, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void CurrentPolicyAndReadinessFollowVersionPolicyWhileV0190NotesStayReal(string language)
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var policy = RepoVersionPolicySource.Read();
        var reference = File.ReadAllText(Path.Combine(root, "docs", language, "09-developer-reference.md"));
        var notes = Read(language);
        var currentNotes = $"release-notes-v{policy.NextVersion}.md";
        var shippedNotes = $"release-notes-v{policy.StableVersion}.md";

        RepoVersionPolicySource.AssertReleaseToBeCutIsAheadOfPublishedStable(policy);
        Assert.True(File.Exists(Path.Combine(root, "docs", language, currentNotes)));
        Assert.True(File.Exists(Path.Combine(root, "docs", language, shippedNotes)));
        Assert.Contains(currentNotes, reference, StringComparison.Ordinal);
        Assert.Contains(shippedNotes, reference, StringComparison.Ordinal);
        Assert.Contains(
            language == "en"
                ? $"Next release readiness (v{policy.NextVersion})"
                : $"次リリース準備(v{policy.NextVersion})",
            reference,
            StringComparison.Ordinal);
        Assert.DoesNotContain("DRAFT /", notes, StringComparison.Ordinal);
        Assert.Contains("Release-readiness gate", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "Publishing v0.19.0" : "v0.19.0 の publish", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en", "11866387f4fe8017bfbc3b8e3dad089435dee9c1da426dadecdfd50ec2bc5221")]
    [InlineData("ja", "2383b58cc3a21910469f2a78c899d2df50592d79c62ab0c0d832dbbfbe509702")]
    public void PublishedV0190NotesRemainByteForByteFrozen(string language, string expectedSha256)
    {
        var bytes = File.ReadAllBytes(Path.Combine(
            RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.19.0.md"));

        Assert.Equal(expectedSha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
    }

    private static string Read(string language) => File.ReadAllText(Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.19.0.md"));
}
