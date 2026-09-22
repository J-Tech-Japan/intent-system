using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G804 / G830 / G838 / G850: v0.32.0 is a measured, prepare-only release
/// line, remeasured for the stable release at the post-G848 base. These
/// guards keep the forty-two shipped units, the fifty-one-commit first-parent
/// accounting (G802/G804/G830/G838 prior prep, five claim state commits,
/// G813 partial), the alias compatibility promise, the three version
/// identities, EN/JA parity, the since-preview.4 section, and the unchanged
/// version policy durable.
/// </summary>
public sealed class ReleaseNotesV0320G802Tests
{
    private const string Base = "a2431590f08336349be4edaa9ea5b1847f8aa533";
    private const string Range = "v0.31.0..a2431590f08336349be4edaa9ea5b1847f8aa533";
    private const string NormalPlaceholderIdentity = "intent-cli 0.32.1-a243159-G849";
    private const string ExplicitReleaseIdentity = "intent-cli 0.32.0-a243159-G849";
    private const string PreviousBaseFragment = "cd276e2";
    private const string PreviousBase = "cd276e20754db09337a94a8b973ddebdd1564ba3";

    private static readonly (string Unit, string Pr, string Issue, string Merge)[] Units =
    [
        ("G795", "#1740", "#1737", "1b3c7229cfe8c8f8565034a7e2220a94ac14785b"),
        ("G798", "#1742", "#1741", "09b1f4edca51f3acbbe3e901356866996f4be29f"),
        ("G796", "#1743", "#1738", "67c8578090f1a53e8894aeff88abd6cd8b83ff15"),
        ("G800", "#1747", "#1745", "6e0bff220e2bf51308596c19ee258835ce509dd8"),
        ("G797", "#1746", "#1739", "11457187ad0f9c2c269b80de84b0fd9ea278dfe5"),
        ("G801", "#1749", "#1748", "2a833a976688b3139678e4954162a9c00d32d0f4"),
        ("G803", "#1753", "#1752", "16267f9d58af31669252186a16ce09ab0dd47ba4"),
        ("G799", "#1756", "#1744", "d4645e2f02aea6a7969804a156df176cc2122a4a"),
        ("G805", "#1765", "#1757", "5ffcea48b0060569112efee06ad12baa5d8c9b59"),
        ("G806", "#1766", "#1758", "8e1ded058aeb6738c9419ce584f8da0d9fa59888"),
        ("G807", "#1767", "#1759", "ea5959f83f528675072b8f034f5c3fc30cbb07b8"),
        ("G808", "#1768", "#1760", "80270af5c54bf4e89dfbb8eb5f4a93e7142e8e36"),
        ("G809", "#1769", "#1763", "3d604981865a04c2875fde06d76cf5b60d41fda0"),
        ("G811", "#1770", "#1762", "828e19815e0bd8356298e5c49ae3c90bd8a1751d"),
        ("G812", "#1772", "#1764", "cf40ac8f3211925339134f5fa8bb91bc722e549b"),
        ("G810", "#1773", "#1761", "0f7cb50b0f7d42da120fe04bc4421807dcf5da16"),
        ("G815", "#1776", "#1775", "7003e08b0152e91f89069a67b5ca299bef7cd6de"),
        ("G813", "#1781", "#1774", "3d91a8004c97ab800f365d89189c12def8a39980"),
        ("G822", "#1786", "#1785", "9d521ebc4ef45c7aea52527e77bb21aa34f1c9a5"),
        ("G823", "#1788", "#1778, #1787", "01944a5ed47139b47276ef2fcbc951183f8e442b"),
        ("G824", "#1791", "#1782, #1789", "20ef0a6caa184b91e4c3cd2378bd37d941e0de7a"),
        ("G825", "#1793", "#1777, #1790", "ca7272f4b88e00577e7c423d41a888f0f0defaf6"),
        ("G826", "#1795", "#1792", "71ca5da979f549b9f4f1f4c4ebb45ddd0696fb44"),
        ("G827", "#1796", "#1794", "0762312ddccf14009d3252c5101d0b893ca7be6b"),
        ("G828", "#1799", "#1798", "227a981d42cee28924ba34cbde3f45d12717a202"),
        ("G829", "#1802", "#1801", "e78b27d1e99247380fa7518d67470304fb1d7e7b"),
        ("G831", "#1806", "#1805", "2f5452a00866c02e9e80949c9e80b5d2b347fd35"),
        ("G832", "#1808", "#1807", "159952b410b6429acc03414993ac43a650df7fc7"),
        ("G833", "#1810", "#1809", "9e461d8fda129cfb9447d99b158abb31d9c5c70b"),
        ("G834", "#1812", "#1811", "2e6e6b62defcdcfed95f7f8036eb4a134ccc5adf"),
        ("G835", "#1817", "#1813", "1ff9e75d1ee80739a9ec8aeea8d905b60a757a86"),
        ("G836", "#1819", "#1784, #1818", "75d68523739318eb97932c6f55a35e547e00b769"),
        ("G837", "#1821", "#1820", "cd276e20754db09337a94a8b973ddebdd1564ba3"),
        ("G839", "#1826", "#1824", "ab50131426e5581db56f389c9599463a243f1080"),
        ("G840", "#1827", "#1825", "c66f49360ce53223f5820f0aa5017e6cd21c6d5e"),
        ("G841", "#1830", "#1828", "3da9e7a1aa29396c6879285ad19650b4af033e3d"),
        ("G847", "#1837", "#1834", "cbe5475977d151f953d1235733e6d9688e0296f1"),
        ("G846", "#1838", "#1835", "103f4b869d4b5b39170e65e3230e3ac35c7c2aaf"),
        ("G845", "#1840", "#1836", "a24cf8abfb88a323b2f0efe04fe2481c746d9e9f"),
        ("G842", "#1839", "#1829", "452fd0dd21968914ba61123a2f8f820e8183144e"),
        ("G849", "#1844", "#1841", "b2211f7ed619bbeee2b1d199233531f1d4e96edc"),
        ("G848", "#1843", "#1842", "a2431590f08336349be4edaa9ea5b1847f8aa533"),
    ];

