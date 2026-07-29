using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G551: the v0.6.0 release-prep deliverable is documentation, so these tests
/// pin it the way code is pinned — both language mirrors exist, they cover
/// exactly the slices that actually merged, they carry the prepare-only
/// publishing contract, and they state the two things a reader would otherwise
/// get wrong: that G547 is retired rather than shipped, and that the v0.5.0
/// external-scheduler recommendation is superseded.
/// </summary>
public sealed class ReleaseNotesV060DocsTests
{
    /// <summary>
    /// The eleven unit ids that actually merged after v0.5.0. G547 is
    /// deliberately absent — it was terminally retired and re-cut as G551, so
    /// listing it would describe unshipped work as shipped.
    /// </summary>
    private static readonly string[] MergedSlices =
    [
        "G539", "G540", "G541", "G542", "G543",
        "G544", "G545", "G546", "G548", "G549", "G550",
    ];

    [Fact]
    public void VersionPolicy_RecordsTheReleaseToBeCut_G551()
    {
        // G557: derived from eng/version.json rather than pinned by value. The
        // readiness gate depends on the PROPERTY (a release-to-be-cut strictly
        // ahead of the published stable), and that property survives every
        // post-release roll — a hardcoded pair does not.
        RepoVersionPolicySource.AssertReleaseToBeCutIsAheadOfPublishedStable(
            RepoVersionPolicySource.Read());
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReleaseNotes_CoverEveryMergedSlice_AndNeverListG547AsShipped(string language)
    {
        var notes = ReadReleaseNotes(language);

        foreach (var slice in MergedSlices)
        {
            Assert.Contains(slice, notes, StringComparison.Ordinal);
        }

        // G547 may only appear in the retired/not-shipped explanation, never in
        // the shipped-slice enumeration.
        Assert.Contains("G547", notes, StringComparison.Ordinal);
        Assert.Contains("G551", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void EnglishNotes_ExplainG547AsRetiredAndRecutAsG551_G551()
    {
        var notes = ReadReleaseNotes("en");

        Assert.Contains("**G547 is deliberately absent from the slice list above.**", notes, StringComparison.Ordinal);
        Assert.Contains("terminal for a unit id", notes, StringComparison.Ordinal);
        Assert.Contains("**G551**, this packet", notes, StringComparison.Ordinal);
        Assert.Contains("G547 shipped no code and no documentation", notes, StringComparison.Ordinal);
        // The unit range is described honestly: twelve ids, eleven shipped.
        Assert.Contains("twelve unit ids of which **eleven are merged and shipped here**", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void JapaneseNotes_ExplainG547AsRetiredAndRecutAsG551_G551()
    {
        var notes = ReadReleaseNotes("ja");

        Assert.Contains("**G547 は上記のスライス一覧に意図的に含まれていません。**", notes, StringComparison.Ordinal);
        Assert.Contains("retire はユニット ID に 対して terminal", notes, StringComparison.Ordinal);
        Assert.Contains("新しいユニットである本パケット **G551** になります", notes, StringComparison.Ordinal);
        Assert.Contains("**本リリースでマージ・出荷されるのはそのうち 11 件**", notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReleaseNotes_GroupSlicesByTheme_AndStateTheMinorBumpRationale(string language)
    {
        var notes = ReadReleaseNotes(language);

        // Four themes, in both mirrors.
        foreach (var themeMarker in ThemeMarkers(language))
        {
            Assert.Contains(themeMarker, notes, StringComparison.Ordinal);
        }

        // Minor-bump rationale: new detection kinds, new command surface,
        // visible behavior changes, primary-model repositioning.
        foreach (var kind in new[] { "backlog-ready-idle", "blocked-label-drift", "repair-stalled" })
        {
            Assert.Contains(kind, notes, StringComparison.Ordinal);
        }

        foreach (var command in new[] { "automation runs-audit", "queue priority-drift", "automation issue-block" })
        {
            Assert.Contains(command, notes, StringComparison.Ordinal);
        }

        Assert.Contains(language == "en" ? "**minor release**" : "**minor リリース**", notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReleaseNotes_SupersedeTheV050ExternalSchedulerRecommendation(string language)
    {
        var notes = ReadReleaseNotes(language);

        if (language == "en")
        {
            Assert.Contains("#### Supersedes the v0.5.0 external-scheduler recommendation", notes, StringComparison.Ordinal);
            Assert.Contains("external cron/launchd OS-scheduler", notes, StringComparison.Ordinal);
            // The reason and its field evidence, not just the fact.
            Assert.Contains("credential store/keychain is unreachable from a cron", notes, StringComparison.Ordinal);
            Assert.Contains("silently on every run for five continuous", notes, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("#### v0.5.0 の外部スケジューラー推奨を置き換えます", notes, StringComparison.Ordinal);
            Assert.Contains("外部 cron/launchd の OS スケジューラー", notes, StringComparison.Ordinal);
            Assert.Contains("credential store / keychain に到達できない", notes, StringComparison.Ordinal);
            Assert.Contains("5 日間連続で毎回 silent に失敗", notes, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReleaseNotes_ListTheRetiredWorkarounds(string language)
    {
        var notes = ReadReleaseNotes(language);

        if (language == "en")
        {
            Assert.Contains("## Workarounds retired by this release", notes, StringComparison.Ordinal);
            Assert.Contains("**Title-convention workaround**", notes, StringComparison.Ordinal);
            Assert.Contains("**Duplicated top-level `domain:` fields**", notes, StringComparison.Ordinal);
            Assert.Contains("**Queue-state hand-edit recovery**", notes, StringComparison.Ordinal);
            Assert.Contains("**Manual repair-stall pings**", notes, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("## 本リリースで retire される workaround", notes, StringComparison.Ordinal);
            Assert.Contains("**title-convention workaround**", notes, StringComparison.Ordinal);
            Assert.Contains("**top-level `domain:` フィールドの重複**", notes, StringComparison.Ordinal);
            Assert.Contains("**queue-state の手編集による復旧**", notes, StringComparison.Ordinal);
            Assert.Contains("**repair stall の手動 ping**", notes, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReleaseNotes_ArePrepareOnly_NoTagOrReleaseOrPublishStep(string language)
    {
        var notes = ReadReleaseNotes(language);

        // The prepare-only contract and the readiness gate are both present.
        Assert.Contains("prepare-only", notes, StringComparison.Ordinal);
        Assert.Contains("G551", notes, StringComparison.Ordinal);
        Assert.Contains("release.yml", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "## Release-readiness gate (G551)" : "## リリース準備ゲート(G551)",
            notes,
            StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "## Publishing v0.6.0" : "## v0.6.0 の publish",
            notes,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PrepareOnlyDiff_AddsNoTagOrPublishAutomation_G551()
    {
        // The packet is documentation + version metadata only. If a future edit
        // ever adds a publish step to this repo's release path, the release
        // model changes and this packet's contract no longer holds — so pin
        // that release.yml still triggers on a published Release rather than on
        // the version-bump merge landing.
        var releaseWorkflow = Path.Combine(RepoRoot(), ".github", "workflows", "release.yml");
        Assert.True(File.Exists(releaseWorkflow), $"Expected {releaseWorkflow} to exist.");

        var workflow = File.ReadAllText(releaseWorkflow);
        Assert.Contains("release:", workflow, StringComparison.Ordinal);
        Assert.Contains("published", workflow, StringComparison.Ordinal);
    }

    private static string[] ThemeMarkers(string language) => language == "en"
        ?
        [
            "### Primary-model repositioning and provisioning",
            "### Stall-detection completion",
            "### Durable-state integrity",
            "### Operational guidance",
        ]
        :
        [
            "### primary モデルへの再配置と provisioning",
            "### stall 検出の完成",
            "### durable state の完全性",
            "### 運用ガイダンス",
        ];

    private static string ReadReleaseNotes(string language)
    {
        var path = Path.Combine(RepoRoot(), "docs", language, "release-notes-v0.6.0.md");
        Assert.True(File.Exists(path), $"Expected {path} to exist.");

        // Both mirrors are hard-wrapped and much of the guidance lives inside
        // blockquotes, so a sentence spans lines and carries `> ` continuation
        // markers. Strip the markers and collapse whitespace runs so the
        // assertions pin wording, not wrap points.
        var unwrapped = File.ReadAllLines(path)
            .Select(line => line.TrimStart().TrimStart('>'));

        return string.Join(' ', string.Join('\n', unwrapped)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "eng", "version.json")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate the repository root from {AppContext.BaseDirectory}.");
    }
}
