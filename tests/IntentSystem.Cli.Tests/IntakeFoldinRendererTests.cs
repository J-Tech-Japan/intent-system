using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeFoldinRendererTests
{
    [Fact]
    public void RenderMarkdown_GivenFoldinRequest_RendersDeterministicArtifact()
    {
        var markdown = IntakeFoldinRenderer.RenderMarkdown(new IntakeFoldinRequest
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

        Assert.Contains("# Intake Fold-In Draft", markdown, StringComparison.Ordinal);
        Assert.Contains("## Interview Coverage", markdown, StringComparison.Ordinal);
        Assert.Contains("## Parent Source-Of-Truth Update Candidates", markdown, StringComparison.Ordinal);
        Assert.Contains("answered_question_ids:", markdown, StringComparison.Ordinal);
        Assert.Contains("recommended_updates:", markdown, StringComparison.Ordinal);
        Assert.Contains("return_to_intent_paths:", markdown, StringComparison.Ordinal);
        Assert.Contains("source_concept_refs:", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderMarkdown_GivenEmptyLists_RendersNoneMarkers()
    {
        var markdown = IntakeFoldinRenderer.RenderMarkdown(new IntakeFoldinRequest
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
    public void WriteSummary_GivenFoldinRequest_WritesDeterministicSummary()
    {
        using var writer = new StringWriter();

        IntakeFoldinRenderer.WriteSummary(
            writer,
            new IntakeFoldinRequest
            {
                Domain = "auth",
                AnsweredQuestionIds = ["iq-1", "iq-2"],
                RecommendedUpdates = ["Add device-code note"],
                ReturnToIntentPaths = ["intents/intent-cli/intent-tree/means/auth-oauth2.md"],
                SourceConceptRefs = ["intents/intent-cli/concepts/auth-oauth2.md"]
            },
            "/repo/.intent-cli/intake/auth.foldin.md");

        var output = writer.ToString();
        Assert.Contains("Intake fold-in draft generated for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Artifact path: /repo/.intent-cli/intake/auth.foldin.md", output, StringComparison.Ordinal);
        Assert.Contains("Answered questions: 2", output, StringComparison.Ordinal);
        Assert.Contains("Recommended updates: 1", output, StringComparison.Ordinal);
        Assert.Contains("Return paths: 1", output, StringComparison.Ordinal);
        Assert.Contains("Source concept refs: 1", output, StringComparison.Ordinal);
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
