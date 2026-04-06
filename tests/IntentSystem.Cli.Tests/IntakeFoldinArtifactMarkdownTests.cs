using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeFoldinArtifactMarkdownTests
{
    [Fact]
    public void Deserialize_GivenFoldinArtifactMarkdown_ParsesPatchDraftSeed()
    {
        var draft = IntakeFoldinArtifactMarkdown.Deserialize(
            """
            # Intake Fold-In Draft

            ## Domain

            `auth`

            ## Interview Coverage

            answered_question_ids:
            - iq-1

            ## Parent Source-Of-Truth Update Candidates

            recommended_updates:
            - Add device-code note

            return_to_intent_paths:
            - intents/intent-cli/intent-tree/means/auth-oauth2.md

            source_concept_refs:
            - intents/intent-cli/concepts/auth-oauth2.md
            """);

        Assert.Equal("auth", draft.Domain);
        Assert.Equal(["iq-1"], draft.AnsweredQuestionIds);
        Assert.Equal(["Add device-code note"], draft.RecommendedUpdates);
        Assert.Equal(["intents/intent-cli/intent-tree/means/auth-oauth2.md"], draft.ReturnToIntentPaths);
        Assert.Equal(["intents/intent-cli/concepts/auth-oauth2.md"], draft.SourceConceptRefs);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredSection_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => IntakeFoldinArtifactMarkdown.Deserialize(
                """
                # Intake Fold-In Draft

                ## Domain

                `auth`

                answered_question_ids:
                - iq-1

                return_to_intent_paths:
                - intents/intent-cli/intent-tree/means/auth-oauth2.md

                source_concept_refs:
                - intents/intent-cli/concepts/auth-oauth2.md
                """));

        Assert.Contains("recommended_updates", exception.Message, StringComparison.Ordinal);
    }
}
