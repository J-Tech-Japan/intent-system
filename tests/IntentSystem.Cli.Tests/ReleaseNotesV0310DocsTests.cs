using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G794: v0.31.0 is a measured, prepare-only release line. These guards keep
/// the exact first-parent inventory, three independently measured identities,
/// EN/JA parity, truthfulness boundaries, and version-policy roll durable while
/// the release base widens through G793.
/// </summary>
public sealed class ReleaseNotesV0310DocsTests
{
    private const string Base = "fed2bbc74449b389565b8241732fe376b7a1c421";
    private const string NormalPlaceholderIdentity = "intent-cli 0.31.1-fed2bbc-G793";
    private const string ExplicitReleaseIdentity = "intent-cli 0.31.0-fed2bbc-G793";
    private const string DeveloperReferenceNormalIdentity = "intent-cli 0.32.1-2a833a9-G801";
    private const string DeveloperReferenceExplicitIdentity = "intent-cli 0.32.0-2a833a9-G801";
    private const string TaggedIdentity = "intent-cli 0.30.0-f4b01c2-G772";

    private static readonly (string Unit, string Pr, string Issue, string Merge)[] Units =
    [
        ("G788", "#1723", "#1722", "cfdacb4a657d9a60ab82fea3faa435ff732f389f"),
        ("G789", "#1725", "#1724", "9d03309a155dc5f714be8a99bb3c2234724bf589"),
        ("G791", "#1728", "#1727", "aa5c49f51bffa634ca7a96a08f1245e53a372904"),
        ("G790", "#1729", "#1726", "79a245c655e17ac654ac440fda31709ee38e28b8"),
        ("G792", "#1732", "#1730", "26f0edf85cc6371c66ede5383de6543e11acd1fb"),
        ("G793", "#1733", "#1731", "fed2bbc74449b389565b8241732fe376b7a1c421"),
    ];

