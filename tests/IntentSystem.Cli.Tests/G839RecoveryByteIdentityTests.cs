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

    [Theory]
    [MemberData(nameof(WorkerNextActionPlannedLabelsFixtureIds))]
    public void PlannedLabelsWorkerNextAction_MatchBase(string fixtureId)
    {
        var expected = File.ReadAllText(FixturePath(fixtureId));
        var actual = CaptureWorkerNextAction();
        Assert.Equal(expected, actual);
    }

    [Fact(Skip = "Set G839_CAPTURE=1 and remove Skip while checked out at base ba496314; running on head overwrites committed base fixtures.")]
    public void Capture_RecoveryBaseFixtures()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("G839_CAPTURE"), "1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Set G839_CAPTURE=1, remove the Skip attribute on this test, and run against base commit ba496314 "
                + "to refresh tests/IntentSystem.Cli.Tests/Fixtures/G839/base/*.txt. Do not capture on head.");
        }

        Directory.CreateDirectory(FixtureRoot);
        foreach (var fixtureId in G839ByteIdentityHarness.RecoveryScenarioIds)
        {
            File.WriteAllText(FixturePath(fixtureId), CaptureRecovery(fixtureId));
        }

        foreach (var fixtureId in G839ByteIdentityHarness.WorkerNextActionPlannedLabelsScenarioIds)
        {
            File.WriteAllText(FixturePath(fixtureId), CaptureWorkerNextAction());
        }
    }

    public static TheoryData<string> RecoveryFixtureIds() =>
        new(G839ByteIdentityHarness.RecoveryScenarioIds);

    public static TheoryData<string> WorkerNextActionPlannedLabelsFixtureIds() =>
        new(G839ByteIdentityHarness.WorkerNextActionPlannedLabelsScenarioIds);

    private static string FixturePath(string fixtureId) => Path.Combine(FixtureRoot, fixtureId + ".txt");
}
