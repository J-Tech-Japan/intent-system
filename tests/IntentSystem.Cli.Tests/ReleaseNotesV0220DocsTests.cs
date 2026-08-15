using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G709: the v0.22.0 preparation notes are a checkable, bilingual inventory
/// of the exact first-parent range, not a second copy of an unverified changelog.
/// </summary>
public sealed class ReleaseNotesV0220DocsTests
{
    private static readonly (string Unit, string Pr, string Merge)[] Units =
    [
        ("G695", "#1504", "dfb6a539fe5c8c76bf29c54eafb643b63af3e48d"),
        ("G696", "#1506", "1a9cf3a9b733de4ffe600c5d528f0e9b30cf5339"),
        ("G697", "#1508", "2021f1d6196fab2b8bb23fb28176f26dddbeb59b"),
        ("G698", "#1510", "86f1ffdf9d9704d15d440b21d4db628bff607cf6"),
        ("G699", "#1512", "48ca83a0f1cf13080f7ddf04a699f42942d919c9"),
        ("G700", "#1514", "c2e7d6002a912b2b712a04f0bc4976d6ba76e47b"),
        ("G701", "#1517", "b95a2d7634cdd72b2ef69fce983062aca6dcbab8"),
        ("G702", "#1520", "1746a6d0c2133f7724c57f7a26caed55c93a3e8f"),
        ("G703", "#1526", "2160c1ddef9c2bf0a8268b8ef3258ba4f965f3fd"),
        ("G704", "#1529", "0c49569129635be6a35a07a3e9cfdf3621b44c4c"),
        ("G705", "#1531", "6163d9b3589d331c6a82bb72923a91a15aef029b"),
        ("G706", "#1522", "abf6dc640eb3131564d146df9783d453d0e5c70a"),
        ("G707", "#1524", "be29c896b01df6a48502748e155e07b076c563c6"),
        ("G708", "#1533", "55d54951b677e8aa6f2d2f0bd49d278ed4e63531"),
    ];

