namespace IntentSystem.Supervisor.Models;

public sealed record QueueState
{
    public required string SchemaVersion { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required IReadOnlyList<QueueItem> Items { get; init; }
}
