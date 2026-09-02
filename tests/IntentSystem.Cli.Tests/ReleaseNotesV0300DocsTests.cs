using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G787: v0.30.0 is a measured, prepare-only release line. These guards pin
/// the eight-unit inventory, independently measured identities, bilingual
/// consumer follow-ups, truthfulness limits, and the post-preparation policy.
/// </summary>
public sealed class ReleaseNotesV0300DocsTests
{
    private const string Base = "d9dc053dd81f53c3a8be420ee7c6798b808f4521";
    private const string NormalPlaceholderIdentity = "intent-cli 0.30.1-d9dc053-G772";
    private const string ExplicitReleaseIdentity = "intent-cli 0.30.0-d9dc053-G772";
    private const string TaggedIdentity = "intent-cli 0.29.0-8d019f8-G772";

    private static readonly (string Unit, string Pr, string Issue, string Merge)[] Units =
    [
        ("G779", "#1705", "#1699", "1057923311a0819d994c5180c1a58adff1e2fd8c"),
        ("G780", "#1713", "#1703", "a16af04342d4dbe05c73a36699fc9b570c9eba69"),
        ("G781", "#1711", "#1704", "d4bcdfcf3db347b887986ebd9beec75c57a8708c"),
        ("G782", "#1714", "#1706", "c09caab877ebaf3a5fc2c1fe6e42a4cfb6709c58"),
        ("G783", "#1715", "#1707", "14888e49288c1c4e826717e485fd6243ff16fcf6"),
        ("G784", "#1716", "#1708", "e26faca0c5ee4e58f71257d08f0601c2934409f6"),
        ("G785", "#1717", "#1709", "140bfc65a744ac7dbf14886a315b40f865d8001e"),
        ("G786", "#1718", "#1712", "d9dc053dd81f53c3a8be420ee7c6798b808f4521"),
    ];

