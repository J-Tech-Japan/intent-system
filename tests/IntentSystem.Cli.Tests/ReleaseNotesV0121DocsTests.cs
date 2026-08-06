using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

public sealed class ReleaseNotesV0121DocsTests
{
    private static readonly (string Unit, string Pr, string Merge)[] Units =
    [
        ("G631", "#1368", "4c4ef22"),
        ("G632", "#1367", "77a57f2"),
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCoverExactlyG631AndG632WithVerifiedMerges(string language)
    {
        var notes = Read(language);
        var listed = Regex.Matches(notes, @"(?m)^- (G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.Equal(Units.Select(unit => unit.Unit), listed);
        Assert.Equal(2, listed.Length);
        foreach (var unit in Units)
        {
            Assert.Contains(unit.Pr, notes, StringComparison.Ordinal);
            Assert.Contains(unit.Merge, notes, StringComparison.Ordinal);
        }

        Assert.Contains("git log v0.12.0..main", notes, StringComparison.Ordinal);
        Assert.Contains("0.12.0", notes, StringComparison.Ordinal);
        Assert.Contains("0.12.1", notes, StringComparison.Ordinal);
        Assert.Contains("prepare-only", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Release", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tag", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("publish", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesContainBothPatchRationalesAndOperatorBoundaries(string language)
    {
        var notes = Read(language);
        Assert.Contains("command", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("flag", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("advisory", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("submodule", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "fails closed" : "fail closed", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "non-ASCII" : "非 ASCII", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pane", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("guard", notes, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(string language) =>
        File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.12.1.md"));
}
