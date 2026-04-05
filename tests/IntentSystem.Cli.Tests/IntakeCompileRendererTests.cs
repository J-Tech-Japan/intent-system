using IntentSystem.Cli.Commands;
using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeCompileRendererTests
{
    [Fact]
    public void RenderMarkdown_GivenCompileRequest_RendersDeterministicArtifact()
    {
        var markdown = IntakeCompileRenderer.RenderMarkdown(new IntakeCompileRequest
        {
            Domain = "auth",
            AnsweredQuestionIds = ["iq-1", "iq-2"],
            RecommendedUpdates = ["Add device-code note", "Document OAuth2 fallback"],
            ReturnToIntentPaths =
            [
                "intents/intent-cli/intent-tree/means/auth-device-code.md",
                "intents/intent-cli/intent-tree/means/auth-oauth2.md"
            ],
            SourceConceptRefs =
            [
                "intents/intent-cli/concepts/auth-device-code.md",
                "intents/intent-cli/concepts/auth-oauth2.md"
            ]
        });

        Assert.Contains("# Intake Compile", markdown, StringComparison.Ordinal);
        Assert.Contains("`auth`", markdown, StringComparison.Ordinal);
        Assert.Contains("answered_question_ids:", markdown, StringComparison.Ordinal);
        Assert.Contains("- iq-1", markdown, StringComparison.Ordinal);
        Assert.Contains("recommended_updates:", markdown, StringComparison.Ordinal);
        Assert.Contains("return_to_intent_paths:", markdown, StringComparison.Ordinal);
        Assert.Contains("source_concept_refs:", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderMarkdown_GivenEmptyLists_RendersNoneMarkers()
    {
        var markdown = IntakeCompileRenderer.RenderMarkdown(new IntakeCompileRequest
        {
            Domain = "auth",
            AnsweredQuestionIds = [],
            RecommendedUpdates = [],
            ReturnToIntentPaths = [],
            SourceConceptRefs = []
        });

        Assert.Equal(4, CountOccurrences(markdown, "- none"));
    }

    [Fact]
    public void WriteNotReady_GivenOpenQuestion_RendersDeterministicMessage()
    {
        using var writer = new StringWriter();

        IntakeCompileRenderer.WriteNotReady(writer, "auth", new InterviewQueueItem
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
        });

        var output = writer.ToString();
        Assert.Contains("Intake compile is not ready for domain 'auth'.", output, StringComparison.Ordinal);
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
