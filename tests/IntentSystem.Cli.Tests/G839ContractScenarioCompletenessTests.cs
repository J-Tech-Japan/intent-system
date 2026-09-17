namespace IntentSystem.Cli.Tests;

public sealed class G839ContractScenarioCompletenessTests
{
    [Fact]
    public void PrTransitionNonRefusalScenarioIds_MatchContractCrossProduct()
    {
        var expected = G839ContractScenarioIds.BuildPrTransitionNonRefusalScenarioIds();
        Assert.Equal(expected, G839ByteIdentityHarness.PrTransitionNonRefusalScenarioIds);
    }

    [Fact]
    public void PrTransitionRefusalTextScenarioIds_MatchContractAc7Rows()
    {
        var expected = G839ContractScenarioIds.BuildPrTransitionRefusalTextScenarioIds();
        Assert.Equal(expected, G839ByteIdentityHarness.PrTransitionRefusalTextScenarioIds);
    }

    [Fact]
    public void RecoveryScenarioIds_MatchContractSection4()
    {
        var expected = G839ContractScenarioIds.BuildRecoveryScenarioIds();
        Assert.Equal(expected, G839ByteIdentityHarness.RecoveryScenarioIds);
    }
}
