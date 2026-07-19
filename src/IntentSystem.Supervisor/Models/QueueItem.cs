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

    /// <summary>
    /// G537 round-4 review repair: a durable, monotonically-incrementing
    /// counter bumped by exactly 1 on every successful <c>queue
    /// reprioritize --write</c>. Deliberately NOT <c>required</c> — legacy
    /// <c>queue-state.json</c> files predating this field simply
    /// deserialize it as <c>0</c> (the JSON default for an absent `int`
    /// property), which is the correct migration semantics: revision
    /// counting starts from the first reprioritize this command ever
    /// applies to a given item. Unlike a content fingerprint of the whole
    /// file, this value can never recur for two distinct mutations of the
    /// same item, even if every other field of <c>queue-state.json</c>
    /// later cycles back to byte-identical content — it only ever moves
    /// forward.
    /// </summary>
    public int PriorityRevision { get; init; }
}
