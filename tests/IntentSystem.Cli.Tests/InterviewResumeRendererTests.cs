using IntentSystem.Cli.Commands;
using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.Cli.Tests;

public sealed class InterviewResumeRendererTests
{
    [Fact]
    public void Write_GivenFoldInReadySummary_RendersDeterministicLists()
    {
        using var writer = new StringWriter();

        InterviewResumeRenderer.Write(writer, new InterviewResumeResult
        {
            Domain = "auth",
            HasArtifacts = true,
            AnsweredQuestionIds = ["iq-1", "iq-2"],
            RecommendedUpdates = ["Add device-code note", "Document OAuth2 fallback"],
            ReturnToIntentPaths =
            [
                "intents/intent-cli/intent-tree/means/auth-device-code.md",
                "intents/intent-cli/intent-tree/means/auth-oauth2.md"
            ]
        });

        var output = writer.ToString();
        Assert.Contains("Interview fold-in-ready summary:", output, StringComparison.Ordinal);
        Assert.Contains("answered_question_ids:", output, StringComparison.Ordinal);
        Assert.Contains("recommended_updates:", output, StringComparison.Ordinal);
        Assert.Contains("return_to_intent_paths:", output, StringComparison.Ordinal);
        Assert.Contains("- iq-1", output, StringComparison.Ordinal);
        Assert.Contains("- Add device-code note", output, StringComparison.Ordinal);
        Assert.Contains("- intents/intent-cli/intent-tree/means/auth-device-code.md", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_GivenFoldInReadySummaryWithoutUpdates_RendersNoneMarkers()
    {
        using var writer = new StringWriter();

        InterviewResumeRenderer.Write(writer, new InterviewResumeResult
        {
            Domain = "auth",
            HasArtifacts = true,
            AnsweredQuestionIds = ["iq-1"],
            RecommendedUpdates = [],
            ReturnToIntentPaths = []
        });

        var output = writer.ToString();
        Assert.Contains("recommended_updates:", output, StringComparison.Ordinal);
        Assert.Contains("return_to_intent_paths:", output, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(output, "- none"));
    }

    [Fact]
    public void Write_GivenNoArtifacts_RendersDeterministicMessage()
    {
        using var writer = new StringWriter();

        InterviewResumeRenderer.Write(writer, new InterviewResumeResult
        {
            Domain = "auth",
            HasArtifacts = false,
            AnsweredQuestionIds = [],
            RecommendedUpdates = [],
            ReturnToIntentPaths = []
        });

        Assert.Contains("No interview artifacts found for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_GivenOpenQuestion_DelegatesToQuestionRenderer()
    {
        using var writer = new StringWriter();

        InterviewResumeRenderer.Write(writer, new InterviewResumeResult
        {
            Domain = "auth",
            HasArtifacts = true,
            NextQuestion = new InterviewQueueItem
            {
                DomainSlug = "auth",
                SourceConceptRef = "intents/intent-cli/concepts/auth-oauth2.md",
                QuestionId = "iq-1",
                QuestionText = "Which auth flow should be canonical?",
                Reason = "Auth direction is still underspecified.",
                Affects = ["auth-oauth2"],
                BlockingOrNonblocking = "blocking",
                Status = InterviewQueueItemStatus.Open,
                ReturnToIntentPaths = ["intents/intent-cli/intent-tree/means/auth-oauth2.md"],
                CreatedAt = DateTimeOffset.Parse("2026-04-13T08:00:00Z"),
                Answer = null
            },
            AnsweredQuestionIds = [],
            RecommendedUpdates = [],
            ReturnToIntentPaths = []
        });

        var output = writer.ToString();
        Assert.Contains("Next interview question:", output, StringComparison.Ordinal);
        Assert.Contains("Question id: iq-1", output, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var currentIndex = 0;

        while ((currentIndex = text.IndexOf(needle, currentIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            currentIndex += needle.Length;
        }

        return count;
    }
}
