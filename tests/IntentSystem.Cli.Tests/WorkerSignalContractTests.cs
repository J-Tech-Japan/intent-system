using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G374: pure contract tests for the structured worker-signal protocol —
/// marker generation/parsing, allowed-target rules, comment-body
/// templating, and label-transition planning for send / handled.
/// </summary>
public sealed class WorkerSignalContractTests
{
    [Theory]
    [InlineData("blocker")]
    [InlineData("follow-up")]
    [InlineData("scope-warning")]
    public void Marker_RoundTrips_KindThroughParser(string kind)
    {
        var marker = WorkerSignalContract.BuildMarker(kind, "issue", 851);

        Assert.StartsWith(WorkerSignalContract.MarkerPrefix, marker);
        Assert.Contains($"kind={kind}", marker, StringComparison.Ordinal);
        Assert.Contains("target=issue#851", marker, StringComparison.Ordinal);

        Assert.True(WorkerSignalContract.TryParseSignalKind(marker, out var parsed));
        Assert.Equal(kind, parsed);
    }

    [Fact]
    public void BuildCommentBody_EmbedsMarkerFirstAndKeepsBody()
    {
        var body = WorkerSignalContract.BuildCommentBody("blocker", "issue", 851, "cannot build: missing contract");

        var firstLine = body.Split('\n')[0];
        Assert.StartsWith(WorkerSignalContract.MarkerPrefix, firstLine);
        Assert.Contains("cannot build: missing contract", body, StringComparison.Ordinal);
        Assert.True(WorkerSignalContract.TryParseSignalKind(body, out var kind));
        Assert.Equal("blocker", kind);
    }

    [Fact]
    public void TryParseSignalKind_NonSignalComment_ReturnsFalse()
    {
        Assert.False(WorkerSignalContract.TryParseSignalKind("just an ordinary review comment", out _));
        Assert.False(WorkerSignalContract.TryParseSignalKind("<!-- intent-signal v=1 kind=bogus target=issue#1 -->", out _));
        Assert.False(WorkerSignalContract.TryParseSignalKind(string.Empty, out _));
        Assert.False(WorkerSignalContract.TryParseSignalKind(null, out _));
    }

    [Fact]
    public void AllowedTargets_EnforceKindRouting()
    {
        Assert.True(WorkerSignalContract.IsTargetAllowed("blocker", "issue"));
        Assert.False(WorkerSignalContract.IsTargetAllowed("blocker", "pr"));

        Assert.True(WorkerSignalContract.IsTargetAllowed("follow-up", "pr"));
        Assert.False(WorkerSignalContract.IsTargetAllowed("follow-up", "issue"));

        Assert.True(WorkerSignalContract.IsTargetAllowed("scope-warning", "issue"));
        Assert.True(WorkerSignalContract.IsTargetAllowed("scope-warning", "pr"));
    }

    [Fact]
    public void PlanSentTransition_AddsSent_AndClearsStaleHandled()
    {
        var plan = WorkerSignalContract.PlanSentTransition(new[] { "intent-signal-handled" });

        Assert.Contains(WorkerSignalContract.Labels.SignalSent, plan.AddLabels);
        Assert.Contains(WorkerSignalContract.Labels.SignalHandled, plan.RemoveLabels);
        Assert.True(plan.HasChanges);
    }

    [Fact]
    public void PlanSentTransition_AlreadySent_PlansNoAdd()
    {
        var plan = WorkerSignalContract.PlanSentTransition(new[] { "intent-signal-sent" });

        Assert.DoesNotContain(WorkerSignalContract.Labels.SignalSent, plan.AddLabels);
        Assert.Empty(plan.RemoveLabels);
        Assert.False(plan.HasChanges);
    }

    [Fact]
    public void PlanHandledTransition_AddsHandled_AndRemovesSent()
    {
        var plan = WorkerSignalContract.PlanHandledTransition(new[] { "intent-signal-sent" });

        Assert.Contains(WorkerSignalContract.Labels.SignalHandled, plan.AddLabels);
        Assert.Contains(WorkerSignalContract.Labels.SignalSent, plan.RemoveLabels);
        Assert.True(plan.HasChanges);
    }

    [Fact]
    public void PlanHandledTransition_AlreadyHandledNoSent_PlansNoChange()
    {
        var plan = WorkerSignalContract.PlanHandledTransition(new[] { "intent-signal-handled" });

        Assert.Empty(plan.AddLabels);
        Assert.Empty(plan.RemoveLabels);
        Assert.False(plan.HasChanges);
    }
}
