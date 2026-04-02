using System.Text.Json;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Supervisor.Tests;

public sealed class RunLogSerializerTests
{
    [Fact]
    public void DeserializeLine_GivenIssueCreatedEvent_ReadsLinkedIssueUrl()
    {
        var line =
            """{"ts":"2026-04-02T09:50:13Z","execution_unit":"B1","event":"issue-created","by":"supervisor","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/7"}""";

        var runEvent = RunLogSerializer.DeserializeLine(line);

        Assert.Equal(DateTimeOffset.Parse("2026-04-02T09:50:13Z"), runEvent.Ts);
        Assert.Equal("B1", runEvent.ExecutionUnit);
        Assert.Equal("issue-created", runEvent.Event);
        Assert.Equal("supervisor", runEvent.By);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/7", runEvent.LinkedIssue);
        Assert.Null(runEvent.LinkedPr);
        Assert.Null(runEvent.Reason);
    }

    [Fact]
    public void DeserializeAll_GivenRunLogContent_AllowsTrackingReviewFixAndClarifyTransitions()
    {
        var content = """
        {"ts":"2026-04-02T09:50:13Z","execution_unit":"B1","event":"queued","by":"supervisor"}
        {"ts":"2026-04-02T09:55:13Z","execution_unit":"B1","event":"review-started","by":"reviewer"}
        {"ts":"2026-04-02T10:00:13Z","execution_unit":"B1","event":"fix-requested","by":"reviewer","reason":"missing linked_issue"}
        {"ts":"2026-04-02T10:05:13Z","execution_unit":"B1","event":"clarify-requested","by":"reviewer","reason":"packet path mismatch"}
        {"ts":"2026-04-02T10:10:13Z","execution_unit":"B1","event":"completed","by":"supervisor","reason":"all checks passed"}
        {"ts":"2026-04-02T10:15:13Z","execution_unit":"A1","event":"completed","by":"supervisor","reason":"done"}
        """;

        var runEvents = RunLogSerializer.DeserializeAll(content);
        var b1Events = runEvents
            .Where(runEvent => runEvent.ExecutionUnit == "B1")
            .Select(runEvent => runEvent.Event)
            .ToArray();

        Assert.Equal(6, runEvents.Count);
        Assert.Equal(
            ["queued", "review-started", "fix-requested", "clarify-requested", "completed"],
            b1Events);
    }

    [Fact]
    public void DeserializeLine_GivenUnknownEvent_PreservesTheOriginalEventName()
    {
        var line =
            """{"ts":"2026-04-02T09:50:13Z","execution_unit":"B1","event":"packet-relinked","by":"supervisor"}""";

        var runEvent = RunLogSerializer.DeserializeLine(line);

        Assert.Equal("packet-relinked", runEvent.Event);
    }

    [Fact]
    public void SerializeLine_GivenNullOptionalFields_OmitsThemFromCompactJson()
    {
        var runEvent = new RunEvent
        {
            Ts = DateTimeOffset.Parse("2026-04-02T09:50:13Z"),
            ExecutionUnit = "B1",
            Event = "queued",
            By = "supervisor"
        };

        var serialized = RunLogSerializer.SerializeLine(runEvent);
        using var document = JsonDocument.Parse(serialized);

        Assert.True(document.RootElement.TryGetProperty("ts", out _));
        Assert.True(document.RootElement.TryGetProperty("execution_unit", out _));
        Assert.True(document.RootElement.TryGetProperty("event", out _));
        Assert.True(document.RootElement.TryGetProperty("by", out _));
        Assert.False(document.RootElement.TryGetProperty("linked_issue", out _));
        Assert.False(document.RootElement.TryGetProperty("linked_pr", out _));
        Assert.False(document.RootElement.TryGetProperty("reason", out _));
        Assert.DoesNotContain("\n", serialized, StringComparison.Ordinal);
    }
}
