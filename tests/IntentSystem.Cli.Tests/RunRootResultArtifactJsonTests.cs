using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class RunRootResultArtifactJsonTests
{
    [Fact]
    public void SerializeAndDeserialize_GivenArtifact_RoundTrips()
    {
        var artifact = new RunRootResultArtifact
        {
            SchemaVersion = "1",
            StopReason = "no-actionable-item",
            TouchedExecutionUnits = ["G226", "G227"],
            ReusedChildCommandRefs = ["run submit", "review run"],
            ExecutionUnit = "G227",
            Detail = "Review direct run for 'G227' is 'running'."
        };

        var json = RunRootResultArtifactJson.Serialize(artifact);
        var roundTripped = RunRootResultArtifactJson.Deserialize(json);

        Assert.Equal(artifact.SchemaVersion, roundTripped.SchemaVersion);
        Assert.Equal(artifact.StopReason, roundTripped.StopReason);
        Assert.Equal(artifact.TouchedExecutionUnits, roundTripped.TouchedExecutionUnits);
        Assert.Equal(artifact.ReusedChildCommandRefs, roundTripped.ReusedChildCommandRefs);
        Assert.Equal(artifact.ExecutionUnit, roundTripped.ExecutionUnit);
        Assert.Equal(artifact.Detail, roundTripped.Detail);
    }
}
