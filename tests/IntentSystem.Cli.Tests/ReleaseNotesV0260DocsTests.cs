using System.Security.Cryptography;
using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G749/G752: the frozen v0.26.0 release-prep notes retain their measured
/// evidence while the post-release roll pins the current v0.26.1 placeholder
/// and readiness mirrors in both languages.
/// </summary>
public sealed class ReleaseNotesV0260DocsTests
{
    private const string PreparedHead =
        "a49ad93c36bd93d1ccc9317622d36fa01ea346b8";
    private const string BuiltDisplayIdentity = "intent-cli 0.25.1-a49ad93-G748";
    private const string FinalBuiltDisplayIdentity = "intent-cli 0.26.0-a49ad93-G748";
    private const string InstalledDisplayIdentity = "intent-cli 0.25.0-74a1c72-G741";
    private const string CurrentStableInstallDisplayIdentity = "intent-cli 0.26.0-93f07f8-G749";
    private const string ArchiveUsage =
        "notify supervise archive --domain <d> --team <t> [--live-window-days <days>] [--dry-run|--write] [--format markdown|json]";

    private static readonly (string Unit, string Pr, string Merge)[] Units =
    [
        ("G743", "#1620", "1ad68963b65a1fe4978d3a0e83d0812842a2de29"),
        ("G744", "#1621", "0e97529c64294677b41e49cd87a40920c1dd3d4e"),
        ("G746", "#1626", "d112dd957826864124d4b8f0d8c1940d4145e1fe"),
        ("G747", "#1627", "7e7d16e4639f22530843b19f065b5a101cf1b0b4"),
        ("G748", "#1629", "a49ad93c36bd93d1ccc9317622d36fa01ea346b8"),
    ];

