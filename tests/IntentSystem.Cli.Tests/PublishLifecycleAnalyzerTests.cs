using IntentSystem.Cli.Commands;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Tests;

public sealed class PublishLifecycleAnalyzerTests
{
    [Fact]
    public void Analyze_ArtifactSaysIssueCreatedButPrAlreadyExists_RecommendsPrCreated()
    {
        var artifact = NewArtifact("G300", lifecycleState: null /* implicit issue-created */);
        var candidate = NewCandidate("G300", artifact,
            queueState: QueueItemState.Active,
            issueState: "open",
            issueLabels: new[] { WorkerNextActionConstants.Labels.IntentTarget, WorkerNextActionConstants.Labels.IntentPrCreated },
            linkedPrNumber: 706,
            linkedPrUrl: "https://github.com/J-Tech-Japan/intent-system/pull/706");

        var result = PublishLifecycleAnalyzer.Analyze(new[] { candidate });

        Assert.Single(result.Entries);
        var entry = result.Entries[0];
        Assert.Equal("stale-issue-created", entry.Classification);
        Assert.Equal("issue-created", entry.CurrentLifecycleState);
        Assert.Equal("pr-created", entry.RecommendedLifecycleState);
        Assert.Equal(706, entry.RecommendedLinkedPrNumber);
        Assert.Equal(1, result.DriftCount);
    }

    [Fact]
    public void Analyze_ArtifactCoherent_ReportsCoherent_NoChange()
    {
        var artifact = NewArtifact("G300", lifecycleState: "pr-created");
        var candidate = NewCandidate("G300", artifact,
            queueState: QueueItemState.Active,
            issueState: "open",
            issueLabels: new[] { WorkerNextActionConstants.Labels.IntentTarget, WorkerNextActionConstants.Labels.IntentPrCreated },
            linkedPrNumber: 706,
            linkedPrUrl: "https://github.com/J-Tech-Japan/intent-system/pull/706");

        var result = PublishLifecycleAnalyzer.Analyze(new[] { candidate });

        Assert.Equal("coherent", result.Entries[0].Classification);
        Assert.Equal(1, result.CoherentCount);
        Assert.Equal(0, result.DriftCount);
    }

    [Fact]
    public void Analyze_ClosedIssueWithCompletedQueue_RecommendsClosedOut()
    {
        var artifact = NewArtifact("G300", lifecycleState: "pr-created");
        var candidate = NewCandidate("G300", artifact,
            queueState: QueueItemState.Completed,
            issueState: "closed",
            issueLabels: new[] { WorkerNextActionConstants.Labels.IntentTarget, WorkerNextActionConstants.Labels.IntentPrCreated },
            linkedPrNumber: 706,
            linkedPrUrl: "https://github.com/J-Tech-Japan/intent-system/pull/706",
            nowIso: "2026-05-09T12:00:00Z");

        var result = PublishLifecycleAnalyzer.Analyze(new[] { candidate });

        Assert.Equal("stale-pr-created", result.Entries[0].Classification);
        Assert.Equal("closed-out", result.Entries[0].RecommendedLifecycleState);
        Assert.Equal("2026-05-09T12:00:00Z", result.Entries[0].RecommendedClosedOutAt);
    }

    [Fact]
    public void Analyze_IssueHasIntentPrCreatedButNoLinkedPr_ReturnsUnsafe_RoutesThroughG303()
    {
        var artifact = NewArtifact("G300", lifecycleState: "issue-created");
        var candidate = NewCandidate("G300", artifact,
            queueState: QueueItemState.Active,
            issueState: "open",
            issueLabels: new[] { WorkerNextActionConstants.Labels.IntentTarget, WorkerNextActionConstants.Labels.IntentPrCreated },
            linkedPrNumber: null,
            linkedPrUrl: null);

        var result = PublishLifecycleAnalyzer.Analyze(new[] { candidate });

        Assert.Equal("unsafe-stale-lifecycle", result.Entries[0].Classification);
        Assert.Null(result.Entries[0].RecommendedLifecycleState);
        Assert.Contains(result.Entries[0].Evidence, e => e.Contains("publish-recovery", StringComparison.Ordinal));
        Assert.Equal(1, result.UnsafeCount);
    }

