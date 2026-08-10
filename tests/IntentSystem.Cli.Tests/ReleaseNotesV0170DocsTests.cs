using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G663 keeps the v0.17.0 preparation bilingual, bounded to the eleven merged
/// units in the twenty-commit range, and explicitly prepare-only.
/// </summary>
public sealed class ReleaseNotesV0170DocsTests
{
    private static readonly (string Unit, string Pr, string Merge)[] Units =
    [
        ("G656", "#1410", "853b48ab"),
        ("G652", "#1412", "542133f7"),
        ("G653", "#1414", "83c5feea"),
        ("G655", "#1416", "c06e16d3"),
        ("G654", "#1418", "eae66f05"),
        ("G657", "#1420", "7ab3e297"),
        ("G658", "#1422", "39d7cf42"),
        ("G659", "#1424", "5331ec11"),
        ("G660", "#1426", "bdc5b5b1"),
        ("G661", "#1428", "b06dac5d"),
        ("G662", "#1430", "f2e53c03"),
    ];

    private static readonly string[] RangeCommits =
    [
        "f3165a5c", "853b48ab", "542133f7", "83c5feea",
        "f6f2b6f0", "c06e16d3", "d1ec27d8", "eae66f05",
        "970eb671", "7ab3e297", "f9b5ff96", "39d7cf42",
        "30931bd2", "5331ec11", "99f5f2b2", "bdc5b5b1",
        "28a68cd0", "b06dac5d", "234a7058", "f2e53c03",
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCoverExactlyTheElevenUnitsWithVerifiedMerges(string language)
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
            Assert.Contains(unit.Merge, notes, StringComparison.Ordinal);
        }

        Assert.Contains("git log v0.16.1..main", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "twenty commits" : "二十 commit", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesAccountForEveryCommitInTheRange(string language)
    {
        var notes = Read(language);

        Assert.Equal(20, RangeCommits.Length);
        Assert.Equal(RangeCommits.Length, RangeCommits.Distinct(StringComparer.Ordinal).Count());
        foreach (var commit in RangeCommits)
        {
            Assert.Contains(commit, notes, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void PreviewStatementPrecedesTheFeatureDescription(string language)
    {
        var notes = Read(language);
        var preview = notes.IndexOf("preview-through-1.x", StringComparison.Ordinal);
        var featureDescription = notes.IndexOf(
            language == "en" ? "## Operating contract for supervised teams" : "## supervised team の operating contract",
            StringComparison.Ordinal);

        Assert.True(preview >= 0, "The preview statement must be present.");
        Assert.True(featureDescription > preview, "The preview statement must precede the feature description.");
        Assert.Contains("[1.0 compatibility promise](1.0-compatibility-promise.md)", notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCarryTheOperatingContractAndDeliberateBoundaries(string language)
    {
        var notes = Read(language);
        var compact = Regex.Replace(notes, @"\s+", " ");

        foreach (var term in new[]
                 {
                     "Codex", "design", "orchestration", "implementation", "review",
                     "supervision process", "watcher infrastructure", "45-unit remote-herdr",
                     "supervise install", "event mode", "interval floor", "realignment", "recovery authority",
                 })
        {
            Assert.Contains(term, compact, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(
            language == "en" ? "four judgment-bearing threads" : "四つの judgment-bearing thread",
            compact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en" ? "one supervision process" : "一つの supervision process",
            compact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "three teams" : "三つの team", compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "never registers" : "register は決して", compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "never schedules" : "schedule、実行、grade することは決して", compact, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void MinorRationaleNamesSurfacesAbsentAtV0161(string language)
    {
        var notes = Read(language);

        foreach (var term in new[]
                 {
                     "supervise install", "event mode", "packet retire --reactivate",
                     "design-thread guide", "improve-run record", "v0.16.1",
                 })
        {
            Assert.Contains(term, notes, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(language == "en" ? "absent at v0.16.1" : "v0.16.1 には存在しませんでした", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCountGuardMatchesAndRejectsAStatedMismatch(string language)
    {
        var notes = Read(language);
        Assert.Empty(CountMismatches(notes, language));

        var wrong = language == "en"
            ? notes.Replace("exactly eleven merged units", "exactly ten merged units", StringComparison.Ordinal)
            : notes.Replace("正確に十一件の merged unit", "正確に十件の merged unit", StringComparison.Ordinal);

        Assert.NotEqual(notes, wrong);
        Assert.NotEmpty(CountMismatches(wrong, language));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReadinessAdvancesBeyondV0180WhilePublishedNotesRemainGuarded(string language)
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var reference = File.ReadAllText(Path.Combine(root, "docs", language, "09-developer-reference.md"));
        var v0180Notes = File.ReadAllText(Path.Combine(root, "docs", language, "release-notes-v0.18.0.md"));

        Assert.False(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.17.1.md")));
        Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.18.0.md")));
        Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.18.1.md")));
        Assert.Contains("release-notes-v0.18.1.md", reference, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.18.0.md", reference, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.17.0.md", v0180Notes, StringComparison.Ordinal);
        Assert.Contains("ReleaseNotesV0180DocsTests", reference, StringComparison.Ordinal);
        Assert.Contains("ReleaseNotesV0170DocsTests", reference, StringComparison.Ordinal);
        Assert.DoesNotContain("ReleaseNotesV0171DocsTests", reference, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesRemainPrepareOnly(string language)
    {
        var notes = Read(language);

        Assert.Contains("prepare-only", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "UNRELEASED" : "未リリース", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Release", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tag", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("package publish", notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DRAFT /", notes, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> CountMismatches(string notes, string language)
    {
        var listedCount = Regex.Matches(notes, @"(?m)^- (G\d+) —").Count;
        var statedCounts = language == "en"
            ? Regex.Matches(notes, @"\b(?<count>one|two|three|four|five|six|seven|eight|nine|ten|eleven)\s+merged units?\b", RegexOptions.IgnoreCase)
                .Select(match => EnglishCount(match.Groups["count"].Value))
            : Regex.Matches(notes, @"正確に(?<count>[一二三四五六七八九十百〇零]+)件の merged unit")
                .Select(match => JapaneseCount(match.Groups["count"].Value));

        return statedCounts
            .Where(count => count != listedCount)
            .Select(count => $"{language} stated {count} units, but lists {listedCount}.")
            .ToArray();
    }

    private static int EnglishCount(string text) => text.ToLowerInvariant() switch
    {
        "one" => 1,
        "two" => 2,
        "three" => 3,
        "four" => 4,
        "five" => 5,
        "six" => 6,
        "seven" => 7,
        "eight" => 8,
        "nine" => 9,
        "ten" => 10,
        "eleven" => 11,
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, "unsupported count")
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

    private static string Read(string language) =>
        File.ReadAllText(Path.Combine(
            RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.17.0.md"));
}
