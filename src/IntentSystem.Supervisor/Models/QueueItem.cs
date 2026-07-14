using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Supervisor.Models;

public sealed record QueueItem
{
    public required string ExecutionUnit { get; init; }

    public required string Title { get; init; }

    public required QueueItemState State { get; init; }

    public required IReadOnlyList<string> Dependencies { get; init; }

    public required IReadOnlyList<string> BlockedBy { get; init; }

    public required string ClarificationReturnPath { get; init; }

    public required PacketPaths PacketPaths { get; init; }

    public LinkedIssue? LinkedIssue { get; init; }

    [JsonConverter(typeof(LinkedPrJsonConverter))]
    public string? LinkedPr { get; init; }

    public required string WorkerRole { get; init; }

    public required string ReviewRole { get; init; }

    public required string Priority { get; init; }

    /// <summary>
    /// G525: set when <see cref="State"/> is <see cref="QueueItemState.Retired"/>
    /// — one of <c>superseded</c>, <c>decomposed</c>, or <c>obsolete</c>,
    /// optionally suffixed with an operator note. Null for every other state.
    /// </summary>
    public string? RetirementReason { get; init; }
}
