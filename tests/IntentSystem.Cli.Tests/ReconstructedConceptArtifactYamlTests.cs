using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class ReconstructedConceptArtifactYamlTests
{
    [Fact]
    public void SerializeAndDeserialize_GivenArtifact_RoundTripsDeterministically()
    {
        var artifact = new ReconstructedConceptArtifact
        {
            DomainSlug = "auth",
            InitialGoal = "Generate From Current",
            CandidateIntentNodes = ["Clarify purpose from issue and PR signals."],
            CandidateUserContext = ["Validate user-facing context from discussion."],
            CandidateMeans = ["Inspect current implementation seam at src/FeatureA.cs."],
            CandidateRules = ["Preserve repo rule guidance captured in AGENTS.md."],
            CandidateSpecs = ["Reconcile external contract and documentation signal from README.md."],
            CandidateExecutionUnits = ["Execution candidate from src/FeatureA.cs."],
            ConfidenceByAltitude = ["purpose: medium", "execution: high"],
            SourceConceptRefs = ["issue:114 https://github.com/J-Tech-Japan/intent-system/issues/114 [G44] Generate From Current"]
        };

        var yaml = ReconstructedConceptArtifactYaml.Serialize(artifact);
        var parsed = ReconstructedConceptArtifactYaml.Deserialize(yaml);

        Assert.Equal(artifact.DomainSlug, parsed.DomainSlug);
        Assert.Equal(artifact.InitialGoal, parsed.InitialGoal);
        Assert.Equal(artifact.CandidateIntentNodes, parsed.CandidateIntentNodes);
        Assert.Equal(artifact.CandidateExecutionUnits, parsed.CandidateExecutionUnits);
        Assert.Equal(artifact.ConfidenceByAltitude, parsed.ConfidenceByAltitude);
        Assert.Equal(artifact.SourceConceptRefs, parsed.SourceConceptRefs);
    }
}
