using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G737: the prepare-only v0.24.0 notes pin the six release units after the
/// pre-rolled v0.23.3 placeholder, account for all seven first-parent commits,
/// and preserve the measured identity of the functional head outside G737.
/// </summary>
public sealed class ReleaseNotesV0240DocsTests
{
    private const string PreparedFunctionalHead =
        "a7d10026a9a4dd2693f464a5c5e34ce134b2c661";
    private const string DisplayIdentity = "intent-cli 0.23.3-a7d1002-G734";
    private const string RetiredRoll =
        "3debf8ee2f571612f969e18ac46898de1057457f";

    private static readonly (string Unit, string Pr, string Merge)[] Units =
    [
        ("G731", "#1589", "d168fac3cbef482879aa9521f6478e7d3a8dc6d1"),
        ("G732", "#1591", "37068fa076ccf9eed5f1f87f92075756f4b5abf7"),
        ("G733", "#1595", "0bb78b85df6467a1ebadb5c9d35e4a5ffb4c9072"),
        ("G734", "#1598", "4aea6b5ef24cf86d8ef6cc2aba88b5ecf02d4e65"),
        ("G735", "#1599", "2d77c557e7e7871fac70d17906c18b0c4416f185"),
        ("G736", "#1600", "a7d10026a9a4dd2693f464a5c5e34ce134b2c661"),
    ];

