using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G802: v0.32.0 is a measured, prepare-only release line. These guards keep
/// the six first-parent units, the alias compatibility promise, the three
/// version identities, EN/JA parity, and the version-policy roll durable.
/// </summary>
public sealed class ReleaseNotesV0320G802Tests
{
    private const string Base = "2a833a976688b3139678e4954162a9c00d32d0f4";
    private const string NormalPlaceholderIdentity = "intent-cli 0.32.1-2a833a9-G801";
    private const string ExplicitReleaseIdentity = "intent-cli 0.32.0-2a833a9-G801";

    private static readonly (string Unit, string Pr, string Issue, string Merge)[] Units =
    [
        ("G795", "#1740", "#1737", "1b3c7229cfe8c8f8565034a7e2220a94ac14785b"),
        ("G798", "#1742", "#1741", "09b1f4edca51f3acbbe3e901356866996f4be29f"),
        ("G796", "#1743", "#1738", "67c8578090f1a53e8894aeff88abd6cd8b83ff15"),
        ("G800", "#1747", "#1745", "6e0bff220e2bf51308596c19ee258835ce509dd8"),
        ("G797", "#1746", "#1739", "11457187ad0f9c2c269b80de84b0fd9ea278dfe5"),
        ("G801", "#1749", "#1748", "2a833a976688b3139678e4954162a9c00d32d0f4"),
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

        Console.WriteLine($"G802 AC1 {language}: six_units={string.Join(',', listed)}; base={Base}");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void AliasPromiseRendersAllFourStatements(string language)
    {
        var notes = Normalize(ReadNotes(language));

        Assert.Contains("design", notes, StringComparison.Ordinal);
        Assert.Contains("orchestration", notes, StringComparison.Ordinal);
        Assert.Contains("implementation", notes, StringComparison.Ordinal);
        Assert.Contains("review", notes, StringComparison.Ordinal);
        Assert.Contains("still work", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("existing roles configuration keeps loading", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("existing queue-state keeps reading and displaying", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no installed guide route changed name", notes, StringComparison.OrdinalIgnoreCase);

        Console.WriteLine($"G802 AC2 {language}: legacy_aliases=design,orchestration,implementation,review; config_loading=preserved; queue_state_read_display=preserved; guide_routes=unchanged");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesPinThreeMeasuredVersionIdentities(string language)
    {
        var notes = ReadNotes(language);

        Assert.Contains(Base, notes, StringComparison.Ordinal);
        Assert.Contains(NormalPlaceholderIdentity, notes, StringComparison.Ordinal);
        Assert.Contains(ExplicitReleaseIdentity, notes, StringComparison.Ordinal);
        Assert.Contains("dotnet build IntentSystem.sln --configuration Release", notes, StringComparison.Ordinal);
        Assert.Contains("-p:Version=0.32.0", notes, StringComparison.Ordinal);
        Assert.Contains("release.yml", notes, StringComparison.Ordinal);
        Assert.Contains("RAW=v0.32.0", notes, StringComparison.Ordinal);
        Assert.Contains("VERSION=0.32.0", notes, StringComparison.Ordinal);
        Assert.Contains("eng/version.json", notes, StringComparison.Ordinal);
        Assert.Contains("local builds", notes, StringComparison.Ordinal);
        Assert.Contains("dry runs", notes, StringComparison.Ordinal);
        Assert.Contains("**not** v0.32.0", Normalize(notes), StringComparison.Ordinal);

        Console.WriteLine($"G802 AC3 {language}: normal={NormalPlaceholderIdentity}; explicit={ExplicitReleaseIdentity}; published=RAW=v0.32.0 -> VERSION=0.32.0");
    }

    [Fact]
    public void EnglishAndJapaneseMirrorsHaveIdenticalUnitPrIssueAndMergeTuples()
    {
        var english = ParseInventory(ReadNotes("en"));
        var japanese = ParseInventory(ReadNotes("ja"));

        Assert.Equal(Units, english);
        Assert.Equal(english, japanese);
        Console.WriteLine($"G802 AC4 parity: en=ja; tuples={english.Count}; mutation_guard=ready");
    }

    [Fact]
    public void MirrorParityDetectsSingleFieldMutation()
    {
        var english = ParseInventory(ReadNotes("en"));
        var japanese = ReadNotes("ja");
        var changedJapanese = japanese.Replace(
            "issue #1737",
            "issue #9999",
            StringComparison.Ordinal);
        var mutated = ParseInventory(changedJapanese);

        Assert.False(english.SequenceEqual(mutated));
        Console.WriteLine($"G802 AC4 parity mutation: changed=issue #1737->#9999; equal={english.SequenceEqual(mutated)}; result=FAIL (expected guard)");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesPinMinorRouteDecisionAndPrepareOnlyBoundary(string language)
    {
        var notes = Normalize(ReadNotes(language));

        Assert.Contains("G796", notes, StringComparison.Ordinal);
        Assert.Contains("G800", notes, StringComparison.Ordinal);
        Assert.Contains("command-route addition is a minor", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("option-level additions do not count as command routes", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not counted", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PREPARED / NOT PUBLISHED", notes, StringComparison.Ordinal);
        Assert.Contains("no tag", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no GitHub Release", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no workflow", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no product source", notes, StringComparison.OrdinalIgnoreCase);

        Console.WriteLine($"G802 AC5 {language}: routes_counted=G796,G800; alias/config/guide/npm=not counted; prepare_only=true");
    }

    [Fact]
    public void VersionPolicyRollAndPlaceholderNotesAreExact()
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
            var stub = File.ReadAllText(Path.Combine(root, "docs", language, "release-notes-v0.32.1.md"));
            Assert.Contains("DRAFT", stub, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("replaceable", stub, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("changelog", stub, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("- G", stub, StringComparison.Ordinal);
        }

        Console.WriteLine("G802 AC6 policy: stableVersion=0.32.0; nextVersion=0.32.1; placeholders=en,ja; entries=0");
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

    private static string Normalize(string value) => Regex.Replace(value, @"\s+", " ");

    private static string ReadNotes(string language) => File.ReadAllText(Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.32.0.md"));
}
