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
    private const string PublishedV0231Tag =
        "d49984dae761d589b2568f8eb1677ce3ff2facbc";
    private const string InvalidPublishedV0231Tag =
        "d49984dae761d589b2568f8eb1677ce3ff2facbc7";

    private static readonly Regex AutomaticRepairClaim = new(
        @"\b(?:automatically|will|can)\b.{0,120}\brepairs?\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly (string Unit, string FalseClaim)[] RepairClaims =
    [
        ("G725", "The detector automatically repairs a roll for the operator."),
        ("G726", "The gate automatically repairs the unreachable commit."),
        ("G727", "The report automatically repairs checkout freshness/provenance."),
    ];

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
    public void PublishedV0231TagShaIsPinnedInBothNoteOccurrences(string language)
    {
        var notes = Read(language);

        Assert.Equal(40, PublishedV0231Tag.Length);
        Assert.Equal(
            2,
            Regex.Matches(notes, Regex.Escape(PublishedV0231Tag)).Count);
        Assert.DoesNotContain(InvalidPublishedV0231Tag, notes, StringComparison.Ordinal);
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

        AssertEntryDoesNotPromiseAutomaticRepair("G725", FindEntry(notes, "G725"));
        AssertEntryDoesNotPromiseAutomaticRepair("G726", FindEntry(notes, "G726"));
        AssertEntryDoesNotPromiseAutomaticRepair("G727", FindEntry(notes, "G727"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void MultiLineEntryRepairGuardsRejectFalseAutomaticRepairClaims(string language)
    {
        var notes = Read(language);

        foreach (var claim in RepairClaims)
        {
            var entry = FindEntry(notes, claim.Unit);
            Assert.NotEmpty(entry);
            AssertEntryDoesNotPromiseAutomaticRepair(claim.Unit, entry);

            var mutatedNotes = notes.Replace(
                entry,
                entry + Environment.NewLine + "  " + claim.FalseClaim,
                StringComparison.Ordinal);
            var mutatedEntry = FindEntry(mutatedNotes, claim.Unit);

            Assert.Contains(claim.FalseClaim, mutatedEntry, StringComparison.Ordinal);
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(
                () => AssertEntryDoesNotPromiseAutomaticRepair(claim.Unit, mutatedEntry));
        }
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

    private static void AssertEntryDoesNotPromiseAutomaticRepair(
        string unit,
        string entry)
    {
        Assert.False(
            AutomaticRepairClaim.IsMatch(entry),
            $"{unit} must describe reporting/gating without promising automatic repair.");
    }

    private static string FindEntry(string notes, string unit)
    {
        var match = Regex.Match(
            notes,
            $@"(?ms)^- {Regex.Escape(unit)} —.*?(?=^- |^## |\z)");
        return match.Success ? match.Value : string.Empty;
    }

    private static string Read(string language) => File.ReadAllText(Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.23.2.md"));
}
