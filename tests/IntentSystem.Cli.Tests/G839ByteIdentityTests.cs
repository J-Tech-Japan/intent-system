using System.Reflection;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

[Collection(AutomationPrTransitionSharedStateCollection.Name)]
public sealed partial class G839ByteIdentityTests
{
    private static readonly string FixtureRoot = Path.Combine(
        RepoVersionPolicySource.RepoRoot(),
        "tests",
        "IntentSystem.Cli.Tests",
        "Fixtures",
        "G839",
        "base");

    [Fact]
    public void NoAppendSeamAdded()
    {
        var recoveryProperties = typeof(AutomationPrCreatedStaleRecoveryCommand)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var transitionProperties = typeof(AutomationPrTransitionCommand)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        string[] expectedRecovery =
        [
            "CandidateListerFactory",
            "ClaimVerifierFactory",
            "IssueLookupFactory",
            "LabelMutatorFactory",
            "PrLookupFactory",
            "UtcNowFactory",
        ];
        string[] expectedTransition = ["MutatorFactory", "NestedProviderLauncher", "PrHeadReader"];

        Assert.Equal(expectedRecovery, recoveryProperties);
        Assert.Equal(expectedTransition, transitionProperties);
        Assert.DoesNotContain(recoveryProperties, name => name.Contains("Append", StringComparison.Ordinal));
        Assert.DoesNotContain(transitionProperties, name => name.Contains("Append", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(PrTransitionRefusalTextFixtureIds))]
    public void PrTransitionRefusalText_MatchesBase(string fixtureId)
    {
        var expected = File.ReadAllText(FixturePath(fixtureId));
        var actual = CapturePrTransition(fixtureId);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(PrTransitionNonRefusalFixtureIds))]
    public void PrTransition_NonRefusalPaths_MatchBase(string fixtureId)
    {
        var expected = File.ReadAllText(FixturePath(fixtureId));
        var actual = CapturePrTransition(fixtureId);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(PlannedLabelsConsumerFixtureIds))]
    public void PlannedLabelsConsumers_MatchBase(string fixtureId)
    {
        var expected = File.ReadAllText(FixturePath(fixtureId));
        var actual = G839ByteIdentityHarness.CapturePlannedLabelsConsumer(fixtureId);
        Assert.Equal(expected, actual);
    }

    [Fact(Skip = "Set G839_CAPTURE=1 and remove Skip while checked out at base ba496314; running on head overwrites committed base fixtures.")]
    public void Capture_PrTransitionBaseFixtures()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("G839_CAPTURE"), "1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Set G839_CAPTURE=1, remove the Skip attribute on this test, and run against base commit ba496314 "
                + "to refresh tests/IntentSystem.Cli.Tests/Fixtures/G839/base/*.txt. Do not capture on head.");
        }

        Directory.CreateDirectory(FixtureRoot);
        foreach (var fixtureId in G839ByteIdentityHarness.PrTransitionRefusalTextScenarioIds
                     .Concat(G839ByteIdentityHarness.PrTransitionNonRefusalScenarioIds))
        {
            File.WriteAllText(FixturePath(fixtureId), CapturePrTransition(fixtureId));
        }

        foreach (var fixtureId in G839ByteIdentityHarness.PlannedLabelsConsumerScenarioIds)
        {
            File.WriteAllText(FixturePath(fixtureId), G839ByteIdentityHarness.CapturePlannedLabelsConsumer(fixtureId));
        }
    }

    public static TheoryData<string> PrTransitionRefusalTextFixtureIds() =>
        new(G839ByteIdentityHarness.PrTransitionRefusalTextScenarioIds);

    public static TheoryData<string> PrTransitionNonRefusalFixtureIds() =>
        new(G839ByteIdentityHarness.PrTransitionNonRefusalScenarioIds);

    public static TheoryData<string> PlannedLabelsConsumerFixtureIds() =>
        new(G839ByteIdentityHarness.PlannedLabelsConsumerScenarioIds);

    private static string FixturePath(string fixtureId) => Path.Combine(FixtureRoot, fixtureId + ".txt");
}
