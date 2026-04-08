using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class ConfirmedReconstructionArtifactYamlTests
{
    [Fact]
    public void SerializeDeserialize_RoundTripsArtifact()
    {
        var artifact = new ConfirmedReconstructionArtifact
        {
            DomainSlug = "auth",
            SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
            ReconstructedArtifactPaths =
            [
                ".intent-cli/intake/auth.reconstructed-concept.yaml",
                ".intent-cli/intake/auth.reconstructed-interview.md"
            ],
            ReviewArtifactPath = ".intent-cli/intake/auth.best-practice-review.md",
            DeveloperConfirmationArtifactPath = ".intent-cli/intake/auth.developer-confirmation.yaml",
            ConfirmedItems = ["confirm: validate current auth boundary"],
            RejectedItems = ["reject: do not rewrite current auth ownership model"],
            DeferredItems = ["defer: return interface cleanup after clarification"],
            BlockedItems = ["defer: return interface cleanup after clarification"],
            ReturnToIntentPaths = ["intents/intent-cli/specs/11-reconstruction-review-and-confirmation.md"],
            DownstreamReadiness = "not-ready"
        };

        var yaml = ConfirmedReconstructionArtifactYaml.Serialize(artifact);
        var roundTripped = ConfirmedReconstructionArtifactYaml.Deserialize(yaml);

        Assert.Equal(artifact.DomainSlug, roundTripped.DomainSlug);
        Assert.Equal(artifact.SourceBundleArtifactPath, roundTripped.SourceBundleArtifactPath);
        Assert.Equal(artifact.ReconstructedArtifactPaths, roundTripped.ReconstructedArtifactPaths);
        Assert.Equal(artifact.ReviewArtifactPath, roundTripped.ReviewArtifactPath);
        Assert.Equal(artifact.DeveloperConfirmationArtifactPath, roundTripped.DeveloperConfirmationArtifactPath);
        Assert.Equal(artifact.ConfirmedItems, roundTripped.ConfirmedItems);
        Assert.Equal(artifact.RejectedItems, roundTripped.RejectedItems);
        Assert.Equal(artifact.DeferredItems, roundTripped.DeferredItems);
        Assert.Equal(artifact.BlockedItems, roundTripped.BlockedItems);
        Assert.Equal(artifact.ReturnToIntentPaths, roundTripped.ReturnToIntentPaths);
        Assert.Equal(artifact.DownstreamReadiness, roundTripped.DownstreamReadiness);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_Throws()
    {
        const string yaml =
            """
            domain_slug: auth
            source_bundle_artifact_path: ".intent-cli/intake/auth.current-sources.yaml"
            reconstructed_artifact_paths: []
            review_artifact_path: ".intent-cli/intake/auth.best-practice-review.md"
            developer_confirmation_artifact_path: ".intent-cli/intake/auth.developer-confirmation.yaml"
            confirmed_items: []
            rejected_items: []
            deferred_items: []
            blocked_items: []
            downstream_readiness: ready
            """;

        var exception = Assert.Throws<InvalidOperationException>(() => ConfirmedReconstructionArtifactYaml.Deserialize(yaml));

        Assert.Contains("return_to_intent_paths", exception.Message, StringComparison.Ordinal);
    }
}
