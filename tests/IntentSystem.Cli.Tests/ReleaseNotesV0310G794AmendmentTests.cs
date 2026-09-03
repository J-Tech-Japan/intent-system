using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G794: the v0.31.0 preparation is re-measured at the G793 merge base.
/// These focused guards make the six-commit range, current identities, minor
/// accounting, and mirror mutation oracle durable without changing product
/// source or release policy.
/// </summary>
public sealed class ReleaseNotesV0310G794AmendmentTests
{
    private const string Base = "fed2bbc74449b389565b8241732fe376b7a1c421";
    private const string Range = "v0.30.0..fed2bbc74449b389565b8241732fe376b7a1c421";
    private static readonly string[] FirstParentCommits =
    [
        "cfdacb4a657d9a60ab82fea3faa435ff732f389f",
        "9d03309a155dc5f714be8a99bb3c2234724bf589",
        "aa5c49f51bffa634ca7a96a08f1245e53a372904",
        "79a245c655e17ac654ac440fda31709ee38e28b8",
        "26f0edf85cc6371c66ede5383de6543e11acd1fb",
        Base,
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReMeasuredFirstParentRangePastesAllSixCommitsAndPrepClassification(string language)
    {
        var notes = ReadNotes(language);

        Assert.Contains($"$ git rev-list --first-parent --reverse {Range}", notes, StringComparison.Ordinal);
        Assert.Contains($"$ git rev-list --first-parent --count {Range}\n6", notes, StringComparison.Ordinal);
        foreach (var commit in FirstParentCommits)
        {
            Assert.Contains(commit, notes, StringComparison.Ordinal);
        }

        Assert.Contains("G792 / PR #1732 / issue #1730", notes, StringComparison.Ordinal);
        Assert.Contains("26f0edf85cc6371c66ede5383de6543e11acd1fb", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "this release's own preparation unit" : "この release 自身の preparation unit",
            notes,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("G793 / PR #1733 / issue #1731", notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ThreeVersionIdentityBannersUseCurrentBaseWithoutTheStaleBaseFragment(string language)
    {
        var notes = ReadNotes(language);

        Assert.Contains(Base, notes, StringComparison.Ordinal);
        Assert.Contains("intent-cli 0.31.1-fed2bbc-G793", notes, StringComparison.Ordinal);
        Assert.Contains("intent-cli 0.31.0-fed2bbc-G793", notes, StringComparison.Ordinal);
        Assert.Contains("RAW=v0.31.0", notes, StringComparison.Ordinal);
        Assert.Contains("VERSION=0.31.0", notes, StringComparison.Ordinal);

        foreach (Match identity in Regex.Matches(notes, @"(?m)^intent-cli [^\r\n]+"))
        {
            Assert.DoesNotContain("79a245c", identity.Value, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void MinorRationaleListsG793AsMeasuredButNotAnotherRoute(string language)
    {
        var notes = ReadNotes(language);
        var identityHeading = language == "en"
            ? "## Measured version identities"
            : "## 測定した version identities";
        var rationale = notes[..notes.IndexOf(identityHeading, StringComparison.Ordinal)];

        Assert.Contains("session-layer inspect", rationale, StringComparison.Ordinal);
        Assert.Contains("command-route", rationale, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("option-level", rationale, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("G793", rationale, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "not counted as additional routes" : "extra route とは数えません",
            rationale,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MirrorMutationRemainsARealParityFailure_G794()
    {
        var english = ParseInventory(ReadNotes("en"));
        var japanese = ReadNotes("ja");
        var changedJapanese = japanese.Replace("issue #1731", "issue #9999", StringComparison.Ordinal);

        Assert.NotEqual(english, ParseInventory(changedJapanese));
        Console.WriteLine("G794 parity mutation: JA issue #1731 -> #9999; tuple oracle changed and the test would fail.");
    }

    private static string ReadNotes(string language) => File.ReadAllText(Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.31.0.md"));

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
}
