using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentReconstructionRendererTests
{
    [Fact]
    public void WriteSummary_GivenResult_WritesDeterministicSummary()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentReconstructionRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentReconstructionResult
            {
                Domain = "auth",
                ConceptArtifactPath = ".intent-cli/intake/auth.reconstructed-concept.yaml",
                InterviewArtifactPath = ".intent-cli/intake/auth.reconstructed-interview.md",
                SelectedAltitudes = ["purpose", "execution"],
                CandidateIntentNodes = ["Clarify the primary purpose for domain 'auth' from selected issue and PR signals."],
                CandidateExecutionUnits = ["Execution candidate from src/feature/FeatureA.cs."],
                ConfidenceByAltitude = ["purpose: medium", "execution: high"],
                SourceConceptRefs = ["issue:114 https://github.com/J-Tech-Japan/intent-system/issues/114 [G44] Generate From Current"],
                RecommendedFollowUpQuestions = ["Which execution-ready change slice should be cut first from the selected current paths?"],
                ReturnToIntentPaths = ["README.md"],
                Gaps = ["Need stronger purpose signal."]
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current reconstruction processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Reconstructed concept artifact: .intent-cli/intake/auth.reconstructed-concept.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Candidate intent nodes:", output, StringComparison.Ordinal);
        Assert.Contains("Candidate execution units:", output, StringComparison.Ordinal);
        Assert.Contains("Recommended follow-up interview questions:", output, StringComparison.Ordinal);
        Assert.Contains("Return-to-intent paths:", output, StringComparison.Ordinal);
    }
}
