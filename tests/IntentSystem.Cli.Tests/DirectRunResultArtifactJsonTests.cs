using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class DirectRunResultArtifactJsonTests
{
    [Fact]
    public void SerializeAndDeserialize_RoundTripsNormalizedRunResult()
    {
        var artifact = new DirectRunResultArtifact
        {
            SchemaVersion = "1",
            ExecutionUnit = "G19",
            EntryKind = "implement",
            Provider = "Claude",
            Model = "default",
            SessionId = "pid:4321",
            RunStatus = "running",
            RawLogRef = ".intent-cli/runs/G19.provider.jsonl",
            PacketRef = ".intent-cli/issues/G19/packet.yaml",
            ReviewContextRef = ".intent-cli/issues/G19/review-context.md",
            LinkedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/66",
            LinkedPrUrl = "https://github.com/J-Tech-Japan/intent-system/pull/67",
            WorktreePath = "/repo/.intent-cli/worktrees/G19"
        };

        var json = DirectRunResultArtifactJson.Serialize(artifact);
        var roundTripped = DirectRunResultArtifactJson.Deserialize(json);

        Assert.Equal("G19", roundTripped.ExecutionUnit);
        Assert.Equal("implement", roundTripped.EntryKind);
        Assert.Equal("Claude", roundTripped.Provider);
        Assert.Equal("default", roundTripped.Model);
        Assert.Equal("pid:4321", roundTripped.SessionId);
        Assert.Equal("running", roundTripped.RunStatus);
        Assert.Equal(".intent-cli/runs/G19.provider.jsonl", roundTripped.RawLogRef);
        Assert.Equal(".intent-cli/issues/G19/packet.yaml", roundTripped.PacketRef);
        Assert.Equal(".intent-cli/issues/G19/review-context.md", roundTripped.ReviewContextRef);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/66", roundTripped.LinkedIssueUrl);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/67", roundTripped.LinkedPrUrl);
        Assert.Equal("/repo/.intent-cli/worktrees/G19", roundTripped.WorktreePath);
    }
}
