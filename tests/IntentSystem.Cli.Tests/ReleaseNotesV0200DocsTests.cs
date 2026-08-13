using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G687 keeps the published v0.20.0 notes bilingual and frozen after the
/// v0.19.0..main first-parent preparation, while the current next-version
/// readiness points at the post-release v0.20.1 stub.
/// </summary>
public sealed class ReleaseNotesV0200DocsTests
{
    private static readonly (string Unit, string Pr, string Merge)[] Units =
    [
        ("G678", "#1468", "3671ba062cd1a4e4b54d634e7160da381fdd3ceb"),
        ("G679", "#1471", "42789d6d8b1e4ac0d7133a277decd6ebcddeaf6b"),
        ("G680", "#1473", "46836e83098c6dd1192beeffe7daf6a32c529d89"),
        ("G681", "#1475", "7540932f61ee34cb2941405d13964b5aa90affb1"),
        ("G682", "#1477", "bbcc360255ecc01fefbf30f4ea06687b763208e6"),
        ("G683", "#1479", "358d8b83b3ea53ae62a5f8323a9b2a26db34235e"),
        ("G684", "#1481", "23a90d36ec9907541b1b3aa6aec789cf3ea00df7"),
        ("G685", "#1483", "5e6bf6b6f1ffa3e882c8445960881ed85cc415d7"),
        ("G686", "#1485", "e759bc04eeb4e4a56ac5334401b130fd749cb084"),
    ];

    private static readonly string[] RangeCommits =
    [
        "32fefec52ae353dbbe10b827020047c57ddfa279",
        .. Units.Select(unit => unit.Merge),
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCoverExactlyG678ThroughG686WithVerifiedPrsAndMerges(string language)
    {
        var notes = Read(language);
        var listed = Regex.Matches(notes, @"(?m)^- (G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(Units.Select(unit => unit.Unit), listed);
        Assert.Equal(9, listed.Length);
        foreach (var unit in Units)
        {
            Assert.Contains(unit.Pr, notes, StringComparison.Ordinal);
            Assert.Contains($"merge commit `{unit.Merge}`", notes, StringComparison.Ordinal);
            Assert.Contains($"`{unit.Merge}`", notes, StringComparison.Ordinal);
        }

        Assert.Contains("git log v0.19.0..main --first-parent", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "exactly nine merged feature units" : "正確に九件の merged feature unit", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "ten commits" : "十 commit", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesAccountForEveryFullFirstParentRangeCommit(string language)
    {
        var notes = Read(language);
        var compact = Regex.Replace(notes, @"\s+", " ");

        Assert.Equal(10, RangeCommits.Length);
        Assert.Equal(RangeCommits.Length, RangeCommits.Distinct(StringComparer.Ordinal).Count());
        foreach (var commit in RangeCommits)
        {
            Assert.Contains(commit, notes, StringComparison.Ordinal);
        }

        Assert.Contains(language == "en" ? "not a release execution unit" : "release execution unit ではありません", compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("post-release roll", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void PreviewStatementPrecedesFeatureDescriptionAndLinksPromise(string language)
    {
        var notes = Read(language);
        var preview = notes.IndexOf("preview-through-1.x", StringComparison.Ordinal);
        var feature = notes.IndexOf(
            language == "en" ? "## The day-scale feedback loop" : "## day-scale で閉じた feedback loop",
            StringComparison.Ordinal);

        Assert.True(preview >= 0);
        Assert.True(feature > preview);
        Assert.Contains("[1.0 compatibility promise](1.0-compatibility-promise.md)", notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesPreserveFourAttributedOriginsAndPrepareOnlyBoundaries(string language)
    {
        var notes = Read(language);
        var compact = Regex.Replace(notes, @"\s+", " ");

        foreach (var term in new[] { "G625", "landing-authority", "multi-user", "#1469", "--model sol", "2026-08-12", "first-cycle", "drift" })
        {
            Assert.Contains(term, compact, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("prepare-only", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "UNRELEASED" : "未リリース", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en" ? "no code or runtime behavior" : "code と runtime behavior は変更しません",
            compact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en" ? "no GitHub Release or tag" : "GitHub Release / tag を作成せず",
            compact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "Earlier release notes are linked" : "earlier release notes は link", compact, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("G687 —", notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void CurrentPolicyAndReadinessFollowVersionPolicyAndPublishedNotesStayFrozen(string language)
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var policy = RepoVersionPolicySource.Read();
        var reference = File.ReadAllText(Path.Combine(root, "docs", language, "09-developer-reference.md"));
        var currentNotes = $"release-notes-v{policy.NextVersion}.md";
        var shippedNotes = $"release-notes-v{policy.StableVersion}.md";
        var notes = Read(language);
        var currentStub = File.ReadAllText(Path.Combine(root, "docs", language, currentNotes));

        RepoVersionPolicySource.AssertReleaseToBeCutIsAheadOfPublishedStable(policy);
        Assert.True(File.Exists(Path.Combine(root, "docs", language, currentNotes)));
        Assert.True(File.Exists(Path.Combine(root, "docs", language, shippedNotes)));
        Assert.False(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.19.1.md")));
        Assert.Contains(currentNotes, reference, StringComparison.Ordinal);
        Assert.Contains(shippedNotes, reference, StringComparison.Ordinal);
        Assert.DoesNotContain("release-notes-v0.19.1.md", reference, StringComparison.Ordinal);
        Assert.Contains(
            language == "en"
                ? $"Next release readiness (v{policy.NextVersion})"
                : $"次リリース準備(v{policy.NextVersion})",
            reference,
            StringComparison.Ordinal);
        Assert.Contains($"JTechJapan.IntentSystem.Cli --version {policy.StableVersion}", notes, StringComparison.Ordinal);
        Assert.Contains($"releases/tag/v{policy.StableVersion}", notes, StringComparison.Ordinal);
        Assert.Contains("DRAFT /", currentStub, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "UNRELEASED" : "未リリース",
            currentStub,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"JTechJapan.IntentSystem.Cli --version {policy.NextVersion}", currentStub, StringComparison.Ordinal);
        Assert.Contains($"releases/tag/v{policy.NextVersion}", currentStub, StringComparison.Ordinal);
        Assert.DoesNotContain("DRAFT /", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void EnglishAndJapaneseNotesHaveTheSameUnitAndMergeInventory()
    {
        var english = Read("en");
        var japanese = Read("ja");

        foreach (var unit in Units)
        {
            Assert.Contains($"{unit.Unit} —", english, StringComparison.Ordinal);
            Assert.Contains($"{unit.Unit} —", japanese, StringComparison.Ordinal);
            Assert.Contains(unit.Merge, english, StringComparison.Ordinal);
            Assert.Contains(unit.Merge, japanese, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en", "f00b7326cb82b49b77c9c3e48d001aa8ba97a1ac24b46c5b84dfc682efeb3aeb")]
    [InlineData("ja", "541a80688f3aea78d32f4c73dd60bb80d189ad44603ae53717261ba52a9db9b6")]
    public void PublishedV0200NotesRemainByteForByteFrozen(string language, string expectedSha256)
    {
        var bytes = File.ReadAllBytes(Path.Combine(
            RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.20.0.md"));

        Assert.Equal(expectedSha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
    }

    private static string Read(string language) => File.ReadAllText(Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.20.0.md"));
}
