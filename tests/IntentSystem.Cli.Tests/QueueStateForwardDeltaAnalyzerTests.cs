using IntentSystem.Cli.Commands;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class QueueStateForwardDeltaAnalyzerTests
{
    [Fact]
    public void Analyze_AddedLinkedPr_ReturnsForwardOnly()
    {
        var head = BuildState(item =>
            item with { LinkedPr = null });
        var working = BuildState(item =>
            item with { LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/551" });

        var result = QueueStateForwardDeltaAnalyzer.Analyze(
            QueueStateSerializer.Serialize(head),
            QueueStateSerializer.Serialize(working));

        Assert.Equal(QueueStateForwardDeltaAnalyzer.ClassificationForwardOnly, result.Classification);
        var change = Assert.Single(result.Changes);
        Assert.Equal(QueueStateForwardChangeKind.AddedLinkedPr, change.Kind);
        Assert.Equal("SKS-G215", change.ExecutionUnit);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/551", change.LinkedPrUrl);
    }

    [Fact]
    public void Analyze_AddedLinkedIssue_ReturnsForwardOnly()
    {
        var head = BuildState(item => item with { LinkedIssue = null });
        var working = BuildState(item => item with
        {
            LinkedIssue = new LinkedIssue
            {
                Repo = "J-Tech-Japan/intent-system",
                Number = 727,
                Url = "https://github.com/J-Tech-Japan/intent-system/issues/727",
            },
        });

        var result = QueueStateForwardDeltaAnalyzer.Analyze(
            QueueStateSerializer.Serialize(head),
            QueueStateSerializer.Serialize(working));

        Assert.Equal(QueueStateForwardDeltaAnalyzer.ClassificationForwardOnly, result.Classification);
        var change = Assert.Single(result.Changes);
        Assert.Equal(QueueStateForwardChangeKind.AddedLinkedIssue, change.Kind);
        Assert.Equal(727, change.LinkedIssueNumber);
        Assert.Equal("J-Tech-Japan/intent-system", change.LinkedIssueRepo);
    }

    [Fact]
    public void Analyze_AddedLinkedIssueAndPr_ReturnsCombinedForwardChange()
    {
        var head = BuildState(item => item with { LinkedIssue = null, LinkedPr = null });
        var working = BuildState(item => item with
        {
            LinkedIssue = new LinkedIssue
            {
                Repo = "J-Tech-Japan/intent-system",
                Number = 727,
                Url = "https://github.com/J-Tech-Japan/intent-system/issues/727",
            },
            LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/728",
        });

        var result = QueueStateForwardDeltaAnalyzer.Analyze(
            QueueStateSerializer.Serialize(head),
            QueueStateSerializer.Serialize(working));

        Assert.Equal(QueueStateForwardDeltaAnalyzer.ClassificationForwardOnly, result.Classification);
        var change = Assert.Single(result.Changes);
        Assert.Equal(QueueStateForwardChangeKind.AddedLinkedIssueAndPr, change.Kind);
    }

    [Fact]
    public void Analyze_LinkedPrChangedToDifferentValue_ReturnsNeedsOperatorReview()
    {
        var head = BuildState(item => item with { LinkedPr = "https://github.com/o/r/pull/1" });
        var working = BuildState(item => item with { LinkedPr = "https://github.com/o/r/pull/2" });

        var result = QueueStateForwardDeltaAnalyzer.Analyze(
            QueueStateSerializer.Serialize(head),
            QueueStateSerializer.Serialize(working));

        Assert.Equal(QueueStateForwardDeltaAnalyzer.ClassificationNeedsOperatorReview, result.Classification);
        Assert.Empty(result.Changes);
        Assert.Contains("linked_pr changed", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_LinkedPrRemoved_ReturnsNeedsOperatorReviewAsRollback()
    {
        var head = BuildState(item => item with { LinkedPr = "https://github.com/o/r/pull/1" });
        var working = BuildState(item => item with { LinkedPr = null });

        var result = QueueStateForwardDeltaAnalyzer.Analyze(
            QueueStateSerializer.Serialize(head),
            QueueStateSerializer.Serialize(working));

        Assert.Equal(QueueStateForwardDeltaAnalyzer.ClassificationNeedsOperatorReview, result.Classification);
        Assert.Contains("removed", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_TitleChanged_ReturnsNeedsOperatorReview()
    {
        var head = BuildState(item => item with { Title = "Original" });
        var working = BuildState(item => item with { Title = "Edited" });

        var result = QueueStateForwardDeltaAnalyzer.Analyze(
            QueueStateSerializer.Serialize(head),
            QueueStateSerializer.Serialize(working));

        Assert.Equal(QueueStateForwardDeltaAnalyzer.ClassificationNeedsOperatorReview, result.Classification);
    }

    [Fact]
    public void Analyze_StateChanged_ReturnsNeedsOperatorReview()
    {
        var head = BuildState(item => item with { State = QueueItemState.Queued });
        var working = BuildState(item => item with { State = QueueItemState.Review });

        var result = QueueStateForwardDeltaAnalyzer.Analyze(
            QueueStateSerializer.Serialize(head),
            QueueStateSerializer.Serialize(working));

        Assert.Equal(QueueStateForwardDeltaAnalyzer.ClassificationNeedsOperatorReview, result.Classification);
    }

    [Fact]
    public void Analyze_ItemCountChanged_ReturnsNeedsOperatorReview()
    {
        var head = BuildState(item => item);
        var working = BuildState(item => item) with
        {
            Items = new[] { BuildState(i => i).Items[0], BuildExtraItem() },
        };

        var result = QueueStateForwardDeltaAnalyzer.Analyze(
            QueueStateSerializer.Serialize(head),
            QueueStateSerializer.Serialize(working));

        Assert.Equal(QueueStateForwardDeltaAnalyzer.ClassificationNeedsOperatorReview, result.Classification);
        Assert.Contains("items[] count", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_NoChanges_ReturnsNeedsOperatorReview()
    {
        // A no-op delta is treated as needs-operator-review (whitespace-only
        // dirty file, etc.); the analyzer must never auto-commit a file that
        // shows no field-level change.
        var head = BuildState(item => item);
        var working = BuildState(item => item);

        var result = QueueStateForwardDeltaAnalyzer.Analyze(
            QueueStateSerializer.Serialize(head),
            QueueStateSerializer.Serialize(working));

        Assert.Equal(QueueStateForwardDeltaAnalyzer.ClassificationNeedsOperatorReview, result.Classification);
    }

    [Fact]
    public void Analyze_InvalidWorkingJson_ReturnsInvalid()
    {
        var head = QueueStateSerializer.Serialize(BuildState(item => item));
        var result = QueueStateForwardDeltaAnalyzer.Analyze(head, "{ not valid json");
        Assert.Equal(QueueStateForwardDeltaAnalyzer.ClassificationInvalid, result.Classification);
    }

    [Fact]
    public void Analyze_InvalidHeadJson_ReturnsInvalid()
    {
        var working = QueueStateSerializer.Serialize(BuildState(item => item));
        var result = QueueStateForwardDeltaAnalyzer.Analyze("not json", working);
        Assert.Equal(QueueStateForwardDeltaAnalyzer.ClassificationInvalid, result.Classification);
    }

    [Fact]
    public void Analyze_SchemaVersionChanged_ReturnsNeedsOperatorReview()
    {
        var head = BuildState(item => item);
        var working = head with { SchemaVersion = "2" };

        var result = QueueStateForwardDeltaAnalyzer.Analyze(
            QueueStateSerializer.Serialize(head),
            QueueStateSerializer.Serialize(working));

        Assert.Equal(QueueStateForwardDeltaAnalyzer.ClassificationNeedsOperatorReview, result.Classification);
        Assert.Contains("schema_version", result.Summary, StringComparison.Ordinal);
    }

    private static QueueState BuildState(Func<QueueItem, QueueItem> mutate)
    {
        var item = new QueueItem
        {
            ExecutionUnit = "SKS-G215",
            Title = "[SKS-G215] Implement",
            State = QueueItemState.Queued,
            Dependencies = Array.Empty<string>(),
            BlockedBy = Array.Empty<string>(),
            ClarificationReturnPath = "intents/sks/clarifications/open.md",
            PacketPaths = new PacketPaths
            {
                Implementation = ".intent-cli/issues/SKS-G215/implementation.md",
                ReviewContext = ".intent-cli/issues/SKS-G215/review-context.md",
                Yaml = ".intent-cli/issues/SKS-G215/packet.yaml",
            },
            LinkedIssue = null,
            LinkedPr = null,
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "high",
        };

        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-05-09T10:00:00Z"),
            Items = new[] { mutate(item) },
        };
    }

    private static QueueItem BuildExtraItem() => new()
    {
        ExecutionUnit = "EXTRA-1",
        Title = "[EXTRA-1] Extra",
        State = QueueItemState.Queued,
        Dependencies = Array.Empty<string>(),
        BlockedBy = Array.Empty<string>(),
        ClarificationReturnPath = "intents/sks/clarifications/open.md",
        PacketPaths = new PacketPaths
        {
            Implementation = ".intent-cli/issues/EXTRA-1/implementation.md",
            ReviewContext = ".intent-cli/issues/EXTRA-1/review-context.md",
            Yaml = ".intent-cli/issues/EXTRA-1/packet.yaml",
        },
        LinkedIssue = null,
        LinkedPr = null,
        WorkerRole = "coder",
        ReviewRole = "reviewer",
        Priority = "high",
    };
}
