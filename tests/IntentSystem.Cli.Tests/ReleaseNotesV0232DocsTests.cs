using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G729: the prepared v0.23.2 notes pin the six-unit inventory, the
/// independently measured prepared-head identity, and the prepare-only
/// boundary in both language mirrors.
/// </summary>
public sealed class ReleaseNotesV0232DocsTests
{
    private const string PreparedFunctionalHead =
        "2caa6d42f1578d57c5667db1d475024d1afbc9f9";
    private const string DisplayIdentity = "intent-cli 0.23.2-2caa6d4-G728";
    private const string InstalledIdentity = "intent-cli 0.23.1-d49984d-G721";

    private static readonly (string Unit, string Pr, string Merge)[] Units =
    [
        ("G723", "#1571", "0252948e631194087a2cdacc7605f6023d8d0213"),
        ("G724", "#1572", "771d5e9d147997cf184e5c8db6be2407cee4b6cf"),
        ("G725", "#1576", "6820fef35dad12c07ef936278bf40e4a2071772e"),
        ("G726", "#1577", "728989c6ef5bc7166718f0b7222a22c95d1c2e2e"),
        ("G727", "#1578", "5d2d1ce51530c035944194e6cb762246fc589b13"),
        ("G728", "#1580", "2caa6d42f1578d57c5667db1d475024d1afbc9f9"),
    ];

    private static readonly (string Group, int Count)[] SubcommandCounts =
    [
        ("automation", 39),
        ("notify", 9),
        ("session-layer", 6),
        ("guide", 35),
        ("worker", 8),
        ("issue", 9),
        ("review", 3),
        ("closeout", 1),
        ("claim", 4),
        ("metadata", 2),
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCoverExactlyTheSixUnreleasedUnits(string language)
    {
        var notes = Read(language);
        var listed = Regex.Matches(notes, @"(?m)^- (G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(Units.Select(unit => unit.Unit), listed);
        Assert.Equal(6, listed.Length);
        Assert.DoesNotContain("G722", notes, StringComparison.Ordinal);

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
    public void EntriesDescribeOperatorResultsWithoutPromisingRepairs(string language)
    {
        var notes = Read(language);
        var compact = Regex.Replace(notes, @"\s+", " ");

        Assert.Contains("operator-visible fix", compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("orchestrator", compact, StringComparison.Ordinal);
        Assert.Contains("heartbeat", compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("session-layer", compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("detects and reports a skipped post-release version roll", compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gates and refuses an unreachable tag", compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reports checkout freshness/provenance", compact, StringComparison.OrdinalIgnoreCase);

        var g725 = FindBullet(notes, "G725");
        var g726 = FindBullet(notes, "G726");
        var g727 = FindBullet(notes, "G727");
        Assert.DoesNotContain("repair a roll", g725, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("repair the unreachable", g726, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("repair", g727, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void IdentityEvidenceIsBoundToThePreparedFunctionalHead(string language)
    {
        var notes = Read(language);
        var compact = Regex.Replace(notes, @"\s+", " ");

        Assert.Contains(PreparedFunctionalHead, notes, StringComparison.Ordinal);
        Assert.Contains(DisplayIdentity, notes, StringComparison.Ordinal);
        Assert.Contains(InstalledIdentity, notes, StringComparison.Ordinal);
        Assert.Contains("Release build", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exact prepared functional head", compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Release identity evidence source revision", notes, StringComparison.Ordinal);
        Assert.All(
            SubcommandCounts,
            group => Assert.Contains(
                $"| `{group.Group}` | {group.Count} | {group.Count} | unchanged |",
                notes,
                StringComparison.Ordinal));
        Assert.Contains(
            language == "en" ? "adds no command surface" : "新しい command surface",
            notes,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesReplaceTheStubAndKeepPreparationOutsideTheFunctionalHead(string language)
    {
        var notes = Read(language);

        Assert.Contains("prepare-only", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no tag", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no GitHub Release", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no publish", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en"
                ? "outside the prepared functional head"
                : "prepared functional head の外側",
            notes,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            language == "en"
                ? "tag will land on the documentation merge commit"
                : "eventual tag は",
            notes,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("This stub is created", notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("この stub は", notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("has no feature scope", notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("まだ feature scope がありません", notes, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindBullet(string notes, string unit) =>
        Regex.Match(notes, $@"(?m)^- {Regex.Escape(unit)} —[^\r\n]*$").Value;

    private static string Read(string language) => File.ReadAllText(Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.23.2.md"));
}
