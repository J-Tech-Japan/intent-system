using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugIntentStartRendererTests
{
    [Fact]
    public void WriteSummary_GivenArtifact_RendersDeterministicSummary()
    {
        using var writer = new StringWriter();

        BugIntentStartRenderer.WriteSummary(
            writer,
            new BugIntentStartArtifact
            {
                BugId = "BUG-123",
                IntentEnqueueRef = ".intent-cli/bugs/BUG-123.intent-enqueue.yaml",
                StartedExecutionUnit = "G41",
                WorktreePath = "/tmp/repo/.intent-cli/worktrees/G41",
                BranchName = "issue-53-g41",
                ReadyToStart = true
            },
            ".intent-cli/bugs/BUG-123.intent-start.yaml");

        var output = writer.ToString();
        Assert.Contains("Bug intent-start artifact generated for 'BUG-123'.", output, StringComparison.Ordinal);
        Assert.Contains("Artifact path: .intent-cli/bugs/BUG-123.intent-start.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Started execution unit: G41", output, StringComparison.Ordinal);
        Assert.Contains("Worktree path: /tmp/repo/.intent-cli/worktrees/G41", output, StringComparison.Ordinal);
        Assert.Contains("Branch name: issue-53-g41", output, StringComparison.Ordinal);
        Assert.Contains("Ready to start: true", output, StringComparison.Ordinal);
    }
}
