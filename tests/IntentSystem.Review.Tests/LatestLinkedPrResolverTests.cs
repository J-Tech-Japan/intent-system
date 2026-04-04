using IntentSystem.Supervisor.Models;

namespace IntentSystem.Review.Tests;

public sealed class LatestLinkedPrResolverTests
{
    [Fact]
    public void Resolve_GivenMultipleLinkedPrEvents_ReturnsLatestMatchingExecutionUnit()
    {
        var linkedPr = LatestLinkedPrResolver.Resolve(
            [
                new RunEvent
                {
                    Ts = DateTimeOffset.Parse("2026-04-03T10:00:00Z"),
                    ExecutionUnit = "G9",
                    Event = "review-started",
                    By = "intent-cli",
                    LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/44"
                },
                new RunEvent
                {
                    Ts = DateTimeOffset.Parse("2026-04-03T10:10:00Z"),
                    ExecutionUnit = "A1",
                    Event = "review-started",
                    By = "intent-cli",
                    LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/12"
                },
                new RunEvent
                {
                    Ts = DateTimeOffset.Parse("2026-04-03T10:20:00Z"),
                    ExecutionUnit = "G9",
                    Event = "review-started",
                    By = "intent-cli",
                    LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/45"
                }
            ],
            "G9");

        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/45", linkedPr);
    }

    [Fact]
    public void Resolve_GivenNoLinkedPrForExecutionUnit_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => LatestLinkedPrResolver.Resolve(
                [
                    new RunEvent
                    {
                        Ts = DateTimeOffset.Parse("2026-04-03T10:00:00Z"),
                        ExecutionUnit = "G9",
                        Event = "queued",
                        By = "intent-cli"
                    }
                ],
                "G9"));

        Assert.Contains("No linked PR found", exception.Message, StringComparison.Ordinal);
    }
}
