using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class DirectRunRequestArtifactJsonTests
{
    [Fact]
    public void SerializeAndDeserialize_GivenArtifact_RoundTrips()
    {
        var artifact = new DirectRunRequestArtifact
        {
            SchemaVersion = "1",
            ExecutionUnit = "G19",
            EntryKind = "implement",
            UpstreamRequestRef = ".intent-cli/implement/G19.request.md",
            Provider = "Claude",
            Model = "sonnet",
            Transport = "stdio",
            LaunchedAt = "2026-04-09T10:15:00.0000000+00:00",
            ProviderSessionId = "claude-implement-g19-20260409101500",
            TransportSummary = "stdio transport selected for provider 'Claude' with model 'sonnet'."
        };

        var roundTripped = DirectRunRequestArtifactJson.Deserialize(DirectRunRequestArtifactJson.Serialize(artifact));

        Assert.Equal(artifact, roundTripped);
    }
}
