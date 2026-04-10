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
        Assert.Null(runEvent.CommentRef);
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
    public void DeserializeLine_GivenCommentRef_ReadsCommentRef()
    {
        var line =
            """{"ts":"2026-04-02T10:00:13Z","execution_unit":"B1","event":"fix-requested","by":"reviewer","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/46#issuecomment-1"}""";

        var runEvent = RunLogSerializer.DeserializeLine(line);

        Assert.Equal(
            "https://github.com/J-Tech-Japan/intent-system/pull/46#issuecomment-1",
            runEvent.CommentRef);
    }

    [Fact]
    public void DeserializeLine_GivenProviderLifecycleFields_ReadsNormalizedProviderContext()
    {
        var line =
            """{"ts":"2026-04-09T10:15:00Z","execution_unit":"G19","event":"provider-lifecycle","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/66","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/67","entry_kind":"implement","provider":"Claude","model":"default","session_id":"pid:4321","run_status":"running","raw_log_ref":".intent-cli/runs/G19.provider.jsonl","result_ref":".intent-cli/runs/G19.result.json","packet_ref":".intent-cli/issues/G19/packet.yaml","review_context_ref":".intent-cli/issues/G19/review-context.md","worktree_path":"/repo/.intent-cli/worktrees/G19"}""";

        var runEvent = RunLogSerializer.DeserializeLine(line);

        Assert.Equal("implement", runEvent.EntryKind);
        Assert.Equal("Claude", runEvent.Provider);
        Assert.Equal("default", runEvent.Model);
        Assert.Equal("pid:4321", runEvent.SessionId);
        Assert.Equal("running", runEvent.RunStatus);
        Assert.Equal(".intent-cli/runs/G19.provider.jsonl", runEvent.RawLogRef);
        Assert.Equal(".intent-cli/runs/G19.result.json", runEvent.ResultRef);
        Assert.Equal(".intent-cli/issues/G19/packet.yaml", runEvent.PacketRef);
        Assert.Equal(".intent-cli/issues/G19/review-context.md", runEvent.ReviewContextRef);
        Assert.Equal("/repo/.intent-cli/worktrees/G19", runEvent.WorktreePath);
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
        Assert.False(document.RootElement.TryGetProperty("comment_ref", out _));
        Assert.False(document.RootElement.TryGetProperty("reason", out _));
        Assert.False(document.RootElement.TryGetProperty("entry_kind", out _));
        Assert.False(document.RootElement.TryGetProperty("provider", out _));
        Assert.False(document.RootElement.TryGetProperty("model", out _));
        Assert.False(document.RootElement.TryGetProperty("session_id", out _));
        Assert.False(document.RootElement.TryGetProperty("run_status", out _));
        Assert.False(document.RootElement.TryGetProperty("raw_log_ref", out _));
        Assert.False(document.RootElement.TryGetProperty("result_ref", out _));
        Assert.False(document.RootElement.TryGetProperty("packet_ref", out _));
        Assert.False(document.RootElement.TryGetProperty("review_context_ref", out _));
        Assert.False(document.RootElement.TryGetProperty("worktree_path", out _));
        Assert.DoesNotContain("\n", serialized, StringComparison.Ordinal);
    }
}
