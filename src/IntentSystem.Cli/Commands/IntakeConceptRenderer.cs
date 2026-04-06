using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.Cli.Commands;

internal static class IntakeConceptRenderer
{
    public static void WriteSummary(TextWriter writer, ConceptIntakePacket packet, string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        writer.WriteLine($"Intake concept artifact generated for domain '{packet.DomainSlug}'.");
        writer.WriteLine($"Artifact path: {artifactPath}");
        writer.WriteLine($"Concept source: {packet.ConceptSource}");
        writer.WriteLine($"Initial goal: {packet.InitialGoal}");
        writer.WriteLine($"Upstream paths: {packet.UpstreamPaths.Count}");
        writer.WriteLine($"Constraints: {packet.Constraints.Count}");
        writer.WriteLine($"Known unknowns: {packet.KnownUnknowns.Count}");
    }
}
