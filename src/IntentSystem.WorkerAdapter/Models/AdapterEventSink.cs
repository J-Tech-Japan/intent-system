namespace IntentSystem.WorkerAdapter.Models;

/// <summary>
/// Identifies where adapter runtime events should be emitted.
/// </summary>
public sealed record AdapterEventSink
{
    public required string SinkType { get; init; }

    public required string SinkRef { get; init; }
}