    private static readonly (string Unit, string Merge)[] FirstParentCommits =
    [
        ("G743", "1ad68963b65a1fe4978d3a0e83d0812842a2de29"),
        ("G744", "0e97529c64294677b41e49cd87a40920c1dd3d4e"),
        ("G745", "b8f249e965cad2c3c2e19dda9dd99e726324485d"),
        ("G746", "d112dd957826864124d4b8f0d8c1940d4145e1fe"),
        ("G747", "7e7d16e4639f22530843b19f065b5a101cf1b0b4"),
        ("G748", "a49ad93c36bd93d1ccc9317622d36fa01ea346b8"),
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCoverExactlyTheFiveReleaseUnits(string language)
    {
        var notes = Read(language);
        var listed = Regex.Matches(notes, @"(?m)^- (G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(Units.Select(unit => unit.Unit), listed);
        Assert.Equal(5, listed.Length);
        Assert.DoesNotContain("- G745 —", notes, StringComparison.Ordinal);

        foreach (var unit in Units)
        {
            var bullet = FindEntry(notes, unit.Unit);
            Assert.True(bullet.Length > 0, $"{language} notes are missing {unit.Unit}.");
            Assert.Contains($"PR {unit.Pr};", bullet, StringComparison.Ordinal);
            Assert.Contains($"merge commit `{unit.Merge}`", bullet, StringComparison.Ordinal);

            var accounting = Regex.Match(
                notes,
                $@"(?m)^\| `{Regex.Escape(unit.Merge)}` \| [^\r\n]*$");
            Assert.True(accounting.Success, $"{language} notes are missing {unit.Unit} accounting.");
            Assert.Contains(unit.Unit, accounting.Value, StringComparison.Ordinal);
            Assert.Contains(unit.Pr, accounting.Value, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesAccountForExactlySixFirstParentCommitsAndClassifyG745Roll(string language)
    {
        var notes = Read(language);

        Assert.Contains("git rev-list --first-parent --reverse v0.25.0", notes, StringComparison.Ordinal);
        Assert.Contains("git rev-list --first-parent --count v0.25.0", notes, StringComparison.Ordinal);
        Assert.Contains("# 6", notes, StringComparison.Ordinal);

        foreach (var commit in FirstParentCommits)
        {
            Assert.Contains(commit.Merge, notes, StringComparison.Ordinal);
        }

        Assert.Contains("G745", notes, StringComparison.Ordinal);
        Assert.Contains("not a release unit", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("classified only", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en"
                ? "release inventory is exactly G743, G744, G746, G747, and G748"
                : "release inventory は G743、G744、G746、G747、G748 の五つだけです",
            notes,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void OwnBuildIdentityAndOnlyArchiveSurfaceArePinned(string language)
    {
        var notes = Read(language);

        Assert.Contains(PreparedHead, notes, StringComparison.Ordinal);
        Assert.Contains(BuiltDisplayIdentity, notes, StringComparison.Ordinal);
        Assert.Contains(FinalBuiltDisplayIdentity, notes, StringComparison.Ordinal);
        Assert.Contains(InstalledDisplayIdentity, notes, StringComparison.Ordinal);
        Assert.Contains("eng/version.json", notes, StringComparison.Ordinal);
        Assert.Contains("Unknown argument 'archive'.", notes, StringComparison.Ordinal);
        Assert.Contains(ArchiveUsage, notes, StringComparison.Ordinal);
        Assert.Contains("automation", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("claim", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("worker", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("byte-identical", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("state-doctor", notes, StringComparison.Ordinal);
        Assert.Contains("closeout-drift-check", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "no other addition was found" : "ほかの追加はありません",
            notes,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void EntriesStateTheHonestOperatorObservableOutcomes(string language)
    {
        var notes = Read(language);
        var g743 = FindEntry(notes, "G743");
        var g744 = FindEntry(notes, "G744");
        var g746 = FindEntry(notes, "G746");
        var g747 = FindEntry(notes, "G747");
        var g748 = FindEntry(notes, "G748");

        Assert.Contains("pre-commit", g743, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cleanup", g743, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bounded", g743, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("v0.25.0", g743, StringComparison.Ordinal);

        Assert.Contains("archive", g744, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("discard", g744, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("duplicate", g744, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("duplicate", g746, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("closeout", g746, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("state-doctor", g746, StringComparison.Ordinal);
        Assert.Contains("ambiguous", g746, StringComparison.OrdinalIgnoreCase);
        AssertG746ConsumerRecoveryChain(g746, language);

        Assert.Contains("default branch", g747, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JSON stdout", g747, StringComparison.Ordinal);
        Assert.Contains("pre-commit", g747, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("idle", g748, StringComparison.Ordinal);
        Assert.Contains("done", g748, StringComparison.Ordinal);
        Assert.Contains("blocked", g748, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unknown", g748, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sixteen qualifying incidents", g748, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("zero", g748, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("operator-observable", g748, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertG746ConsumerRecoveryChain(string entry, string language)
    {
        Assert.Contains("#1622", entry, StringComparison.Ordinal);
        Assert.Contains("closeout-drift-check", entry, StringComparison.Ordinal);
        Assert.Contains("duplicate-key", entry, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("crash", entry, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("canonical", entry, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".intent-cli/queue-state.json", entry, StringComparison.Ordinal);

        if (language == "en")
        {
            Assert.Contains("could not recover", entry, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("had to hand-edit", entry, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("replaces that manual recovery", entry, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains("recovery できず", entry, StringComparison.Ordinal);
            Assert.Contains("手動編集", entry, StringComparison.Ordinal);
            Assert.Contains("手動 recovery を置き換え", entry, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesRemainPrepareOnly(string language)
    {
        var notes = Read(language);

        Assert.Contains("PREPARED / NOT PUBLISHED", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "no tag" : "tag、publish、package release",
            notes,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en" ? "no GitHub Release" : "GitHub Release はまだ存在せず",
            notes,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("package publish", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("post-release roll", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source runtime", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VersionPolicyIsExactAndCurrentPlaceholderFilesArePresent()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var policyPath = Path.Combine(root, "eng", "version.json");
        Assert.Equal(
            "{\n  \"stableVersion\": \"0.26.0\",\n  \"nextVersion\": \"0.26.1\"\n}\n",
            File.ReadAllText(policyPath));

        var policy = RepoVersionPolicySource.Read();
        Assert.Equal("0.26.0", policy.StableVersion);
        Assert.Equal("0.26.1", policy.NextVersion);

        foreach (var language in new[] { "en", "ja" })
        {
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.25.0.md")));
            Assert.False(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.25.1.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.26.0.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.26.1.md")));
        }
    }

    [Fact]
    public void ShippedV0250NoteBytesRemainPinned()
    {
        var root = RepoVersionPolicySource.RepoRoot();

        Assert.Equal(
            "7f82cfc9f6f5caff50b36659abdb062e9e85585c2cac10d9a2eddc871e805d5f",
            Sha256(Path.Combine(root, "docs", "en", "release-notes-v0.25.0.md")));
        Assert.Equal(
            "1310ca9c58620dec4a2a40b9cc25426d2299e5d7a0c230c1fd11409d2f3b9ed1",
            Sha256(Path.Combine(root, "docs", "ja", "release-notes-v0.25.0.md")));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReadinessMirrorsCurrentPostReleasePlaceholder(string language)
    {
        var readiness = ReadCurrentReadiness(language);

        Assert.Contains(
            language == "en" ? "Next release readiness (v0.26.1)" : "次リリース準備(v0.26.1)",
            readiness,
            StringComparison.Ordinal);
        Assert.Contains("0.25.0", readiness, StringComparison.Ordinal);
        Assert.Contains("0.26.0", readiness, StringComparison.Ordinal);
        Assert.Contains("0.26.1", readiness, StringComparison.Ordinal);
        Assert.Contains(CurrentStableInstallDisplayIdentity, readiness, StringComparison.Ordinal);
        Assert.Contains("placeholder", readiness, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("replaceable", readiness, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en" ? "not a changelog" : "changelog ではありません",
            readiness,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bb9754859ac8055adbd504f294145b7494668c1a", readiness, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "silence is non-evidence" : "silence は non-evidence",
            readiness,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(PreparedHead, readiness, StringComparison.Ordinal);
        Assert.Contains(BuiltDisplayIdentity, readiness, StringComparison.Ordinal);
        Assert.Contains(FinalBuiltDisplayIdentity, readiness, StringComparison.Ordinal);
        Assert.Contains(InstalledDisplayIdentity, readiness, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.26.0.md", readiness, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.26.1.md", readiness, StringComparison.Ordinal);
        Assert.DoesNotContain("release-notes-v0.25.1.md", readiness, StringComparison.Ordinal);
        Assert.Contains("byte-identical", readiness, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("G743", readiness, StringComparison.Ordinal);
        Assert.Contains("G744", readiness, StringComparison.Ordinal);
        Assert.Contains("G746", readiness, StringComparison.Ordinal);
        Assert.Contains("G747", readiness, StringComparison.Ordinal);
        Assert.Contains("G748", readiness, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void CurrentPlaceholderStubsDescribeTheirOwnReplaceableRole(string language)
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var stub = File.ReadAllText(Path.Combine(
            root, "docs", language, "release-notes-v0.26.1.md"));

        Assert.Contains("0.26.1", stub, StringComparison.Ordinal);
        Assert.Contains("DRAFT", stub, StringComparison.Ordinal);
        Assert.Contains("replaceable", stub, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en" ? "not a changelog" : "changelog ではありません",
            stub,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("G743", stub, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippedV0260NoteBytesRemainPinned()
    {
        var root = RepoVersionPolicySource.RepoRoot();

        Assert.Equal(
            "b385042d2276067120d1e9412b3a65cbf0d725cee63a93940736ea11472f4cbe",
            Sha256(Path.Combine(root, "docs", "en", "release-notes-v0.26.0.md")));
        Assert.Equal(
            "11a859a307bf2d07c239e7c30f7db95ee78b57f72a3415fe0d047f8ce68e9f9f",
            Sha256(Path.Combine(root, "docs", "ja", "release-notes-v0.26.0.md")));
    }

    private static string FindEntry(string notes, string unit)
    {
        var match = Regex.Match(
            notes,
            $@"(?ms)^- {Regex.Escape(unit)} —.*?(?=^- |^## |\z)");
        return match.Success ? match.Value : string.Empty;
    }

    private static string Read(string language) => File.ReadAllText(Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.26.0.md"));

    private static string ReadCurrentReadiness(string language)
    {
        var content = File.ReadAllText(Path.Combine(
            RepoVersionPolicySource.RepoRoot(), "docs", language, "09-developer-reference.md"));
        var heading = language == "en" ? "### Next release readiness (v0.26.1)" : "### 次リリース準備(v0.26.1)";
        var start = content.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing current readiness heading in {language}.");
        var end = content.IndexOf("**Previous v0.25.0 preparation evidence", start, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing prior-readiness boundary in {language}.");
        return content[start..end];
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
