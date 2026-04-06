using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IntakePatchArtifactMarkdownTests
{
    [Fact]
    public void Deserialize_GivenPatchArtifactMarkdown_ParsesPatchRequest()
    {
        var request = IntakePatchArtifactMarkdown.Deserialize(
            """
            # Intake Patch Draft

            ## Domain

            `auth`

            target_file_paths:
            - intents/intent-cli/intent-tree/means/auth-oauth2.md

            source_concept_refs:
            - intents/intent-cli/concepts/auth-oauth2.md

            ## File-By-File Patch Candidates

            ### `intents/intent-cli/intent-tree/means/auth-oauth2.md`

            current_file_state: present
            foldin_anchors:
            - answered_question_ids:iq-1
            source_concept_refs:
            - intents/intent-cli/concepts/auth-oauth2.md
            proposed_edits:
            - Apply update candidate: Add device-code note
            rationale:
            - This path is listed in return_to_intent_paths.
            current_file_excerpt:
            ```text
            # Auth Means
            Existing line
            ```
            """);

        Assert.Equal("auth", request.Domain);
        Assert.Equal(["intents/intent-cli/intent-tree/means/auth-oauth2.md"], request.TargetFilePaths);
        Assert.Equal(["intents/intent-cli/concepts/auth-oauth2.md"], request.SourceConceptRefs);
        Assert.Single(request.FileDrafts);
        Assert.Equal("present", request.FileDrafts[0].CurrentFileState);
        Assert.Contains("Apply update candidate: Add device-code note", request.FileDrafts[0].ProposedEdits, StringComparer.Ordinal);
    }

    [Fact]
    public void Deserialize_GivenMissingTargetFilePaths_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => IntakePatchArtifactMarkdown.Deserialize(
                """
                # Intake Patch Draft

                ## Domain

                `auth`

                source_concept_refs:
                - intents/intent-cli/concepts/auth-oauth2.md

                ## File-By-File Patch Candidates
                """));

        Assert.Contains("target_file_paths", exception.Message, StringComparison.Ordinal);
    }
}