    private static readonly string[] RangeCommits =
    [
        "8ee71bc81697b91b9e155a52a25b64225ecc7427",
        .. Units.Select(unit => unit.Merge),
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCoverExactlyG695ThroughG708WithVerifiedPrsAndMerges(string language)
    {
        var notes = Read(language);
        var listed = Regex.Matches(notes, @"(?m)^- (G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(Units.Select(unit => unit.Unit), listed);
        Assert.Equal(14, listed.Length);
        foreach (var unit in Units)
        {
            AssertUnitCitationAssociations(notes, language, unit);
        }

        Assert.Contains("git log --first-parent v0.21.0..origin/main", notes, StringComparison.Ordinal);
        Assert.Contains("git log --first-parent v0.21.0..main", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "exactly fourteen merged feature units" : "正確に十四件の merged feature unit",
            notes,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "fifteen commits" : "十五 commit", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesAccountForEveryFullFirstParentRangeCommit(string language)
    {
        var notes = Read(language);

        Assert.Equal(15, RangeCommits.Length);
        Assert.Equal(RangeCommits.Length, RangeCommits.Distinct(StringComparer.Ordinal).Count());
        foreach (var commit in RangeCommits)
        {
            Assert.Contains(commit, notes, StringComparison.Ordinal);
        }

        Assert.Contains(
            language == "en" ? "not a release execution unit" : "release execution unit ではありません",
            notes,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("post-v0.21.0 roll", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void PreviewStatementPrecedesFeatureDescriptionAndLinksPromise(string language)
    {
        var notes = Read(language);
        var preview = notes.IndexOf("preview-through-1.x", StringComparison.Ordinal);
        var feature = notes.IndexOf(
            language == "en" ? "## Fourteen merged feature units" : "## 十四件の merged feature unit",
            StringComparison.Ordinal);

        Assert.True(preview >= 0);
        Assert.True(feature > preview);
        Assert.Contains("[1.0 compatibility promise](1.0-compatibility-promise.md)", notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesMakePrepareOnlyAndNpmDistributionGapCheckable(string language)
    {
        var notes = Read(language);
        var compact = Regex.Replace(notes, @"\s+", " ");

        Assert.Contains("prepare-only", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "UNRELEASED" : "未リリース", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("G702 npm publish step", compact, StringComparison.Ordinal);
        Assert.Contains("v0.22.0", compact, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "npm organisation" : "npm organisation",
            compact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("package-name reservation", compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en" ? "distribution gap, not a defect" : "defect ではなく distribution gap",
            compact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en" ? "creates no GitHub Release or tag" : "GitHub Release または tag を作成せず",
            compact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en" ? "Release-readiness gate" : "リリース準備ゲート",
            notes,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void OriginsAndMinorRationaleRemainDistinguishable(string language)
    {
        var notes = Read(language);
        var compact = Regex.Replace(notes, @"\s+", " ");

        foreach (var term in new[] { "#1491", "#1516", "#1518", "#1527", "G704", "G696-G700", "operator" })
        {
            Assert.Contains(term, compact, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(language == "en" ? "own backlog" : "own backlog", compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "minor version" : "minor version", compact, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void BilingualCountGuardRejectsAChangedUnitCount(string language)
    {
        var notes = Read(language);
        var wrong = language == "en"
            ? notes.Replace("exactly fourteen merged feature units", "exactly thirteen merged feature units", StringComparison.Ordinal)
            : notes.Replace("正確に十四件の merged feature unit", "正確に十三件の merged feature unit", StringComparison.Ordinal);

        Assert.NotEqual(notes, wrong);
        Assert.NotEmpty(CountMismatches(wrong, language));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void AssociationGuardRejectsAUnitCitationSwap(string language)
    {
        var notes = Read(language);
        var swapped = SwapUnitCitations(notes, Units[0], Units[1]);

        var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(
            () => AssertUnitCitationAssociations(swapped, language, Units[0]));

        Assert.Contains(Units[0].Unit, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnglishAndJapaneseNotesHaveTheSameUnitAndMergeInventory()
    {
        var english = Read("en");
        var japanese = Read("ja");

        foreach (var unit in Units)
        {
            AssertUnitCitationAssociations(english, "en", unit);
            AssertUnitCitationAssociations(japanese, "ja", unit);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DeveloperReadinessMirrorsThePrepareOnlyContract(string language)
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var reference = File.ReadAllText(Path.Combine(root, "docs", language, "09-developer-reference.md"));
        var heading = language == "en"
            ? "### Next release readiness (v0.22.0)"
            : "### 次リリース準備(v0.22.0)";
        var start = reference.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var nextHeading = reference.IndexOf("\n### ", start + heading.Length, StringComparison.Ordinal);
        var section = reference[start..(nextHeading < 0 ? reference.Length : nextHeading)];

        Assert.Contains("release-notes-v0.22.0.md", section, StringComparison.Ordinal);
        Assert.Contains("G702 npm publish step", section, StringComparison.Ordinal);
        Assert.Contains("distribution gap", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("guide orchestrator-thread", section, StringComparison.Ordinal);
        Assert.Contains("ReleaseNotesV0220DocsTests", section, StringComparison.Ordinal);
        Assert.DoesNotContain("v0.21.1", section, StringComparison.Ordinal);
    }

    private static void AssertUnitCitationAssociations(
        string notes,
        string language,
        params (string Unit, string Pr, string Merge)[] units)
    {
        foreach (var unit in units)
        {
            var bullet = Regex.Match(notes, $@"(?m)^- {Regex.Escape(unit.Unit)} —[^\r\n]*$");
            Assert.True(bullet.Success, $"{language} notes are missing the bullet for {unit.Unit}.");
            Assert.Contains($"PR {unit.Pr};", bullet.Value, StringComparison.Ordinal);
            Assert.Contains($"merge commit `{unit.Merge}`", bullet.Value, StringComparison.Ordinal);

            var table = Regex.Match(notes, $@"(?m)^\| `{Regex.Escape(unit.Merge)}` \| [^\r\n]*$");
            Assert.True(table.Success, $"{language} notes are missing the accounting row for {unit.Unit}.");
            Assert.Contains(unit.Unit, table.Value, StringComparison.Ordinal);
            Assert.Contains(unit.Pr, table.Value, StringComparison.Ordinal);
        }
    }

    private static string SwapUnitCitations(
        string notes,
        (string Unit, string Pr, string Merge) first,
        (string Unit, string Pr, string Merge) second)
    {
        var lines = notes.Split('\n').ToArray();
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].StartsWith($"- {first.Unit} —", StringComparison.Ordinal))
            {
                lines[index] = lines[index]
                    .Replace($"PR {first.Pr}", $"PR {second.Pr}", StringComparison.Ordinal)
                    .Replace($"merge commit `{first.Merge}`", $"merge commit `{second.Merge}`", StringComparison.Ordinal);
            }
        }

        return string.Join('\n', lines);
    }

    private static IReadOnlyList<string> CountMismatches(string notes, string language)
    {
        var listedCount = Regex.Matches(notes, @"(?m)^- (G\d+) —").Count;
        var statedCounts = language == "en"
            ? Regex.Matches(notes, @"\b(?<count>one|two|three|fourteen|thirteen)\s+merged feature units?\b", RegexOptions.IgnoreCase)
                .Select(match => match.Groups["count"].Value.ToLowerInvariant() switch
                {
                    "one" => 1,
                    "two" => 2,
                    "three" => 3,
                    "thirteen" => 13,
                    "fourteen" => 14,
                    _ => throw new InvalidOperationException(),
                })
            : Regex.Matches(notes, @"正確に(?<count>[一二三四五六七八九十百〇零]+)件の merged feature unit")
                .Select(match => JapaneseCount(match.Groups["count"].Value));

        return statedCounts.Where(count => count != listedCount).Select(count => $"stated {count}; listed {listedCount}").ToArray();
    }

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
        RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.22.0.md"));
}
