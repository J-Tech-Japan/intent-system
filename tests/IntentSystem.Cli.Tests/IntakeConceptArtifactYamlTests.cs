using IntentSystem.Cli.Commands;
using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeConceptArtifactYamlTests
{
    [Fact]
    public void Serialize_GivenPacket_ContainsAllRequiredFields()
    {
        var yaml = IntakeConceptArtifactYaml.Serialize(CreatePacket());

        Assert.Contains("domain_slug: auth", yaml, StringComparison.Ordinal);
        Assert.Contains("concept_source: interactive", yaml, StringComparison.Ordinal);
        Assert.Contains("concept_text: \"Add OAuth2 provider support.\"", yaml, StringComparison.Ordinal);
        Assert.Contains("upstream_paths:", yaml, StringComparison.Ordinal);
        Assert.Contains("initial_goal: \"Add OAuth2 provider support.\"", yaml, StringComparison.Ordinal);
        Assert.Contains("constraints:", yaml, StringComparison.Ordinal);
        Assert.Contains("known_unknowns:", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_GivenSerializedPacket_RestoresAllFields()
    {
        var packet = IntakeConceptArtifactYaml.Deserialize(IntakeConceptArtifactYaml.Serialize(CreatePacket()));

        Assert.Equal("auth", packet.DomainSlug);
        Assert.Equal("interactive", packet.ConceptSource);
        Assert.Equal("Add OAuth2 provider support.", packet.ConceptText);
        Assert.Single(packet.UpstreamPaths);
        Assert.Equal("Add OAuth2 provider support.", packet.InitialGoal);
        Assert.Single(packet.Constraints);
        Assert.Single(packet.KnownUnknowns);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_ThrowsInvalidOperationException()
    {
        var yaml = """
        domain_slug: auth
        concept_source: interactive
        concept_text: "Add OAuth2 provider support."
        upstream_paths: []
        constraints: []
        known_unknowns: []
        """;

        var ex = Assert.Throws<InvalidOperationException>(() => IntakeConceptArtifactYaml.Deserialize(yaml));

        Assert.Contains("required field", ex.Message, StringComparison.Ordinal);
    }

    private static ConceptIntakePacket CreatePacket()
    {
        return new ConceptIntakePacket
        {
            DomainSlug = "auth",
            ConceptSource = "interactive",
            ConceptText = "Add OAuth2 provider support.",
            UpstreamPaths = ["intents/intent-cli/intent-tree/means/04-worker-interface-strategy.md"],
            InitialGoal = "Add OAuth2 provider support.",
            Constraints = ["Must not break existing session flow"],
            KnownUnknowns = ["Which OAuth providers to support?"]
        };
    }
}
