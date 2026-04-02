namespace IntentSystem.Supervisor.Models;

public sealed record PacketPaths
{
    public required string Implementation { get; init; }

    public required string ReviewContext { get; init; }

    public required string Yaml { get; init; }
}