    private static readonly (string Issue, string Unit, string FollowUp)[] ConsumerFollowUps =
    [
        ("#1697", "G779", "cite v0.30.0"),
        ("#1658", "G780", "cite v0.30.0"),
        ("#1700", "G784", "cite v0.30.0"),
        ("#1701", "G781", "cite v0.30.0"),
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCoverExactlyTheEightGitDerivedUnits(string language)
    {
        var notes = ReadNotes(language);
        var listed = Regex.Matches(notes, @"(?m)^- (G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(Units.Select(unit => unit.Unit), listed);
        Assert.Equal(8, listed.Length);

        foreach (var unit in Units)
        {
            var entry = FindEntry(notes, unit.Unit);
            Assert.NotEmpty(entry);
            Assert.Contains($"PR {unit.Pr} / issue {unit.Issue};", entry, StringComparison.Ordinal);
            Assert.Contains($"merge commit `{unit.Merge}`", entry, StringComparison.Ordinal);
            Assert.Contains("Operator-observable outcome", entry, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EnglishAndJapaneseMirrorsHaveIdenticalUnitTuplesAndConsumerFollowUps()
    {
        var english = ReadNotes("en");
        var japanese = ReadNotes("ja");

        Assert.Equal(Units, ParseInventory(english));
        Assert.Equal(Units, ParseInventory(japanese));
        Assert.Equal(ParseInventory(english), ParseInventory(japanese));
        Assert.Equal(ConsumerFollowUps, ParseConsumerFollowUps(english));
        Assert.Equal(ConsumerFollowUps, ParseConsumerFollowUps(japanese));
        Assert.Equal(ParseConsumerFollowUps(english), ParseConsumerFollowUps(japanese));
        Assert.All(
            ParseConsumerFollowUps(english),
            followUp => Assert.Contains(Units, unit => unit.Unit == followUp.Unit));
    }

    [Fact]
    public void MirrorParityDetectsASingleFieldMutation()
    {
        var english = ReadNotes("en");
        var japanese = ReadNotes("ja");

        var changedIssue = japanese.Replace("issue #1699", "issue #9999", StringComparison.Ordinal);
        var changedFollowUp = japanese.Replace("| #1697 | G779 | cite v0.30.0 |", "| #1697 | G786 | cite v0.30.0 |", StringComparison.Ordinal);

        Assert.False(ParseInventory(english).SequenceEqual(ParseInventory(changedIssue)));
        Assert.False(ParseConsumerFollowUps(english).SequenceEqual(ParseConsumerFollowUps(changedFollowUp)));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesPinThreeMeasuredVersionIdentitiesAndMinorDecision(string language)
    {
        var notes = ReadNotes(language);
        var normalized = Regex.Replace(notes, @"\s+", " ");

        Assert.Contains(Base, notes, StringComparison.Ordinal);
        Assert.Contains("dotnet build IntentSystem.sln --configuration Release", notes, StringComparison.Ordinal);
        Assert.Contains("-p:Version=0.30.0", notes, StringComparison.Ordinal);
        Assert.Contains(NormalPlaceholderIdentity, notes, StringComparison.Ordinal);
        Assert.Contains(ExplicitReleaseIdentity, notes, StringComparison.Ordinal);
        Assert.Contains("release.yml", notes, StringComparison.Ordinal);
        Assert.Contains("RAW=v0.30.0", notes, StringComparison.Ordinal);
        Assert.Contains("VERSION=0.30.0", notes, StringComparison.Ordinal);
        Assert.Contains("eng/version.json", notes, StringComparison.Ordinal);
        Assert.Contains("local builds", notes, StringComparison.Ordinal);
        Assert.Contains("dry runs", notes, StringComparison.Ordinal);
        Assert.Contains("same_repo_topology = true", notes, StringComparison.Ordinal);
        Assert.Contains("metadata_write_branch", notes, StringComparison.Ordinal);
        Assert.Contains("refs/heads/<metadata_write_branch>", notes, StringComparison.Ordinal);
        Assert.Contains("claim stranded", notes, StringComparison.Ordinal);
        Assert.Contains("push-rejected", notes, StringComparison.Ordinal);
        Assert.Contains("command-route", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("option-level", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--verify", notes, StringComparison.Ordinal);
        Assert.Contains("--accept-evidence-gap", notes, StringComparison.Ordinal);
        Assert.Contains("--shell-policy", notes, StringComparison.Ordinal);
        Assert.Contains(TaggedIdentity, notes, StringComparison.Ordinal);
        Assert.Contains("[--verify|--dry-run|--write]", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "**not** v0.30.0" : "**v0.30.0 ではありません**", normalized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesPinEveryFirstParentCommitAndPrepareOnlyBoundary(string language)
    {
        var notes = ReadNotes(language);
        const string Range = "v0.29.0..d9dc053dd81f53c3a8be420ee7c6798b808f4521";

        Assert.Contains($"git rev-list --first-parent --reverse {Range}", notes, StringComparison.Ordinal);
        Assert.Contains($"git rev-list --first-parent --count {Range}", notes, StringComparison.Ordinal);
        Assert.Contains("\n8\n", notes, StringComparison.Ordinal);
        Assert.Contains("PREPARED / NOT PUBLISHED", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("DRAFT /", notes, StringComparison.OrdinalIgnoreCase);

        foreach (var unit in Units)
        {
            Assert.Contains(unit.Merge, notes, StringComparison.Ordinal);
            Assert.Contains($"{unit.Unit} / PR {unit.Pr} / issue {unit.Issue}", notes, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesPinFourTruthfulnessBoundaries(string language)
    {
        var notes = ReadNotes(language);
        var normalized = Regex.Replace(notes, @"\s+", " ");

        Assert.Contains("byte-identical", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("claim stranded migrate", notes, StringComparison.Ordinal);
        Assert.Contains("automatic", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("install --verify", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "without rewriting" : "artifact を rewrite せず",
            normalized,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("first-cycle-verified", notes, StringComparison.Ordinal);
        Assert.Contains("fleet claim", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AST verifier", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "rules are unchanged" : "rules は unchanged",
            normalized,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ShellCommandPolicy", notes, StringComparison.Ordinal);
        Assert.Contains("G689 ledger identity", notes, StringComparison.Ordinal);
        Assert.Contains("G690 CAS", notes, StringComparison.Ordinal);
        Assert.Contains("--accept-evidence-gap", notes, StringComparison.Ordinal);
        Assert.Contains("unaffected", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never loads, manages, or queries", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionPolicyRollAndNextVersionPlaceholdersAreExact()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        Assert.Equal(
            "{\n  \"stableVersion\": \"0.30.0\",\n  \"nextVersion\": \"0.30.1\"\n}\n",
            File.ReadAllText(Path.Combine(root, "eng", "version.json")));

        var policy = RepoVersionPolicySource.Read();
        Assert.Equal("0.30.0", policy.StableVersion);
        Assert.Equal("0.30.1", policy.NextVersion);

        foreach (var language in new[] { "en", "ja" })
        {
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.30.0.md")));
            var stub = File.ReadAllText(Path.Combine(root, "docs", language, "release-notes-v0.30.1.md"));
            Assert.Contains("DRAFT", stub, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("replaceable", stub, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(language == "en" ? "not a changelog" : "changelog ではありません", stub, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("- G", stub, StringComparison.Ordinal);
        }
    }

    private static string FindEntry(string notes, string unit)
    {
        var match = Regex.Match(notes, $@"(?ms)^- {Regex.Escape(unit)} —.*?(?=^- |^## |\z)");
        return match.Success ? match.Value : string.Empty;
    }

    private static IReadOnlyList<(string Unit, string Pr, string Issue, string Merge)> ParseInventory(string notes) =>
        Regex.Matches(
                notes,
                @"(?ms)^- (G\d+) — PR (#\d+) / issue (#\d+); merge commit `([0-9a-f]{40})`.*?(?=^- |^## |\z)")
            .Select(match => (
                match.Groups[1].Value,
                match.Groups[2].Value,
                match.Groups[3].Value,
                match.Groups[4].Value))
            .ToArray();

    private static IReadOnlyList<(string Issue, string Unit, string FollowUp)> ParseConsumerFollowUps(string notes) =>
        Regex.Matches(notes, @"(?m)^\| (#\d+) \| (G\d+) \| (cite v0\.30\.0) \|$")
            .Select(match => (
                match.Groups[1].Value,
                match.Groups[2].Value,
                match.Groups[3].Value))
            .ToArray();

    private static string ReadNotes(string language) => File.ReadAllText(Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.30.0.md"));
}