    private static readonly string[] FirstParentRange =
    [
        RetiredRoll,
        .. Units.Select(unit => unit.Merge),
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCoverExactlyG731ThroughG736WithVerifiedPrsAndMerges(string language)
    {
        var notes = Read(language);
        var listed = Regex.Matches(notes, @"(?m)^- (G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(Units.Select(unit => unit.Unit), listed);
        Assert.Equal(6, listed.Length);
        Assert.DoesNotContain("- G730 —", notes, StringComparison.Ordinal);

        foreach (var unit in Units)
        {
            var bullet = Regex.Match(
                notes,
                $@"(?m)^- {Regex.Escape(unit.Unit)} —[^\r\n]*$");
            Assert.True(bullet.Success, $"{language} notes are missing {unit.Unit}.");
            Assert.Contains($"PR {unit.Pr};", bullet.Value, StringComparison.Ordinal);
            Assert.Contains($"merge commit `{unit.Merge}`", bullet.Value, StringComparison.Ordinal);

            var accounting = Regex.Match(
                notes,
                $@"(?m)^\| `{Regex.Escape(unit.Merge)}` \| [^\r\n]*$");
            Assert.True(
                accounting.Success,
                $"{language} notes are missing accounting for {unit.Unit}.");
            Assert.Contains(unit.Unit, accounting.Value, StringComparison.Ordinal);
            Assert.Contains(unit.Pr, accounting.Value, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesAccountForSevenFirstParentCommitsButExcludeG730AsAUnit(string language)
    {
        var notes = Read(language);

        Assert.Contains("git log --first-parent v0.23.2..main", notes, StringComparison.Ordinal);
        Assert.Contains("git rev-list --first-parent --count v0.23.2..main", notes, StringComparison.Ordinal);
        Assert.Contains("seven", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("G730", notes, StringComparison.Ordinal);
        Assert.Contains("not a release unit", notes, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(7, FirstParentRange.Length);
        Assert.Equal(FirstParentRange.Length, FirstParentRange.Distinct(StringComparer.Ordinal).Count());
        foreach (var commit in FirstParentRange)
        {
            Assert.Contains(commit, notes, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void RetargetReasonNamesBothMeasuredNewCommandSurfaces(string language)
    {
        var notes = Read(language);
        var compact = Regex.Replace(notes, @"\s+", " ");

        Assert.Contains("0.23.3", notes, StringComparison.Ordinal);
        Assert.Contains("0.24.0", notes, StringComparison.Ordinal);
        Assert.Contains("notify supervise shrink", notes, StringComparison.Ordinal);
        Assert.Contains("session-layer topology record-host-state", notes, StringComparison.Ordinal);
        Assert.Contains("Unknown argument 'shrink'", notes, StringComparison.Ordinal);
        Assert.Contains("Unknown session-layer topology subcommand", notes, StringComparison.Ordinal);
        Assert.Contains("minor", compact, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void EntriesUseOperatorObservableWordingForG733AndG736(string language)
    {
        var notes = Read(language);
        var g733 = FindEntry(notes, "G733");
        var g736 = FindEntry(notes, "G736");

        Assert.NotEmpty(g733);
        Assert.NotEmpty(g736);
        if (language == "en")
        {
            Assert.Contains("without a host round trip", g733, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("before the first publish attempt", g736, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("does not create a capable participant", g736, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains("host round trip なし", g733, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("first publish attempt の前", g736, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("declaration は capable participant を作りません", g736, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void IdentityEvidenceNamesThePreparedHeadAndProducingRevision(string language)
    {
        var notes = Read(language);
        var compact = Regex.Replace(notes, @"\s+", " ");

        Assert.Contains(PreparedFunctionalHead, notes, StringComparison.Ordinal);
        Assert.Contains(DisplayIdentity, notes, StringComparison.Ordinal);
        Assert.Contains("Release build", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("G737", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "outside its own prepared functional head" : "自分自身の prepared functional head の外側",
            compact,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesRemainPrepareOnlyAndRecordFinalVerificationSurfaces(string language)
    {
        var notes = Read(language);

        Assert.Contains("prepare-only", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no tag", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no GitHub Release", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no publish", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no post-release roll", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Targeted release-prep guards:", notes, StringComparison.Ordinal);
        Assert.Contains("Full Release suite:", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("to be recorded", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShippedV0240NotesAndReadinessPointAtTheCurrentV0291Line()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var policy = RepoVersionPolicySource.Read();
        Assert.Equal("0.32.0", policy.StableVersion);
        Assert.Equal("0.32.1", policy.NextVersion);

        foreach (var language in new[] { "en", "ja" })
        {
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.24.0.md")));
            Assert.False(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.24.1.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.25.0.md")));
            Assert.False(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.25.1.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.26.0.md")));
            Assert.False(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.26.1.md")));
            Assert.False(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.27.0.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.27.1.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.28.0.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.28.1.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.29.0.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.29.1.md")));

            var reference = File.ReadAllText(Path.Combine(root, "docs", language, "09-developer-reference.md"));
            Assert.Contains("0.24.0", reference, StringComparison.Ordinal);
            Assert.Contains("release-notes-v0.24.0.md", reference, StringComparison.Ordinal);
            Assert.Contains("0.25.0", reference, StringComparison.Ordinal);
            Assert.Contains("release-notes-v0.28.0.md", reference, StringComparison.Ordinal);
            Assert.Contains("release-notes-v0.28.1.md", reference, StringComparison.Ordinal);
            Assert.Contains("release-notes-v0.29.0.md", reference, StringComparison.Ordinal);
            Assert.Contains("release-notes-v0.29.1.md", reference, StringComparison.Ordinal);
            Assert.DoesNotContain("release-notes-v0.27.0.md", reference, StringComparison.Ordinal);
            Assert.DoesNotContain("release-notes-v0.24.1.md", reference, StringComparison.Ordinal);
            Assert.Contains(
                language == "en" ? "Next release readiness (v0.32.1)" : "次リリース準備(v0.32.1)",
                reference,
                StringComparison.Ordinal);
            Assert.Contains("intent-cli 0.29.0-65e02d8-G772", reference, StringComparison.Ordinal);
            Assert.Contains("ReleasePackageMetadataTests", reference, StringComparison.Ordinal);
            Assert.Contains("VersionSourcePolicyGuardTests", reference, StringComparison.Ordinal);
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
        RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.24.0.md"));
}
