using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G872: the 0.32.1-preview.1 notes and install guidance describe the measured
/// source candidate without changing stable release policy or claiming that
/// candidate assets have been published.
/// </summary>
public sealed class ReleaseNotesV0321Preview1DocsTests
{
    private const string Version = "0.32.1-preview.1";
    private const string Baseline = "6054eb9f46d8fe4e87e370a53bd527c116e3a05b";

    private static readonly (string Unit, string Pr)[] SourceUnits =
    [
        ("G851", "1848"),
        ("G852", "1850"),
        ("G844", "1851"),
        ("G843", "1852"),
        ("G814", "1854"),
        ("G853", "1856"),
        ("G854", "1860"),
        ("G855", "1863"),
        ("G856", "1871"),
        ("G857", "1874"),
        ("G858", "1877"),
        ("G859", "1880"),
        ("G860", "1882"),
        ("G861", "1884"),
        ("G862", "1886"),
        ("G863", "1888"),
        ("G864", "1890"),
        ("G865", "1892"),
        ("G866", "1894"),
        ("G867", "1896"),
        ("G868", "1898"),
        ("G869", "1900"),
        ("G870", "1902"),
        ("G871", "1904"),
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesLinkEveryMeasuredUnitExactlyOnce(string language)
    {
        var notes = Normalize(Read(language, $"release-notes-v{Version}.md"));
        var actualPrs = Regex.Matches(
                notes,
                @"https://github\.com/J-Tech-Japan/intent-system/pull/(\d+)",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(SourceUnits.Length, actualPrs.Length);
        Assert.Equal(SourceUnits.Select(unit => unit.Pr), actualPrs);
        foreach (var (unit, pr) in SourceUnits)
        {
            Assert.Contains($"| {unit} | [PR #{pr}](https://github.com/J-Tech-Japan/intent-system/pull/{pr}) |", notes, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("G872 |", notes, StringComparison.Ordinal);
        Assert.Contains("#1864", notes, StringComparison.Ordinal);
        Assert.Contains("#1865", notes, StringComparison.Ordinal);
        Assert.Contains("#1866", notes, StringComparison.Ordinal);
        Assert.Contains("#1867", notes, StringComparison.Ordinal);
        Assert.Contains("#1868", notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void IndexAndInstallGuideExposeOptInPreviewAndPreserveStableCommands(string language)
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var index = File.ReadAllText(Path.Combine(root, "docs", language, "README.md"));
        var install = Read(language, "01-install.md");

        Assert.Contains($"release-notes-v{Version}.md", index, StringComparison.Ordinal);
        Assert.Contains("dotnet tool install -g JTechJapan.IntentSystem.Cli", install, StringComparison.Ordinal);
        Assert.Contains("npm install -g intent-system", install, StringComparison.Ordinal);
        Assert.Contains(
            $"dotnet tool install --tool-path <isolated-directory> JTechJapan.IntentSystem.Cli --version {Version}",
            install,
            StringComparison.Ordinal);
        Assert.Contains($"npm install -g intent-system@{Version}", install, StringComparison.Ordinal);
        Assert.Contains("intent-system@0.32.0", install, StringComparison.Ordinal);
        Assert.Contains("intent-cli --version", install, StringComparison.Ordinal);
        Assert.Contains("macOS-arm64", install, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Linux-x64", install, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Windows-x64", install, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".sha256", install, StringComparison.Ordinal);
        Assert.Contains("PATH", install, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesSeparatePreparationSourceTestsAndFuturePublicArtifacts(string language)
    {
        var notes = Normalize(Read(language, $"release-notes-v{Version}.md"));

        Assert.Contains(Version, notes, StringComparison.Ordinal);
        Assert.Contains(Baseline, notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "24 commits" : "24 件のコミット", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("10,701", notes, StringComparison.Ordinal);
        Assert.Contains("NotExecuted", notes, StringComparison.Ordinal);
        Assert.Contains("0.32.1-preview.483.1", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("/private/tmp/", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("/Users/tomohisa/", notes, StringComparison.Ordinal);

        if (language == "en")
        {
            Assert.Contains("PREPARED — NOT PUBLISHED", notes, StringComparison.Ordinal);
            Assert.Contains("separate from the replaceable", notes, StringComparison.Ordinal);
            Assert.Contains("destination, not evidence", notes, StringComparison.Ordinal);
            Assert.Contains("not authenticated", notes, StringComparison.Ordinal);
            Assert.Contains("declared-not-verified", notes, StringComparison.Ordinal);
            Assert.Contains("partial or foreign residual", notes, StringComparison.Ordinal);
            Assert.Contains("published GitHub Release event rebuilds", notes, StringComparison.Ordinal);
            Assert.Contains("G856–G861", notes, StringComparison.Ordinal);
            Assert.Contains("no time-based preview expiry", notes, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("準備中 — 未公開", notes, StringComparison.Ordinal);
            Assert.Contains("別の文書です", notes, StringComparison.Ordinal);
            Assert.Contains("公開先", notes, StringComparison.Ordinal);
            Assert.Contains("証拠ではありません", notes, StringComparison.Ordinal);
            Assert.Contains("認証されません", notes, StringComparison.Ordinal);
            Assert.Contains("declared-not-verified", notes, StringComparison.Ordinal);
            Assert.Contains("不完全な出力や別プロセスが置いたファイル", notes, StringComparison.Ordinal);
            Assert.Contains("別途確認", notes, StringComparison.Ordinal);
            Assert.Contains("G856–G861", notes, StringComparison.Ordinal);
            Assert.Contains("有効期限が設定されていない", notes, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void StablePolicyAndReplaceablePlaceholdersRemainUnchanged()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        Assert.Equal(
            "{\n  \"stableVersion\": \"0.32.0\",\n  \"nextVersion\": \"0.32.1\"\n}\n",
            File.ReadAllText(Path.Combine(root, "eng", "version.json")));

        foreach (var language in new[] { "en", "ja" })
        {
            var placeholder = Read(language, "release-notes-v0.32.1.md");
            Assert.Contains("DRAFT", placeholder, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("replaceable", placeholder, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("G851", placeholder, StringComparison.Ordinal);
        }
    }

    private static string Read(string language, string fileName) => File.ReadAllText(Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "docs", language, fileName));

    private static string Normalize(string value) => Regex.Replace(value, @"\s+", " ").Trim();
}
