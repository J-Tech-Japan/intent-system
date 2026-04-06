using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class QueueEnqueueRendererTests
{
    [Fact]
    public void WriteSummary_GivenCommandResult_WritesDeterministicSummary()
    {
        using var writer = new StringWriter();

        QueueEnqueueRenderer.WriteSummary(
            writer,
            new QueueEnqueueCommandResult
            {
                ExecutionUnit = "G38",
                EnqueuedExecutionUnits = ["G38"],
                PacketPaths =
                [
                    ".intent-cli/issues/G38/implementation.md",
                    ".intent-cli/issues/G38/review-context.md",
                    ".intent-cli/issues/G38/packet.yaml"
                ],
                SkippedUnits = []
            });

        var output = writer.ToString();
        Assert.Contains("Queue enqueue processed for execution unit 'G38'.", output, StringComparison.Ordinal);
        Assert.Contains("Enqueued execution units:", output, StringComparison.Ordinal);
        Assert.Contains("- G38", output, StringComparison.Ordinal);
        Assert.Contains("Packet paths:", output, StringComparison.Ordinal);
        Assert.Contains(".intent-cli/issues/G38/packet.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Skipped units:", output, StringComparison.Ordinal);
        Assert.Contains("- none", output, StringComparison.Ordinal);
    }
}
