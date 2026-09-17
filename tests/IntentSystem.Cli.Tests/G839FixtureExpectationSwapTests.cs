namespace IntentSystem.Cli.Tests;

[Collection(AutomationPrTransitionSharedStateCollection.Name)]
public sealed class G839FixtureExpectationSwapTests
{
    private static readonly string[] AllFixtureIds =
        G839FixtureExpectations.FixtureGroups.SelectMany(group => group).ToArray();

    [Theory]
    [MemberData(nameof(AllFixtureIdData))]
    public void FixtureExpectation_RejectsNeighbouringCapture(string fixtureId)
    {
        var neighbourId = G839FixtureExpectations.GetNeighborFixtureId(fixtureId);
        var wrongOutput = CaptureFixture(neighbourId);
        var exception = Record.Exception(() => G839FixtureExpectations.AssertMatchesFixtureName(fixtureId, wrongOutput));
        Assert.NotNull(exception);
    }

    [Fact]
    public void FixtureExpectation_RejectsG839ByteUnitMismatch()
    {
        var wrongOutput = G839RecoveryByteIdentityTests.CaptureRecovery("recovery-proceed-dry-run-json", "G839-BYTE");
        var exception = Record.Exception(() =>
            G839FixtureExpectations.AssertMatchesFixtureName("recovery-proceed-dry-run-json", wrongOutput));
        Assert.NotNull(exception);
        Assert.Contains("expected outcome 'proceed'", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SwapTable_PrintVerificationRows()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("G839_SWAP_TABLE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var rows = new List<string>();
        foreach (var fixtureId in AllFixtureIds)
        {
            var neighbourId = G839FixtureExpectations.GetNeighborFixtureId(fixtureId);
            var wrongOutput = CaptureFixture(neighbourId);
            var exception = Record.Exception(() => G839FixtureExpectations.AssertMatchesFixtureName(fixtureId, wrongOutput));
            rows.Add($"{fixtureId}\t{neighbourId}\t{exception?.Message?.Replace('\n', ' ') ?? "(null)"}");
        }

        throw new InvalidOperationException(string.Join(Environment.NewLine, rows));
    }

    public static TheoryData<string> AllFixtureIdData() => new(AllFixtureIds);

    private static string CaptureFixture(string fixtureId)
    {
        if (fixtureId.StartsWith("recovery-", StringComparison.Ordinal))
        {
            return G839RecoveryByteIdentityTests.CaptureRecovery(fixtureId);
        }

        if (fixtureId.StartsWith("planned-labels-", StringComparison.Ordinal))
        {
            return fixtureId == "planned-labels-worker-pr-comment-preflight"
                ? G839PlannedLabelsPreflightByteIdentityTests.Capture()
                : G839ByteIdentityHarness.CapturePlannedLabelsConsumer(fixtureId);
        }

        return G839ByteIdentityTests.CapturePrTransition(fixtureId);
    }
}
