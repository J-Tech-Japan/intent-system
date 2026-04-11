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
            UpstreamRequestRef = ".intent-cli/implement/G19.request.md",
            Provider = "Claude",
            Model = "default",
            SessionId = "pid:4321",
            RunStatus = "running",
            RawLogRef = ".intent-cli/runs/G19.provider.jsonl",
            PacketRef = ".intent-cli/issues/G19/packet.yaml",
            ReviewContextRef = ".intent-cli/issues/G19/review-context.md",
            LinkedIssue = new DirectRunLinkedIssueContext
            {
                Repo = "J-Tech-Japan/intent-system",
                Number = 66,
                Url = "https://github.com/J-Tech-Japan/intent-system/issues/66"
            },
            LinkedPr = new DirectRunLinkedPullRequestContext
            {
                Repo = "J-Tech-Japan/intent-system",
                Number = 67,
                Url = "https://github.com/J-Tech-Japan/intent-system/pull/67"
            },
            Worktree = new DirectRunWorktreeContext
            {
                Path = "/repo/.intent-cli/worktrees/G19"
            }
        };

        var json = DirectRunResultArtifactJson.Serialize(artifact);
        var roundTripped = DirectRunResultArtifactJson.Deserialize(json);

        Assert.Equal("G19", roundTripped.ExecutionUnit);
        Assert.Equal("implement", roundTripped.EntryKind);
        Assert.Equal(".intent-cli/implement/G19.request.md", roundTripped.UpstreamRequestRef);
        Assert.Equal("Claude", roundTripped.Provider);
        Assert.Equal("default", roundTripped.Model);
        Assert.Equal("pid:4321", roundTripped.SessionId);
        Assert.Equal("running", roundTripped.RunStatus);
        Assert.Equal(".intent-cli/runs/G19.provider.jsonl", roundTripped.RawLogRef);
        Assert.Equal(".intent-cli/issues/G19/packet.yaml", roundTripped.PacketRef);
        Assert.Equal(".intent-cli/issues/G19/review-context.md", roundTripped.ReviewContextRef);
        Assert.Equal("J-Tech-Japan/intent-system", roundTripped.LinkedIssue?.Repo);
        Assert.Equal(66, roundTripped.LinkedIssue?.Number);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/66", roundTripped.LinkedIssue?.Url);
        Assert.Equal("J-Tech-Japan/intent-system", roundTripped.LinkedPr?.Repo);
        Assert.Equal(67, roundTripped.LinkedPr?.Number);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/67", roundTripped.LinkedPr?.Url);
        Assert.Equal("/repo/.intent-cli/worktrees/G19", roundTripped.Worktree.Path);
    }

    [Fact]
    public void Deserialize_GivenLegacyArtifactWithoutUpstreamRequestRef_PreservesBackwardCompatibility()
    {
        var json = """
        {
          "schema_version": "1",
          "execution_unit": "G19",
          "entry_kind": "review",
          "provider": "ReviewBot",
          "model": "gpt-5.4-mini",
          "session_id": "pid:legacy",
          "run_status": "running",
          "raw_log_ref": ".intent-cli/runs/G19.provider.jsonl",
          "packet_ref": ".intent-cli/issues/G19/packet.yaml",
          "review_context_ref": ".intent-cli/issues/G19/review-context.md",
          "worktree": {
            "path": "/repo/.intent-cli/worktrees/G19"
          }
        }
        """;

        var artifact = DirectRunResultArtifactJson.Deserialize(json);

        Assert.Equal("G19", artifact.ExecutionUnit);
        Assert.Equal("review", artifact.EntryKind);
        Assert.Equal(string.Empty, artifact.UpstreamRequestRef);
        Assert.Equal("pid:legacy", artifact.SessionId);
    }
}
