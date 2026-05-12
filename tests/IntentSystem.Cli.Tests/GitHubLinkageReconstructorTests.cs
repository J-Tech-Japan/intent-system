using IntentSystem.Cli.Commands;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G329: tests for the pure <see cref="GitHubLinkageReconstructor"/>.
/// Cover the four outcome kinds — Deterministic, Ambiguous,
/// NoClosingReferences, NoMatch — plus the null-queue-state guard.
/// </summary>
public sealed class GitHubLinkageReconstructorTests
{
    [Fact]
    public void Reconstruct_GivenSingleQueueItemMatchingClosingIssue_ReturnsDeterministic()
    {
        var queue = NewQueueState(
            Item("G329", linkedIssueNumber: 759));

        var result = GitHubLinkageReconstructor.Reconstruct(new[] { 759 }, queue);

        Assert.Equal(LinkageReconstructionKind.Deterministic, result.Kind);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("G329", candidate.ExecutionUnit);
        Assert.Equal(759, candidate.LinkedIssueNumber);
    }

    [Fact]
    public void Reconstruct_GivenTwoQueueItemsLinkedToSameClosingIssue_ReturnsAmbiguous()
    {
        var queue = NewQueueState(
            Item("G329", linkedIssueNumber: 759),
            Item("G329-PRIME", linkedIssueNumber: 759));

        var result = GitHubLinkageReconstructor.Reconstruct(new[] { 759 }, queue);

        Assert.Equal(LinkageReconstructionKind.Ambiguous, result.Kind);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Contains(result.Candidates, c => c.ExecutionUnit == "G329");
        Assert.Contains(result.Candidates, c => c.ExecutionUnit == "G329-PRIME");
    }

    [Fact]
    public void Reconstruct_GivenEmptyClosingIssues_ReturnsNoClosingReferences()
    {
        // G329 out-of-scope: do not guess without closing references.
        // The reconstructor reports the absence as a distinct kind so
        // the caller can keep its existing host-metadata-blocked path.
        var queue = NewQueueState(Item("G329", linkedIssueNumber: 759));

        var result = GitHubLinkageReconstructor.Reconstruct(Array.Empty<int>(), queue);

        Assert.Equal(LinkageReconstructionKind.NoClosingReferences, result.Kind);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Reconstruct_GivenClosingIssueWithNoQueueLink_ReturnsNoMatch()
    {
        var queue = NewQueueState(Item("OTHER", linkedIssueNumber: 1));

        var result = GitHubLinkageReconstructor.Reconstruct(new[] { 759 }, queue);

        Assert.Equal(LinkageReconstructionKind.NoMatch, result.Kind);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Reconstruct_GivenNullQueueState_ReturnsNoMatch()
    {
        // G329 fail-soft: a null queue-state (caller couldn't load or
        // parse it) must not crash — the reconstructor falls back to
        // NoMatch so the caller keeps its existing flow.
        var result = GitHubLinkageReconstructor.Reconstruct(new[] { 759 }, queueState: null);

        Assert.Equal(LinkageReconstructionKind.NoMatch, result.Kind);
    }

    [Fact]
    public void Reconstruct_SkipsItemsWithoutLinkedIssueNumber()
    {
        // Items missing linked_issue.number must not be candidates;
        // the reconstructor uses the number as the keying fact.
        var queue = NewQueueState(
            Item("G329", linkedIssueNumber: null),
            Item("OTHER", linkedIssueNumber: 759));

        var result = GitHubLinkageReconstructor.Reconstruct(new[] { 759 }, queue);

        Assert.Equal(LinkageReconstructionKind.Deterministic, result.Kind);
        Assert.Equal("OTHER", result.Candidates[0].ExecutionUnit);
    }

    [Fact]
    public void Reconstruct_GivenMultipleClosingIssuesMatchingOneItem_ReturnsDeterministic()
    {
        // Closing-issue list is a SET of issues the PR closes. Even
        // when the PR closes two issues but only one of them is in
        // the queue, the result is deterministic — the unmatched
        // closing issue is ignored.
        var queue = NewQueueState(Item("G329", linkedIssueNumber: 759));

        var result = GitHubLinkageReconstructor.Reconstruct(new[] { 759, 1000 }, queue);

        Assert.Equal(LinkageReconstructionKind.Deterministic, result.Kind);
        Assert.Equal("G329", result.Candidates[0].ExecutionUnit);
    }

    private static QueueItem Item(string executionUnit, int? linkedIssueNumber) =>
        new()
        {
            ExecutionUnit = executionUnit,
            Title = executionUnit,
            State = QueueItemState.Review,
            Dependencies = Array.Empty<string>(),
            BlockedBy = Array.Empty<string>(),
            ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
            PacketPaths = new PacketPaths
            {
                Implementation = "a",
                ReviewContext = "b",
                Yaml = "c"
            },
            LinkedIssue = linkedIssueNumber is null
                ? null
                : new LinkedIssue
                {
                    Repo = "J-Tech-Japan/intent-system",
                    Number = linkedIssueNumber.Value,
                    Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{linkedIssueNumber.Value}"
                },
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "normal"
        };

    private static QueueState NewQueueState(params QueueItem[] items) =>
        new()
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-05-11T00:00:00Z"),
            Items = items
        };
}
