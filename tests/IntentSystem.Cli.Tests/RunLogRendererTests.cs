using IntentSystem.Cli.Commands;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Tests;

public sealed class RunLogRendererTests
{
    [Fact]
    public void Write_GivenRunEventsWithOptionalRefs_RendersDeterministicHistory()
    {
        using var writer = new StringWriter();

        RunLogRenderer.Write(writer, CreateQueueItem(), CreateRunEvents());

        var output = writer.ToString();
        Assert.Contains("Execution unit: G18", output, StringComparison.Ordinal);
        Assert.Contains("Current state: fixing", output, StringComparison.Ordinal);
        Assert.Contains("Linked issue: https://github.com/J-Tech-Japan/intent-system/issues/64", output, StringComparison.Ordinal);
        Assert.Contains("event=issue-created", output, StringComparison.Ordinal);
        Assert.Contains("linked_issue=https://github.com/J-Tech-Japan/intent-system/issues/64", output, StringComparison.Ordinal);
        Assert.Contains("linked_pr=https://github.com/J-Tech-Japan/intent-system/pull/65", output, StringComparison.Ordinal);
        Assert.Contains("comment_ref=https://github.com/J-Tech-Japan/intent-system/pull/65#issuecomment-1", output, StringComparison.Ordinal);
        Assert.Contains("reason=contract mismatch", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_GivenNoRunEvents_RendersNone()
    {
        using var writer = new StringWriter();

        RunLogRenderer.Write(writer, CreateQueueItem(), []);

        Assert.Contains("Run history:", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("- none", writer.ToString(), StringComparison.Ordinal);
    }

    private static QueueItem CreateQueueItem()
    {
        return new QueueItem
        {
            ExecutionUnit = "G18",
            Title = "[G18] Run Log Command",
            State = QueueItemState.Fixing,
            Dependencies = [],
            BlockedBy = [],
            ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
            PacketPaths = new PacketPaths
            {
                Implementation = ".intent-cli/issues/G18/implementation.md",
                ReviewContext = ".intent-cli/issues/G18/review-context.md",
                Yaml = ".intent-cli/issues/G18/packet.yaml"
            },
            LinkedIssue = new LinkedIssue
            {
                Repo = "J-Tech-Japan/intent-system",
                Number = 64,
                Url = "https://github.com/J-Tech-Japan/intent-system/issues/64"
            },
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "high"
        };
    }

    private static IReadOnlyList<RunEvent> CreateRunEvents()
    {
        return
        [
            new RunEvent
            {
                Ts = DateTimeOffset.Parse("2026-04-07T08:00:00Z"),
                ExecutionUnit = "G18",
                Event = "issue-created",
                By = "intent-cli",
                LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/64"
            },
            new RunEvent
            {
                Ts = DateTimeOffset.Parse("2026-04-07T08:10:00Z"),
                ExecutionUnit = "G18",
                Event = "activated",
                By = "intent-cli"
            },
            new RunEvent
            {
                Ts = DateTimeOffset.Parse("2026-04-07T08:20:00Z"),
                ExecutionUnit = "G18",
                Event = "review",
                By = "intent-cli",
                LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/65"
            },
            new RunEvent
            {
                Ts = DateTimeOffset.Parse("2026-04-07T08:30:00Z"),
                ExecutionUnit = "G18",
                Event = "fix-requested",
                By = "intent-cli",
                CommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/65#issuecomment-1",
                Reason = "contract mismatch"
            }
        ];
    }
}
