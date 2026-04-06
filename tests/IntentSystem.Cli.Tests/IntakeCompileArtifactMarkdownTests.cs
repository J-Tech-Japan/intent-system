using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeCompileArtifactMarkdownTests
{
    [Fact]
    public void Deserialize_GivenCompileArtifactMarkdown_ParsesFoldinRequest()
    {
        var request = IntakeCompileArtifactMarkdown.Deserialize(
            """
            # Intake Compile

            ## Domain

            `auth`

            answered_question_ids:
            - iq-1
            - iq-2

            recommended_updates:
            - Add device-code note

            return_to_intent_paths:
            - intents/intent-cli/intent-tree/means/auth-oauth2.md

            source_concept_refs:
            - intents/intent-cli/concepts/auth-oauth2.md
            """);

        Assert.Equal("auth", request.Domain);
        Assert.Equal(["iq-1", "iq-2"], request.AnsweredQuestionIds);
        Assert.Equal(["Add device-code note"], request.RecommendedUpdates);
        Assert.Equal(
            ["intents/intent-cli/intent-tree/means/auth-oauth2.md"],
            request.ReturnToIntentPaths);
        Assert.Equal(
            ["intents/intent-cli/concepts/auth-oauth2.md"],
            request.SourceConceptRefs);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredSection_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => IntakeCompileArtifactMarkdown.Deserialize(
                """
                # Intake Compile

                ## Domain

                `auth`

                answered_question_ids:
                - iq-1

                recommended_updates:
                - Add device-code note

                source_concept_refs:
                - intents/intent-cli/concepts/auth-oauth2.md
                """));

        Assert.Contains("return_to_intent_paths", exception.Message, StringComparison.Ordinal);
    }
}
