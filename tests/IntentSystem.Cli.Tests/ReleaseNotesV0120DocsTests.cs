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
        Assert.Empty(CountMismatches(notes, language));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReleaseNotes_UnitCountGuardRejectsAStatedMismatch_G634(string language)
    {
        var notes = Read(language);
        var wrong = language == "en"
            ? notes.Replace("eighteen units", "seventeen units", StringComparison.Ordinal)
            : notes.Replace("十八件", "十七件", StringComparison.Ordinal);

        Assert.NotEmpty(CountMismatches(wrong, language));
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
        foreach (var language in new[] { "en", "ja" })
        {
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.12.0.md")));
            Assert.False(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.11.2.md")));
        }
    }

    private static string Read(string language) =>
        File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.12.0.md"));

    private static IReadOnlyList<string> CountMismatches(string notes, string language)
    {
        var listedUnits = Regex.Matches(notes, @"(?m)^- (G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        var statedCounts = language == "en"
            ? Regex.Matches(notes, @"\b(?<count>zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty)\s+(?:units|merges)\b", RegexOptions.IgnoreCase)
                .Select(match => (Text: match.Groups["count"].Value, Value: EnglishCount(match.Groups["count"].Value)))
            : Regex.Matches(notes, @"(?<count>[一二三四五六七八九十百〇零]+)件")
                .Select(match => (Text: match.Groups["count"].Value, Value: JapaneseCount(match.Groups["count"].Value)));

        return statedCounts
            .Where(count => count.Value != listedUnits.Length)
            .Select(count => $"{language} stated {count.Text} ({count.Value}) units, but lists {listedUnits.Length}.")
            .ToArray();
    }

    private static int EnglishCount(string text) => text.ToLowerInvariant() switch
    {
        "zero" => 0, "one" => 1, "two" => 2, "three" => 3, "four" => 4,
        "five" => 5, "six" => 6, "seven" => 7, "eight" => 8, "nine" => 9,
        "ten" => 10, "eleven" => 11, "twelve" => 12, "thirteen" => 13,
        "fourteen" => 14, "fifteen" => 15, "sixteen" => 16, "seventeen" => 17,
        "eighteen" => 18, "nineteen" => 19, "twenty" => 20,
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
}
