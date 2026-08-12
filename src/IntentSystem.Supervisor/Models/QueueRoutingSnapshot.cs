namespace IntentSystem.Supervisor.Models;

/// <summary>
/// Optional immutable routing facts copied into a queue item when a named
/// branch lane is accepted. The optional queue field keeps legacy queue-state
/// JSON valid while preventing later registry edits from retargeting a queued
/// execution unit.
/// </summary>
public sealed record QueueRoutingSnapshot
{
    public required string LaneId { get; init; }

    public required string DefinitionRevision { get; init; }

    public required string StartBranch { get; init; }

    public required string PrBaseBranch { get; init; }

    public required string LandingMode { get; init; }
}
