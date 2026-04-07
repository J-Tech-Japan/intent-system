using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class ReconstructedInterviewArtifactMarkdownTests
{
    [Fact]
    public void Deserialize_GivenCanonicalMarkdown_ReturnsArtifact()
    {
        var markdown = GenerateFromCurrentReconstructionRenderer.RenderInterviewMarkdown(
            "auth",
            ["purpose", "execution"],
            ["Clarify the auth domain mission."],
            ["Inspect OAuth entry points."],
            ["purpose: medium"],
            ["issue:114 https://github.com/J-Tech-Japan/intent-system/issues/114 [G44] Generate From Current"],
            ["Which execution-ready change slice should be cut first from the selected current paths?"],
            [
                new ReconstructedBridgeQuestion
                {
                    QuestionId = "iq-1",
                    QuestionText = "Which execution-ready change slice should be cut first from the selected current paths?",
                    Reason = "Clarify execution-near detail before standard intake resumes.",
                    Affects = ["auth"],
                    BlockingOrNonblocking = "nonblocking"
                }
            ],
            ["README.md"],
            ["Need stronger auth purpose signal."]);

        var artifact = ReconstructedInterviewArtifactMarkdown.Deserialize(markdown);

        Assert.Equal("auth", artifact.Domain);
        Assert.Equal(["purpose", "execution"], artifact.SelectedAltitudes);
        Assert.Equal(["Clarify the auth domain mission."], artifact.RootNearIntentCandidates);
        Assert.Equal(["Inspect OAuth entry points."], artifact.ExecutionNearUpdateCandidates);
        Assert.Equal(
            ["Which execution-ready change slice should be cut first from the selected current paths?"],
            artifact.RecommendedFollowUpQuestions);
        Assert.Single(artifact.BridgeQuestions);
        Assert.Equal("iq-1", artifact.BridgeQuestions[0].QuestionId);
        Assert.Equal("Clarify execution-near detail before standard intake resumes.", artifact.BridgeQuestions[0].Reason);
        Assert.Equal("nonblocking", artifact.BridgeQuestions[0].BlockingOrNonblocking);
        Assert.Equal(["README.md"], artifact.ReturnToIntentPaths);
        Assert.Equal(["Need stronger auth purpose signal."], artifact.Gaps);
    }

    [Fact]
    public void Deserialize_GivenMissingSection_Throws()
    {
        const string markdown = """
            # Reconstructed Interview

            ## Domain

            `auth`

            selected_altitudes:
            - purpose
            """;

        var exception = Assert.Throws<InvalidOperationException>(() => ReconstructedInterviewArtifactMarkdown.Deserialize(markdown));

        Assert.Contains("root_near_intent_candidates", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_GivenMismatchedQuestionSets_Throws()
    {
        const string markdown = """
            # Reconstructed Interview

            ## Domain

            `auth`

            selected_altitudes:
            - purpose

            root_near_intent_candidates:
            - Clarify the auth domain mission.

            execution_near_update_candidates:
            - none

            confidence_by_altitude:
            - purpose: medium

            source_concept_refs:
            - issue:114 https://github.com/J-Tech-Japan/intent-system/issues/114 [G44] Generate From Current

            recommended_follow_up_questions:
            - Human-facing question text.

            bridge_questions:
            - {"question_id":"iq-1","question_text":"Different bridge question text.","reason":"Clarify root-near intent before standard intake resumes.","affects":["auth"],"blocking_or_nonblocking":"blocking"}

            return_to_intent_paths:
            - README.md

            gaps:
            - none
            """;

        var exception = Assert.Throws<InvalidOperationException>(() => ReconstructedInterviewArtifactMarkdown.Deserialize(markdown));

        Assert.Contains("aligned one-to-one", exception.Message, StringComparison.Ordinal);
    }
}
