using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugIntentEnqueueRendererTests
{
    [Fact]
    public void WriteSummary_GivenArtifact_RendersDeterministicSummary()
    {
        using var writer = new StringWriter();

        BugIntentEnqueueRenderer.WriteSummary(
            writer,
            new BugIntentEnqueueArtifact
            {
                BugId = "BUG-123",
                IntentIssueRef = ".intent-cli/bugs/BUG-123.intent-issue.yaml",
                IntentRepairRef = ".intent-cli/bugs/BUG-123.intent-repair.yaml",
                AllocatedExecutionUnit = "G41",
                LinkedIssueUrl = "https://github.com/J-Tech-Japan/MyIntentHost/issues/53",
                ParentRepairTargets = ["intent:intents/intent-cli/means/auth.md"],
                GeneratedPacketPaths =
                [
                    ".intent-cli/issues/G41/implementation.md",
                    ".intent-cli/issues/G41/review-context.md",
                    ".intent-cli/issues/G41/packet.yaml"
                ],
                WasEnqueued = true
            },
            ".intent-cli/bugs/BUG-123.intent-enqueue.yaml");

        var output = writer.ToString();
        Assert.Contains("Bug intent-enqueue artifact generated for 'BUG-123'.", output, StringComparison.Ordinal);
        Assert.Contains("Artifact path: .intent-cli/bugs/BUG-123.intent-enqueue.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Allocated execution unit: G41", output, StringComparison.Ordinal);
        Assert.Contains("Linked issue URL: https://github.com/J-Tech-Japan/MyIntentHost/issues/53", output, StringComparison.Ordinal);
        Assert.Contains("Generated packet paths: 3", output, StringComparison.Ordinal);
        Assert.Contains("Was enqueued: true", output, StringComparison.Ordinal);
    }
}