    [Fact]
    public void Analyze_IssueWithIntentTargetOnly_RecommendsPublished()
    {
        var artifact = NewArtifact("G300", lifecycleState: null);
        var candidate = NewCandidate("G300", artifact,
            queueState: QueueItemState.Queued,
            issueState: "open",
            issueLabels: new[] { WorkerNextActionConstants.Labels.IntentTarget });

        var result = PublishLifecycleAnalyzer.Analyze(new[] { candidate });

        Assert.Equal("stale-issue-created", result.Entries[0].Classification);
        Assert.Equal("published", result.Entries[0].RecommendedLifecycleState);
    }

    [Fact]
    public void Analyze_MissingArtifact_ReturnsMissingArtifactClassification()
    {
        var candidate = NewCandidate("G300", artifact: null,
            queueState: QueueItemState.Queued,
            issueState: null,
            issueLabels: Array.Empty<string>());

        var result = PublishLifecycleAnalyzer.Analyze(new[] { candidate });

        Assert.Equal("missing-publish-artifact", result.Entries[0].Classification);
        Assert.Equal(1, result.MissingArtifactCount);
    }

    [Fact]
    public void Analyze_RefusesToDowngrade_LifecycleRecordedAheadOfReality()
    {
        // Recorded as pr-created but reality only has intent-target (e.g. label
        // was deleted or PR was reverted). Refusing to downgrade is correct
        // because that case is host-policy-ambiguous.
        var artifact = NewArtifact("G300", lifecycleState: "pr-created");
        var candidate = NewCandidate("G300", artifact,
            queueState: QueueItemState.Queued,
            issueState: "open",
            issueLabels: new[] { WorkerNextActionConstants.Labels.IntentTarget });

        var result = PublishLifecycleAnalyzer.Analyze(new[] { candidate });

        Assert.Equal("unsafe-stale-lifecycle", result.Entries[0].Classification);
        Assert.Null(result.Entries[0].RecommendedLifecycleState);
    }

    [Fact]
    public void Analyze_NoIntentTargetYet_StaysAtIssueCreated()
    {
        var artifact = NewArtifact("G300", lifecycleState: null);
        var candidate = NewCandidate("G300", artifact,
            queueState: QueueItemState.Queued,
            issueState: "open",
            issueLabels: Array.Empty<string>());

        var result = PublishLifecycleAnalyzer.Analyze(new[] { candidate });

        Assert.Equal("coherent", result.Entries[0].Classification);
        Assert.Equal("issue-created", result.Entries[0].CurrentLifecycleState);
    }

    private static IssuePublishArtifact NewArtifact(string executionUnit, string? lifecycleState) =>
        new()
        {
            ExecutionUnit = executionUnit,
            PublishStatus = "issue-created",
            PacketPath = $".intent-cli/issues/{executionUnit}/packet.yaml",
            IssueBodyPath = $".intent-cli/issues/{executionUnit}/github-body.md",
            CreatedIssueNumber = 700,
            CreatedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/700",
            PublishedLabelName = "intent-target",
            LifecycleState = lifecycleState
        };

    private static PublishLifecycleCandidate NewCandidate(
        string executionUnit,
        IssuePublishArtifact? artifact,
        QueueItemState? queueState,
        string? issueState,
        IReadOnlyCollection<string> issueLabels,
        int? linkedPrNumber = null,
        string? linkedPrUrl = null,
        string nowIso = "2026-05-09T00:00:00Z") =>
        new()
        {
            ExecutionUnit = executionUnit,
            ArtifactPath = $".intent-cli/issues/{executionUnit}/publish.yaml",
            Artifact = artifact,
            QueueItemState = queueState,
            LinkedPrNumber = linkedPrNumber,
            LinkedPrUrl = linkedPrUrl,
            IssueState = issueState,
            IssueLabels = issueLabels,
            NowIso = nowIso
        };
}
