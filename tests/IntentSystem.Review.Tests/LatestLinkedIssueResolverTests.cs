using IntentSystem.Supervisor.Models;

namespace IntentSystem.Review.Tests;

public sealed class LatestLinkedIssueResolverTests
{
    [Fact]
    public void Resolve_GivenMultipleEvents_ReturnsLatestLinkedIssueForExecutionUnit()
    {
        var linkedIssue = LatestLinkedIssueResolver.Resolve(
            [
                new RunEvent
                {
                    Ts = DateTimeOffset.Parse("2026-04-03T10:00:00Z"),
                    ExecutionUnit = "G12",
                    Event = "issue-created",
                    By = "intent-cli",
                    LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/49"
                },
                new RunEvent
                {
                    Ts = DateTimeOffset.Parse("2026-04-03T10:10:00Z"),
                    ExecutionUnit = "A1",
                    Event = "issue-created",
                    By = "intent-cli",
                    LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/1"
                },
                new RunEvent
                {
                    Ts = DateTimeOffset.Parse("2026-04-03T10:20:00Z"),
                    ExecutionUnit = "G12",
                    Event = "issue-linked",
                    By = "intent-cli",
                    LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/51"
                }
            ],
            "G12");

        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/51", linkedIssue);
    }

    [Fact]
    public void Resolve_GivenMissingLinkedIssue_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => LatestLinkedIssueResolver.Resolve(
                [
                    new RunEvent
                    {
                        Ts = DateTimeOffset.Parse("2026-04-03T10:00:00Z"),
                        ExecutionUnit = "G12",
                        Event = "queued",
                        By = "intent-cli"
                    }
                ],
                "G12"));

        Assert.Contains("No linked issue found", exception.Message, StringComparison.Ordinal);
    }
}
