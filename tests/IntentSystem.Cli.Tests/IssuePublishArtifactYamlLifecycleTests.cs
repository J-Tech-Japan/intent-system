using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IssuePublishArtifactYamlLifecycleTests
{
    [Fact]
    public void Deserialize_PreG307Artifact_LeavesLifecycleFieldsNull_BackwardCompatible()
    {
        var yaml = """
        execution_unit: G700
        publish_status: issue-created
        packet_path: ".intent-cli/issues/G700/packet.yaml"
        issue_body_path: ".intent-cli/issues/G700/github-body.md"
        created_issue_number: 700
        created_issue_url: "https://github.com/J-Tech-Japan/intent-system/issues/700"
        published_label_name: "intent-target"
        """;

        var artifact = IssuePublishArtifactYaml.Deserialize(yaml);

        Assert.Equal("G700", artifact.ExecutionUnit);
        Assert.Null(artifact.LifecycleState);
        Assert.Null(artifact.LinkedPrNumber);
        Assert.Null(artifact.LinkedPrUrl);
        Assert.Null(artifact.ClosedOutAt);
    }

    [Fact]
    public void RoundTrip_LifecycleStatePopulated_PreservesAllG307Fields()
    {
        var original = new IssuePublishArtifact
        {
            ExecutionUnit = "G700",
            PublishStatus = "issue-created",
            PacketPath = ".intent-cli/issues/G700/packet.yaml",
            IssueBodyPath = ".intent-cli/issues/G700/github-body.md",
            CreatedIssueNumber = 700,
            CreatedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/700",
            PublishedLabelName = "intent-target",
            LifecycleState = "pr-created",
            LinkedPrNumber = 706,
            LinkedPrUrl = "https://github.com/J-Tech-Japan/intent-system/pull/706",
            ClosedOutAt = "2026-05-09T12:00:00Z"
        };

        var yaml = IssuePublishArtifactYaml.Serialize(original);
        var roundTrip = IssuePublishArtifactYaml.Deserialize(yaml);

        Assert.Equal(original, roundTrip);
    }

    [Fact]
    public void Serialize_OmitsLifecycleFields_WhenAllNull_KeepsByteStableForOldArtifacts()
    {
        var artifact = new IssuePublishArtifact
        {
            ExecutionUnit = "G700",
            PublishStatus = "issue-created",
            PacketPath = ".intent-cli/issues/G700/packet.yaml",
            IssueBodyPath = ".intent-cli/issues/G700/github-body.md",
            CreatedIssueNumber = 700,
            CreatedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/700",
            PublishedLabelName = "intent-target",
            LifecycleState = null,
            LinkedPrNumber = null,
            LinkedPrUrl = null,
            ClosedOutAt = null
        };

        var yaml = IssuePublishArtifactYaml.Serialize(artifact);

        Assert.DoesNotContain("lifecycle_state:", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("linked_pr_number:", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("linked_pr_url:", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("closed_out_at:", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void IssuePublishLifecycle_RankAndMembership_AreCanonical()
    {
        Assert.Equal(0, IssuePublishLifecycle.Rank("issue-created"));
        Assert.Equal(1, IssuePublishLifecycle.Rank("published"));
        Assert.Equal(2, IssuePublishLifecycle.Rank("pr-created"));
        Assert.Equal(3, IssuePublishLifecycle.Rank("closed-out"));
        Assert.Equal(0, IssuePublishLifecycle.Rank(null));
        Assert.Equal(0, IssuePublishLifecycle.Rank("not-a-state"));

        Assert.True(IssuePublishLifecycle.IsKnown("issue-created"));
        Assert.True(IssuePublishLifecycle.IsKnown("closed-out"));
        Assert.False(IssuePublishLifecycle.IsKnown(null));
        Assert.False(IssuePublishLifecycle.IsKnown(""));
        Assert.False(IssuePublishLifecycle.IsKnown("unknown"));
    }
}
