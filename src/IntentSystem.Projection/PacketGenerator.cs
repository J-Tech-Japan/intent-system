using IntentSystem.Projection.Models;
using IntentSystem.Projection.Rendering;

namespace IntentSystem.Projection;

public sealed record GeneratedPacket
{
    public required string ImplementationMarkdown { get; init; }
    public required string ReviewContextMarkdown { get; init; }
    public required string PacketYaml { get; init; }
    public required ResolvedPacketPaths Paths { get; init; }
}

public static class PacketGenerator
{
    public static GeneratedPacket Generate(SubSliceRow row, ProjectionContext context)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(context);

        var implementationPacket = ProjectionMapper.ToImplementationPacket(row, context);
        var reviewContextPacket = ProjectionMapper.ToReviewContextPacket(row, context);
        var paths = PacketPathResolver.Resolve(row.SourceExecutionUnit);

        return new GeneratedPacket
        {
            ImplementationMarkdown = ImplementationMarkdownRenderer.Render(implementationPacket),
            ReviewContextMarkdown = ReviewContextMarkdownRenderer.Render(reviewContextPacket),
            PacketYaml = PacketYamlRenderer.Render(implementationPacket, reviewContextPacket),
            Paths = paths
        };
    }

    public static GeneratedPacket Generate(
        ImplementationIssuePacket implementationPacket,
        ReviewContextPacket reviewContextPacket)
    {
        ArgumentNullException.ThrowIfNull(implementationPacket);
        ArgumentNullException.ThrowIfNull(reviewContextPacket);

        if (!string.Equals(
                implementationPacket.SourceExecutionUnit,
                reviewContextPacket.SourceExecutionUnit,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Implementation packet execution unit must match review context execution unit.");
        }

        var paths = PacketPathResolver.Resolve(implementationPacket.SourceExecutionUnit);

        return new GeneratedPacket
        {
            ImplementationMarkdown = ImplementationMarkdownRenderer.Render(implementationPacket),
            ReviewContextMarkdown = ReviewContextMarkdownRenderer.Render(reviewContextPacket),
            PacketYaml = PacketYamlRenderer.Render(implementationPacket, reviewContextPacket),
            Paths = paths
        };
    }
}
