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
}