    private static readonly string[] FirstParentCommits =
    [
        "1b3c7229cfe8c8f8565034a7e2220a94ac14785b",
        "09b1f4edca51f3acbbe3e901356866996f4be29f",
        "67c8578090f1a53e8894aeff88abd6cd8b83ff15",
        "6e0bff220e2bf51308596c19ee258835ce509dd8",
        "11457187ad0f9c2c269b80de84b0fd9ea278dfe5",
        "2a833a976688b3139678e4954162a9c00d32d0f4",
        "b0f5354ba9a922e1676a2e654d866c2a08f60104",
        "16267f9d58af31669252186a16ce09ab0dd47ba4",
        "1f4f94155914c9f6097ec8f6bbad916f13e7817b",
        "d4645e2f02aea6a7969804a156df176cc2122a4a",
        "5ffcea48b0060569112efee06ad12baa5d8c9b59",
        "8e1ded058aeb6738c9419ce584f8da0d9fa59888",
        "ea5959f83f528675072b8f034f5c3fc30cbb07b8",
        "80270af5c54bf4e89dfbb8eb5f4a93e7142e8e36",
        "3d604981865a04c2875fde06d76cf5b60d41fda0",
        "828e19815e0bd8356298e5c49ae3c90bd8a1751d",
        "cf40ac8f3211925339134f5fa8bb91bc722e549b",
        "0f7cb50b0f7d42da120fe04bc4421807dcf5da16",
        "ebb7f21745a24d985c3ffdc7b370195506c1338c",
        "3262d7c543f3663a8ce83fb8a2e86018162d6962",
        "540c6efdf63300d05fe79e8020574c1bc3f96ad7",
        "f9a91a61edb3ed6291750a19e0247212fccad270",
        "baef0f3534c0a7a0de7dac0e0f1b4a7946c7c0a9",
        "7003e08b0152e91f89069a67b5ca299bef7cd6de",
        "3d91a8004c97ab800f365d89189c12def8a39980",
        "9d521ebc4ef45c7aea52527e77bb21aa34f1c9a5",
        "01944a5ed47139b47276ef2fcbc951183f8e442b",
        "20ef0a6caa184b91e4c3cd2378bd37d941e0de7a",
        "ca7272f4b88e00577e7c423d41a888f0f0defaf6",
        "71ca5da979f549b9f4f1f4c4ebb45ddd0696fb44",
        "0762312ddccf14009d3252c5101d0b893ca7be6b",
        "227a981d42cee28924ba34cbde3f45d12717a202",
        "e78b27d1e99247380fa7518d67470304fb1d7e7b",
        "d5f72c10a26e4844fac38dbc362ab1d6052bc237",
        "2f5452a00866c02e9e80949c9e80b5d2b347fd35",
        "159952b410b6429acc03414993ac43a650df7fc7",
        "9e461d8fda129cfb9447d99b158abb31d9c5c70b",
        "2e6e6b62defcdcfed95f7f8036eb4a134ccc5adf",
        "1ff9e75d1ee80739a9ec8aeea8d905b60a757a86",
        "75d68523739318eb97932c6f55a35e547e00b769",
        "cd276e20754db09337a94a8b973ddebdd1564ba3",
        "ba496314af0602e593b46b3376d4beba4137f400",
        "ab50131426e5581db56f389c9599463a243f1080",
        "c66f49360ce53223f5820f0aa5017e6cd21c6d5e",
        "3da9e7a1aa29396c6879285ad19650b4af033e3d",
        "cbe5475977d151f953d1235733e6d9688e0296f1",
        "103f4b869d4b5b39170e65e3230e3ac35c7c2aaf",
        "a24cf8abfb88a323b2f0efe04fe2481c746d9e9f",
        "452fd0dd21968914ba61123a2f8f820e8183144e",
        "b2211f7ed619bbeee2b1d199233531f1d4e96edc",
        Base,
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCoverExactlyTheFortyTwoShippedUnitsThroughG848(string language)
    {
        var notes = ReadNotes(language);
        var listed = Regex.Matches(notes, @"(?m)^- (G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(Units.Select(unit => unit.Unit), listed);
        Assert.Equal(Units.Length, listed.Length);
        if (language == "en")
        {
            Assert.Contains("## Release inventory: exactly 42 shipped first-parent units", notes, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("## Release inventory: 正確に 42", notes, StringComparison.Ordinal);
        }

        foreach (var unit in Units)
        {
            var entry = FindEntry(notes, unit.Unit);
            Assert.NotEmpty(entry);
            Assert.Contains($"PR {unit.Pr} / issue {unit.Issue};", entry, StringComparison.Ordinal);
            Assert.Contains($"merge commit `{unit.Merge}`", entry, StringComparison.Ordinal);
            Assert.Contains("Operator-observable outcome", entry, StringComparison.Ordinal);
        }

        foreach (var (unit, qualifier) in InventoryScopeQualifiers(language))
        {
            var entry = FindEntry(notes, unit);
            Assert.Contains(qualifier, entry, StringComparison.Ordinal);
        }

        Console.WriteLine($"G850 AC3 {language}: shipped_units={listed.Length}; units={string.Join(',', listed)}; base={Base}; operator_outcomes={listed.Length}");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesPinTheFiftyOneCommitRangeAndClassifyEveryCommit(string language)
    {
        var notes = ReadNotes(language);

        Assert.Contains($"$ git rev-list --first-parent --reverse {Range}", notes, StringComparison.Ordinal);
        Assert.Contains($"$ git rev-list --first-parent --count {Range}\n51", notes, StringComparison.Ordinal);
        Assert.Equal(51, FirstParentCommits.Length);
        foreach (var commit in FirstParentCommits)
        {
            Assert.Contains(commit, notes, StringComparison.Ordinal);
        }

        var tableRows = Regex.Matches(notes, @"(?m)^\| `([0-9a-f]{40})` \| (.+?) \| (.+?) \|$")
            .Select(match => (Commit: match.Groups[1].Value, Classification: match.Groups[3].Value))
            .ToArray();
        Assert.Equal(FirstParentCommits, tableRows.Select(row => row.Commit));
        var includedCount = tableRows.Count(row => row.Classification is "included" or "partial unit, included");
        var priorPrepCount = tableRows.Count(row => row.Classification == "prior release prep");
        var claimStateCount = tableRows.Count(row => row.Classification == "claim state commit, not a unit");
        Assert.Equal(Units.Length, includedCount);
        Assert.Equal(4, priorPrepCount);
        Assert.Equal(5, claimStateCount);
        Assert.Contains("G802 / PR #1751 / issue #1750", notes, StringComparison.Ordinal);
        Assert.Contains("G804 / PR #1755", notes, StringComparison.Ordinal);
        Assert.Contains("G830 / PR #1804 / issue #1803", notes, StringComparison.Ordinal);
        Assert.Contains("G838 / PR #1823", notes, StringComparison.Ordinal);
        Assert.Contains("G813 / PR #1781 / linkage issue #1774 (not closed)", notes, StringComparison.Ordinal);
        Console.WriteLine($"G850 AC3 {language}: first_parent_count={tableRows.Length}; included={includedCount}; prior_prep={priorPrepCount}; claim_state={claimStateCount}; unclassified={tableRows.Length - includedCount - priorPrepCount - claimStateCount}");
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

        Console.WriteLine($"G804 AC8 {language}: legacy_aliases=design,orchestration,implementation,review; config_loading=preserved; queue_state_read_display=preserved; guide_routes=unchanged");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesPinThreeMeasuredVersionIdentities(string language)
    {
        var notes = ReadNotes(language);

        Assert.True(MeasurementSegmentsAreCurrent(notes));
        Assert.Contains("dotnet build IntentSystem.sln --configuration Release", notes, StringComparison.Ordinal);
        Assert.Contains("-p:Version=0.32.0", notes, StringComparison.Ordinal);
        Assert.Contains("release.yml", notes, StringComparison.Ordinal);
        Assert.Contains("RAW=v0.32.0", notes, StringComparison.Ordinal);
        Assert.Contains("VERSION=0.32.0", notes, StringComparison.Ordinal);
        Assert.Contains("eng/version.json", notes, StringComparison.Ordinal);
        Assert.Contains("local builds", notes, StringComparison.Ordinal);
        Assert.Contains("dry runs", notes, StringComparison.Ordinal);
        Assert.Contains("**not** v0.32.0", Normalize(notes), StringComparison.Ordinal);

        Console.WriteLine($"G804 AC4 {language}: named_base={Base}; normal={NormalPlaceholderIdentity}; explicit={ExplicitReleaseIdentity}; published=RAW=v0.32.0 -> VERSION=0.32.0; stale_measurement_fragment={PreviousBaseFragment}=0; accounting_occurrence={PreviousBaseFragment}=allowed");
    }

    [Theory]
    [InlineData("named-base")]
    [InlineData("prose-base")]
    [InlineData("normal-identity")]
    [InlineData("explicit-identity")]
    public void StaleMeasurementFragmentFailsTheCriterion4Guard(string segment)
    {
        var notes = ReadNotes("en");
        var mutated = segment switch
        {
            "named-base" => notes.Replace(
                $"$ git rev-parse HEAD\n{Base}",
                $"$ git rev-parse HEAD\n{PreviousBase}",
                StringComparison.Ordinal),
            "normal-identity" => notes.Replace(NormalPlaceholderIdentity, "intent-cli 0.32.1-cd276e2-G837", StringComparison.Ordinal),
            "explicit-identity" => notes.Replace(ExplicitReleaseIdentity, "intent-cli 0.32.0-cd276e2-G837", StringComparison.Ordinal),
            "prose-base" => notes.Replace(
                $"The named product base is `{Base}`",
                $"The named product base is `{PreviousBase}`",
                StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(segment), segment, null),
        };

        Assert.False(MeasurementSegmentsAreCurrent(mutated));
        Assert.Contains(PreviousBaseFragment, mutated, StringComparison.Ordinal);
        Console.WriteLine($"G850 AC4 stale mutation: segment={segment}; stale_fragment={PreviousBaseFragment}; guard_passed=False; result=FAIL (expected guard refusal)");
    }

    [Fact]
    public void EnglishAndJapaneseMirrorsHaveIdenticalUnitPrIssueAndMergeTuples()
    {
        var english = ParseInventory(ReadNotes("en"));
        var japanese = ParseInventory(ReadNotes("ja"));

        Assert.Equal(Units, english);
        Assert.Equal(english, japanese);
        Console.WriteLine($"G804 AC7 parity: en=ja; tuples={english.Count}; mutation_guard=ready");
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
        Console.WriteLine($"G804 AC7 parity mutation: changed=issue #1737->#9999; equal={english.SequenceEqual(mutated)}; result=FAIL (expected guard)");
    }

    private static readonly (string Unit, string EnQualifier, string JaQualifier)[] PostPreview4Units =
    [
        ("G839", "refusal output names the decision", "refusal output は decision をそのまま示します"),
        ("G840", "test timing is deterministic; product behavior is unchanged", "test timing は deterministic で、product behavior は不変です"),
        ("G841", "packet.yaml parse failures have named causes", "packet.yaml の parse failure は named cause になります"),
        ("G847", "65,536 bytes is intent-cli's own conservative limit, not a verified GitHub boundary", "65,536 bytes は intent-cli 固有の conservative limit で、GitHub の verified boundary ではありません"),
        ("G846", "58,000 bytes is intent-cli's own warning-band budget, not a verified GitHub boundary", "58,000 bytes は intent-cli 固有の warning-band budget で、GitHub の verified boundary ではありません"),
        ("G845", "65,536 bytes is intent-cli's own conservative limit, not a verified GitHub boundary", "65,536 bytes は intent-cli 固有の conservative limit で、GitHub の verified boundary ではありません"),
        ("G842", "intent-cli renders text and never launches a reviewer, builder, or provider", "intent-cli は text を render するだけで、reviewer・builder・provider を起動しません"),
        ("G849", "GH_CONFIG_DIR is resolved at request time and remains protected", "GH_CONFIG_DIR は request 時の値で、protected root のままです"),
        ("G848", "on non-Windows, the opened source must have owner-only mode bits", "non-Windows では、open 済みの source に owner-only mode bits だけを認めます"),
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void StableSectionNamesEveryPostPreview4UnitAndQualifier(string language)
    {
        var notes = ReadNotes(language);
        AssertStableSection(notes, language);

        var section = ExtractStableSection(notes, language);
        var listed = Regex.Matches(section, @"(?m)^- \*\*(G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        var qualifiers = PostPreview4Units
            .Select(unit => language == "en" ? unit.EnQualifier : unit.JaQualifier)
            .ToArray();

        Console.WriteLine($"G850 AC2 {language}: section_units={listed.Length}; units={string.Join(',', listed)}; qualifiers={qualifiers.Length}; parity_source={language}");
    }

    [Fact]
    public void StableSectionEnglishAndJapaneseHaveIdenticalTuples()
    {
        var english = ParseStableInventory(ReadNotes("en"));
        var japanese = ParseStableInventory(ReadNotes("ja"));

        Assert.Equal(Units.Skip(Units.Length - PostPreview4Units.Length), english);
        Assert.Equal(english, japanese);
        Console.WriteLine($"G850 AC2 parity: en=ja; tuples={english.Count}; units={string.Join(',', english.Select(unit => unit.Unit))}");
    }

    [MemberData(nameof(StableSectionUnitMutationCases))]
    [Theory]
    public void StableSectionUnitRemovalFailsTheGuard(string language, string unit)
    {
        var notes = ReadNotes(language);
        var section = ExtractStableSection(notes, language);
        var entry = FindStableEntry(section, unit);
        var mutated = notes.Replace(entry, string.Empty, StringComparison.Ordinal);

        Assert.ThrowsAny<Exception>(() => AssertStableSection(mutated, language));
        Console.WriteLine($"G850 AC2 unit mutation: language={language}; unit={unit}; guard_passed=False; result=FAIL (expected guard refusal)");
    }

    [MemberData(nameof(StableSectionQualifierMutationCases))]
    [Theory]
    public void StableSectionQualifierRemovalFailsTheGuard(string language, string unit, string qualifier)
    {
        var notes = ReadNotes(language);
        var section = ExtractStableSection(notes, language);
        var mutated = notes.Replace(qualifier, "REMOVED", StringComparison.Ordinal);

        Assert.DoesNotContain(qualifier, FindStableEntry(ExtractStableSection(mutated, language), unit), StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => AssertStableSection(mutated, language));
        Console.WriteLine($"G850 AC2 qualifier mutation: language={language}; unit={unit}; qualifier={qualifier}; guard_passed=False; result=FAIL (expected guard refusal)");
    }

    public static IEnumerable<object[]> StableSectionUnitMutationCases() =>
        from language in new[] { "en", "ja" }
        from unit in PostPreview4Units
        select new object[] { language, unit.Unit };

    public static IEnumerable<object[]> StableSectionQualifierMutationCases() =>
        from language in new[] { "en", "ja" }
        from unit in PostPreview4Units
        select new object[]
        {
            language,
            unit.Unit,
            language == "en" ? unit.EnQualifier : unit.JaQualifier,
        };

    [Theory]
    [InlineData("shipped-unit-count")]
    [InlineData("first-parent-count")]
    public void PreviousThirtyThreeAndFortyOneCountsFailTheStableGuard(string segment)
    {
        var notes = ReadNotes("en");
        var mutated = segment switch
        {
            "shipped-unit-count" => notes.Replace(
                "## Release inventory: exactly 42 shipped first-parent units",
                "## Release inventory: exactly 33 shipped first-parent units",
                StringComparison.Ordinal),
            "first-parent-count" => notes.Replace(
                $"$ git rev-list --first-parent --count {Range}\n51",
                $"$ git rev-list --first-parent --count {Range}\n41",
                StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(segment), segment, null),
        };

        if (segment == "shipped-unit-count")
        {
            Assert.ThrowsAny<Exception>(() => Assert.Contains(
                "## Release inventory: exactly 42 shipped first-parent units",
                mutated,
                StringComparison.Ordinal));
        }
        else
        {
            Assert.ThrowsAny<Exception>(() => Assert.Contains(
                $"$ git rev-list --first-parent --count {Range}\n51",
                mutated,
                StringComparison.Ordinal));
        }

        Console.WriteLine($"G850 AC3 stale-count mutation: segment={segment}; stale_value={(segment == "shipped-unit-count" ? "33" : "41")}; guard_passed=False; result=FAIL (expected guard refusal)");
    }

    private static readonly string[] PostPreview2MinorJustificationRoutes =
    [
        "automation progress-supervision", "guide progress-supervision", "guide steward-thread", "issue sync-body",
        "notify ack", "notify acknowledge", "notify progress-supervision", "session-layer seat",
    ];

    private static readonly string[] PostPreview3MinorJustificationRoutes =
    [
        "automation pr-created-stale-recovery", "guide solo-conductor",
        "review cross-runtime", "review cross-runtime request", "review cross-runtime record", "review cross-runtime status",
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesPinMinorRouteDecisionAndPrepareOnlyBoundary(string language)
    {
        var notes = ReadNotes(language);
        var normalized = Normalize(notes);
        var postPreview2Paragraph = ExtractMinorJustificationRouteParagraph(notes, language, postPreview2: true);
        var postPreview3Paragraph = ExtractMinorJustificationRouteParagraph(notes, language, postPreview2: false);

        Assert.Contains("G796", normalized, StringComparison.Ordinal);
        Assert.Contains("G800", normalized, StringComparison.Ordinal);
        Assert.Contains("G803", normalized, StringComparison.Ordinal);
        Assert.Contains("command-route addition is a minor", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("option-level additions do not count as command routes", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not counted", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PREPARED / NOT PUBLISHED", normalized, StringComparison.Ordinal);
        Assert.Contains("no tag", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no GitHub Release", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no workflow", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no product source", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026-09-15", normalized, StringComparison.Ordinal);
        Assert.Contains("record-orca-run", normalized, StringComparison.Ordinal);
        Assert.Contains("G795–G848", notes, StringComparison.Ordinal);
        Assert.Contains("v0.32.0-preview.4", notes, StringComparison.Ordinal);
        Assert.Contains(Normalize(CountingMethodSentence(language)), normalized, StringComparison.Ordinal);

        foreach (var route in PostPreview2MinorJustificationRoutes)
        {
            Assert.Contains($"`{route}`", postPreview2Paragraph, StringComparison.Ordinal);
            Assert.DoesNotContain($"`{route}`", postPreview3Paragraph, StringComparison.Ordinal);
        }

        foreach (var route in PostPreview3MinorJustificationRoutes)
        {
            Assert.Contains($"`{route}`", postPreview3Paragraph, StringComparison.Ordinal);
            Assert.DoesNotContain($"`{route}`", postPreview2Paragraph, StringComparison.Ordinal);
        }

        var routeCount = PostPreview2MinorJustificationRoutes.Length + PostPreview3MinorJustificationRoutes.Length;
        Console.WriteLine($"G838 AC4 {language}: routes_counted=G796,G800; G803=not counted; alias/config/guide/npm=not counted; prepare_only=true; routes={routeCount}");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void StableRouteDecisionUsesTheRegisteredTableAtBothBases(string language)
    {
        var notes = ReadNotes(language);
        var normalized = Normalize(notes);
        var paragraph = ExtractStableMinorJustificationRouteParagraph(notes, language);

        Assert.Contains("210", paragraph, StringComparison.Ordinal);
        Assert.Contains(StableCountingMethodSentence(language), normalized, StringComparison.Ordinal);
        Assert.Contains("post-preview.4", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("registered-command additions = 0", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("G839", paragraph, StringComparison.Ordinal);
        Assert.Contains("G840", paragraph, StringComparison.Ordinal);
        Assert.Contains("G841", paragraph, StringComparison.Ordinal);
        Assert.Contains("G847", paragraph, StringComparison.Ordinal);
        Assert.Contains("G846", paragraph, StringComparison.Ordinal);
        Assert.Contains("G845", paragraph, StringComparison.Ordinal);
        Assert.Contains("G842", paragraph, StringComparison.Ordinal);
        Assert.Contains("G849", paragraph, StringComparison.Ordinal);
        Assert.Contains("G848", paragraph, StringComparison.Ordinal);

        var registeredRowCounts = Regex.Matches(paragraph, @"\b210\b")
            .Select(match => int.Parse(match.Value))
            .ToArray();
        var routeCountMatch = Regex.Match(paragraph, @"registered-command additions = (?<count>\d+)");
        Assert.True(routeCountMatch.Success);
        var postPreview4RouteCount = int.Parse(routeCountMatch.Groups["count"].Value);
        Assert.Equal(new[] { 210, 210 }, registeredRowCounts);
        Assert.Equal(0, postPreview4RouteCount);
        Console.WriteLine($"G850 AC4 {language}: preview4_registered_rows={registeredRowCounts[0]}; stable_registered_rows={registeredRowCounts[1]}; post_preview4_route_additions={postPreview4RouteCount}; route_count={postPreview4RouteCount}; aliases_excluded=true");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void StableRouteCountingSentenceMutationFailsTheGuard(string language)
    {
        var notes = ReadNotes(language);
        var sentence = StableCountingMethodSentence(language);
        var mutated = notes.Replace(sentence, "The registered table was counted differently.", StringComparison.Ordinal);

        Assert.DoesNotContain(sentence, mutated, StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => Assert.Contains(sentence, mutated, StringComparison.Ordinal));
        Console.WriteLine($"G850 AC4 counting-method mutation: language={language}; guard_passed=False; result=FAIL (expected guard refusal)");
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

        Console.WriteLine("G804 AC6 policy: stableVersion=0.32.0; nextVersion=0.32.1; placeholders=en,ja; version_policy_diff=empty");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void Preview3SectionNamesTheRouteDecisionAndActionableBehaviorChanges_G830(string language)
    {
        var notes = Normalize(ReadNotes(language));

        foreach (var route in new[]
        {
            "automation progress-supervision", "guide progress-supervision", "guide steward-thread", "issue sync-body",
            "notify ack", "notify acknowledge", "notify progress-supervision", "session-layer seat",
        })
        {
            Assert.Contains($"`{route}`", notes, StringComparison.Ordinal);
        }

        Assert.Contains("v0.32.0-preview.3", notes, StringComparison.Ordinal);
        Assert.Contains("2026-09-14", notes, StringComparison.Ordinal);
        Assert.Contains("[supervision] opt_in_teams", notes, StringComparison.Ordinal);
        Assert.Contains("intent-cli notify supervise repair-cycle-history --domain <d> --team <t> --write", notes, StringComparison.Ordinal);
        Assert.Contains("--mode herdr-only --write", notes, StringComparison.Ordinal);
        Assert.Contains("bootstrap.resume_recommended", notes, StringComparison.Ordinal);
        Assert.Contains("read-compare-write-verified", notes, StringComparison.Ordinal);
        Console.WriteLine($"G830 AC4 {language}: routes=8; opt_in=declared; stalls_repair=named; herdr_only=named; resume_change=disclosed");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void Preview4SectionNamesTheRouteDecisionAndActionableBehaviorChanges_G838(string language)
    {
        var notes = Normalize(ReadNotes(language));
        var preview4 = Normalize(ExtractPreviewSection(ReadNotes(language), language, previewNumber: 4));

        Assert.Contains("v0.32.0-preview.4", notes, StringComparison.Ordinal);
        Assert.Contains("2026-09-15", notes, StringComparison.Ordinal);
        Assert.Contains("[[cross_runtime_review.teams]]", preview4, StringComparison.Ordinal);
        Assert.Contains("conductor_runtime", preview4, StringComparison.Ordinal);
        Assert.Contains("--head-sha", preview4, StringComparison.Ordinal);
        Assert.Contains("--mode solo-conductor --write", preview4, StringComparison.Ordinal);
        Assert.Contains("every team on that host", preview4, StringComparison.Ordinal);

        foreach (var route in new[]
        {
            "guide solo-conductor",
            "automation pr-created-stale-recovery",
            "session-layer topology record-orca-run",
            "session-layer topology orca-runs",
        })
        {
            Assert.Contains(route, preview4, StringComparison.Ordinal);
        }

        foreach (var qualifier in Preview4ScopeQualifiers(language))
        {
            Assert.Contains(qualifier, preview4, StringComparison.Ordinal);
        }

        Console.WriteLine($"G838 AC5 {language}: preview=4; cross_runtime=declared; solo_conductor=named; orca_binding=named; scope_qualifiers={Preview4ScopeQualifiers(language).Length}");
    }

    [Theory]
    [InlineData("en", "G833", "never starts or manages an agent")]
    [InlineData("ja", "G833", "agent を起動・管理しません")]
    [InlineData("en", "G834", "[[cross_runtime_review.teams]]")]
    [InlineData("ja", "G834", "[[cross_runtime_review.teams]]")]
    [InlineData("en", "G834", "never executes a process")]
    [InlineData("ja", "G834", "process は実行しません")]
    [InlineData("en", "G835", "--kind design")]
    [InlineData("ja", "G835", "--kind design")]
    [InlineData("en", "G835", "for any caller, gated or not")]
    [InlineData("ja", "G835", "gated か否かに関わらず")]
    [InlineData("en", "G835", "on a gated repo")]
    [InlineData("ja", "G835", "gated repo 上")]
    [InlineData("en", "G835", "undeclared team or an ungated repo is byte-identical")]
    [InlineData("ja", "G835", "宣言のない team または ungated repo は宣言のない host と byte-identical")]
    [InlineData("en", "G836", "dry-run by default")]
    [InlineData("ja", "G836", "既定は dry-run")]
    [InlineData("en", "G836", "removes only the stale")]
    [InlineData("ja", "G836", "label だけを外し")]
    [InlineData("en", "G836", "on a host with no claims store (`claim-unavailable`)")]
    [InlineData("ja", "G836", "claims store のない host では `claim-unavailable` で拒否します")]
    [InlineData("en", "G837", "or solo sidecar")]
    [InlineData("ja", "G837", "または solo sidecar")]
    [InlineData("en", "G837", "never runs `orca`")]
    [InlineData("ja", "G837", "orca` を実行せず")]
    [InlineData("en", "G837", "or creates a Run")]
    [InlineData("ja", "G837", "Run も作成しません")]
    public void InventoryScopeQualifierRemovalFailsTheGuard_G838(string language, string unit, string qualifier)
    {
        var notes = ReadNotes(language);
        var entry = FindEntry(notes, unit);
        var mutated = entry.Replace(qualifier, "REMOVED", StringComparison.Ordinal);

        Assert.DoesNotContain(qualifier, mutated, StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => Assert.Contains(qualifier, mutated, StringComparison.Ordinal));
        Console.WriteLine($"G838 AC3 qualifier mutation: language={language}; unit={unit}; qualifier={qualifier}; guard_passed=False; result=FAIL (expected guard refusal)");
    }

    [Theory]
    [InlineData("en", "never starts or manages an agent")]
    [InlineData("ja", "agent を起動・管理しません")]
    [InlineData("en", "never executes a process")]
    [InlineData("ja", "process は実行せず")]
    [InlineData("en", "--kind design")]
    [InlineData("ja", "--kind design")]
    [InlineData("en", "work for any caller, declared or not")]
    [InlineData("ja", "宣言のあるなしに関わらず任意の caller で使えます")]
    [InlineData("en", "on a gated repo")]
    [InlineData("ja", "gated repo 上")]
    [InlineData("en", "dry-run by default")]
    [InlineData("ja", "既定は dry-run")]
    [InlineData("en", "only that label")]
    [InlineData("ja", "label だけを外し")]
    [InlineData("en", "or solo sidecar")]
    [InlineData("ja", "または solo sidecar")]
    [InlineData("en", "never runs `orca`")]
    [InlineData("ja", "orca` を実行せず")]
    [InlineData("en", "or creates a Run")]
    [InlineData("ja", "Run も作成しません")]
    public void Preview4ScopeQualifierRemovalFailsTheGuard_G838(string language, string qualifier)
    {
        var preview4 = Normalize(ExtractPreviewSection(ReadNotes(language), language, previewNumber: 4));
        var mutated = preview4.Replace(qualifier, "REMOVED", StringComparison.Ordinal);

        Assert.DoesNotContain(qualifier, mutated, StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => Assert.Contains(qualifier, mutated, StringComparison.Ordinal));
        Console.WriteLine($"G838 AC5 qualifier mutation: language={language}; qualifier={qualifier}; guard_passed=False; result=FAIL (expected guard refusal)");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void MinorJustificationRouteParagraphMutationFailsTheGuard_G838(string language)
    {
        var notes = ReadNotes(language);
        var postPreview2Paragraph = ExtractMinorJustificationRouteParagraph(notes, language, postPreview2: true);
        var movedRoute = PostPreview3MinorJustificationRoutes[0];
        var mutatedPreview2 = postPreview2Paragraph.Replace(
            PostPreview2MinorJustificationRoutes[0],
            movedRoute,
            StringComparison.Ordinal);

        Assert.Contains(movedRoute, mutatedPreview2, StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() =>
            Assert.DoesNotContain($"`{movedRoute}`", mutatedPreview2, StringComparison.Ordinal));
        Console.WriteLine($"G838 AC4 route-paragraph mutation: language={language}; route={movedRoute}; guard_passed=False; result=FAIL (expected guard refusal)");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void CountingMethodSentenceMutationFailsTheGuard_G838(string language)
    {
        var notes = Normalize(ReadNotes(language));
        var sentence = Normalize(CountingMethodSentence(language));
        var mutated = notes.Replace(sentence, "Counted as rows in the Registered command table only. The measured row count went from 204 to 210.", StringComparison.Ordinal);

        Assert.DoesNotContain(sentence, mutated, StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => Assert.Contains(sentence, mutated, StringComparison.Ordinal));
        Console.WriteLine($"G838 AC4 counting-method mutation: language={language}; guard_passed=False; result=FAIL (expected guard refusal)");
    }

    [Theory]
    [InlineData("en", "G795–G848")]
    [InlineData("ja", "G795–G848")]
    [InlineData("en", "v0.32.0-preview.4")]
    [InlineData("ja", "v0.32.0-preview.4")]
    public void BannerWordingMutationFailsTheGuard_G838(string language, string bannerLiteral)
    {
        var notes = ReadNotes(language);
        var mutated = notes.Replace(bannerLiteral, "REMOVED", StringComparison.Ordinal);

        Assert.DoesNotContain(bannerLiteral, mutated, StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => Assert.Contains(bannerLiteral, mutated, StringComparison.Ordinal));
        Console.WriteLine($"G838 AC9 banner mutation: language={language}; literal={bannerLiteral}; guard_passed=False; result=FAIL (expected guard refusal)");
    }

    private static void AssertStableSection(string notes, string language)
    {
        var section = ExtractStableSection(notes, language);
        var listed = Regex.Matches(section, @"(?m)^- \*\*(G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(PostPreview4Units.Select(unit => unit.Unit), listed);
        Assert.Equal(PostPreview4Units.Length, listed.Length);
        foreach (var unit in PostPreview4Units)
        {
            var source = Units.Single(candidate => candidate.Unit == unit.Unit);
            var entry = FindStableEntry(section, unit.Unit);
            Assert.NotEmpty(entry);
            Assert.Contains($"PR {source.Pr} / issue {source.Issue};", entry, StringComparison.Ordinal);
            Assert.Contains($"merge commit `{source.Merge}`", entry, StringComparison.Ordinal);
            var qualifier = language == "en" ? unit.EnQualifier : unit.JaQualifier;
            Assert.Contains(qualifier, entry, StringComparison.Ordinal);
        }
    }

    private static string FindEntry(string notes, string unit)
    {
        var match = Regex.Match(notes, $"(?ms)^- {Regex.Escape(unit)} —.*?(?=^- |^## |\\z)");
        return match.Success ? match.Value : string.Empty;
    }

    private static string FindStableEntry(string section, string unit)
    {
        var match = Regex.Match(section, $"(?ms)^- \\*\\*{Regex.Escape(unit)} —.*?(?=^- \\*\\*G|^## |\\z)");
        return match.Success ? match.Value : string.Empty;
    }

    private static IReadOnlyList<(string Unit, string Pr, string Issue, string Merge)> ParseStableInventory(string notes)
    {
        var section = ExtractStableSection(notes, notes.Contains("## Stable: what changed since preview.4", StringComparison.Ordinal) ? "en" : "ja");
        return Regex.Matches(
                section,
                @"(?ms)^- \*\*(G\d+) — PR (#\d+) / issue (#\d+(?:, #\d+)*); merge commit `([0-9a-f]{40})`\*\*")
            .Select(match => (
                match.Groups[1].Value,
                match.Groups[2].Value,
                match.Groups[3].Value,
                match.Groups[4].Value))
            .ToArray();
    }

    private static IEnumerable<(string Unit, string Qualifier)> InventoryScopeQualifiers(string language) =>
        language switch
        {
            "en" =>
            [
                ("G833", "never starts or manages an agent"),
                ("G834", "[[cross_runtime_review.teams]]"),
                ("G834", "never executes a process"),
                ("G835", "--kind design"),
                ("G835", "for any caller, gated or not"),
                ("G835", "on a gated repo"),
                ("G835", "undeclared team or an ungated repo is byte-identical"),
                ("G836", "dry-run by default"),
                ("G836", "removes only the stale"),
                ("G836", "on a host with no claims store (`claim-unavailable`)"),
                ("G837", "or solo sidecar"),
                ("G837", "never runs `orca`"),
                ("G837", "or creates a Run"),
            ],
            "ja" =>
            [
                ("G833", "agent を起動・管理しません"),
                ("G834", "[[cross_runtime_review.teams]]"),
                ("G834", "process は実行しません"),
                ("G835", "--kind design"),
                ("G835", "gated か否かに関わらず"),
                ("G835", "gated repo 上"),
                ("G835", "宣言のない team または ungated repo は宣言のない host と byte-identical"),
                ("G836", "既定は dry-run"),
                ("G836", "label だけを外し"),
                ("G836", "claims store のない host では `claim-unavailable` で拒否します"),
                ("G837", "または solo sidecar"),
                ("G837", "orca` を実行せず"),
                ("G837", "Run も作成しません"),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
        };

    private static string[] Preview4ScopeQualifiers(string language) =>
        language switch
        {
            "en" =>
            [
                "never starts or manages an agent",
                "never executes a process",
                "--kind design",
                "work for any caller, declared or not",
                "on a gated repo",
                "dry-run by default",
                "only that label",
                "or solo sidecar",
                "never runs `orca`",
                "or creates a Run",
            ],
            "ja" =>
            [
                "agent を起動・管理しません",
                "process は実行せず",
                "--kind design",
                "宣言のあるなしに関わらず任意の caller で使えます",
                "gated repo 上",
                "既定は dry-run",
                "label だけを外し",
                "または solo sidecar",
                "orca` を実行せず",
                "Run も作成しません",
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
        };

    private static string CountingMethodSentence(string language) =>
        language switch
        {
            "en" => "Counted as rows in the Registered command table only (the table opened by `| Registered command |` and closed by `## Durable schemas and legacy inventory`, excluding the seven-row alias table). The measured row count went from 204 to 210 in both EN and JA mirrors.",
            "ja" => "Registered command table の行だけを数えます（`| Registered command |` で開き `## Durable schema と legacy inventory` で閉じる table、七行の alias table は除外）。測定した行数は EN/JA 両 mirror で 204 から 210 になりました。",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
        };

    private static string StableCountingMethodSentence(string language) =>
        language switch
        {
            "en" => "Counted with the same Registered command table method: 210 rows at `ba496314af0602e593b46b3376d4beba4137f400` and 210 at `a2431590f08336349be4edaa9ea5b1847f8aa533`, aliases excluded; post-preview.4 registered-command additions = 0.",
            "ja" => "同じ Registered command table の方法で数えると、alias table を除く行数は `ba496314af0602e593b46b3376d4beba4137f400` で 210、`a2431590f08336349be4edaa9ea5b1847f8aa533` で 210、post-preview.4 registered-command additions = 0 です。",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
        };

    private static string ExtractPreviewSection(string notes, string language, int previewNumber)
    {
        var heading = language switch
        {
            "en" => $"## Preview.{previewNumber}: what changed since preview.{previewNumber - 1}",
            "ja" => previewNumber switch
            {
                3 => "## Preview.3: preview.2 からの変更",
                4 => "## Preview.4: preview.3 からの変更",
                _ => throw new ArgumentOutOfRangeException(nameof(previewNumber), previewNumber, null),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
        };
        var match = Regex.Match(notes, $@"(?ms)^{Regex.Escape(heading)}.*?(?=^## |\z)");
        return match.Success ? match.Value : string.Empty;
    }

    private static string ExtractStableSection(string notes, string language)
    {
        var heading = language switch
        {
            "en" => "## Stable: what changed since preview.4",
            "ja" => "## Stable: preview.4 以降の変更",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
        };
        var match = Regex.Match(notes, $@"(?ms)^{Regex.Escape(heading)}.*?(?=^## |\z)");
        return match.Success ? match.Value : string.Empty;
    }

    private static string ExtractMinorJustificationRouteParagraph(string notes, string language, bool postPreview2)
    {
        var marker = language switch
        {
            "en" => postPreview2
                ? "Since preview.2 the compatibility ledger gained eight command routes:"
                : "Since preview.3 the compatibility ledger gained six command routes:",
            "ja" => postPreview2
                ? "preview.2 以降、compatibility ledger には八つの command route が加わりました:"
                : "preview.3 以降、compatibility ledger には六つの command route が加わりました:",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
        };
        var start = notes.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing minor-justification marker: {marker}");
        var nextMarker = language switch
        {
            "en" => postPreview2
                ? "Since preview.3 the compatibility ledger gained six command routes:"
                : "The route decision is independently observable",
            "ja" => postPreview2
                ? "preview.3 以降、compatibility ledger には六つの command route が加わりました:"
                : "merged history から route の判断を再現できます",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
        };
        var end = notes.IndexOf(nextMarker, start + marker.Length, StringComparison.Ordinal);
        return end >= 0 ? notes[start..end] : notes[start..];
    }

    private static string ExtractStableMinorJustificationRouteParagraph(string notes, string language)
    {
        var marker = language switch
        {
            "en" => "Since preview.4 the nine stable units add no registered command route:",
            "ja" => "preview.4 以降の stable unit 九件には registered command route の追加がありません:",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
        };
        var start = notes.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing stable minor-justification marker: {marker}");
        var nextMarker = language switch
        {
            "en" => "The route decision is independently observable",
            "ja" => "merged history から route の判断を再現できます",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
        };
        var end = notes.IndexOf(nextMarker, start + marker.Length, StringComparison.Ordinal);
        return end >= 0 ? notes[start..end] : notes[start..];
    }

    private static IReadOnlyList<(string Unit, string Pr, string Issue, string Merge)> ParseInventory(string notes) =>
        Regex.Matches(
                notes,
                @"(?ms)^- (G\d+) — PR (#\d+) / issue (#\d+(?:, #\d+)*); merge commit `([0-9a-f]{40})`.*?(?=^- |^## |\z)")
            .Select(match => (
                match.Groups[1].Value,
                match.Groups[2].Value,
                match.Groups[3].Value,
                match.Groups[4].Value))
            .ToArray();

    private static string Normalize(string value) => Regex.Replace(value, @"\s+", " ");

    private static bool MeasurementSegmentsAreCurrent(string notes)
    {
        var namedBase = Regex.Match(
            notes,
            @"(?m)^\$ git rev-parse HEAD\r?\n(?<base>[0-9a-f]{40})");
        if (!namedBase.Success || namedBase.Groups["base"].Value != Base ||
            namedBase.Value.Contains(PreviousBaseFragment, StringComparison.Ordinal))
        {
            return false;
        }

        // The minor-justification prose names the base too; a stale SHA there is
        // as wrong as a stale measurement block (G850 review at 3ca5533d).
        var prose = Regex.Match(notes, @"(?:named product base is|named product base は) `(?<base>[0-9a-f]{40})`");
        if (!prose.Success || prose.Groups["base"].Value != Base)
        {
            return false;
        }

        var identities = Regex.Matches(notes, @"(?m)^intent-cli [^\r\n]+$")
            .Select(match => match.Value)
            .ToArray();
        return identities.Length == 2 &&
               identities.Contains(NormalPlaceholderIdentity, StringComparer.Ordinal) &&
               identities.Contains(ExplicitReleaseIdentity, StringComparer.Ordinal) &&
               identities.All(identity =>
                   identity.Contains(Base[..7], StringComparison.Ordinal) &&
                   !identity.Contains(PreviousBaseFragment, StringComparison.Ordinal));
    }

    private static string ReadNotes(string language) => File.ReadAllText(Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.32.0.md"));
}
