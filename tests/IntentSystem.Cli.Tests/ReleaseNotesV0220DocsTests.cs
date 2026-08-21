using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G710: the v0.22.0 released notes are a checkable, bilingual inventory of the
/// exact first-parent range and published distribution evidence.
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

    private static readonly string[] ReleaseAssets =
    [
        "intent-cli-0.22.0-linux-x64.tar.gz",
        "intent-cli-0.22.0-linux-x64.tar.gz.sha256",
        "intent-cli-0.22.0-osx-arm64.tar.gz",
        "intent-cli-0.22.0-osx-arm64.tar.gz.sha256",
        "intent-cli-0.22.0-win-x64.zip",
        "intent-cli-0.22.0-win-x64.zip.sha256",
        "intent-system-0.22.0.tgz",
        "intent-system-0.22.0.tgz.sha256",
        "j-tech-japan-intent-cli-darwin-arm64-0.22.0.tgz",
        "j-tech-japan-intent-cli-darwin-arm64-0.22.0.tgz.sha256",
        "j-tech-japan-intent-cli-linux-x64-0.22.0.tgz",
        "j-tech-japan-intent-cli-linux-x64-0.22.0.tgz.sha256",
        "j-tech-japan-intent-cli-win32-x64-0.22.0.tgz",
        "j-tech-japan-intent-cli-win32-x64-0.22.0.tgz.sha256",
        "JTechJapan.IntentSystem.Cli.0.22.0.nupkg",
        "JTechJapan.IntentSystem.Cli.0.22.0.nupkg.sha256",
    ];

    private static readonly string[] NpmAssets =
    [
        "intent-system-0.22.0.tgz",
        "intent-system-0.22.0.tgz.sha256",
        "j-tech-japan-intent-cli-darwin-arm64-0.22.0.tgz",
        "j-tech-japan-intent-cli-darwin-arm64-0.22.0.tgz.sha256",
        "j-tech-japan-intent-cli-linux-x64-0.22.0.tgz",
        "j-tech-japan-intent-cli-linux-x64-0.22.0.tgz.sha256",
        "j-tech-japan-intent-cli-win32-x64-0.22.0.tgz",
        "j-tech-japan-intent-cli-win32-x64-0.22.0.tgz.sha256",
    ];

    private const string CleanInstallCommand =
        "dotnet tool install JTechJapan.IntentSystem.Cli --version 0.22.0 --tool-path <clean-dir> --source https://api.nuget.org/v3/index.json";
    private const string CleanQueryCommand = "<clean-dir>/intent-cli --version";

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
    public void NotesMakeReleasedEvidenceAndNpmDistributionGapCheckable(string language)
    {
        var notes = Read(language);
        var compact = Regex.Replace(notes, @"\s+", " ");

        Assert.Contains(language == "en" ? "RELEASED" : "公開済み", notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prepare-only", notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UNRELEASED", notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("未リリース", notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("v0.22.0 is not released", compact, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("v0.22.0 は未リリース", compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.22.0", notes, StringComparison.Ordinal);
        Assert.Contains("c06dc49e89446bf3b723612dd72004d628914734", notes, StringComparison.Ordinal);
        Assert.Contains("31903789754", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "five jobs" : "五つの job", compact, StringComparison.OrdinalIgnoreCase);
        AssertCleanInstallEvidence(notes, language);
        Assert.Contains("https://www.nuget.org/packages/JTechJapan.IntentSystem.Cli/0.22.0", notes, StringComparison.Ordinal);
        Assert.Contains("https://api.nuget.org/v3/registration5-gz-semver2/jtechjapan.intentsystem.cli/index.json", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "all sixteen attached assets" : "十六個の asset", compact, StringComparison.OrdinalIgnoreCase);
        foreach (var asset in ReleaseAssets)
        {
            Assert.Contains(asset, notes, StringComparison.Ordinal);
        }

        Assert.Contains("G702 npm publish step", compact, StringComparison.Ordinal);
        Assert.Contains("v0.22.0", compact, StringComparison.Ordinal);
        Assert.Contains("npm organisation", compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("package-name reservation", compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NPM_TOKEN", compact, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "No npm package was published to a registry" : "registry に npm package は publish しておらず",
            compact,
            StringComparison.OrdinalIgnoreCase);
        foreach (var asset in NpmAssets)
        {
            Assert.Contains(asset, notes, StringComparison.Ordinal);
        }

        Assert.Contains(
            language == "en" ? "distribution gap, not a defect" : "defect ではなく distribution gap",
            compact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en" ? "does not execute that command and does not mutate GitHub Release state" : "command を実行せず、GitHub Release state を変更しません",
            compact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en"
                ? "gh release edit v0.22.0 --repo J-Tech-Japan/intent-system --notes-file docs/en/release-notes-v0.22.0.md"
                : "gh release edit v0.22.0 --repo J-Tech-Japan/intent-system --notes-file docs/en/release-notes-v0.22.0.md",
            notes,
            StringComparison.Ordinal);
        if (language == "en")
        {
            Assert.Contains("canonical source", compact, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains("parity mirror", compact, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("JA note で上書きしてはいけません", compact, StringComparison.Ordinal);
            Assert.DoesNotContain("--notes-file docs/ja/release-notes-v0.22.0.md", notes, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void CleanInstallGuardRejectsPackageIdAsCommand(string language)
    {
        var notes = Read(language);
        AssertCleanInstallEvidence(notes, language);

        var invalid = notes.Replace(
            CleanInstallCommand,
            "JTechJapan.IntentSystem.Cli --version 0.22.0",
            StringComparison.Ordinal);

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(
            () => AssertCleanInstallEvidence(invalid, language));
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
    public void DeveloperReadinessMirrorsTheReleasedRollContract(string language)
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var policy = RepoVersionPolicySource.Read();
        var reference = File.ReadAllText(Path.Combine(root, "docs", language, "09-developer-reference.md"));
        var heading = language == "en"
            ? $"### Next release readiness (v{policy.NextVersion})"
            : $"### 次リリース準備(v{policy.NextVersion})";
        var start = reference.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var nextHeading = reference.IndexOf("\n### ", start + heading.Length, StringComparison.Ordinal);
        var section = reference[start..(nextHeading < 0 ? reference.Length : nextHeading)];
        var compact = Regex.Replace(section, @"\s+", " ");

        Assert.Contains($"release-notes-v{policy.StableVersion}.md", section, StringComparison.Ordinal);
        Assert.Contains($"release-notes-v{policy.NextVersion}.md", section, StringComparison.Ordinal);
        Assert.Contains("v0.23.1 GitHub Release", section, StringComparison.Ordinal);
        Assert.Contains("G725 detector", section, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "no tag" : "tag または Release を作成せず",
            compact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en" ? "no package" : "package を publish せず",
            compact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("release-notes-v0.23.1.md", section, StringComparison.Ordinal);
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

    private static void AssertCleanInstallEvidence(string notes, string language)
    {
        Assert.Contains(CleanInstallCommand, notes, StringComparison.Ordinal);
        Assert.Contains(CleanQueryCommand, notes, StringComparison.Ordinal);
        Assert.Contains("intent-cli 0.22.0-c06dc49-G708", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("`JTechJapan.IntentSystem.Cli --version 0.22.0`", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "yielding exactly" : "正確に",
            notes,
            StringComparison.OrdinalIgnoreCase);
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
