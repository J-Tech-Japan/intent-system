using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G742: the v0.25.0 release-prep notes pin the measured command-surface
/// reason, the exact four-commit first-parent accounting, and the three-unit
/// release inventory in both language mirrors.
/// </summary>
public sealed class ReleaseNotesV0250DocsTests
{
    private const string PreparedFunctionalHead =
        "5c4af5d88ddcfa47335bad4df56ad3e40dae9140";
    private const string BuiltDisplayIdentity = "intent-cli 0.24.1-5c4af5d-G741";
    private const string InstalledDisplayIdentity = "intent-cli 0.24.0-df472fe-G737";
    private const string CurrentStableInstallDisplayIdentity = "intent-cli 0.29.0-65e02d8-G772";

    private static readonly (string Unit, string Pr, string Merge)[] Units =
    [
        ("G738", "#1609", "f0a30f08de6281b34b6fd4a5e8732243ad176053"),
        ("G739", "#1611", "f0ea90fd3df65de3f1b95bd38f6f8c79b011d171"),
        ("G741", "#1614", "5c4af5d88ddcfa47335bad4df56ad3e40dae9140"),
    ];

    private static readonly (string Unit, string Merge)[] FirstParentCommits =
    [
        ("G738", "f0a30f08de6281b34b6fd4a5e8732243ad176053"),
        ("G739", "f0ea90fd3df65de3f1b95bd38f6f8c79b011d171"),
        ("G740", "8bcab9766412e3c946f3299274f969277135eb03"),
        ("G741", "5c4af5d88ddcfa47335bad4df56ad3e40dae9140"),
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCoverExactlyTheThreeReleaseUnits(string language)
    {
        var notes = Read(language);
        var listed = Regex.Matches(notes, @"(?m)^- (G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(Units.Select(unit => unit.Unit), listed);
        Assert.Equal(3, listed.Length);
        Assert.DoesNotContain("- G740 —", notes, StringComparison.Ordinal);

        foreach (var unit in Units)
        {
            var bullet = Regex.Match(
                notes,
                $@"(?ms)^- {Regex.Escape(unit.Unit)} —.*?(?=^- |^## |\z)");
            Assert.True(bullet.Success, $"{language} notes are missing {unit.Unit}.");
            Assert.Contains($"PR {unit.Pr};", bullet.Value, StringComparison.Ordinal);
            Assert.Contains($"merge commit {unit.Merge}", bullet.Value, StringComparison.Ordinal);

            var accounting = Regex.Match(
                notes,
                $@"(?m)^\| {Regex.Escape(unit.Merge)} \| [^\r\n]*$");
            Assert.True(
                accounting.Success,
                $"{language} notes are missing first-parent accounting for {unit.Unit}.");
            Assert.Contains(unit.Unit, accounting.Value, StringComparison.Ordinal);
            Assert.Contains(unit.Pr, accounting.Value, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesAccountForExactlyFourFirstParentCommitsAndClassifyTheRoll(string language)
    {
        var notes = Read(language);

        Assert.Contains("git rev-list --first-parent --reverse v0.24.0", notes, StringComparison.Ordinal);
        Assert.Contains("git rev-list --first-parent --count v0.24.0", notes, StringComparison.Ordinal);
        Assert.Contains("# 4", notes, StringComparison.Ordinal);

        foreach (var commit in FirstParentCommits)
        {
            Assert.Contains(commit.Merge, notes, StringComparison.Ordinal);
        }

        Assert.Contains("G740 post-release version roll to the 0.24.1 placeholder; not a release unit", notes, StringComparison.Ordinal);
        Assert.Contains("classified only", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "release inventory is exactly G738, G739, and G741" : "release inventory は G738、G739、G741 の三つだけです", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void MeasuredIdentityAndSurfaceDifferenceArePinned(string language)
    {
        var notes = Read(language);

        Assert.Contains(PreparedFunctionalHead, notes, StringComparison.Ordinal);
        Assert.Contains(BuiltDisplayIdentity, notes, StringComparison.Ordinal);
        Assert.Contains(InstalledDisplayIdentity, notes, StringComparison.Ordinal);
        Assert.Contains("Release build", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("installed", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("session-layer topology record --model <text>", notes, StringComparison.Ordinal);
        Assert.Contains("session-layer topology record --reasoning-effort <text>", notes, StringComparison.Ordinal);
        Assert.Contains("notify supervise --delegation-execution-window-seconds <seconds>", notes, StringComparison.Ordinal);
        Assert.Contains("default 300", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("absent", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("present", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "not an enumerated model list" : "enumerated model list や measurement ではありません", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void EntriesRemainOperatorObservableAndDoNotInventActions(string language)
    {
        var notes = Read(language);
        var g738 = FindEntry(notes, "G738");
        var g739 = FindEntry(notes, "G739");
        var g741 = FindEntry(notes, "G741");

        Assert.Contains(language == "en" ? "cannot fail or hang" : "fail や hang", g738, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "Windows users" : "Windows user", g738, StringComparison.Ordinal);
        Assert.Contains("background", g738, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("topology", g739, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "Who did this work" : "recorded topology", g739, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "operator declarations rather than measurements" : "operator declaration", g739, StringComparison.OrdinalIgnoreCase);

        foreach (var condition in new[]
                 {
                     "delivery succeeded",
                     "recipient is idle",
                     "configured window elapsed",
                     "canonical report is absent",
                     "expected artifact is absent",
                     "durable target-entity transition is absent",
                 })
        {
            Assert.Contains(condition, Regex.Replace(g741, @"\s+", " ").Replace("recipient idle", "recipient is idle", StringComparison.OrdinalIgnoreCase).Replace("canonical report absent", "canonical report is absent", StringComparison.OrdinalIgnoreCase).Replace("expected artifact absent", "expected artifact is absent", StringComparison.OrdinalIgnoreCase).Replace("durable target-entity transition absent", "durable target-entity transition is absent", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(language == "en" ? "Slow-but-started work is not a finding" : "slow-but-started は finding ではなく", g741, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "without prompting, restarting, or mutating" : "prompt、restart、mutation", g741, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "Six motivating incidents" : "六つの", g741, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "no seat is named" : "seat は名指ししません", g741, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesStayPrepareOnly(string language)
    {
        var notes = Read(language);

        Assert.Contains("PREPARED / NOT PUBLISHED", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "no tag" : "tag", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "no GitHub Release" : "GitHub Release はまだ存在せず", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("package publish", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("post-release roll", notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("release-notes-v0.24.1.md", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionPolicyAndStubDeletionMatchTheCurrentLine()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var policy = RepoVersionPolicySource.Read();

        Assert.Equal("0.30.0", policy.StableVersion);
        Assert.Equal("0.30.1", policy.NextVersion);

        foreach (var language in new[] { "en", "ja" })
        {
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.25.0.md")));
            Assert.False(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.24.1.md")));
            Assert.False(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.25.1.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.26.0.md")));
            Assert.False(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.26.1.md")));
            Assert.False(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.27.0.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.27.1.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.28.0.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.28.1.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.29.0.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.29.1.md")));
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DeveloperReferenceReadinessMirrorsTheMeasuredRelease(string language)
    {
        var reference = File.ReadAllText(Path.Combine(
            RepoVersionPolicySource.RepoRoot(), "docs", language, "09-developer-reference.md"));

        Assert.Contains(
            language == "en" ? "Next release readiness (v0.30.1)" : "次リリース準備(v0.30.1)",
            reference,
            StringComparison.Ordinal);
        Assert.Contains(CurrentStableInstallDisplayIdentity, reference, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.26.0.md", reference, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.28.0.md", reference, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.28.1.md", reference, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.29.0.md", reference, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.29.1.md", reference, StringComparison.Ordinal);
        Assert.DoesNotContain("release-notes-v0.27.0.md", reference, StringComparison.Ordinal);
        Assert.DoesNotContain("release-notes-v0.24.1.md", reference, StringComparison.Ordinal);
        Assert.Contains("ReleaseNotesV0250DocsTests", reference, StringComparison.Ordinal);
        Assert.Contains("JapaneseTerminologyGuardG613Tests", reference, StringComparison.Ordinal);
        Assert.Contains("Full Release suite", reference, StringComparison.Ordinal);

        foreach (var unit in FirstParentCommits)
        {
            Assert.Contains(unit.Merge, reference, StringComparison.Ordinal);
        }
    }

    private static string FindEntry(string notes, string unit)
    {
        var match = Regex.Match(
            notes,
            $@"(?ms)^- {Regex.Escape(unit)} —.*?(?=^- |^## |\z)");
        return match.Success ? match.Value : string.Empty;
    }

    private static string Read(string language) => File.ReadAllText(Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.25.0.md"));
}
