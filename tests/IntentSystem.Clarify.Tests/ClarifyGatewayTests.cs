using IntentSystem.Clarify;
using IntentSystem.Clarify.Models;
using IntentSystem.Projection;
using IntentSystem.Projection.Models;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Clarify.Tests;

public sealed class ClarifyGatewayTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Apply_GivenAnsweredClarification_AdvancesArtifactToApplied()
    {
        var clarification = CreateAnsweredClarification("A2", blocking: true);
        var queueState = CreateQueueState("A2", QueueItemState.ClarifyBlocked);

        var result = ClarifyGateway.Apply(clarification, queueState, "gateway", BaseTime);

        Assert.Equal(ClarificationStatus.Applied, result.AppliedClarification.Status);
        Assert.Equal("A2", result.AppliedClarification.ExecutionUnit);
        Assert.Equal("Use execution_unit as link key.", result.AppliedClarification.Answer);
    }

    [Fact]
    public void Apply_GivenBlockingClarification_ResumesQueueItemToReview()
    {
        var clarification = CreateAnsweredClarification("A2", blocking: true);
        var queueState = CreateQueueState("A2", QueueItemState.ClarifyBlocked);

        var result = ClarifyGateway.Apply(clarification, queueState, "gateway", BaseTime);

        var item = FindItem(result.UpdatedQueueState, "A2");
        Assert.Equal(QueueItemState.Review, item.State);
    }

    [Fact]
    public void Apply_GivenNonblockingClarification_ResumesQueueItemToQueued()
    {
        var clarification = CreateAnsweredClarification("A2", blocking: false);
        var queueState = CreateQueueState("A2", QueueItemState.ClarifyBlocked);

        var result = ClarifyGateway.Apply(clarification, queueState, "gateway", BaseTime);

        var item = FindItem(result.UpdatedQueueState, "A2");
        Assert.Equal(QueueItemState.Queued, item.State);
    }

    [Fact]
    public void Apply_GivenBlockingClarification_EmitsClarifyAppliedAndClarifyResumedEvents()
    {
        var clarification = CreateAnsweredClarification("A2", blocking: true);
        var queueState = CreateQueueState("A2", QueueItemState.ClarifyBlocked);

        var result = ClarifyGateway.Apply(clarification, queueState, "gateway", BaseTime);

        Assert.Equal(2, result.Events.Count);
        Assert.Equal("clarify-applied", result.Events[0].Event);
        Assert.Contains("clar-1", result.Events[0].Reason!, StringComparison.Ordinal);
        Assert.Equal("clarify-resumed", result.Events[1].Event);
        Assert.Contains("review", result.Events[1].Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_GivenNonblockingClarification_ResumedEventMentionsQueued()
    {
        var clarification = CreateAnsweredClarification("A2", blocking: false);
        var queueState = CreateQueueState("A2", QueueItemState.ClarifyBlocked);

        var result = ClarifyGateway.Apply(clarification, queueState, "gateway", BaseTime);

        Assert.Equal("clarify-resumed", result.Events[1].Event);
        Assert.Contains("queued", result.Events[1].Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_GivenRegeneratePacketCallback_IncludesRegeneratedPacketAndEvent()
    {
        var clarification = CreateAnsweredClarification("A2", blocking: true);
        var queueState = CreateQueueState("A2", QueueItemState.ClarifyBlocked);
        var mockPacket = CreateMockPacket();

        var result = ClarifyGateway.Apply(
            clarification, queueState, "gateway", BaseTime,
            regeneratePacket: () => mockPacket);

        Assert.NotNull(result.RegeneratedPacket);
        Assert.Equal(mockPacket, result.RegeneratedPacket);
        Assert.Equal(3, result.Events.Count);
        Assert.Equal("packet-regenerated", result.Events[2].Event);
    }

    [Fact]
    public void Apply_GivenNoRegenerateCallback_OmitsRegeneratedPacket()
    {
        var clarification = CreateAnsweredClarification("A2", blocking: true);
        var queueState = CreateQueueState("A2", QueueItemState.ClarifyBlocked);

        var result = ClarifyGateway.Apply(clarification, queueState, "gateway", BaseTime);

        Assert.Null(result.RegeneratedPacket);
        Assert.Equal(2, result.Events.Count);
    }

    [Fact]
    public void Apply_GivenOpenClarification_ThrowsInvalidOperationException()
    {
        var clarification = CreateOpenClarification("A2");
        var queueState = CreateQueueState("A2", QueueItemState.ClarifyBlocked);

        var ex = Assert.Throws<InvalidOperationException>(
            () => ClarifyGateway.Apply(clarification, queueState, "gateway", BaseTime));

        Assert.Contains("Answered", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_GivenQueueItemNotClarifyBlocked_ThrowsInvalidOperationException()
    {
        var clarification = CreateAnsweredClarification("A2", blocking: true);
        var queueState = CreateQueueState("A2", QueueItemState.Active);

        Assert.Throws<InvalidOperationException>(
            () => ClarifyGateway.Apply(clarification, queueState, "gateway", BaseTime));
    }

    [Fact]
    public void Apply_GivenSameInputs_ProducesDeterministicResult()
    {
        var clarification = CreateAnsweredClarification("A2", blocking: true);
        var queueState = CreateQueueState("A2", QueueItemState.ClarifyBlocked);

        var first = ClarifyGateway.Apply(clarification, queueState, "gateway", BaseTime);
        var second = ClarifyGateway.Apply(clarification, queueState, "gateway", BaseTime);

        Assert.Equal(first.AppliedClarification, second.AppliedClarification);
        Assert.Equal(first.Events.Count, second.Events.Count);
        for (var i = 0; i < first.Events.Count; i++)
        {
            Assert.Equal(first.Events[i].Event, second.Events[i].Event);
            Assert.Equal(first.Events[i].ExecutionUnit, second.Events[i].ExecutionUnit);
        }
    }

    [Fact]
    public void Apply_DoesNotMutateOriginalClarificationOrQueueState()
    {
        var clarification = CreateAnsweredClarification("A2", blocking: true);
        var queueState = CreateQueueState("A2", QueueItemState.ClarifyBlocked);

        _ = ClarifyGateway.Apply(clarification, queueState, "gateway", BaseTime);

        Assert.Equal(ClarificationStatus.Answered, clarification.Status);
        Assert.Equal(QueueItemState.ClarifyBlocked, FindItem(queueState, "A2").State);
    }

    [Fact]
    public void ApplyAll_GivenMultipleBlockingClarifications_AppliesAllAndResumesToReview()
    {
        var clarifications = new[]
        {
            CreateAnsweredClarification("A2", blocking: true, questionId: "clar-1"),
            CreateAnsweredClarification("A2", blocking: true, questionId: "clar-2")
        };
        var queueState = CreateQueueState("A2", QueueItemState.ClarifyBlocked);

        var result = ClarifyGateway.ApplyAll(clarifications, queueState, "gateway", BaseTime);

        Assert.Equal(ClarificationStatus.Applied, result.AppliedClarification.Status);
        Assert.Equal(QueueItemState.Review, FindItem(result.UpdatedQueueState, "A2").State);
        Assert.Equal(3, result.Events.Count);
        Assert.Equal("clarify-applied", result.Events[0].Event);
        Assert.Equal("clarify-applied", result.Events[1].Event);
        Assert.Equal("clarify-resumed", result.Events[2].Event);
    }

    [Fact]
    public void ApplyAll_GivenMixedBlockingAndNonblocking_ResumesToReview()
    {
        var clarifications = new[]
        {
            CreateAnsweredClarification("A2", blocking: false, questionId: "clar-1"),
            CreateAnsweredClarification("A2", blocking: true, questionId: "clar-2")
        };
        var queueState = CreateQueueState("A2", QueueItemState.ClarifyBlocked);

        var result = ClarifyGateway.ApplyAll(clarifications, queueState, "gateway", BaseTime);

        Assert.Equal(QueueItemState.Review, FindItem(result.UpdatedQueueState, "A2").State);
    }

    [Fact]
    public void ApplyAll_GivenAllNonblocking_ResumesToQueued()
    {
        var clarifications = new[]
        {
            CreateAnsweredClarification("A2", blocking: false, questionId: "clar-1"),
            CreateAnsweredClarification("A2", blocking: false, questionId: "clar-2")
        };
        var queueState = CreateQueueState("A2", QueueItemState.ClarifyBlocked);

        var result = ClarifyGateway.ApplyAll(clarifications, queueState, "gateway", BaseTime);

        Assert.Equal(QueueItemState.Queued, FindItem(result.UpdatedQueueState, "A2").State);
    }

    [Fact]
    public void ApplyAll_GivenMixedExecutionUnits_ThrowsInvalidOperationException()
    {
        var clarifications = new[]
        {
            CreateAnsweredClarification("A2", blocking: true, questionId: "clar-1"),
            CreateAnsweredClarification("B1", blocking: true, questionId: "clar-2")
        };
        var queueState = CreateQueueState("A2", QueueItemState.ClarifyBlocked);

        var ex = Assert.Throws<InvalidOperationException>(
            () => ClarifyGateway.ApplyAll(clarifications, queueState, "gateway", BaseTime));

        Assert.Contains("same execution unit", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyAll_GivenEmptyList_ThrowsInvalidOperationException()
    {
        var queueState = CreateQueueState("A2", QueueItemState.ClarifyBlocked);

        Assert.Throws<InvalidOperationException>(
            () => ClarifyGateway.ApplyAll([], queueState, "gateway", BaseTime));
    }

    [Fact]
    public void AllEventsShareTimestampAndExecutionUnit()
    {
        var clarification = CreateAnsweredClarification("A2", blocking: true);
        var queueState = CreateQueueState("A2", QueueItemState.ClarifyBlocked);

        var result = ClarifyGateway.Apply(
            clarification, queueState, "gateway", BaseTime,
            regeneratePacket: CreateMockPacket);

        Assert.All(result.Events, evt =>
        {
            Assert.Equal(BaseTime, evt.Ts);
            Assert.Equal("A2", evt.ExecutionUnit);
            Assert.Equal("gateway", evt.By);
        });
    }

    private static ClarificationItem CreateAnsweredClarification(
        string executionUnit, bool blocking, string questionId = "clar-1")
    {
        return new ClarificationItem
        {
            ClarificationSource = "review",
            QuestionId = questionId,
            ExecutionUnit = executionUnit,
            QuestionText = "Which queue field owns the return path?",
            Reason = "Unclear ownership of clarification return path.",
            AffectedIntents = ["intents/intent-cli/intent-tree/00-map.md"],
            AffectedExecutionUnits = [executionUnit],
            BlockingOrNonblocking = blocking ? "blocking" : "nonblocking",
            ClarificationReturnPath = "intents/rules/issue-template-and-review-context.md",
            Status = ClarificationStatus.Answered,
            CreatedAt = BaseTime.AddHours(-1),
            Answer = "Use execution_unit as link key.",
            AnsweredAt = BaseTime.AddMinutes(-10)
        };
    }

    private static ClarificationItem CreateOpenClarification(string executionUnit)
    {
        return new ClarificationItem
        {
            ClarificationSource = "review",
            QuestionId = "clar-1",
            ExecutionUnit = executionUnit,
            QuestionText = "Which queue field owns the return path?",
            Reason = "Unclear ownership.",
            AffectedIntents = [],
            AffectedExecutionUnits = [executionUnit],
            BlockingOrNonblocking = "blocking",
            ClarificationReturnPath = "intents/rules/issue-template-and-review-context.md",
            Status = ClarificationStatus.Open,
            CreatedAt = BaseTime.AddHours(-1)
        };
    }

    private static QueueState CreateQueueState(string executionUnit, QueueItemState state)
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = BaseTime.AddHours(-1),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = executionUnit,
                    Title = $"[{executionUnit}] Test Item",
                    State = state,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/rules/issue-template-and-review-context.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = $".intent-cli/issues/{executionUnit.ToLowerInvariant()}/implementation.md",
                        ReviewContext = $".intent-cli/issues/{executionUnit.ToLowerInvariant()}/review-context.md",
                        Yaml = $".intent-cli/issues/{executionUnit.ToLowerInvariant()}/packet.yaml"
                    },
                    WorkerRole = "claude",
                    ReviewRole = "human",
                    Priority = "normal"
                }
            ]
        };
    }

    private static QueueItem FindItem(QueueState state, string executionUnit)
    {
        return state.Items.First(
            i => string.Equals(i.ExecutionUnit, executionUnit, StringComparison.Ordinal));
    }

    private static GeneratedPacket CreateMockPacket()
    {
        return new GeneratedPacket
        {
            ImplementationMarkdown = "# Mock Implementation",
            ReviewContextMarkdown = "# Mock Review Context",
            PacketYaml = "issue_title: mock",
            Paths = new ResolvedPacketPaths
            {
                Implementation = ".intent-cli/issues/a2/implementation.md",
                ReviewContext = ".intent-cli/issues/a2/review-context.md",
                Yaml = ".intent-cli/issues/a2/packet.yaml"
            }
        };
    }
}
