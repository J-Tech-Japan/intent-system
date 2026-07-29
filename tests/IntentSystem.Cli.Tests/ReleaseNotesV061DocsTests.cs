using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G554: the v0.6.1 release-prep deliverable is documentation, so it is pinned
/// the way code is pinned — both language mirrors exist, they cover exactly the
/// two slices that shipped after v0.6.0, they carry the patch rationale and the
/// prepare-only publishing contract, and the post-release version-roll rule is
/// present in the developer reference AND in its closeout checklist. The roll
/// rule is the one a skipped step already broke in the field, so it is pinned
/// on every surface that states it.
/// </summary>
public sealed class ReleaseNotesV061DocsTests
{
    /// <summary>The two slices merged after v0.6.0 — and the only ones this release covers.</summary>
    private static readonly string[] ReleasedSlices = ["G552", "G553"];

    [Fact]
    public void VersionPolicy_RecordsTheV061ReleaseToBeCut_G554()
    {
        var policy = VersionPolicy.TryReadFromRepo(RepoRoot());

        Assert.NotNull(policy);
        Assert.Equal("0.6.0", policy.StableVersion);
        Assert.Equal("0.6.1", policy.NextVersion);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReleaseNotes_CoverExactlyG552AndG553(string language)
    {
        var notes = ReadReleaseNotes(language);

        foreach (var slice in ReleasedSlices)
        {
            Assert.Contains(slice, notes, StringComparison.Ordinal);
        }

        // Exactly these two: no slice from the v0.6.0 batch is re-listed as
        // shipping here.
        foreach (var earlier in new[] { "G549", "G550", "G551" })
        {
            Assert.DoesNotContain($"({earlier})", notes, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReleaseNotes_StateThePatchRationale_AndThePrepareOnlyContract(string language)
    {
        var notes = ReadReleaseNotes(language);

        Assert.Contains(language == "en" ? "**patch release**" : "**patch リリース**", notes, StringComparison.Ordinal);
        // The rationale is stated, not merely the label: no new command surface.
        Assert.Contains(
            language == "en" ? "neither slice ships a new command surface" : "どちらのスライスも新しい コマンドサーフェスを出荷しない",
            notes,
            StringComparison.Ordinal);

        Assert.Contains("prepare-only", notes, StringComparison.Ordinal);
        Assert.Contains("release.yml", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "## Release-readiness gate (G554)" : "## リリース準備ゲート(G554)",
            notes,
            StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "## Publishing v0.6.1" : "## v0.6.1 の publish",
            notes,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReleaseNotes_NameTheFieldIncidentsBehindBothSlices(string language)
    {
        var notes = ReadReleaseNotes(language);

        // G552: the nine-hour design hold.
        Assert.Contains(language == "en" ? "**nine hours**" : "**9 時間**", notes, StringComparison.Ordinal);
        // G553: the sekiban #1783 WIP starvation.
        Assert.Contains("#1783", notes, StringComparison.Ordinal);
        Assert.Contains("sekiban-as-a-service", notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReleaseNotes_CarryThePreviewChannelNoteAboutTheRollRule(string language)
    {
        var notes = ReadReleaseNotes(language);

        // The preview-sorts-below-release incident, its cause, and what a
        // preview-channel user should do about it.
        Assert.Contains("0.6.0-preview", notes, StringComparison.Ordinal);
        Assert.Contains("0.6.2-preview", notes, StringComparison.Ordinal);
        Assert.Contains("dotnet tool update", notes, StringComparison.Ordinal);

        if (language == "en")
        {
            Assert.Contains("sorts **below** the released", notes, StringComparison.Ordinal);
            Assert.Contains("roll `eng/version.json` in a follow-up commit", notes, StringComparison.Ordinal);
            Assert.Contains("not** renumbered retroactively", notes, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("リリース済みの `0.6.0` より **下** にソート", notes, StringComparison.Ordinal);
            Assert.Contains("follow-up commit で `eng/version.json` を roll", notes, StringComparison.Ordinal);
            Assert.Contains("遡って番号を振り直しません", notes, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DeveloperReference_CarriesTheRollRule_AndItsCloseoutChecklistStep(string language)
    {
        var reference = ReadDeveloperReference(language);

        Assert.Contains(
            language == "en" ? "### Post-release version roll (G554) — required, immediate" : "### リリース後の version roll(G554) — 必須・即時",
            reference,
            StringComparison.Ordinal);

        // stable = released, next = next patch.
        Assert.Contains(
            language == "en" ? "`stableVersion` = the version just released" : "`stableVersion` = 今リリースした バージョン",
            reference,
            StringComparison.Ordinal);

        // The reason, with the field incident that proves it matters.
        Assert.Contains("2026-07-29", reference, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "sorts **below** its own release version" : "そのリリース バージョンより **下** にソート",
            reference,
            StringComparison.Ordinal);

        // And it is a numbered closeout step, not advice floating next to one.
        Assert.Contains(
            language == "en" ? "**Release closeout checklist** (the roll is step 4" : "**リリース closeout チェックリスト**(roll はステップ 4",
            reference,
            StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "4. **Roll `eng/version.json` in a follow-up commit**" : "4. **follow-up commit で `eng/version.json` を roll する**",
            reference,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DeveloperReference_VersionFlowIsOnThe061Line(string language)
    {
        var reference = ReadDeveloperReference(language);

        Assert.Contains("\"stableVersion\": \"0.6.0\"", reference, StringComparison.Ordinal);
        Assert.Contains("\"nextVersion\": \"0.6.1\"", reference, StringComparison.Ordinal);
        // Post-release main builds are described on the NEXT line, above the
        // release — the whole point of the rule.
        Assert.Contains("0.6.2-preview.<run>.<attempt>", reference, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DeveloperReference_ActiveReadinessIsTheV061Section_NotAStaleV060One(string language)
    {
        var reference = ReadDeveloperReference(language);

        Assert.Contains(
            language == "en" ? "### Next release readiness (v0.6.1)" : "### 次リリース準備(v0.6.1)",
            reference,
            StringComparison.Ordinal);
        // The superseded section is re-cut, not left standing beside the new one.
        Assert.DoesNotContain(
            language == "en" ? "### Next release readiness (v0.6.0)" : "### 次リリース準備(v0.6.0)",
            reference,
            StringComparison.Ordinal);

        // It states what actually shipped and what is being cut.
        Assert.Contains(
            language == "en" ? "**`v0.6.0` shipped**" : "**`v0.6.0` は publish 済み**",
            reference,
            StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "a **patch** bump, not a minor" : "minor ではなく **patch** バンプ",
            reference,
            StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.6.1.md", reference, StringComparison.Ordinal);
        foreach (var slice in ReleasedSlices)
        {
            Assert.Contains(slice, reference, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DeveloperReference_CarriesNoStaleActiveVersionGuidance(string language)
    {
        var reference = ReadDeveloperReference(language);

        // Negative coverage: the pre-roll policy values and the deferral that
        // caused the 2026-07-29 preview-channel break must not reappear as
        // active guidance.
        Assert.DoesNotContain("\"stableVersion\": \"0.5.0\"", reference, StringComparison.Ordinal);
        Assert.DoesNotContain(
            language == "en" ? "stableVersion 0.5.0 (published)" : "stableVersion 0.5.0（公開済み）",
            reference,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            language == "en" ? "deferred to the NEXT release-prep packet" : "次の release-prep パケットに委ねられます",
            reference,
            StringComparison.Ordinal);

        // And the readiness checks name the current line.
        Assert.Contains(
            language == "en" ? "stableVersion 0.6.0 (published), nextVersion 0.6.1 (to release)" : "stableVersion 0.6.0（公開済み）, nextVersion 0.6.1（リリース対象）",
            reference,
            StringComparison.Ordinal);
        Assert.Contains("JTechJapan.IntentSystem.Cli.0.6.1.nupkg", reference, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReleaseNotes_PostReleaseChecklistNotifiesCompletion_RatherThanRequestingPublication(string language)
    {
        var notes = ReadReleaseNotes(language);

        // The checklist is defined as post-Release, so by the time it runs the
        // Release is already published — the item reports completion.
        Assert.Contains(
            language == "en" ? "publication **and** verification of `v0.6.1` are complete" : "`v0.6.1` の publish **および** 検証が完了したことを",
            notes,
            StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "belongs to the pre-release phase" : "リリース前フェーズに属します",
            notes,
            StringComparison.Ordinal);

        // The stale publish-request wording is gone from the post-release list.
        Assert.DoesNotContain(
            language == "en" ? "Notify the operator to publish the `v0.6.1` GitHub Release" : "オペレーターに `v0.6.1` GitHub Release の publish を通知し",
            notes,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PrepareOnlyDiff_AddsNoPublishAutomation_G554()
    {
        // The packet is documentation + version metadata only. Pin that
        // release.yml still triggers on a published Release rather than on the
        // version-bump merge landing.
        var releaseWorkflow = Path.Combine(RepoRoot(), ".github", "workflows", "release.yml");
        Assert.True(File.Exists(releaseWorkflow), $"Expected {releaseWorkflow} to exist.");

        var workflow = File.ReadAllText(releaseWorkflow);
        Assert.Contains("release:", workflow, StringComparison.Ordinal);
        Assert.Contains("published", workflow, StringComparison.Ordinal);
    }

    private static string ReadReleaseNotes(string language) =>
        ReadCollapsed(Path.Combine(RepoRoot(), "docs", language, "release-notes-v0.6.1.md"));

    private static string ReadDeveloperReference(string language) =>
        ReadCollapsed(Path.Combine(RepoRoot(), "docs", language, "09-developer-reference.md"));

    private static string ReadCollapsed(string path)
    {
        Assert.True(File.Exists(path), $"Expected {path} to exist.");

        // Both mirrors are hard-wrapped and some guidance lives inside
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
