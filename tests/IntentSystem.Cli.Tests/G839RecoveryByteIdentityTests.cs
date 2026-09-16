namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed partial class G839RecoveryByteIdentityTests
{
    private static readonly string FixtureRoot = Path.Combine(
        RepoVersionPolicySource.RepoRoot(),
        "tests",
        "IntentSystem.Cli.Tests",
        "Fixtures",
        "G839",
        "base");

    [Theory]
    [MemberData(nameof(RecoveryFixtureIds))]
    public void RecoveryCommand_NonTargetPaths_MatchBase(string fixtureId)
    {
        var expected = File.ReadAllText(FixturePath(fixtureId));
        var actual = CaptureRecovery(fixtureId);
        Assert.Equal(expected, actual);
    }

    public static TheoryData<string> RecoveryFixtureIds() =>
        new(G839ByteIdentityHarness.RecoveryScenarioIds);

    private static string FixturePath(string fixtureId) => Path.Combine(FixtureRoot, fixtureId + ".txt");
}
