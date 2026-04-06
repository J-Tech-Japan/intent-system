using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeEnqueueRendererTests
{
    [Fact]
    public void WriteSummary_GivenResult_WritesDeterministicSummary()
    {
        using var writer = new StringWriter();

        IntakeEnqueueRenderer.WriteSummary(
            writer,
            new IntakeEnqueueResult
            {
                Domain = "auth",
                EnqueuedExecutionUnits = ["AUTH-01", "AUTH-02"],
                PacketPaths =
                [
                    ".intent-cli/issues/AUTH-01/implementation.md",
                    ".intent-cli/issues/AUTH-01/review-context.md",
                    ".intent-cli/issues/AUTH-01/packet.yaml"
                ],
                SkippedUnits = ["AUTH-03"]
            });

        var output = writer.ToString();
        Assert.Contains("Intake enqueue processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Enqueued execution units:", output, StringComparison.Ordinal);
        Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
        Assert.Contains("Packet paths:", output, StringComparison.Ordinal);
        Assert.Contains(".intent-cli/issues/AUTH-01/packet.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Skipped units:", output, StringComparison.Ordinal);
        Assert.Contains("- AUTH-03", output, StringComparison.Ordinal);
    }
}
