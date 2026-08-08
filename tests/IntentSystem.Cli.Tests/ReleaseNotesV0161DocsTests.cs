using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G651 keeps the v0.16.1 preparation bilingual, bounded to the one merged
/// repair in the version range, and visibly distinct from the post-roll stub.
/// </summary>
public sealed class ReleaseNotesV0161DocsTests
{
    private static readonly (string Unit, string Pr, string Merge)[] Units =
    [
        ("G650", "#1405", "53ee440e"),
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCoverExactlyG650WithTheVerifiedMerge(string language)
    {
        var notes = Read(language);
        var listed = Regex.Matches(notes, @"(?m)^- (G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(Units.Select(unit => unit.Unit), listed);
        Assert.Single(listed);
        foreach (var unit in Units)
        {
            Assert.Contains(unit.Pr, notes, StringComparison.Ordinal);
            Assert.Contains(unit.Merge, notes, StringComparison.Ordinal);
        }

        Assert.Contains("git log v0.16.0..main", notes, StringComparison.Ordinal);
        Assert.Contains("428eea70", notes, StringComparison.Ordinal);
        Assert.Contains("0.16.0", notes, StringComparison.Ordinal);
        Assert.Contains("0.16.1", notes, StringComparison.Ordinal);
        Assert.Contains("prepare-only", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Release", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tag", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("package", notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DRAFT /", notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCarryTheUserRegressionAndPreservedBoundaries(string language)
    {
        var notes = Read(language);

        foreach (var term in new[]
                 {
                     "G650", "guide orchestrator-thread", "--team", "herdr-only",
                     "undeclared-fragment", "source presence is not reachability",
                     "guard",
                 })
        {
            Assert.Contains(term, notes, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(language == "en" ? "fails closed" : "fail closed", notes, StringComparison.OrdinalIgnoreCase);

        if (language == "en")
        {
            Assert.Contains("no command", notes, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no flag", notes, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains("command", notes, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("flag", notes, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("追加していません", notes, StringComparison.Ordinal);
        }
        Assert.Contains("every session-layer mode", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void Notes_CountGuardMatchesTheListedUnit_AndRejectsAStatedMismatch(string language)
    {
        var notes = Read(language);
        Assert.Empty(CountMismatches(notes, language));

        var wrong = language == "en"
            ? notes.Replace("exactly one merged unit", "exactly two merged units", StringComparison.Ordinal)
            : notes.Replace("正確に一件の merged unit", "正確に二件の merged units", StringComparison.Ordinal);

        Assert.NotEmpty(CountMismatches(wrong, language));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReadinessMirrorNamesTheAuthoredNotesGuard(string language)
    {
        var path = Path.Combine(
            RepoVersionPolicySource.RepoRoot(), "docs", language, "09-developer-reference.md");
        var reference = File.ReadAllText(path);

        Assert.Contains("release-notes-v0.16.1.md", reference, StringComparison.Ordinal);
        Assert.Contains("ReleaseNotesV0161DocsTests", reference, StringComparison.Ordinal);
        Assert.DoesNotContain("release-notes-v0.16.1.md) is the required prepare-only placeholder", reference, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> CountMismatches(string notes, string language)
    {
        var listedCount = Regex.Matches(notes, @"(?m)^- (G\d+) —").Count;
        var statedCounts = language == "en"
            ? Regex.Matches(notes, @"\b(?<count>one|two|three|four|five|six|seven|eight|nine|ten)\s+merged units?\b", RegexOptions.IgnoreCase)
                .Select(match => EnglishCount(match.Groups["count"].Value))
            : Regex.Matches(notes, @"(?<count>[一二三四五六七八九十百〇零]+)件")
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
            RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.16.1.md"));
}
