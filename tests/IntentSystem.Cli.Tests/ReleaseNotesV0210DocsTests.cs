using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G694 keeps the published v0.21.0 notes bilingual and frozen while G710
/// points post-release readiness at the v0.22.1 draft line.
/// </summary>
public sealed class ReleaseNotesV0210DocsTests
{
    private static readonly (string Unit, string Pr, string Merge)[] Units =
    [
        ("G689", "#1492", "b80d358913be6375741fe95ef93113159b2e0087"),
        ("G690", "#1494", "bf9ca28b670362c24d439c847e477dfd55598440"),
        ("G691", "#1496", "d305987bc6580e2bd137a17e1764e77bc6b219aa"),
        ("G692", "#1498", "05b0aa575fb3fb160a6f0035de6c5aaab0aa8bd9"),
    ];

    private static readonly string[] RangeCommits =
    [
        "a73fea1c54fb544645074cf0edf038158f539332",
        .. Units.Select(unit => unit.Merge),
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCoverExactlyG689ThroughG692WithVerifiedPrsAndMerges(string language)
    {
        var notes = Read(language);
        var listed = Regex.Matches(notes, @"(?m)^- (G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(Units.Select(unit => unit.Unit), listed);
        Assert.Equal(4, listed.Length);
        foreach (var unit in Units)
        {
            Assert.Contains(unit.Pr, notes, StringComparison.Ordinal);
            Assert.Contains($"merge commit `{unit.Merge}`", notes, StringComparison.Ordinal);
            Assert.Contains($"`{unit.Merge}`", notes, StringComparison.Ordinal);
        }

        Assert.Contains("git log v0.20.0..main --first-parent", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "exactly four merged feature units" : "正確に四件の merged feature unit",
            notes,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "five commits" : "五 commit", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesAccountForEveryFullFirstParentRangeCommit(string language)
    {
        var notes = Read(language);
        var compact = Regex.Replace(notes, @"\s+", " ");

        Assert.Equal(5, RangeCommits.Length);
        Assert.Equal(RangeCommits.Length, RangeCommits.Distinct(StringComparer.Ordinal).Count());
        foreach (var commit in RangeCommits)
        {
            Assert.Contains(commit, notes, StringComparison.Ordinal);
        }

        Assert.Contains(
            language == "en" ? "not a release execution unit" : "release execution unit ではありません",
            compact,
            StringComparison.OrdinalIgnoreCase);
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
            language == "en" ? "## The four-unit feedback loop" : "## 四つの unit で閉じた feedback loop",
            StringComparison.Ordinal);

        Assert.True(preview >= 0);
        Assert.True(feature > preview);
        Assert.Contains("[1.0 compatibility promise](1.0-compatibility-promise.md)", notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesPreserveOriginsMinorRationaleAndPublishedBoundaries(string language)
    {
        var notes = Read(language);
        var compact = Regex.Replace(notes, @"\s+", " ");

        foreach (var term in new[]
                 {
                     "G625", "#1489", "operator-filed", "authoring-only", "prompt-class list/describe",
                     "adjudicate", "answerable_by", "team_mode", "mode-capability matrix",
                     "non-overridable", "rm-containing compound", "design-unanswerable by design",
                 })
        {
            Assert.Contains(term, compact, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("prepare-only", notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(language == "en" ? "UNRELEASED" : "未リリース", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en" ? "no code or runtime behaviour" : "code と runtime behaviour は変更しません",
            compact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en" ? "Released / stable" : "公開済み",
            notes,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("31766364883", notes, StringComparison.Ordinal);
        Assert.Contains("c77c92fe", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "eight release assets" : "八つの release asset",
            compact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en" ? "Four checksum verifications passed" : "四つの checksum verification が pass",
            compact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NuGet.org", notes, StringComparison.Ordinal);
        Assert.Contains("intent-cli 0.21.0-c77c92f-G691", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("G693 —", notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void CurrentPolicyAndReadinessFollowVersionPolicyAndNextNotesAreDraftOrPrepared(string language)
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var policy = RepoVersionPolicySource.Read();
        var reference = File.ReadAllText(Path.Combine(root, "docs", language, "09-developer-reference.md"));
        var referenceCompact = Regex.Replace(reference, @"\s+", " ");
        var notes = Read(language);
        var currentNotes = $"release-notes-v{policy.NextVersion}.md";
        var shippedNotes = $"release-notes-v{policy.StableVersion}.md";
        var shippedNotesPath = Path.Combine(root, "docs", language, shippedNotes);
        var missingStableNoteMarker = language == "en"
            ? $"no tracked `{shippedNotes}`"
            : $"tracked な `{shippedNotes}` がなく";

        RepoVersionPolicySource.AssertReleaseToBeCutIsAheadOfPublishedStable(policy);
        Assert.True(File.Exists(Path.Combine(root, "docs", language, currentNotes)));
        Assert.Contains(currentNotes, reference, StringComparison.Ordinal);
        Assert.Contains(shippedNotes, reference, StringComparison.Ordinal);
        Assert.True(
            File.Exists(shippedNotesPath) || referenceCompact.Contains(missingStableNoteMarker, StringComparison.Ordinal),
            $"Readiness must either carry the policy-derived stable note {shippedNotes} or explain its missing local source file.");
        Assert.Contains(
            language == "en"
                ? $"Next release readiness (v{policy.NextVersion})"
                : $"次リリース準備(v{policy.NextVersion})",
            reference,
            StringComparison.Ordinal);
        var currentNotesText = File.ReadAllText(Path.Combine(root, "docs", language, currentNotes));
        var currentNotesCompact = Regex.Replace(currentNotesText, @"[>\s]+", " ");
        var isDraft = currentNotesCompact.Contains("DRAFT /", StringComparison.OrdinalIgnoreCase);
        if (isDraft)
        {
            Assert.Contains(language == "en" ? "UNRELEASED" : "未リリース", currentNotesCompact, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                language == "en"
                    ? "release-prep packet will replace this placeholder"
                    : "release-prep パケットが",
                currentNotesCompact,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                language == "en"
                    ? "must not be treated as a changelog"
                    : "changelog として扱ってはいけません",
                currentNotesCompact,
                StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains("prepare-only", currentNotesCompact, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no tag", currentNotesCompact, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no GitHub Release", currentNotesCompact, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no publish", currentNotesCompact, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("JTechJapan.IntentSystem.Cli --version", currentNotesText, StringComparison.Ordinal);
        Assert.True(
            currentNotesText.Contains($"releases/tag/v{policy.NextVersion}", StringComparison.Ordinal)
                || currentNotesCompact.Contains("no GitHub Release", StringComparison.OrdinalIgnoreCase),
            "Next-version notes must either link the future tag or state that no GitHub Release exists yet.");
        if (File.Exists(shippedNotesPath))
        {
            var stableNotes = File.ReadAllText(shippedNotesPath);
            var stableNoteIsPrepareOnly =
                stableNotes.Contains("PREPARED / NOT PUBLISHED", StringComparison.OrdinalIgnoreCase)
                || stableNotes.Contains("prepare-only", StringComparison.OrdinalIgnoreCase);
            if (stableNoteIsPrepareOnly)
            {
                // A prepare-only stable note can coexist with authoritative
                // published Release evidence; readiness records that
                // source-note inconsistency for the still-prepared line.
                Assert.Contains(
                    $"v{policy.StableVersion} GitHub Release",
                    referenceCompact,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "source-note inconsistency",
                    referenceCompact,
                    StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                Assert.Contains($"releases/tag/v{policy.StableVersion}", stableNotes, StringComparison.Ordinal);
                Assert.DoesNotContain("DRAFT /", stableNotes, StringComparison.Ordinal);
                Assert.DoesNotContain("UNRELEASED", stableNotes, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("未リリース", stableNotes, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("prepare-only", stableNotes, StringComparison.OrdinalIgnoreCase);
            }
        }
        else
        {
            Assert.Contains($"v{policy.StableVersion} GitHub Release", referenceCompact, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void BilingualCountGuardMatchesExactlyFourUnitsAndRejectsMismatch(string language)
    {
        var notes = Read(language);
        Assert.Empty(CountMismatches(notes, language));

        var wrong = language == "en"
            ? notes.Replace("exactly four merged feature units", "exactly five merged feature units", StringComparison.Ordinal)
            : notes.Replace("正確に四件の merged feature unit", "正確に五件の merged feature unit", StringComparison.Ordinal);

        Assert.NotEqual(notes, wrong);
        Assert.NotEmpty(CountMismatches(wrong, language));
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

    [Theory]
    [InlineData("en", "95b9f17459860f14665170d17dea2e4afbfe4fe5a547a359cd79442d10b488f7")]
    [InlineData("ja", "448c9f34a899712e39b1fcbcd0be15c6012d9ac6b3dac63d7ca5a5549011bb25")]
    public void PublishedV0210NotesRemainByteForByteFrozen(string language, string expectedSha256)
    {
        var bytes = File.ReadAllBytes(Path.Combine(
            RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.21.0.md"));

        Assert.Equal(expectedSha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
    }

    private static IReadOnlyList<string> CountMismatches(string notes, string language)
    {
        var listedCount = Regex.Matches(notes, @"(?m)^- (G\d+) —").Count;
        var statedCounts = language == "en"
            ? Regex.Matches(notes, @"\b(?<count>one|two|three|four|five|six|seven|eight|nine|ten)\s+merged feature units?\b", RegexOptions.IgnoreCase)
                .Select(match => EnglishCount(match.Groups["count"].Value))
            : Regex.Matches(notes, @"正確に(?<count>[一二三四五六七八九十百〇零]+)件の merged feature unit")
                .Select(match => JapaneseCount(match.Groups["count"].Value));

        return statedCounts.Where(count => count != listedCount).Select(count => $"stated {count}; listed {listedCount}").ToArray();
    }

    private static int EnglishCount(string text) => text.ToLowerInvariant() switch
    {
        "one" => 1, "two" => 2, "three" => 3, "four" => 4, "five" => 5,
        "six" => 6, "seven" => 7, "eight" => 8, "nine" => 9, "ten" => 10,
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, "unsupported count"),
    };

    private static int JapaneseCount(string text)
    {
        var values = new Dictionary<char, int>
        {
            ['〇'] = 0, ['零'] = 0, ['一'] = 1, ['二'] = 2, ['三'] = 3, ['四'] = 4,
            ['五'] = 5, ['六'] = 6, ['七'] = 7, ['八'] = 8, ['九'] = 9, ['十'] = 10,
            ['百'] = 100,
        };
        var total = 0;
        var pending = 0;
        foreach (var character in text)
        {
            var value = values[character];
            if (value is 10 or 100)
            {
                total += (pending == 0 ? 1 : pending) * value;
                pending = 0;
            }
            else
            {
                pending = value;
            }
        }
        return total + pending;
    }

    private static string Read(string language) => File.ReadAllText(Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.21.0.md"));
}
