using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class ClaimCommandG855Tests
{
    [Fact]
    public void DescribeLocalReadPathsUsesConfiguredPrecedenceWithoutTouchingFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"g855-claim-paths-{Guid.NewGuid():N}");
        var context = Context(root, metadataSourceBranch: "refsafe/source", metadataBranch: "legacy", metadataWriteBranch: "write");

        var paths = ClaimCommand.DescribeLocalReadPaths(context, "execution-unit:G855");

        Assert.Equal(ClaimCommand.ClaimPath("execution-unit:G855"), paths.ClaimPath);
        Assert.Equal(
            $"{ClaimCommand.ClaimsDirectory}/history/{Path.GetFileNameWithoutExtension(paths.ClaimPath)}",
            paths.HistoryDirectory);
        Assert.Equal("refs/remotes/origin/refsafe/source", paths.MetadataRef);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void DescribeLocalReadPathsRefusesMissingConfigurationAndInvalidScope()
    {
        var context = Context(Path.Combine(Path.GetTempPath(), $"g855-claim-paths-{Guid.NewGuid():N}"));

        var unavailable = Assert.Throws<InvalidOperationException>(
            () => ClaimCommand.DescribeLocalReadPaths(context, "execution-unit:G855"));
        Assert.Contains("local-claim-ref-unavailable", unavailable.Message, StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(
            () => ClaimCommand.DescribeLocalReadPaths(context, "execution-unit:../escape"));
        Assert.Throws<ArgumentException>(
            () => ClaimCommand.DescribeLocalReadPaths(context, "release-prep:owner/repo:v1"));
    }

    private static CliContext Context(
        string root,
        string metadataSourceBranch = "",
        string metadataBranch = "",
        string metadataWriteBranch = "") => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = "g855-test",
                ArtifactRoot = ".intent-cli",
                MetadataSourceBranch = metadataSourceBranch,
                MetadataBranch = metadataBranch,
                MetadataWriteBranch = metadataWriteBranch,
            },
        },
    };
}
