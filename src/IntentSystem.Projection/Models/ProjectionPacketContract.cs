namespace IntentSystem.Projection.Models;

public sealed record ProjectionPacketContract
{
    public required ImplementationIssuePacket ImplementationIssuePacket { get; init; }

    public required ReviewContextPacket ReviewContextPacket { get; init; }
}
