using IntentSystem.Cli.Commands;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class RunsJsonlAppendOnlyAnalyzerTests
{
    [Fact]
    public void Analyze_AppendsOneNewEvent_ReturnsAppendOnly()
    {
        var head = BuildJsonl(BuildEvent("issue-published", DateTimeOffset.Parse("2026-05-09T10:00:00Z")));
        var working = BuildJsonl(
            BuildEvent("issue-published", DateTimeOffset.Parse("2026-05-09T10:00:00Z")),
            BuildEvent("pr-created", DateTimeOffset.Parse("2026-05-09T10:30:00Z")));

        var result = RunsJsonlAppendOnlyAnalyzer.Analyze(head, working);

        Assert.Equal(RunsJsonlAppendOnlyAnalyzer.ClassificationAppendOnly, result.Classification);
        Assert.Equal(1, result.AppendedEventCount);
    }

    [Fact]
    public void Analyze_AppendsThreeNewEvents_ReturnsAppendOnly()
    {
        var head = BuildJsonl(BuildEvent("issue-published", DateTimeOffset.Parse("2026-05-09T10:00:00Z")));
        var working = BuildJsonl(
            BuildEvent("issue-published", DateTimeOffset.Parse("2026-05-09T10:00:00Z")),
            BuildEvent("pr-created", DateTimeOffset.Parse("2026-05-09T10:30:00Z")),
            BuildEvent("pr-reviewed", DateTimeOffset.Parse("2026-05-09T11:00:00Z")),
            BuildEvent("pr-merged", DateTimeOffset.Parse("2026-05-09T11:30:00Z")));

        var result = RunsJsonlAppendOnlyAnalyzer.Analyze(head, working);

        Assert.Equal(RunsJsonlAppendOnlyAnalyzer.ClassificationAppendOnly, result.Classification);
        Assert.Equal(3, result.AppendedEventCount);
    }

    [Fact]
    public void Analyze_NoChange_ReturnsNeedsOperatorReview()
    {
        // Whitespace-only / no-event-added case must NOT be auto-committed.
        var head = BuildJsonl(BuildEvent("issue-published", DateTimeOffset.Parse("2026-05-09T10:00:00Z")));
        var result = RunsJsonlAppendOnlyAnalyzer.Analyze(head, head);

        Assert.Equal(RunsJsonlAppendOnlyAnalyzer.ClassificationNeedsOperatorReview, result.Classification);
        Assert.Equal(0, result.AppendedEventCount);
    }

    [Fact]
    public void Analyze_HeadLineModifiedInPlace_ReturnsNeedsOperatorReview()
    {
        var head = BuildJsonl(BuildEvent("issue-published", DateTimeOffset.Parse("2026-05-09T10:00:00Z")));
        // Same execution-unit but rewritten as a different event.
        var working = BuildJsonl(BuildEvent("pr-created", DateTimeOffset.Parse("2026-05-09T10:00:00Z")));

        var result = RunsJsonlAppendOnlyAnalyzer.Analyze(head, working);

        Assert.Equal(RunsJsonlAppendOnlyAnalyzer.ClassificationNeedsOperatorReview, result.Classification);
        Assert.Contains("modified in place", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_FileShrunk_ReturnsNeedsOperatorReview()
    {
        var head = BuildJsonl(
            BuildEvent("issue-published", DateTimeOffset.Parse("2026-05-09T10:00:00Z")),
            BuildEvent("pr-created", DateTimeOffset.Parse("2026-05-09T10:30:00Z")));
        var working = BuildJsonl(
            BuildEvent("issue-published", DateTimeOffset.Parse("2026-05-09T10:00:00Z")));

        var result = RunsJsonlAppendOnlyAnalyzer.Analyze(head, working);

        Assert.Equal(RunsJsonlAppendOnlyAnalyzer.ClassificationNeedsOperatorReview, result.Classification);
        Assert.Contains("shrank", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_AppendedLineIsInvalidJson_ReturnsInvalid()
    {
        var head = BuildJsonl(BuildEvent("issue-published", DateTimeOffset.Parse("2026-05-09T10:00:00Z")));
        var working = head + "\n{ this is not json";

        var result = RunsJsonlAppendOnlyAnalyzer.Analyze(head, working);

        Assert.Equal(RunsJsonlAppendOnlyAnalyzer.ClassificationInvalid, result.Classification);
    }

    [Fact]
    public void Analyze_EmptyHead_AppendsOneEvent_ReturnsAppendOnly()
    {
        // Brand-new file: HEAD blob is empty / does not exist; a single
        // append must still classify as append-only.
        var working = BuildJsonl(BuildEvent("issue-published", DateTimeOffset.Parse("2026-05-09T10:00:00Z")));

        var result = RunsJsonlAppendOnlyAnalyzer.Analyze(string.Empty, working);

        Assert.Equal(RunsJsonlAppendOnlyAnalyzer.ClassificationAppendOnly, result.Classification);
        Assert.Equal(1, result.AppendedEventCount);
    }

    private static RunEvent BuildEvent(string @event, DateTimeOffset ts) => new()
    {
        Ts = ts,
        ExecutionUnit = "SKS-G215",
        Event = @event,
        By = "host-loop",
    };

    private static string BuildJsonl(params RunEvent[] events) =>
        string.Join("\n", events.Select(RunLogSerializer.SerializeLine)) + "\n";
}
