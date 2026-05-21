using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G382: pure tests for the interview readiness classifier — the four
/// verdicts (packet-ready / issue-ready / clarification-required /
/// remaining-gaps), the concrete missing list, and the next-question
/// priority.
/// </summary>
public sealed class InterviewReadinessAnalyzerTests
{
    private static readonly string[] AllDimensions =
    {
        "owner-decision", "open-decisions", "goal", "scope", "non-goals",
        "constraints", "target", "acceptance", "verification", "dependencies", "risks",
    };

    private static readonly string[] IssueDimensions =
    {
        "owner-decision", "open-decisions", "goal", "scope", "non-goals",
        "constraints", "target", "acceptance", "verification",
    };

    [Fact]
    public void Analyze_AllDimensionsResolved_IsPacketReady()
    {
        var result = InterviewReadinessAnalyzer.Analyze(AllDimensions);

        Assert.Equal(InterviewReadinessAnalyzer.Classifications.PacketReady, result.Classification);
        Assert.Empty(result.MissingDimensions);
        Assert.Null(result.NextQuestion);
    }

    [Fact]
    public void Analyze_IssueDimensionsResolved_ButNoPacketDims_IsIssueReady()
    {
        var result = InterviewReadinessAnalyzer.Analyze(IssueDimensions);

        Assert.Equal(InterviewReadinessAnalyzer.Classifications.IssueReady, result.Classification);
        Assert.Contains("dependencies", result.MissingDimensions);
        Assert.Contains("risks", result.MissingDimensions);
        // Next question is the first missing in priority order (dependencies).
        Assert.Equal("dependencies", result.NextQuestionDimension);
    }

    [Fact]
    public void Analyze_OwnerDecisionPending_IsClarificationRequired_EvenWhenEverythingElseResolved()
    {
        var resolved = AllDimensions.Where(d => d != "owner-decision").ToArray();

        var result = InterviewReadinessAnalyzer.Analyze(resolved);

        Assert.Equal(InterviewReadinessAnalyzer.Classifications.ClarificationRequired, result.Classification);
        // Blocking decision is the highest-priority next question.
        Assert.Equal("owner-decision", result.NextQuestionDimension);
    }

    [Fact]
    public void Analyze_OpenDecisionsPending_IsClarificationRequired()
    {
        var resolved = AllDimensions.Where(d => d != "open-decisions").ToArray();

        var result = InterviewReadinessAnalyzer.Analyze(resolved);

        Assert.Equal(InterviewReadinessAnalyzer.Classifications.ClarificationRequired, result.Classification);
        Assert.Equal("open-decisions", result.NextQuestionDimension);
    }

    [Fact]
    public void Analyze_BlockingResolvedButIssueIncomplete_IsRemainingGaps_WithConcreteMissingAndNextQuestion()
    {
        var result = InterviewReadinessAnalyzer.Analyze(new[] { "owner-decision", "open-decisions", "goal", "scope" });

        Assert.Equal(InterviewReadinessAnalyzer.Classifications.RemainingGaps, result.Classification);
        // Concrete missing list.
        Assert.Contains("target", result.MissingDimensions);
        Assert.Contains("acceptance", result.MissingDimensions);
        Assert.Contains("verification", result.MissingDimensions);
        // Next highest-value question after goal+scope is target.
        Assert.Equal("target", result.NextQuestionDimension);
        Assert.False(string.IsNullOrWhiteSpace(result.NextQuestion));
    }

    [Fact]
    public void Analyze_EmptyInput_IsClarificationRequired_AndChecklistHasElevenDimensions()
    {
        var result = InterviewReadinessAnalyzer.Analyze(Array.Empty<string>());

        // Nothing resolved → blocking decisions unresolved → clarification-required.
        Assert.Equal(InterviewReadinessAnalyzer.Classifications.ClarificationRequired, result.Classification);
        Assert.Equal(11, result.Dimensions.Count);
        Assert.Equal(11, result.MissingDimensions.Count);
        Assert.Equal("owner-decision", result.NextQuestionDimension);
    }

    [Fact]
    public void Analyze_IsCaseInsensitive_AndTrimsInput()
    {
        var result = InterviewReadinessAnalyzer.Analyze(new[] { "  GOAL ", "Scope" });
        var goal = result.Dimensions.Single(d => d.Key == "goal");
        var scope = result.Dimensions.Single(d => d.Key == "scope");
        Assert.True(goal.Resolved);
        Assert.True(scope.Resolved);
    }
}