    private static readonly (string Issue, string Unit, string FollowUp)[] ConsumerFollowUps =
    [
        ("#1721", "G788", "cite v0.31.0"),
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCoverExactlyTheSixGitDerivedUnits(string language)
    {
        var notes = ReadNotes(language);
        var listed = Regex.Matches(notes, @"(?m)^- (G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(Units.Select(unit => unit.Unit), listed);
        Assert.Equal(6, listed.Length);

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
    public void EnglishAndJapaneseMirrorsHaveIdenticalUnitTuplesAndConsumerFollowUp()
    {
        var english = ReadNotes("en");
        var japanese = ReadNotes("ja");

        Assert.Equal(Units, ParseInventory(english));
        Assert.Equal(Units, ParseInventory(japanese));
        Assert.Equal(ParseInventory(english), ParseInventory(japanese));
        Assert.Equal(ConsumerFollowUps, ParseConsumerFollowUps(english));
        Assert.Equal(ConsumerFollowUps, ParseConsumerFollowUps(japanese));
        Assert.Equal(ParseConsumerFollowUps(english), ParseConsumerFollowUps(japanese));
    }

    [Fact]
    public void MirrorParityDetectsSingleFieldMutation()
    {
        var english = ReadNotes("en");
        var japanese = ReadNotes("ja");

        var changedIssue = japanese.Replace("issue #1722", "issue #9999", StringComparison.Ordinal);
        var changedFollowUp = japanese.Replace(
            "| (#1721) | G788 | cite v0.31.0 and close after the consumer report |",
            "| (#1721) | G790 | cite v0.31.0 and close after the consumer report |",
            StringComparison.Ordinal);

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
        Assert.Contains(TaggedIdentity, notes, StringComparison.Ordinal);
        Assert.Contains(NormalPlaceholderIdentity, notes, StringComparison.Ordinal);
        Assert.Contains(ExplicitReleaseIdentity, notes, StringComparison.Ordinal);
        Assert.Contains("dotnet build IntentSystem.sln --configuration Release", notes, StringComparison.Ordinal);
        Assert.Contains("-p:Version=0.31.0", notes, StringComparison.Ordinal);
        Assert.Contains("release.yml", notes, StringComparison.Ordinal);
        Assert.Contains("RAW=v0.31.0", notes, StringComparison.Ordinal);
        Assert.Contains("VERSION=0.31.0", notes, StringComparison.Ordinal);
        Assert.Contains("eng/version.json", notes, StringComparison.Ordinal);
        Assert.Contains("local builds", notes, StringComparison.Ordinal);
        Assert.Contains("dry runs", notes, StringComparison.Ordinal);
        Assert.Contains("session-layer inspect", notes, StringComparison.Ordinal);
        Assert.Contains("Command 'session-layer inspect' is not yet implemented.", notes, StringComparison.Ordinal);
        Assert.Contains("EXIT:1", notes, StringComparison.Ordinal);
        Assert.Contains("command-route", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("option-level", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "**not** v0.31.0" : "**v0.31.0 ではありません**", normalized, StringComparison.Ordinal);

        foreach (Match identity in Regex.Matches(notes, @"(?m)^intent-cli [^\r\n]+"))
        {
            Assert.DoesNotContain("79a245c", identity.Value, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesPinEveryFirstParentCommitAndPrepareOnlyBoundary(string language)
    {
        var notes = ReadNotes(language);
        var range = $"v0.30.0..{Base}";

        Assert.Contains($"git rev-list --first-parent --reverse {range}", notes, StringComparison.Ordinal);
        Assert.Contains($"git rev-list --first-parent --count {range}", notes, StringComparison.Ordinal);
        Assert.Contains("\n6\n", notes, StringComparison.Ordinal);
        Assert.Contains("PREPARED / NOT PUBLISHED", notes, StringComparison.Ordinal);
        Assert.Contains("no tag", notes, StringComparison.OrdinalIgnoreCase);

        foreach (var unit in Units)
        {
            Assert.Contains(unit.Merge, notes, StringComparison.Ordinal);
            Assert.Contains($"{unit.Unit} / PR {unit.Pr} / issue {unit.Issue}", notes, StringComparison.Ordinal);
        }

        Assert.Contains("G792", notes, StringComparison.Ordinal);
        Assert.Contains("G793", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "this release's own preparation unit" : "この release 自身の preparation unit",
            notes,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesPinFourTruthfulnessBoundaries(string language)
    {
        var notes = ReadNotes(language);
        var normalized = Regex.Replace(notes, @"\s+", " ");

        Assert.Contains("downstream delegation", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("child report", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("queue transition", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("true stall", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "lists what it checked" : "checked list", normalized, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("read-only", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recorded topology", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--role", normalized, StringComparison.Ordinal);
        Assert.Contains("focus default", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exit 0", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("notify adjudicate", normalized, StringComparison.Ordinal);

        Assert.Contains(language == "en" ? "every nested checkout is clean" : "すべての nested checkout が clean", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("uncommitted nested content", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "no other domain's submodule" : "他 domain の submodule", normalized, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("non-normative", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "neither launches" : "launch も manage もしません", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Orca", notes, StringComparison.Ordinal);
        Assert.Contains("settled outcome", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("merged linked PR", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("closed linked issue", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("applied-elsewhere", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionPolicyRollAndNextVersionPlaceholdersAreExact()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        Assert.Equal(
            "{\n  \"stableVersion\": \"0.32.0\",\n  \"nextVersion\": \"0.32.1\"\n}\n",
            File.ReadAllText(Path.Combine(root, "eng", "version.json")));

        var policy = RepoVersionPolicySource.Read();
        Assert.Equal("0.32.0", policy.StableVersion);
        Assert.Equal("0.32.1", policy.NextVersion);

        foreach (var language in new[] { "en", "ja" })
        {
            Assert.True(File.Exists(Path.Combine(root, "docs", language, "release-notes-v0.31.0.md")));
            var stub = File.ReadAllText(Path.Combine(root, "docs", language, "release-notes-v0.32.1.md"));
            Assert.Contains("DRAFT", stub, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("replaceable", stub, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(language == "en" ? "not a changelog" : "changelog ではありません", stub, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("- G", stub, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DeveloperReferenceReadinessMirrorsCurrentPreparedLine(string language)
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var reference = File.ReadAllText(Path.Combine(root, "docs", language, "09-developer-reference.md"));

        Assert.Contains(
            language == "en" ? "### Next release readiness (v0.32.1)" : "### 次リリース準備(v0.32.1)",
            reference,
            StringComparison.Ordinal);
        Assert.Contains(DeveloperReferenceNormalIdentity, reference, StringComparison.Ordinal);
        Assert.Contains(DeveloperReferenceExplicitIdentity, reference, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.32.0.md", reference, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.32.1.md", reference, StringComparison.Ordinal);
        Assert.Contains("ReleaseNotesV0320G802Tests", reference, StringComparison.Ordinal);
        Assert.Contains("ReleasePackageMetadataTests", reference, StringComparison.Ordinal);
    }

    private static string FindEntry(string notes, string unit)
    {
        var match = Regex.Match(notes, $"(?ms)^- {Regex.Escape(unit)} —.*?(?=^- |^## |\\z)");
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
        Regex.Matches(notes, @"(?m)^\| \(#(\d+)\) \| (G\d+) \| (cite v0\.31\.0).*$")
            .Select(match => (
                $"#{match.Groups[1].Value}",
                match.Groups[2].Value,
                match.Groups[3].Value))
            .ToArray();

    private static string ReadNotes(string language) => File.ReadAllText(Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.31.0.md"));
}
