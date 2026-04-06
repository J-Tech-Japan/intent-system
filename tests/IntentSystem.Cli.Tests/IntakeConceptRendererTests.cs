using IntentSystem.Cli.Commands;
using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeConceptRendererTests
{
    [Fact]
    public void WriteSummary_GivenPacket_WritesDeterministicSummary()
    {
        using var writer = new StringWriter();

        IntakeConceptRenderer.WriteSummary(writer, new ConceptIntakePacket
        {
            DomainSlug = "auth",
            ConceptSource = "interactive",
            ConceptText = "Add OAuth2 provider support.",
            UpstreamPaths = ["intents/intent-cli/intent-tree/means/04-worker-interface-strategy.md"],
            InitialGoal = "Add OAuth2 provider support.",
            Constraints = ["Must not break existing session flow"],
            KnownUnknowns = ["Which OAuth providers to support?"]
        }, "/repo/.intent-cli/intake/auth.concept.yaml");

        var output = writer.ToString();
        Assert.Contains("Intake concept artifact generated for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Artifact path: /repo/.intent-cli/intake/auth.concept.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Concept source: interactive", output, StringComparison.Ordinal);
        Assert.Contains("Initial goal: Add OAuth2 provider support.", output, StringComparison.Ordinal);
        Assert.Contains("Upstream paths: 1", output, StringComparison.Ordinal);
        Assert.Contains("Constraints: 1", output, StringComparison.Ordinal);
        Assert.Contains("Known unknowns: 1", output, StringComparison.Ordinal);
    }
}
